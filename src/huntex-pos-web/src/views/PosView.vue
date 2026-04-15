<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { http } from '@/api/http'
import { useToast } from '@/composables/useToast'
import { formatZAR } from '@/utils/format'
import BarcodeScanner from '@/components/BarcodeScanner.vue'
import McPageHeader from '@/components/ui/McPageHeader.vue'
import McCard from '@/components/ui/McCard.vue'
import McButton from '@/components/ui/McButton.vue'
import McField from '@/components/ui/McField.vue'
import McAlert from '@/components/ui/McAlert.vue'
import McEmptyState from '@/components/ui/McEmptyState.vue'
import McSkeleton from '@/components/ui/McSkeleton.vue'
import McModal from '@/components/ui/McModal.vue'
import McSpinner from '@/components/ui/McSpinner.vue'
import McCheckbox from '@/components/ui/McCheckbox.vue'
import McBadge from '@/components/ui/McBadge.vue'

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
const paymentMethod = ref('Cash')
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

function getEffectivePrice(p: Product): { price: number; hasDiscount: boolean } {
  if (!activePromo.value) return { price: p.sellPrice, hasDiscount: false }
  const special = activePromo.value.specials.find(s => s.productId === p.id)
  if (special) {
    if (special.specialPrice != null) return { price: special.specialPrice, hasDiscount: special.specialPrice !== p.sellPrice }
    if (special.discountPercent != null) {
      const price = Math.round(p.sellPrice * (1 - special.discountPercent / 100) * 100) / 100
      return { price, hasDiscount: special.discountPercent > 0 }
    }
  }
  if (activePromo.value.siteDiscountPercent > 0) {
    const price = Math.round(p.sellPrice * (1 - activePromo.value.siteDiscountPercent / 100) * 100) / 100
    return { price, hasDiscount: true }
  }
  return { price: p.sellPrice, hasDiscount: false }
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
})

let searchTimer: ReturnType<typeof setTimeout> | null = null
watch(q, () => {
  if (searchTimer) clearTimeout(searchTimer)
  searchTimer = setTimeout(() => void runSearch(), 250)
})

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
    if (existing.qty >= p.qtyOnHand) return
    existing.qty += 1
  } else {
    if (p.qtyOnHand < 1) return
    const { price } = getEffectivePrice(p)
    cart.value.push({ product: p, qty: 1, unitPrice: price, originalPrice: p.sellPrice, lineDiscount: 0, discMode: 'R', discInput: 0 })
  }
}

function onScan(code: string) {
  q.value = code.trim()
  scanOpen.value = false
  void (async () => {
    await runSearch()
    const exact =
      results.value.find((p) => p.barcode === code.trim() || p.sku === code.trim()) ?? results.value[0]
    if (exact) addToCart(exact)
  })()
}

function bumpQty(l: Line, delta: number) {
  const next = l.qty + delta
  if (next < 1) return
  if (next > l.product.qtyOnHand) return
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

const totalCost = computed(() =>
  cart.value.reduce((s, l) => s + (l.product.cost ?? 0) * l.qty, 0)
)

const belowCostWarning = computed(() => {
  if (!isManager.value || cart.value.length === 0) return null
  if (grandPreview.value < totalCost.value && totalCost.value > 0)
    return `Sale total ${formatZAR(grandPreview.value)} is below total cost ${formatZAR(totalCost.value)}`
  return null
})

async function doCheckout() {
  err.value = null
  busy.value = true
  try {
    const { data } = await http.post('/api/invoices', {
      customerName: customerName.value || null,
      customerEmail: customerEmail.value || null,
      customerType: customerType.value || null,
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
    cart.value = []
    discountTotal.value = 0
    let msg = `Invoice ${data.invoiceNumber} — total ${formatZAR(data.grandTotal)}`
    if (data.belowCostWarning) msg += `\n${data.belowCostWarning}`
    toast.success(msg)
    if (data.emailWarning) toast.error(data.emailWarning)
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
  if (belowCostWarning.value) {
    showBelowCostModal.value = true
    return
  }
  void doCheckout()
}

const searchEmpty = computed(() => !q.value.trim())
const searchNoHits = computed(() => !searchLoading.value && q.value.trim() && !results.value.length)
</script>

<template>
  <div class="pos-page">
    <McPageHeader title="Point of sale">
      <template #default>
        <span v-if="posRules && isManager">Manager mode — discounts and price overrides allowed. Below-cost sales are flagged.</span>
        <span v-else-if="posRules">Sales mode — list price only. Ask a manager for overrides.</span>
      </template>
    </McPageHeader>

    <div v-if="activePromo?.promotionName" class="pos-promo-banner">
      <McBadge variant="accent">{{ activePromo.promotionName }}</McBadge>
      <span v-if="activePromo.siteDiscountPercent > 0">{{ activePromo.siteDiscountPercent }}% off all items</span>
      <span v-if="activePromo.specials.length"> · {{ activePromo.specials.length }} product special{{ activePromo.specials.length !== 1 ? 's' : '' }}</span>
    </div>

    <McAlert v-if="err" variant="error">{{ err }}</McAlert>

    <div class="pos-grid">
      <div class="pos-col pos-col--search">
        <McCard title="Find products">
          <div class="pos-scan-row">
            <McButton variant="secondary" type="button" @click="scanOpen = !scanOpen">
              {{ scanOpen ? 'Hide scanner' : 'Scan barcode' }}
            </McButton>
          </div>
          <div v-if="scanOpen" class="pos-scanner-wrap">
            <BarcodeScanner :active="scanOpen" @decode="onScan" />
          </div>
          <McField label="Search" for-id="pos-search">
            <input
              id="pos-search"
              v-model="q"
              type="search"
              autocomplete="off"
              placeholder="SKU, barcode, or name…"
              class="pos-search-input"
            />
          </McField>

          <McSkeleton v-if="searchLoading" :lines="4" />

          <McEmptyState
            v-else-if="searchEmpty"
            title="Search to add items"
            hint="Type a SKU, barcode, or part of the product name. Use the scanner for faster entry at the counter."
          />

          <McEmptyState
            v-else-if="searchNoHits"
            title="No matches"
            hint="Try other words — search matches text anywhere in the name (e.g. hornady 6.5 aero). Words can be in any order."
          />

          <ul v-else class="pos-results">
            <li v-for="p in results" :key="p.id" class="pos-result">
              <div class="pos-result__main">
                <p class="pos-result__name">{{ p.name }}</p>
                <p class="pos-result__meta">
                  <span>{{ p.sku }}</span>
                  <span v-if="p.barcode">· {{ p.barcode }}</span>
                </p>
              </div>
              <div class="pos-result__side">
                <template v-if="getEffectivePrice(p).hasDiscount">
                  <span class="pos-result__price pos-result__price--sale">{{ formatZAR(getEffectivePrice(p).price) }}</span>
                  <span class="pos-result__price--was">{{ formatZAR(p.sellPrice) }}</span>
                </template>
                <span v-else class="pos-result__price">{{ formatZAR(p.sellPrice) }}</span>
                <span class="pos-result__stock" :class="{ 'pos-result__stock--low': p.qtyOnHand <= 3 }">
                  Stock {{ p.qtyOnHand }}
                </span>
                <McButton
                  variant="primary"
                  type="button"
                  :disabled="p.qtyOnHand < 1"
                  @click="addToCart(p)"
                >
                  Add
                </McButton>
              </div>
            </li>
          </ul>
        </McCard>
      </div>

      <div class="pos-col pos-col--cart">
        <McCard title="Cart">
          <McEmptyState
            v-if="!cart.length"
            title="Cart is empty"
            hint="Add products from the search results."
          />
          <div v-else class="pos-cart-table-wrap">
            <table class="pos-cart-table mc-table">
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
                <tr v-for="l in cart" :key="l.product.id">
                  <td class="pos-cart-name">
                    {{ l.product.name }}
                    <span v-if="l.originalPrice !== l.unitPrice" class="pos-cart-was">was {{ formatZAR(l.originalPrice) }}</span>
                  </td>
                  <td>
                    <div class="pos-stepper">
                      <button type="button" class="pos-stepper__btn" aria-label="Decrease" @click="bumpQty(l, -1)">
                        −
                      </button>
                      <span class="pos-stepper__val">{{ l.qty }}</span>
                      <button type="button" class="pos-stepper__btn" aria-label="Increase" @click="bumpQty(l, 1)">
                        +
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
                    <McButton variant="ghost" type="button" @click="removeLine(l)">Remove</McButton>
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
        </McCard>

        <McCard title="Customer &amp; payment">
          <div class="pos-customer-grid">
            <McField label="Customer name" for-id="cust-name">
              <input id="cust-name" v-model="customerName" type="text" autocomplete="name" />
            </McField>
            <McField label="Email (receipt link)" for-id="cust-email">
              <input id="cust-email" v-model="customerEmail" type="email" autocomplete="email" />
            </McField>
            <McField label="Customer type" for-id="cust-type" hint="Optional">
              <input id="cust-type" v-model="customerType" placeholder="e.g. ENT" />
            </McField>
            <McField label="Payment" for-id="pay-meth">
              <select id="pay-meth" v-model="paymentMethod">
                <option>Cash</option>
                <option>Card</option>
                <option>Bank</option>
              </select>
            </McField>
            <McField v-if="isManager" label="Order discount (R)" for-id="order-disc">
              <input id="order-disc" v-model.number="discountTotal" type="number" step="0.01" min="0" />
            </McField>
          </div>
          <McCheckbox v-model="sendEmail" label="Email invoice link" hint="Sends the customer a link to view &amp; download their invoice" />
        </McCard>

        <div class="pos-sticky-foot">
          <div class="pos-totals">
            <div class="pos-totals__row">
              <span>Subtotal</span>
              <strong>{{ formatZAR(subTotal) }}</strong>
            </div>
            <div v-if="isManager && discountTotal > 0" class="pos-totals__row pos-totals__row--muted">
              <span>After order discount</span>
              <strong>{{ formatZAR(grandPreview) }}</strong>
            </div>
            <div class="pos-totals__row pos-totals__grand">
              <span>Total due</span>
              <strong>{{ formatZAR(grandPreview) }}</strong>
            </div>
          </div>
          <McAlert v-if="belowCostWarning" variant="warning">{{ belowCostWarning }}</McAlert>
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
    </div>

    <McModal v-model="showBelowCostModal" title="Below cost">
      <p>{{ belowCostWarning }}</p>
      <p class="mc-text-muted" style="margin-bottom: 0; font-size: 0.9rem">Continue only if you intend to approve this sale.</p>
      <template #footer>
        <McButton variant="secondary" type="button" @click="showBelowCostModal = false">Cancel</McButton>
        <McButton variant="primary" type="button" :disabled="busy" @click="doCheckout">Proceed</McButton>
      </template>
    </McModal>
  </div>
</template>

<style scoped>
.pos-page {
  max-width: 100%;
}

.pos-col {
  min-width: 0;
  max-width: 100%;
}

.pos-grid {
  display: grid;
  grid-template-columns: 1fr;
  gap: 1.5rem;
  align-items: start;
  max-width: 100%;
}

@media (min-width: 1200px) {
  .pos-grid {
    grid-template-columns: 1fr min(440px, 36vw);
  }
}

.pos-scan-row {
  margin-bottom: 1.25rem;
}

.pos-scanner-wrap {
  margin-bottom: 1.25rem;
  padding: 1rem;
  background: var(--mc-app-surface-2, #f9f8f6);
  border-radius: 12px;
  border: 1px solid var(--mc-app-border-faint, #eceae5);
}

.pos-search-input {
  width: 100%;
  min-height: 50px;
  font-size: 1.05rem;
  box-sizing: border-box;
}

.pos-results {
  list-style: none;
  margin: 0;
  padding: 0;
}

.pos-result {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
  padding: 1rem 0;
  border-bottom: 1px solid var(--mc-app-border-faint, #eceae5);
}

@media (min-width: 480px) {
  .pos-result {
    flex-direction: row;
    flex-wrap: wrap;
    align-items: center;
    justify-content: space-between;
    gap: 0.75rem;
  }
}

.pos-result:last-child {
  border-bottom: none;
}

.pos-result__name {
  margin: 0 0 0.25rem;
  font-weight: 600;
  color: var(--mc-app-text, #1a1a1c);
  font-size: 1rem;
}

.pos-result__meta {
  margin: 0;
  font-size: 0.85rem;
  color: var(--mc-app-text-muted, #5c5a56);
  font-weight: 500;
}

.pos-result__side {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.75rem;
}

.pos-result__price {
  font-weight: 700;
  font-size: 1.05rem;
  color: var(--mc-app-text, #1a1a1c);
}

.pos-result__stock {
  font-size: 0.78rem;
  font-weight: 700;
  color: #2e7d32;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  padding: 0.2rem 0.55rem;
  border-radius: 6px;
  background: rgba(46, 125, 50, 0.08);
}

.pos-result__stock--low {
  color: #e65100;
  background: rgba(230, 81, 0, 0.08);
}

.pos-cart-table-wrap {
  overflow-x: auto;
}

.pos-cart-table {
  width: 100%;
  font-size: 0.85rem;
}

@media (min-width: 480px) {
  .pos-cart-table {
    font-size: 0.9rem;
  }
}

.pos-cart-name {
  max-width: 12rem;
  font-weight: 600;
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
  min-width: 38px;
  min-height: 38px;
  border: none;
  background: var(--mc-app-surface-muted, #f0eeea);
  font-size: 1.1rem;
  font-weight: 700;
  color: var(--mc-app-text-secondary, #333336);
  cursor: pointer;
  transition: background 0.12s ease;
}

@media (min-width: 480px) {
  .pos-stepper__btn {
    min-width: 44px;
    min-height: 44px;
    font-size: 1.25rem;
  }
}

.pos-stepper__btn:hover {
  background: var(--mc-app-border-faint, #eceae5);
}

.pos-stepper__val {
  min-width: 2.25rem;
  text-align: center;
  font-weight: 700;
  font-size: 0.95rem;
}

.pos-cart-input {
  width: 4.5rem;
  min-height: 38px;
  padding: 0.3rem 0.5rem;
  border-radius: 8px;
  border: 1.5px solid var(--mc-app-border-subtle, #c8c5bd);
  font-size: 0.85rem;
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
  min-height: 38px;
  padding: 0 0.2rem;
  border-radius: 8px;
  border: 1.5px solid var(--mc-app-border-subtle, #c8c5bd);
  font-size: 0.8rem;
  text-align: center;
  background: var(--mc-app-surface-muted, #f0eeea);
  cursor: pointer;
}

.pos-disc-group .pos-cart-input {
  width: 3.5rem;
}

@media (min-width: 480px) {
  .pos-cart-input {
    width: 5.5rem;
    min-height: 44px;
    padding: 0.35rem 0.55rem;
    font-size: inherit;
  }
  .pos-disc-group .pos-cart-input {
    width: 4rem;
  }
  .pos-disc-mode {
    min-height: 44px;
    font-size: 0.85rem;
  }
}

.pos-cart-line-total {
  font-weight: 700;
  white-space: nowrap;
}

.pos-customer-grid {
  display: grid;
  grid-template-columns: 1fr;
  gap: 0;
  max-width: 100%;
}

@media (min-width: 480px) {
  .pos-customer-grid {
    grid-template-columns: repeat(auto-fill, minmax(200px, 1fr));
    gap: 0 1.25rem;
  }
}

.pos-sticky-foot {
  position: sticky;
  bottom: 0;
  z-index: 5;
  margin-top: 0.75rem;
  padding: 1rem 0 0.25rem;
  background: linear-gradient(180deg, transparent 0%, var(--mc-app-page-bg, #eae8e3) 18%);
}

.pos-totals {
  background: var(--mc-app-surface, #fff);
  border: 1px solid var(--mc-app-border-soft, #ddd9d3);
  border-radius: var(--mc-app-radius-card, 18px);
  padding: 1.15rem 1.5rem;
  margin-bottom: 0.85rem;
  box-shadow: var(--mc-app-shadow-md, 0 8px 32px rgba(0, 0, 0, 0.12));
}

@media (max-width: 479px) {
  .pos-totals {
    padding: 0.85rem 1rem;
  }
}

.pos-totals__row {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 0.35rem 0;
  font-size: 0.95rem;
  color: var(--mc-app-text-secondary, #333336);
}

.pos-totals__row--muted {
  color: var(--mc-app-text-muted, #5c5a56);
  font-size: 0.88rem;
  font-weight: 500;
}

.pos-totals__grand {
  margin-top: 0.5rem;
  padding-top: 0.65rem;
  border-top: 3px solid var(--mc-accent, #f47a20);
  font-size: 1.35rem;
  font-weight: 700;
  color: var(--mc-app-heading, #0a0a0c);
}

.pos-checkout-btn {
  min-height: 58px;
  font-size: 1rem;
  letter-spacing: 0.06em;
}

/* Promotion banner */
.pos-promo-banner {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  padding: 0.6rem 1rem;
  background: rgba(244, 122, 32, 0.08);
  border: 1px solid rgba(244, 122, 32, 0.25);
  border-radius: var(--mc-app-radius-card, 18px);
  margin-bottom: 0.75rem;
  font-size: 0.9rem;
  font-weight: 500;
  color: var(--mc-app-text-secondary, #333336);
}

/* Sale pricing in search results */
.pos-result__price--sale {
  color: #dc2626;
  font-weight: 700;
}

.pos-result__price--was {
  font-size: 0.78rem;
  color: var(--mc-app-text-muted, #5c5a56);
  text-decoration: line-through;
}

.pos-cart-was {
  display: block;
  font-size: 0.75rem;
  color: var(--mc-app-text-muted, #5c5a56);
  text-decoration: line-through;
  font-weight: 400;
}
</style>
