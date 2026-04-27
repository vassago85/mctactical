<script setup lang="ts">
import { computed, nextTick, onMounted, ref, watch } from 'vue'
import { http } from '@/api/http'
import { useToast } from '@/composables/useToast'
import { formatZAR } from '@/utils/format'
import BarcodeScanner from '@/components/BarcodeScanner.vue'
import McButton from '@/components/ui/McButton.vue'
import McField from '@/components/ui/McField.vue'
import McAlert from '@/components/ui/McAlert.vue'
import McEmptyState from '@/components/ui/McEmptyState.vue'
import McSkeleton from '@/components/ui/McSkeleton.vue'
import McModal from '@/components/ui/McModal.vue'
import McSpinner from '@/components/ui/McSpinner.vue'
import McCheckbox from '@/components/ui/McCheckbox.vue'
import McBadge from '@/components/ui/McBadge.vue'
import { Minus, Plus, ChevronDown, ChevronRight, Search, Camera, Check } from 'lucide-vue-next'
import { beepSuccess, beepError } from '@/utils/beep'

type Product = {
  id: string
  sku: string
  barcode?: string | null
  name: string
  sellPrice: number
  qtyOnHand: number
  cost?: number | null
}

type ActiveSpecial = { productId: string; specialPrice?: number | null; discountPercent?: number | null }
type ActivePromotion = {
  promotionId?: string | null
  promotionName?: string | null
  siteDiscountPercent: number
  specials: ActiveSpecial[]
}

type Line = { product: Product; qty: number; unitPrice: number; lineDiscount: number; originalPrice: number; discMode: 'R' | '%'; discInput: number }

const q = ref('')
const results = ref<Product[]>([])
const cart = ref<Line[]>([])
const scanOpen = ref(false)
const customerName = ref('')
const customerEmail = ref('')
const customerType = ref('')
const customerCompany = ref('')
const customerAddress = ref('')
const customerVatNumber = ref('')
const showBusinessFields = ref(false)
const customerLoading = ref(false)
const customerMatch = ref(false)
const paymentMethod = ref('Card')
const discountTotal = ref(0)
const sendEmail = ref(true)
const busy = ref(false)
const err = ref<string | null>(null)
const searchLoading = ref(false)
const toast = useToast()
const isManager = ref(false)
const posRules = ref<{
  maxCartDiscountPercent: number
  maxLineDiscountPercent: number
  maxPriceDecreasePercentFromList: number
  maxPriceIncreasePercentFromList: number
  isManager: boolean
} | null>(null)
const activePromo = ref<ActivePromotion | null>(null)

const showBelowCostModal = ref(false)
const showSaleSummary = ref(false)
const saleSummary = ref<{
  invoiceId: string
  invoiceNumber: string
  grandTotal: number
  customerName: string | null
  paymentMethod: string
  emailSent: boolean
  emailWarning: string | null
  belowCostWarning: string | null
  isSpecialOrder: boolean
  lines: { name: string; qty: number; unitPrice: number; lineTotal: number }[]
} | null>(null)

// ── Transient add-to-cart feedback (non-breaking additions) ──────────────────
// Beeps on successful add and briefly highlights the affected row, plus a
// short "Added: X" status pill near the search bar. None of this alters cart
// maths, stock checks, or the commit pipeline — it just reacts to outcomes.
const recentlyAdded = ref<Set<string>>(new Set())
const lastAddedLabel = ref<string | null>(null)
let lastAddedTimer: ReturnType<typeof setTimeout> | null = null
const totalPulse = ref(false)
let totalPulseTimer: ReturnType<typeof setTimeout> | null = null

function markAdded(p: Product) {
  try { beepSuccess() } catch { /* audio not available — ignore */ }
  const next = new Set(recentlyAdded.value)
  next.add(p.id)
  recentlyAdded.value = next
  setTimeout(() => {
    const after = new Set(recentlyAdded.value)
    after.delete(p.id)
    recentlyAdded.value = after
  }, 900)
  lastAddedLabel.value = p.name
  if (lastAddedTimer) clearTimeout(lastAddedTimer)
  lastAddedTimer = setTimeout(() => { lastAddedLabel.value = null }, 1400)
  totalPulse.value = true
  if (totalPulseTimer) clearTimeout(totalPulseTimer)
  totalPulseTimer = setTimeout(() => { totalPulse.value = false }, 700)
}

function roundUpR10(v: number): number {
  return Math.ceil(v / 10) * 10
}

function getEffectivePrice(p: Product): { price: number; hasDiscount: boolean } {
  if (!activePromo.value) return { price: p.sellPrice, hasDiscount: false }
  const special = activePromo.value.specials.find(s => s.productId === p.id)
  if (special) {
    if (special.specialPrice != null) return { price: special.specialPrice, hasDiscount: special.specialPrice !== p.sellPrice }
    if (special.discountPercent != null) {
      const price = roundUpR10(p.sellPrice * (1 - special.discountPercent / 100))
      return { price, hasDiscount: special.discountPercent > 0 }
    }
  }
  if (activePromo.value.siteDiscountPercent > 0) {
    const price = roundUpR10(p.sellPrice * (1 - activePromo.value.siteDiscountPercent / 100))
    return { price, hasDiscount: true }
  }
  return { price: p.sellPrice, hasDiscount: false }
}

type RecentInvoice = {
  id: string
  invoiceNumber: string
  customerName: string | null
  grandTotal: number
  paymentMethod: string
  createdAt: string
  publicToken: string
}
const recentInvoices = ref<RecentInvoice[]>([])

async function loadRecentInvoices() {
  try {
    const { data } = await http.get<RecentInvoice[]>('/api/invoices/recent?take=5')
    recentInvoices.value = data
  } catch { /* best effort */ }
}

onMounted(async () => {
  try {
    const { data } = await http.get('/api/settings/pos-rules')
    posRules.value = data
    isManager.value = data.isManager ?? false
  } catch {
    posRules.value = null
  }
  try {
    const { data } = await http.get<ActivePromotion>('/api/promotions/active')
    if (data.promotionId || data.specials.length) activePromo.value = data
  } catch { /* no active promotion */ }
  loadRecentInvoices()
})

let searchTimer: ReturnType<typeof setTimeout> | null = null
watch(q, () => {
  if (searchTimer) clearTimeout(searchTimer)
  if (scannerBufferActive) {
    results.value = []
    searchLoading.value = false
    return
  }
  searchTimer = setTimeout(() => void runSearch(), 250)
})

// ── Scanner heuristic ─────────────────────────────────────────────────────────
// Hand scanners emit every character within a few ms and (almost always) finish
// with Enter. We watch inter-key gaps on the POS search field: if 3+ characters
// arrive within 35ms of each other we flag the current buffer as a scan. When
// Enter then fires we add the matched product and clear, ready for the next
// scan. If the heuristic didn't trip (slow typing) Enter falls through to the
// existing "search and add if single hit" behaviour.
const SCAN_MAX_GAP_MS = 35
const SCAN_MIN_FAST_KEYS = 3
const SCAN_RESET_GAP_MS = 500
let lastKeyAt = 0
let fastKeyCount = 0
let scannerBufferActive = false

function noteKeyTimingFromKey(ev: KeyboardEvent) {
  // Ignore modifier/navigation-only presses so Shift etc. don't count as fast keys.
  if (ev.key.length !== 1 && ev.key !== 'Enter') return
  const now = performance.now()
  if (lastKeyAt === 0 || now - lastKeyAt > SCAN_RESET_GAP_MS) {
    lastKeyAt = now
    fastKeyCount = 1
    scannerBufferActive = false
    return
  }
  const gap = now - lastKeyAt
  lastKeyAt = now
  if (gap <= SCAN_MAX_GAP_MS) {
    fastKeyCount += 1
    if (fastKeyCount >= SCAN_MIN_FAST_KEYS) scannerBufferActive = true
  } else {
    fastKeyCount = 1
  }
}

function resetScannerHeuristic() {
  lastKeyAt = 0
  fastKeyCount = 0
  scannerBufferActive = false
}

watch(q, (val) => { if (!val) resetScannerHeuristic() })

function findExactMatch(code: string, list: Product[]): Product | null {
  const trimmed = code.trim()
  if (!trimmed) return null
  const byBarcode = list.find((p) => p.barcode === trimmed)
  if (byBarcode) return byBarcode
  const bySku = list.find((p) => p.sku === trimmed)
  if (bySku) return bySku
  if (list.length === 1) return list[0]
  return null
}

async function refocusSearch() {
  await nextTick()
  const el = document.getElementById('pos-search') as HTMLInputElement | null
  el?.focus()
}

/**
 * Add the product that matches the current query, then clear the field and
 * refocus it. Used by the physical scanner (Enter on a fast buffer), the camera
 * BarcodeScanner component, and the keyboard "exact-one-hit + Enter" flow.
 * Returns true when a line was added.
 */
async function commitEntry(rawCode: string, options?: { forceExact?: boolean }): Promise<boolean> {
  const code = rawCode.trim()
  if (!code) return false

  // Try to match against currently loaded results first — this is the common
  // case when the search debounce already fired before Enter arrived.
  let match = findExactMatch(code, results.value)
  if (!match) {
    try {
      const { data } = await http.get<Product[]>('/api/products', { params: { q: code, take: 5 } })
      match = findExactMatch(code, data)
    } catch {
      match = null
    }
  }

  if (!match) {
    if (options?.forceExact) {
      try { beepError() } catch { /* ignore */ }
      toast.error(`Not found: ${code}`)
      q.value = ''
      resetScannerHeuristic()
      await refocusSearch()
    }
    return false
  }

  if (!isManager.value && match.qtyOnHand < 1) {
    try { beepError() } catch { /* ignore */ }
    toast.error(`${match.name} is out of stock`)
    q.value = ''
    resetScannerHeuristic()
    await refocusSearch()
    return false
  }

  addToCart(match)
  toast.success(`Added: ${match.name}`)
  q.value = ''
  results.value = []
  resetScannerHeuristic()
  await refocusSearch()
  return true
}

function onSearchKeydown(ev: KeyboardEvent) {
  noteKeyTimingFromKey(ev)
  if (ev.key !== 'Enter') return
  ev.preventDefault()
  const code = q.value.trim()
  if (!code) return
  // Scanner-shaped input: treat Enter as a firm commit (error toast on miss).
  // Slow manual typing: only commit if there's a single, unambiguous hit.
  const forceExact = scannerBufferActive
  void commitEntry(code, { forceExact })
}

async function runSearch() {
  err.value = null
  const s = q.value.trim()
  if (!s) {
    results.value = []
    searchLoading.value = false
    return
  }
  searchLoading.value = true
  try {
    const { data } = await http.get<Product[]>('/api/products', {
      params: { q: s, take: 40 }
    })
    results.value = data
  } catch {
    results.value = []
  } finally {
    searchLoading.value = false
  }
}

function addToCart(p: Product) {
  const existing = cart.value.find((l) => l.product.id === p.id)
  if (existing) {
    if (!isManager.value && existing.qty >= p.qtyOnHand) return
    existing.qty += 1
  } else {
    if (!isManager.value && p.qtyOnHand < 1) return
    const { price } = getEffectivePrice(p)
    cart.value.push({ product: p, qty: 1, unitPrice: price, originalPrice: p.sellPrice, lineDiscount: 0, discMode: 'R', discInput: 0 })
  }
  markAdded(p)
}

function onScan(code: string) {
  const trimmed = code.trim()
  q.value = trimmed
  scanOpen.value = false
  void commitEntry(trimmed, { forceExact: true })
}

function bumpQty(l: Line, delta: number) {
  const next = l.qty + delta
  if (next < 1) return
  if (!isManager.value && next > l.product.qtyOnHand) return
  l.qty = next
}

function removeLine(l: Line) {
  cart.value = cart.value.filter((x) => x.product.id !== l.product.id)
}

function computedLineDiscount(l: Line): number {
  if (l.discInput <= 0) return 0
  if (l.discMode === '%') return Math.round(l.unitPrice * l.qty * l.discInput / 100 * 100) / 100
  return l.discInput
}

const subTotal = computed(() =>
  cart.value.reduce((s, l) => {
    const ld = computedLineDiscount(l)
    return s + Math.max(0, l.unitPrice * l.qty - ld)
  }, 0)
)

const grandPreview = computed(() => Math.max(0, subTotal.value - discountTotal.value))
const vatAmount = computed(() => {
  const total = grandPreview.value
  return Math.round((total - total / 1.15) * 100) / 100
})

const totalCostInclVat = computed(() =>
  cart.value.reduce((s, l) => s + Math.round((l.product.cost ?? 0) * 1.15 * 100) / 100 * l.qty, 0)
)

const belowCostWarning = computed(() => {
  if (!isManager.value || cart.value.length === 0) return null
  const lines = cart.value.filter(l => {
    const costIncl = Math.round((l.product.cost ?? 0) * 1.15 * 100) / 100
    const linePrice = l.unitPrice * l.qty - computedLineDiscount(l)
    return costIncl > 0 && linePrice < costIncl * l.qty
  })
  if (lines.length)
    return `Below cost (incl VAT): ${lines.map(l => l.product.name).join(', ')}`
  if (grandPreview.value < totalCostInclVat.value && totalCostInclVat.value > 0)
    return `Sale total ${formatZAR(grandPreview.value)} is below total cost incl VAT ${formatZAR(totalCostInclVat.value)}`
  return null
})

const hasSpecialOrderLines = computed(() =>
  cart.value.some(l => l.qty > l.product.qtyOnHand)
)
const showSpecialOrderModal = ref(false)

async function lookupCustomer() {
  const email = customerEmail.value.trim()
  if (!email || email.length < 3) return
  customerLoading.value = true
  customerMatch.value = false
  try {
    const { data } = await http.get('/api/customers/by-email', { params: { email } })
    if (data) {
      customerName.value = data.name || customerName.value
      customerType.value = data.customerType || customerType.value
      customerCompany.value = data.company || ''
      customerAddress.value = data.address || ''
      customerVatNumber.value = data.vatNumber || ''
      if (data.company || data.vatNumber) showBusinessFields.value = true
      customerMatch.value = true
    }
  } catch { /* 404 = new customer, that's fine */ }
  finally { customerLoading.value = false }
}

async function doCheckout() {
  err.value = null
  busy.value = true
  try {
    const summaryLines = cart.value.map(l => ({
      name: l.product.name,
      qty: l.qty,
      unitPrice: l.unitPrice,
      lineTotal: Math.max(0, l.unitPrice * l.qty - computedLineDiscount(l))
    }))
    const { data } = await http.post('/api/invoices', {
      customerName: customerName.value || null,
      customerEmail: customerEmail.value || null,
      customerType: customerType.value || null,
      customerCompany: customerCompany.value || null,
      customerAddress: customerAddress.value || null,
      customerVatNumber: customerVatNumber.value || null,
      paymentMethod: paymentMethod.value,
      discountTotal: discountTotal.value,
      promotionName: activePromo.value?.promotionName || null,
      sendEmail: sendEmail.value && !!customerEmail.value.trim(),
      lines: cart.value.map((l) => ({
        productId: l.product.id,
        quantity: l.qty,
        unitPriceOverride: l.unitPrice !== l.product.sellPrice ? l.unitPrice : null,
        originalUnitPrice: l.product.sellPrice,
        lineDiscount: computedLineDiscount(l)
      }))
    })
    saleSummary.value = {
      invoiceId: data.id,
      invoiceNumber: data.invoiceNumber,
      grandTotal: data.grandTotal,
      customerName: customerName.value || null,
      paymentMethod: paymentMethod.value,
      emailSent: sendEmail.value && !!customerEmail.value.trim(),
      emailWarning: data.emailWarning ?? null,
      belowCostWarning: data.belowCostWarning ?? null,
      isSpecialOrder: data.isSpecialOrder ?? false,
      lines: summaryLines
    }
    cart.value = []
    discountTotal.value = 0
    customerName.value = ''
    customerEmail.value = ''
    customerType.value = ''
    customerCompany.value = ''
    customerAddress.value = ''
    customerVatNumber.value = ''
    showBusinessFields.value = false
    customerMatch.value = false
    paymentMethod.value = 'Card'
    showSaleSummary.value = true
    results.value = []
    q.value = ''
    loadRecentInvoices()
  } catch (e: unknown) {
    const ax = e as { response?: { data?: { error?: string } } }
    err.value = ax.response?.data?.error ?? 'Checkout failed'
    toast.error(err.value)
  } finally {
    busy.value = false
    showBelowCostModal.value = false
  }
}

function requestCheckout() {
  if (!cart.value.length) return
  if (hasSpecialOrderLines.value) {
    showSpecialOrderModal.value = true
    return
  }
  if (belowCostWarning.value) {
    showBelowCostModal.value = true
    return
  }
  void doCheckout()
}

function confirmSpecialOrder() {
  showSpecialOrderModal.value = false
  if (belowCostWarning.value) {
    showBelowCostModal.value = true
    return
  }
  void doCheckout()
}

async function openOrderConfirmationPdf() {
  if (!saleSummary.value) return
  try {
    const { data } = await http.get(`/api/invoices/${saleSummary.value.invoiceId}/order-confirmation-pdf`, { responseType: 'blob' })
    const url = URL.createObjectURL(data)
    window.open(url, '_blank')
    setTimeout(() => URL.revokeObjectURL(url), 60_000)
  } catch { /* silently fail */ }
}

const searchEmpty = computed(() => !q.value.trim())
const searchNoHits = computed(() => !searchLoading.value && q.value.trim() && !results.value.length)
</script>

<template>
  <div class="pos-shell">
    <!-- Compact banner row: title, mode hint, promo, errors -->
    <div class="pos-banner-row">
      <div class="pos-banner-row__left">
        <h1 class="pos-title">Point of sale</h1>
        <span v-if="posRules && isManager" class="pos-mode pos-mode--manager">Manager — overrides allowed</span>
        <span v-else-if="posRules" class="pos-mode">Sales — list price only</span>
      </div>
      <div v-if="activePromo?.promotionName" class="pos-promo-chip">
        <McBadge variant="accent">{{ activePromo.promotionName }}</McBadge>
        <span v-if="activePromo.siteDiscountPercent > 0">{{ activePromo.siteDiscountPercent }}% off all</span>
        <span v-if="activePromo.specials.length"> · {{ activePromo.specials.length }} special{{ activePromo.specials.length !== 1 ? 's' : '' }}</span>
      </div>
    </div>

    <McAlert v-if="err" variant="error" class="pos-alert">{{ err }}</McAlert>

    <!-- Compact sticky search/scan toolbar -->
    <div class="pos-toolbar">
      <div class="pos-toolbar__search">
        <Search :size="18" class="pos-toolbar__icon" />
        <input
          id="pos-search"
          v-model="q"
          type="search"
          autocomplete="off"
          placeholder="Scan barcode, or type SKU / name…"
          class="pos-toolbar__input"
          @keydown="onSearchKeydown"
        />
        <McSpinner v-if="searchLoading" class="pos-toolbar__spinner" />
      </div>
      <Transition name="pos-fade">
        <div v-if="lastAddedLabel" class="pos-added-pill" role="status" aria-live="polite">
          <Check :size="14" />
          <span>Added: {{ lastAddedLabel }}</span>
        </div>
      </Transition>
      <button
        type="button"
        class="pos-toolbar__camera"
        :class="{ 'pos-toolbar__camera--on': scanOpen }"
        @click="scanOpen = !scanOpen"
      >
        <Camera :size="16" />
        <span>{{ scanOpen ? 'Hide camera' : 'Camera' }}</span>
      </button>
    </div>

    <div v-if="scanOpen" class="pos-camera-wrap">
      <BarcodeScanner :active="scanOpen" @decode="onScan" />
    </div>

    <div class="pos-workspace">
      <!-- LEFT COLUMN: results + cart + recent invoices -->
      <div class="pos-workspace__left">
        <!-- Results panel: only when user is actively searching -->
        <aside v-if="q.trim()" class="pos-results-aside">
          <div class="pos-panel">
            <div class="pos-panel__head">
              <span>Results</span>
              <span class="pos-panel__meta" v-if="!searchLoading && results.length">{{ results.length }}</span>
            </div>
            <div class="pos-panel__body">
              <McSkeleton v-if="searchLoading" :lines="4" />
              <McEmptyState
                v-else-if="searchNoHits"
                title="No matches"
                hint="Try other words — order doesn't matter."
              />
              <div v-else class="pos-results-grid">
                <button
                  v-for="p in results"
                  :key="p.id"
                  type="button"
                  class="pos-card"
                  :class="{ 'pos-card--out': !isManager && p.qtyOnHand < 1 }"
                  :disabled="!isManager && p.qtyOnHand < 1"
                  @click="addToCart(p)"
                >
                  <p class="pos-card__name">{{ p.name }}</p>
                  <p class="pos-card__meta">
                    <span>{{ p.sku }}</span>
                    <span v-if="p.barcode"> · {{ p.barcode }}</span>
                  </p>
                  <div class="pos-card__foot">
                    <div class="pos-card__prices">
                      <template v-if="getEffectivePrice(p).hasDiscount">
                        <span class="pos-card__price pos-card__price--sale">{{ formatZAR(getEffectivePrice(p).price) }}</span>
                        <span class="pos-card__price--was">{{ formatZAR(p.sellPrice) }}</span>
                      </template>
                      <span v-else class="pos-card__price">{{ formatZAR(p.sellPrice) }}</span>
                    </div>
                    <span
                      class="pos-card__stock"
                      :class="{
                        'pos-card__stock--low': p.qtyOnHand <= 3 && p.qtyOnHand > 0,
                        'pos-card__stock--out': p.qtyOnHand < 1
                      }"
                    >{{ p.qtyOnHand < 1 ? 'Out of stock' : `${p.qtyOnHand} in stock` }}</span>
                  </div>
                </button>
              </div>
            </div>
          </div>
        </aside>

        <!-- Cart lines -->
        <div class="pos-panel pos-panel--cart">
          <div class="pos-panel__head">
            <span>Cart</span>
            <span class="pos-panel__meta" v-if="cart.length">{{ cart.length }} line{{ cart.length !== 1 ? 's' : '' }}</span>
          </div>
          <div class="pos-panel__body pos-panel__body--scroll">
            <McEmptyState
              v-if="!cart.length"
              title="Scan or search to start"
              hint="Hand scanners are auto-detected — just aim and fire."
            />
            <table v-else class="pos-cart-table mc-table">
              <thead>
                <tr>
                  <th>Item</th>
                  <th>Qty</th>
                  <th>Price</th>
                  <th v-if="isManager">Disc</th>
                  <th>Total</th>
                  <th></th>
                </tr>
              </thead>
              <tbody>
                <tr
                  v-for="l in cart"
                  :key="l.product.id"
                  :class="{ 'pos-cart-row--just-added': recentlyAdded.has(l.product.id) }"
                >
                  <td class="pos-cart-name">
                    <span class="pos-cart-name__title">{{ l.product.name }}</span>
                    <span class="pos-cart-name__meta">
                      {{ l.product.sku }}<template v-if="l.product.barcode"> · {{ l.product.barcode }}</template>
                    </span>
                    <span v-if="l.originalPrice !== l.unitPrice" class="pos-cart-was">was {{ formatZAR(l.originalPrice) }}</span>
                    <McBadge v-if="l.qty > l.product.qtyOnHand" variant="warning">Special order — {{ l.qty - Math.max(0, l.product.qtyOnHand) }} to deliver</McBadge>
                  </td>
                  <td>
                    <div class="pos-stepper">
                      <button type="button" class="pos-stepper__btn" aria-label="Decrease" @click="bumpQty(l, -1)">
                        <Minus :size="16" />
                      </button>
                      <span class="pos-stepper__val">{{ l.qty }}</span>
                      <button type="button" class="pos-stepper__btn" aria-label="Increase" @click="bumpQty(l, 1)">
                        <Plus :size="16" />
                      </button>
                    </div>
                  </td>
                  <td>
                    <input
                      v-if="isManager"
                      v-model.number="l.unitPrice"
                      type="number"
                      class="pos-cart-input"
                      step="0.01"
                      min="0"
                    />
                    <span v-else>{{ formatZAR(l.unitPrice) }}</span>
                  </td>
                  <td v-if="isManager">
                    <div class="pos-disc-group">
                      <select v-model="l.discMode" class="pos-disc-mode">
                        <option value="R">R</option>
                        <option value="%">%</option>
                      </select>
                      <input v-model.number="l.discInput" type="number" class="pos-cart-input" step="0.01" min="0" />
                    </div>
                  </td>
                  <td class="pos-cart-line-total">{{ formatZAR(Math.max(0, l.unitPrice * l.qty - computedLineDiscount(l))) }}</td>
                  <td>
                    <McButton variant="ghost" type="button" dense @click="removeLine(l)">×</McButton>
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>

        <!-- Recent invoices -->
        <div v-if="recentInvoices.length" class="pos-panel pos-panel--recent">
          <div class="pos-panel__head">
            <span>Recent invoices</span>
          </div>
          <div class="pos-panel__body pos-recent-list">
            <a
              v-for="inv in recentInvoices"
              :key="inv.id"
              class="pos-recent-item"
              :href="'/#/invoice/' + inv.publicToken"
              target="_blank"
              rel="noreferrer"
            >
              <span class="pos-recent-item__num">{{ inv.invoiceNumber }}</span>
              <span class="pos-recent-item__who">{{ inv.customerName || '—' }}</span>
              <span class="pos-recent-item__total">{{ formatZAR(inv.grandTotal) }}</span>
            </a>
          </div>
        </div>
      </div>

      <!-- RIGHT COLUMN: sticky checkout panel -->
      <aside class="pos-workspace__right">
        <div class="pos-checkout">
          <div class="pos-checkout__head">
            <span>Checkout</span>
          </div>
          <div class="pos-checkout__body">
            <!-- Customer info -->
            <div class="pos-checkout__group">
              <McField label="Customer name" for-id="cust-name">
                <input id="cust-name" v-model="customerName" type="text" autocomplete="name" />
              </McField>
              <McField label="Email (receipt)" for-id="cust-email">
                <div class="pos-email-wrap">
                  <input id="cust-email" v-model="customerEmail" type="email" autocomplete="email" @blur="lookupCustomer" />
                  <McSpinner v-if="customerLoading" class="pos-email-spinner" />
                </div>
                <small v-if="customerMatch" class="pos-email-match">Existing customer loaded</small>
              </McField>
              <McField label="Customer type" for-id="cust-type">
                <input id="cust-type" v-model="customerType" placeholder="e.g. ENT" />
              </McField>
              <div class="pos-checkout__inline">
                <McCheckbox v-model="sendEmail" label="Email invoice link" />
                <button type="button" class="btn-link-toggle" @click="showBusinessFields = !showBusinessFields">
                  <component :is="showBusinessFields ? ChevronDown : ChevronRight" :size="14" />
                  {{ showBusinessFields ? 'Hide' : 'Add' }} business details
                </button>
              </div>
              <div v-if="showBusinessFields" class="pos-checkout__business">
                <McField label="Company name" for-id="cust-company">
                  <input id="cust-company" v-model="customerCompany" type="text" placeholder="Business name" />
                </McField>
                <McField label="VAT number" for-id="cust-vat">
                  <input id="cust-vat" v-model="customerVatNumber" type="text" placeholder="e.g. 4123456789" />
                </McField>
                <McField label="Business address" for-id="cust-addr">
                  <textarea id="cust-addr" v-model="customerAddress" rows="2" placeholder="Street, City, Postal code" />
                </McField>
              </div>
            </div>

            <!-- Payment method as button group -->
            <div class="pos-checkout__group">
              <div class="pos-checkout__label">Payment method</div>
              <div class="pos-pay-group" role="group" aria-label="Payment method">
                <button
                  v-for="method in ['Card', 'Cash', 'EFT']"
                  :key="method"
                  type="button"
                  class="pos-pay-btn"
                  :class="{ 'pos-pay-btn--on': paymentMethod === method }"
                  @click="paymentMethod = method"
                >{{ method }}</button>
              </div>
            </div>

            <!-- Manager: order discount -->
            <div v-if="isManager" class="pos-checkout__group">
              <McField label="Order discount (R)" for-id="order-disc">
                <input id="order-disc" v-model.number="discountTotal" type="number" step="0.01" min="0" />
              </McField>
            </div>

            <!-- Totals -->
            <div class="pos-totals" :class="{ 'pos-totals--pulse': totalPulse }">
              <div class="pos-totals__row">
                <span>Subtotal</span>
                <strong>{{ formatZAR(subTotal) }}</strong>
              </div>
              <div v-if="isManager && discountTotal > 0" class="pos-totals__row pos-totals__row--muted">
                <span>Discount</span>
                <strong>− {{ formatZAR(discountTotal) }}</strong>
              </div>
              <div v-if="grandPreview > 0" class="pos-totals__row pos-totals__row--muted">
                <span>Incl. VAT (15%)</span>
                <span>{{ formatZAR(vatAmount) }}</span>
              </div>
              <div class="pos-totals__grand">
                <span>Total due</span>
                <strong>{{ formatZAR(grandPreview) }}</strong>
              </div>
            </div>

            <McAlert v-if="belowCostWarning" variant="warning" class="pos-checkout__warn">{{ belowCostWarning }}</McAlert>

            <McButton
              variant="primary"
              type="button"
              block
              :disabled="busy || !cart.length"
              class="pos-checkout-btn"
              @click="requestCheckout"
            >
              <McSpinner v-if="busy" />
              <span v-else>Complete sale</span>
            </McButton>
          </div>
        </div>
      </aside>
    </div>

    <McModal v-model="showBelowCostModal" title="Below cost">
      <p>{{ belowCostWarning }}</p>
      <p class="mc-text-muted" style="margin-bottom: 0; font-size: 0.9rem">Continue only if you intend to approve this sale.</p>
      <template #footer>
        <McButton variant="secondary" type="button" @click="showBelowCostModal = false">Cancel</McButton>
        <McButton variant="primary" type="button" :disabled="busy" @click="doCheckout">Proceed</McButton>
      </template>
    </McModal>

    <McModal v-model="showSpecialOrderModal" title="Special order">
      <p>Some items in this cart exceed available stock. This will create a <strong>Special Order</strong> — stock will go negative and items must be delivered to the customer.</p>
      <ul style="margin: 0.5rem 0; padding-left: 1.25rem">
        <li v-for="l in cart.filter(x => x.qty > x.product.qtyOnHand)" :key="l.product.id">
          {{ l.product.name }} — {{ l.qty - Math.max(0, l.product.qtyOnHand) }} unit(s) to deliver
        </li>
      </ul>
      <template #footer>
        <McButton variant="secondary" type="button" @click="showSpecialOrderModal = false">Cancel</McButton>
        <McButton variant="primary" type="button" :disabled="busy" @click="confirmSpecialOrder">Confirm special order</McButton>
      </template>
    </McModal>

    <McModal v-model="showSaleSummary" :title="saleSummary?.isSpecialOrder ? 'Special order created' : 'Sale complete'">
      <template v-if="saleSummary">
        <div class="sale-summary">
          <div class="sale-summary__header">
            <span class="sale-summary__invoice">{{ saleSummary.invoiceNumber }}</span>
            <span class="sale-summary__method">{{ saleSummary.paymentMethod }}</span>
          </div>

          <table class="mc-table sale-summary__table">
            <thead>
              <tr>
                <th>Item</th>
                <th style="text-align:right">Qty</th>
                <th style="text-align:right">Price</th>
                <th style="text-align:right">Total</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="(line, i) in saleSummary.lines" :key="i">
                <td>{{ line.name }}</td>
                <td style="text-align:right">{{ line.qty }}</td>
                <td style="text-align:right">{{ formatZAR(line.unitPrice) }}</td>
                <td style="text-align:right">{{ formatZAR(line.lineTotal) }}</td>
              </tr>
            </tbody>
            <tfoot>
              <tr>
                <td colspan="3" style="text-align:right"><strong>Total</strong></td>
                <td style="text-align:right"><strong>{{ formatZAR(saleSummary.grandTotal) }}</strong></td>
              </tr>
            </tfoot>
          </table>

          <p v-if="saleSummary.customerName" class="sale-summary__detail">Customer: {{ saleSummary.customerName }}</p>
          <p v-if="saleSummary.emailSent" class="sale-summary__detail">Receipt emailed</p>

          <McAlert v-if="saleSummary.isSpecialOrder" variant="warning">
            This is a special order. Items need to be delivered to the customer.
          </McAlert>
          <McAlert v-if="saleSummary.belowCostWarning" variant="warning">{{ saleSummary.belowCostWarning }}</McAlert>
          <McAlert v-if="saleSummary.emailWarning" variant="error">{{ saleSummary.emailWarning }}</McAlert>
        </div>
      </template>
      <template #footer>
        <McButton v-if="saleSummary?.isSpecialOrder" variant="secondary" type="button" @click="openOrderConfirmationPdf">Order confirmation PDF</McButton>
        <McButton variant="primary" type="button" @click="showSaleSummary = false">Done</McButton>
      </template>
    </McModal>
  </div>
</template>

<style scoped>
/* ──────────────────────────────────────────────────────────────────────────
   Laptop-first POS layout. The shell fills the available viewport the app
   provides (AppShell clips scrolls at the sidebar), so we use min-height:0
   children to let the cart / results scroll internally without ever making
   the whole page scroll. On tablet (<1100px) we gracefully stack.
   ────────────────────────────────────────────────────────────────────── */

.pos-shell {
  display: block;
  max-width: 100%;
}

.pos-banner-row {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  flex-wrap: wrap;
}
.pos-banner-row__left {
  display: flex;
  align-items: baseline;
  gap: 0.75rem;
  min-width: 0;
}
.pos-title {
  margin: 0;
  font-size: 1.35rem;
  font-weight: 800;
  letter-spacing: -0.01em;
  color: var(--mc-app-heading, #0a0a0c);
}
.pos-mode {
  font-size: 0.78rem;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.06em;
  padding: 0.2rem 0.55rem;
  border-radius: 6px;
  background: var(--mc-app-surface-muted, #f0eeea);
  color: var(--mc-app-text-muted, #5c5a56);
}
.pos-mode--manager {
  background: rgba(244, 122, 32, 0.12);
  color: #b44a0c;
}
.pos-promo-chip {
  display: inline-flex;
  align-items: center;
  gap: 0.5rem;
  padding: 0.35rem 0.75rem;
  background: rgba(244, 122, 32, 0.08);
  border: 1px solid rgba(244, 122, 32, 0.25);
  border-radius: 999px;
  font-size: 0.82rem;
  font-weight: 500;
  color: var(--mc-app-text-secondary, #333336);
  margin-left: auto;
}

.pos-alert { margin: 0; }

/* ── Sticky compact search/scan toolbar ──────────────────────────────── */
.pos-toolbar {
  position: sticky;
  top: 0;
  z-index: 4;
  display: flex;
  align-items: center;
  gap: 0.6rem;
  padding: 0.55rem 0.65rem;
  background: var(--mc-app-surface, #fff);
  border: 1px solid var(--mc-app-border-soft, #ddd9d3);
  border-radius: 14px;
  box-shadow: 0 2px 10px rgba(0, 0, 0, 0.04);
}
.pos-toolbar__search {
  position: relative;
  display: flex;
  align-items: center;
  flex: 1;
  min-width: 0;
  padding: 0 0.75rem;
  background: var(--mc-app-surface-muted, #f6f5f1);
  border: 1.5px solid transparent;
  border-radius: 10px;
  transition: border-color 0.12s ease, background 0.12s ease;
}
.pos-toolbar__search:focus-within {
  background: var(--mc-app-surface, #fff);
  border-color: var(--mc-accent, #f47a20);
}
.pos-toolbar__icon {
  color: var(--mc-app-text-muted, #5c5a56);
  flex-shrink: 0;
}
.pos-toolbar__input {
  flex: 1;
  min-width: 0;
  border: 0;
  outline: none;
  background: transparent;
  padding: 0.72rem 0.6rem;
  font-size: 1.02rem;
  font-weight: 500;
  color: var(--mc-app-text, #1a1a1c);
}
.pos-toolbar__input::placeholder {
  color: var(--mc-app-text-muted, #8a877f);
  font-weight: 400;
}
.pos-toolbar__spinner {
  width: 16px;
  height: 16px;
  flex-shrink: 0;
}
.pos-toolbar__camera {
  display: inline-flex;
  align-items: center;
  gap: 0.35rem;
  padding: 0.55rem 0.85rem;
  border: 1.5px solid var(--mc-app-border-subtle, #c8c5bd);
  border-radius: 10px;
  background: var(--mc-app-surface, #fff);
  font-size: 0.88rem;
  font-weight: 600;
  color: var(--mc-app-text-secondary, #333336);
  cursor: pointer;
  flex-shrink: 0;
  transition: background 0.12s ease, border-color 0.12s ease, color 0.12s ease;
}
.pos-toolbar__camera:hover {
  background: var(--mc-app-surface-muted, #f6f5f1);
}
.pos-toolbar__camera--on {
  background: var(--mc-accent, #f47a20);
  border-color: var(--mc-accent, #f47a20);
  color: #fff;
}

.pos-added-pill {
  display: inline-flex;
  align-items: center;
  gap: 0.35rem;
  padding: 0.4rem 0.7rem;
  border-radius: 999px;
  background: rgba(46, 125, 50, 0.12);
  color: #2e7d32;
  font-size: 0.82rem;
  font-weight: 600;
  white-space: nowrap;
  max-width: 22rem;
  overflow: hidden;
  text-overflow: ellipsis;
  flex-shrink: 0;
}

.pos-fade-enter-active,
.pos-fade-leave-active {
  transition: opacity 0.18s ease, transform 0.18s ease;
}
.pos-fade-enter-from,
.pos-fade-leave-to {
  opacity: 0;
  transform: translateY(-3px);
}

.pos-camera-wrap {
  padding: 0.85rem;
  background: var(--mc-app-surface-2, #f9f8f6);
  border-radius: 12px;
  border: 1px solid var(--mc-app-border-faint, #eceae5);
}

/* ── Two-column workspace ─────────────────────────────────────────────── */
.pos-workspace {
  display: grid;
  grid-template-columns: 1fr;
  gap: 1rem;
  align-items: start;
  margin-top: 0.65rem;
}
.pos-workspace__left {
  min-width: 0;
}
.pos-workspace__right {
  min-width: 0;
}
.pos-results-aside {
  margin-bottom: 0.75rem;
}

@media (min-width: 1100px) {
  .pos-workspace {
    grid-template-columns: minmax(0, 1fr) 360px;
    gap: 1.25rem;
  }
  .pos-workspace__right {
    position: sticky;
    top: 4.25rem;
    align-self: start;
    max-height: calc(100vh - 5rem);
    overflow-y: auto;
  }
}
@media (min-width: 1400px) {
  .pos-workspace {
    grid-template-columns: minmax(0, 1fr) 400px;
  }
}

/* ── Shared panel styling (replaces McCard at POS-level for density) ── */
.pos-panel {
  background: var(--mc-app-surface, #fff);
  border: 1px solid var(--mc-app-border-soft, #ddd9d3);
  border-radius: 14px;
  overflow: hidden;
  margin-bottom: 0.75rem;
}
.pos-panel__head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 0.55rem 1rem;
  font-size: 0.78rem;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.08em;
  color: var(--mc-app-text-muted, #5c5a56);
  border-bottom: 1px solid var(--mc-app-border-faint, #eceae5);
  background: var(--mc-app-surface-2, #faf9f6);
  flex-shrink: 0;
}
.pos-panel__meta {
  font-size: 0.75rem;
  font-weight: 600;
  padding: 0.15rem 0.5rem;
  border-radius: 6px;
  background: var(--mc-app-surface-muted, #f0eeea);
  color: var(--mc-app-text-secondary, #333336);
  letter-spacing: normal;
  text-transform: none;
}
.pos-panel__body {
  padding: 0.85rem 1rem;
  min-height: 0;
}
.pos-panel__body--scroll {
  overflow-y: auto;
  padding: 0;
}
.pos-panel--cart {
  min-height: 180px;
}

/* ── Results card grid ────────────────────────────────────────────────── */
.pos-results-aside .pos-panel__body {
  background: var(--mc-app-page-bg, #eae8e3);
}
.pos-results-grid {
  display: grid;
  grid-template-columns: 1fr;
  gap: 0.55rem;
  padding: 0.55rem;
}
@media (min-width: 480px) and (max-width: 1099px) {
  .pos-results-grid { grid-template-columns: repeat(2, 1fr); }
}

.pos-card {
  display: flex;
  flex-direction: column;
  gap: 0.4rem;
  padding: 0.85rem 1rem;
  background: var(--mc-app-surface-2, #faf9f6);
  border: 2px solid var(--mc-app-border-soft, #ddd9d3);
  border-radius: 12px;
  text-align: left;
  cursor: pointer;
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.06);
  transition: border-color 0.12s ease, box-shadow 0.12s ease, background 0.12s ease, transform 0.1s ease;
}
.pos-card:hover:not(:disabled) {
  border-color: var(--mc-accent, #f47a20);
  box-shadow: 0 3px 12px rgba(244, 122, 32, 0.18);
  background: #fff;
  transform: translateY(-1px);
}
.pos-card:active:not(:disabled) {
  background: rgba(244, 122, 32, 0.08);
  transform: translateY(0);
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.06);
}
.pos-card--out {
  opacity: 0.4;
  cursor: not-allowed;
}

.pos-card__name {
  margin: 0;
  font-weight: 700;
  font-size: 0.92rem;
  line-height: 1.3;
  color: var(--mc-app-heading, #0a0a0c);
}
.pos-card__meta {
  margin: 0;
  font-size: 0.74rem;
  color: var(--mc-app-text-muted, #5c5a56);
}
.pos-card__foot {
  display: flex;
  align-items: flex-end;
  justify-content: space-between;
  gap: 0.5rem;
  margin-top: auto;
  padding-top: 0.35rem;
  border-top: 1px solid var(--mc-app-border-faint, #eceae5);
}
.pos-card__prices {
  display: flex;
  align-items: baseline;
  gap: 0.4rem;
  line-height: 1.1;
}
.pos-card__price {
  font-weight: 800;
  font-size: 1rem;
  color: var(--mc-app-heading, #0a0a0c);
}
.pos-card__price--sale {
  color: #dc2626;
}
.pos-card__price--was {
  font-size: 0.74rem;
  color: var(--mc-app-text-muted, #5c5a56);
  text-decoration: line-through;
}
.pos-card__stock {
  font-size: 0.68rem;
  font-weight: 700;
  color: #2e7d32;
  text-transform: uppercase;
  letter-spacing: 0.04em;
  padding: 0.15rem 0.45rem;
  border-radius: 5px;
  background: rgba(46, 125, 50, 0.1);
  flex-shrink: 0;
  white-space: nowrap;
}
.pos-card__stock--low { color: #e65100; background: rgba(230, 81, 0, 0.1); }
.pos-card__stock--out { color: #c62828; background: rgba(198, 40, 40, 0.1); }

/* ── Cart table ───────────────────────────────────────────────────────── */
.pos-cart-table {
  width: 100%;
  font-size: 0.9rem;
  border-collapse: collapse;
}
.pos-cart-table thead th {
  position: sticky;
  top: 0;
  background: var(--mc-app-surface-2, #faf9f6);
  z-index: 1;
  padding: 0.55rem 0.75rem;
  font-size: 0.72rem;
  text-transform: uppercase;
  letter-spacing: 0.06em;
  color: var(--mc-app-text-muted, #5c5a56);
  border-bottom: 1px solid var(--mc-app-border-faint, #eceae5);
}
.pos-cart-table tbody td {
  padding: 0.65rem 0.75rem;
  border-bottom: 1px solid var(--mc-app-border-faint, #eceae5);
  vertical-align: middle;
}
.pos-cart-table tbody tr:last-child td { border-bottom: none; }
.pos-cart-name {
  max-width: 22rem;
  font-weight: 600;
  display: flex;
  flex-direction: column;
  gap: 0.15rem;
  line-height: 1.25;
}
.pos-cart-name__title {
  font-weight: 700;
  color: var(--mc-app-heading, #0a0a0c);
  font-size: 0.92rem;
}
.pos-cart-name__meta {
  font-size: 0.72rem;
  font-weight: 400;
  color: var(--mc-app-text-muted, #5c5a56);
  letter-spacing: 0.01em;
}
.pos-cart-row--just-added td {
  animation: pos-row-flash 900ms ease-out;
}
@keyframes pos-row-flash {
  0%   { background: rgba(46, 125, 50, 0.22); }
  60%  { background: rgba(46, 125, 50, 0.10); }
  100% { background: transparent; }
}

.pos-stepper {
  display: inline-flex;
  align-items: center;
  border: 1.5px solid var(--mc-app-border-subtle, #c8c5bd);
  border-radius: 10px;
  overflow: hidden;
  background: var(--mc-app-surface, #fff);
}
.pos-stepper__btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-width: 36px;
  min-height: 36px;
  border: none;
  background: var(--mc-app-surface-muted, #f0eeea);
  font-weight: 700;
  color: var(--mc-app-text-secondary, #333336);
  cursor: pointer;
  transition: background 0.12s ease;
}
.pos-stepper__btn:hover { background: var(--mc-app-border-faint, #eceae5); }
.pos-stepper__val {
  min-width: 2.1rem;
  text-align: center;
  font-weight: 700;
  font-size: 0.95rem;
}

.pos-cart-input {
  width: 5rem;
  min-height: 36px;
  padding: 0.3rem 0.5rem;
  border-radius: 8px;
  border: 1.5px solid var(--mc-app-border-subtle, #c8c5bd);
  font-size: 0.88rem;
  box-sizing: border-box;
  transition: border-color 0.15s ease, box-shadow 0.15s ease;
}
.pos-cart-input:focus {
  outline: none;
  border-color: var(--mc-accent, #f47a20);
  box-shadow: inset 0 0 0 1px var(--mc-accent, #f47a20);
}
.pos-disc-group {
  display: flex;
  align-items: center;
  gap: 0.25rem;
}
.pos-disc-mode {
  width: 2.5rem;
  min-height: 36px;
  padding: 0 0.2rem;
  border-radius: 8px;
  border: 1.5px solid var(--mc-app-border-subtle, #c8c5bd);
  font-size: 0.8rem;
  text-align: center;
  background: var(--mc-app-surface-muted, #f0eeea);
  cursor: pointer;
}
.pos-disc-group .pos-cart-input { width: 3.8rem; }
.pos-cart-line-total {
  font-weight: 700;
  white-space: nowrap;
  font-variant-numeric: tabular-nums;
}

/* ── Checkout panel (right column, sticky on desktop) ─────────────────── */
.pos-checkout {
  background: var(--mc-app-surface, #fff);
  border: 1px solid var(--mc-app-border-soft, #ddd9d3);
  border-radius: 14px;
  overflow: hidden;
  box-shadow: 0 4px 16px rgba(0, 0, 0, 0.05);
}
.pos-checkout__head {
  padding: 0.6rem 1rem;
  font-size: 0.78rem;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.08em;
  color: var(--mc-app-text-muted, #5c5a56);
  border-bottom: 1px solid var(--mc-app-border-faint, #eceae5);
  background: var(--mc-app-surface-2, #faf9f6);
}
.pos-checkout__body {
  padding: 0.85rem 1rem 1rem;
  display: flex;
  flex-direction: column;
  gap: 0.85rem;
}
.pos-checkout__group {
  display: flex;
  flex-direction: column;
  gap: 0.6rem;
}
.pos-checkout__group + .pos-checkout__group {
  border-top: 1px solid var(--mc-app-border-faint, #eceae5);
  padding-top: 0.85rem;
}
.pos-checkout__label {
  font-size: 0.7rem;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.07em;
  color: var(--mc-app-text-muted, #5c5a56);
}
.pos-checkout__inline {
  display: flex;
  flex-wrap: wrap;
  gap: 0.65rem 1rem;
  align-items: center;
  justify-content: space-between;
}
.pos-checkout__business {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
  padding-top: 0.25rem;
}
.pos-checkout :deep(input[type='text']),
.pos-checkout :deep(input[type='email']),
.pos-checkout :deep(input[type='number']),
.pos-checkout :deep(select),
.pos-checkout :deep(textarea) {
  width: 100%;
  min-width: 0;
  box-sizing: border-box;
}
.pos-checkout__warn { margin: 0; }

/* Payment method button group */
.pos-pay-group {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 0.4rem;
}
.pos-pay-btn {
  appearance: none;
  border: 1.5px solid var(--mc-app-border-subtle, #c8c5bd);
  background: var(--mc-app-surface, #fff);
  color: var(--mc-app-text-secondary, #333336);
  border-radius: 10px;
  padding: 0.65rem 0.5rem;
  font-size: 0.92rem;
  font-weight: 700;
  letter-spacing: 0.04em;
  cursor: pointer;
  transition: background 0.12s ease, border-color 0.12s ease, color 0.12s ease, box-shadow 0.12s ease;
}
.pos-pay-btn:hover {
  border-color: var(--mc-accent, #f47a20);
  background: rgba(244, 122, 32, 0.05);
}
.pos-pay-btn--on {
  background: var(--mc-accent, #f47a20);
  border-color: var(--mc-accent, #f47a20);
  color: #fff;
  box-shadow: 0 2px 6px rgba(244, 122, 32, 0.35);
}

.pos-email-wrap { position: relative; width: 100%; }
.pos-email-spinner {
  position: absolute;
  right: 8px;
  top: 50%;
  transform: translateY(-50%);
  width: 16px;
  height: 16px;
}
.pos-email-match {
  color: var(--mc-accent, #f47a20);
  font-size: 0.78rem;
}
.btn-link-toggle {
  display: inline-flex;
  align-items: center;
  gap: 0.25rem;
  background: none;
  border: none;
  color: var(--mc-accent, #f47a20);
  cursor: pointer;
  font-size: 0.85rem;
  padding: 0;
  text-decoration: underline;
}

/* ── Recent invoices panel ──────────────────────────────────────────── */
.pos-panel--recent {
  margin-top: 0.5rem;
  background: var(--mc-app-surface, #fff);
  border: 1px solid var(--mc-app-border-soft, #ddd9d3);
  border-radius: 12px;
  overflow: hidden;
}
.pos-panel__head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 0.55rem 0.9rem;
  font-size: 0.82rem;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.03em;
  color: var(--mc-text-muted, #7a7874);
  border-bottom: 1px solid var(--mc-app-border-soft, #ddd9d3);
}
.pos-recent-list {
  display: flex;
  flex-direction: column;
}
.pos-recent-item {
  display: grid;
  grid-template-columns: auto 1fr auto;
  gap: 0.5rem;
  align-items: center;
  padding: 0.55rem 0.9rem;
  font-size: 0.84rem;
  text-decoration: none;
  color: inherit;
  border-bottom: 1px solid var(--mc-app-border-soft, #eceae6);
  transition: background 0.15s ease;
}
.pos-recent-item:last-child { border-bottom: none; }
.pos-recent-item:hover { background: rgba(244, 122, 32, 0.06); }
.pos-recent-item__num {
  font-weight: 600;
  color: var(--mc-accent, #f47a20);
  white-space: nowrap;
}
.pos-recent-item__who {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  color: var(--mc-text-muted, #7a7874);
}
.pos-recent-item__total {
  font-weight: 600;
  font-variant-numeric: tabular-nums;
  white-space: nowrap;
}

/* ── Totals (inside checkout panel) ───────────────────────────────────── */
.pos-totals {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
  padding: 0.7rem 0.85rem 0.85rem;
  background: var(--mc-app-surface-2, #faf9f6);
  border: 1px solid var(--mc-app-border-faint, #eceae5);
  border-radius: 12px;
  font-variant-numeric: tabular-nums;
  transition: box-shadow 0.3s ease;
}
.pos-totals--pulse {
  box-shadow: 0 0 0 2px rgba(244, 122, 32, 0.35);
}
.pos-totals__row {
  display: flex;
  justify-content: space-between;
  align-items: baseline;
  gap: 1rem;
  font-size: 0.88rem;
  color: var(--mc-app-text-secondary, #333336);
}
.pos-totals__row--muted {
  color: var(--mc-app-text-muted, #5c5a56);
  font-size: 0.8rem;
}
.pos-totals__grand {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  gap: 1rem;
  padding-top: 0.55rem;
  margin-top: 0.4rem;
  border-top: 2px solid var(--mc-accent, #f47a20);
}
.pos-totals__grand span {
  font-size: 0.78rem;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.08em;
  color: var(--mc-app-text, #1a1a1c);
}
.pos-totals__grand strong {
  font-size: 1.65rem;
  font-weight: 800;
  color: var(--mc-app-heading, #0a0a0c);
  line-height: 1.05;
}

.pos-checkout-btn {
  min-height: 52px;
  font-size: 1rem;
  letter-spacing: 0.06em;
}

.pos-cart-was {
  display: block;
  font-size: 0.72rem;
  color: var(--mc-app-text-muted, #5c5a56);
  text-decoration: line-through;
  font-weight: 400;
}

/* ── Sale summary modal (unchanged look) ──────────────────────────────── */
.sale-summary__header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 1rem;
}
.sale-summary__invoice { font-weight: 700; font-size: 1.1rem; }
.sale-summary__method {
  font-size: 0.9rem;
  color: var(--mc-app-text-muted, #5c5a56);
  background: var(--mc-app-surface-alt, #f5f4f0);
  padding: 0.2rem 0.6rem;
  border-radius: 4px;
}
.sale-summary__table { margin-bottom: 1rem; }
.sale-summary__detail {
  margin: 0.25rem 0;
  font-size: 0.9rem;
  color: var(--mc-app-text-muted, #5c5a56);
}

/* ── Tablet: stack results above cart ─────────────────────────────────── */
@media (max-width: 1099px) {
  .pos-panel__body--scroll { max-height: 55vh; }
}
@media (max-width: 720px) {
  .pos-pay-group { grid-template-columns: 1fr 1fr 1fr; }
}

@media (prefers-reduced-motion: reduce) {
  .pos-cart-row--just-added td { animation: none; }
  .pos-fade-enter-active,
  .pos-fade-leave-active { transition: none; }
  .pos-totals { transition: none; }
}
</style>
