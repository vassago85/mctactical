<script setup lang="ts">
import { computed, nextTick, onMounted, ref, watch } from 'vue'
import { http } from '@/api/http'
import { useAuthStore } from '@/stores/auth'
import { useToast } from '@/composables/useToast'
import { formatZAR } from '@/utils/format'
import McPageHeader from '@/components/ui/McPageHeader.vue'
import McCard from '@/components/ui/McCard.vue'
import McButton from '@/components/ui/McButton.vue'
import McField from '@/components/ui/McField.vue'
import McAlert from '@/components/ui/McAlert.vue'
import McBadge from '@/components/ui/McBadge.vue'
import McSpinner from '@/components/ui/McSpinner.vue'
import McEmptyState from '@/components/ui/McEmptyState.vue'
import McModal from '@/components/ui/McModal.vue'
import McCheckbox from '@/components/ui/McCheckbox.vue'

type Supplier = { id: string; name: string }

type Product = {
  id: string
  sku: string
  barcode?: string | null
  name: string
  category?: string | null
  manufacturer?: string | null
  itemType?: string | null
  supplierId?: string | null
  supplierName?: string | null
  cost?: number | null
  sellPrice: number
  qtyOnHand: number
  qtyConsignment: number
  active: boolean
  warning?: string | null
  specialPrice?: number | null
  specialLabel?: string | null
}

type Page = { total: number; skip: number; take: number; items: Product[] }

type StockReceipt = {
  id: string
  productId: string
  supplierId?: string | null
  supplierName?: string | null
  type: string
  quantity: number
  notes?: string | null
  processedBy?: string | null
  createdAt: string
}

type ConsignmentSummaryLine = {
  supplierId: string
  supplierName: string
  totalIn: number
  totalMovedToStock: number
  totalReturned: number
  onHand: number
}

const auth = useAuthStore()
const toast = useToast()
const q = ref('')
const includeInactive = ref(false)
const skip = ref(0)
const pageSize = ref(500)
const page = ref<Page | null>(null)
const busy = ref(false)
const err = ref<string | null>(null)
const suppliers = ref<Supplier[]>([])

const canManage = computed(() => auth.hasRole('Admin', 'Owner', 'Dev'))
const canExport = canManage
const pageLabel = computed(() => {
  if (!page.value) return ''
  const from = page.value.total === 0 ? 0 : page.value.skip + 1
  const to = Math.min(page.value.skip + page.value.items.length, page.value.total)
  return `${from}–${to} of ${page.value.total}`
})

let debounce: ReturnType<typeof setTimeout> | null = null
watch([q, includeInactive], () => {
  skip.value = 0
  if (debounce) clearTimeout(debounce)
  debounce = setTimeout(() => void load(), 300)
})

watch([skip, pageSize], () => void load())
watch(pageSize, () => {
  skip.value = 0
})

async function loadSuppliers() {
  try {
    const { data } = await http.get<Supplier[]>('/api/suppliers')
    suppliers.value = data
  } catch { /* non-critical */ }
}

async function load() {
  err.value = null
  busy.value = true
  try {
    const { data } = await http.get<Page>('/api/products/stocklist', {
      params: {
        q: q.value.trim() || undefined,
        includeInactive: includeInactive.value,
        skip: skip.value,
        take: pageSize.value
      }
    })
    page.value = data
  } catch {
    err.value = 'Could not load stock list'
    page.value = null
  } finally {
    busy.value = false
  }
}

function nextPage() {
  if (!page.value) return
  if (skip.value + page.value.take < page.value.total) skip.value += page.value.take
}

function prevPage() {
  skip.value = Math.max(0, skip.value - pageSize.value)
}

async function exportCsv() {
  try {
    const params: Record<string, string | boolean> = { includeInactive: includeInactive.value }
    if (q.value.trim()) params.q = q.value.trim()
    const res = await http.get('/api/products/stocklist/export', { params, responseType: 'blob' })
    const url = URL.createObjectURL(res.data)
    const a = document.createElement('a')
    a.href = url
    a.download = 'stocklist.csv'
    a.click()
    URL.revokeObjectURL(url)
    toast.success('CSV downloaded')
  } catch {
    toast.error('Export failed')
  }
}

/* ── Product add/edit drawer ── */

const showForm = ref(false)
const editId = ref<string | null>(null)
const form = ref({
  sku: '',
  barcode: '',
  name: '',
  category: '',
  manufacturer: '',
  itemType: '',
  supplierId: '' as string,
  cost: 0,
  sellPrice: 0,
  qtyOnHand: 0
})
const formBusy = ref(false)
const formErr = ref<string | null>(null)
const sellPriceManual = ref(false)
let computeTimer: ReturnType<typeof setTimeout> | null = null

function closeDrawer() {
  showForm.value = false
}

function openAdd() {
  editId.value = null
  sellPriceManual.value = false
  form.value = { sku: '', barcode: '', name: '', category: '', manufacturer: '', itemType: '', supplierId: '', cost: 0, sellPrice: 0, qtyOnHand: 0 }
  formErr.value = null
  showForm.value = true
}

function openEdit(p: Product) {
  editId.value = p.id
  sellPriceManual.value = true
  form.value = {
    sku: p.sku,
    barcode: p.barcode ?? '',
    name: p.name,
    category: p.category ?? '',
    manufacturer: p.manufacturer ?? '',
    itemType: p.itemType ?? '',
    supplierId: p.supplierId ?? '',
    cost: p.cost ?? 0,
    sellPrice: p.sellPrice,
    qtyOnHand: p.qtyOnHand
  }
  formErr.value = null
  showForm.value = true
}

async function computeSellFromCost(cost: number) {
  if (!cost || cost <= 0) { form.value.sellPrice = 0; return }
  try {
    const { data } = await http.get<{ sellPrice: number }>('/api/settings/pricing/compute-sell', { params: { cost } })
    if (!sellPriceManual.value) form.value.sellPrice = data.sellPrice
  } catch { /* keep current value */ }
}

watch(() => form.value.cost, (cost) => {
  if (sellPriceManual.value) return
  if (computeTimer) clearTimeout(computeTimer)
  if (!cost || cost <= 0) { form.value.sellPrice = 0; return }
  computeTimer = setTimeout(() => computeSellFromCost(cost), 300)
})

async function saveProduct() {
  formErr.value = null
  formBusy.value = true
  try {
    const payload = {
      sku: form.value.sku,
      barcode: form.value.barcode || null,
      name: form.value.name,
      category: form.value.category || null,
      manufacturer: form.value.manufacturer || null,
      itemType: form.value.itemType || null,
      supplierId: form.value.supplierId || null,
      cost: form.value.cost,
      sellPrice: form.value.sellPrice,
      qtyOnHand: form.value.qtyOnHand
    }
    if (editId.value) {
      await http.put(`/api/products/${editId.value}`, payload)
      toast.success('Product updated')
    } else {
      await http.post('/api/products', payload)
      toast.success('Product created')
    }
    showForm.value = false
    await load()
  } catch (e: unknown) {
    const ax = e as { response?: { data?: { error?: string } } }
    formErr.value = ax.response?.data?.error ?? 'Save failed'
  } finally {
    formBusy.value = false
  }
}

async function toggleActive(p: Product) {
  err.value = null
  try {
    await http.put(`/api/products/${p.id}`, { active: !p.active })
    toast.success(p.active ? 'Product deactivated' : 'Product activated')
    await load()
  } catch {
    err.value = 'Update failed'
    toast.error('Update failed')
  }
}

/* ── Stock receipt modal (receive / move / return) ── */

const showReceiptModal = ref(false)
const receiptProduct = ref<Product | null>(null)
const receiptType = ref<'OwnedIn' | 'ConsignmentIn' | 'ConsignmentToStock' | 'ConsignmentReturn'>('OwnedIn')
const receiptSupplierId = ref('')
const receiptQty = ref(1)
const receiptNotes = ref('')
const receiptBusy = ref(false)
const receiptErr = ref<string | null>(null)
const consignmentSummary = ref<ConsignmentSummaryLine[]>([])

function openReceiptModal(p: Product, type: typeof receiptType.value) {
  receiptProduct.value = p
  receiptType.value = type
  receiptSupplierId.value = ''
  receiptQty.value = 1
  receiptNotes.value = ''
  receiptErr.value = null
  consignmentSummary.value = []
  showReceiptModal.value = true
  if (type === 'ConsignmentToStock' || type === 'ConsignmentReturn') {
    void loadConsignmentSummary(p.id)
  }
}

async function loadConsignmentSummary(productId: string) {
  try {
    const { data } = await http.get<ConsignmentSummaryLine[]>(`/api/products/${productId}/consignment-summary`)
    consignmentSummary.value = data.filter(s => s.onHand > 0)
    if (consignmentSummary.value.length === 1) {
      receiptSupplierId.value = consignmentSummary.value[0].supplierId
    }
  } catch { /* non-critical */ }
}

const receiptMaxQty = computed(() => {
  if (receiptType.value !== 'ConsignmentToStock' && receiptType.value !== 'ConsignmentReturn') return 999999
  const line = consignmentSummary.value.find(s => s.supplierId === receiptSupplierId.value)
  return line?.onHand ?? 0
})

const receiptTypeLabel = computed(() => {
  switch (receiptType.value) {
    case 'OwnedIn': return 'Receive owned stock'
    case 'ConsignmentIn': return 'Receive consignment'
    case 'ConsignmentToStock': return 'Move to owned stock'
    case 'ConsignmentReturn': return 'Return to supplier'
    default: return 'Stock movement'
  }
})

async function submitReceipt() {
  receiptErr.value = null
  if (!receiptProduct.value) return
  if (receiptType.value !== 'OwnedIn' && !receiptSupplierId.value) {
    receiptErr.value = 'Please select a supplier.'
    return
  }
  receiptBusy.value = true
  try {
    await http.post(`/api/products/${receiptProduct.value.id}/stock-receipts`, {
      type: receiptType.value,
      supplierId: receiptSupplierId.value || null,
      quantity: receiptQty.value,
      notes: receiptNotes.value || null
    })
    toast.success(`${receiptTypeLabel.value}: ${receiptQty.value} units`)
    showReceiptModal.value = false
    await load()
  } catch (e: unknown) {
    const ax = e as { response?: { data?: { error?: string } } }
    receiptErr.value = ax.response?.data?.error ?? 'Operation failed'
  } finally {
    receiptBusy.value = false
  }
}

/* ── Stock history drawer ── */

const showHistory = ref(false)
const historyProduct = ref<Product | null>(null)
const historyReceipts = ref<StockReceipt[]>([])
const historyBusy = ref(false)

async function openHistory(p: Product) {
  historyProduct.value = p
  historyReceipts.value = []
  historyBusy.value = true
  showHistory.value = true
  try {
    const { data } = await http.get<StockReceipt[]>(`/api/products/${p.id}/stock-receipts`)
    historyReceipts.value = data
  } catch {
    toast.error('Could not load history')
  } finally {
    historyBusy.value = false
  }
}

function receiptTypeBadge(type: string): { label: string; variant: 'success' | 'neutral' | 'warning' | 'error' } {
  switch (type) {
    case 'OwnedIn': return { label: 'Received (owned)', variant: 'success' }
    case 'ConsignmentIn': return { label: 'Received (consignment)', variant: 'warning' }
    case 'ConsignmentToStock': return { label: 'Moved to stock', variant: 'success' }
    case 'ConsignmentReturn': return { label: 'Returned', variant: 'error' }
    default: return { label: type, variant: 'neutral' }
  }
}

function formatDate(iso: string) {
  return new Date(iso).toLocaleDateString('en-ZA', { day: 'numeric', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit' })
}

/* ── Per-product specials (standalone) ── */

type ProductSpecialItem = {
  id: string; productId: string; promotionId?: string | null; promotionName?: string | null
  specialPrice?: number | null; discountPercent?: number | null; effectivePrice: number
  isActive: boolean; baseSellPrice: number; productSku: string; productName: string
}

const showSpecialModal = ref(false)
const specialProduct = ref<Product | null>(null)
const existingSpecials = ref<ProductSpecialItem[]>([])
const specialBusy = ref(false)
const specialForm = ref({ usePrice: true, specialPrice: 0, discountPercent: 0 })
const specialFormErr = ref<string | null>(null)
const specialFormBusy = ref(false)

async function openSpecialModal(p: Product) {
  specialProduct.value = p
  specialForm.value = { usePrice: true, specialPrice: p.sellPrice, discountPercent: 10 }
  specialFormErr.value = null
  existingSpecials.value = []
  specialBusy.value = true
  showSpecialModal.value = true
  try {
    const { data } = await http.get<ProductSpecialItem[]>(`/api/products/${p.id}/specials`)
    existingSpecials.value = data
  } catch { /* may not exist yet */ }
  specialBusy.value = false
}

async function saveStandaloneSpecial() {
  specialFormErr.value = null
  if (!specialProduct.value) return
  specialFormBusy.value = true
  try {
    await http.post('/api/promotions/specials', {
      productId: specialProduct.value.id,
      promotionId: null,
      specialPrice: specialForm.value.usePrice ? specialForm.value.specialPrice : null,
      discountPercent: !specialForm.value.usePrice ? specialForm.value.discountPercent : null,
      isActive: true
    })
    toast.success('Special saved')
    await openSpecialModal(specialProduct.value)
  } catch (e: unknown) {
    const ax = e as { response?: { data?: { error?: string } } }
    specialFormErr.value = ax.response?.data?.error ?? 'Save failed'
  } finally {
    specialFormBusy.value = false
  }
}

async function removeSpecial(s: ProductSpecialItem) {
  try {
    await http.delete(`/api/promotions/specials/${s.id}`)
    toast.success('Special removed')
    if (specialProduct.value) await openSpecialModal(specialProduct.value)
  } catch { toast.error('Delete failed') }
}

/* ── Label printing (Brother QL-800) ── */

const showLabelModal = ref(false)
const labelProduct = ref<Product | null>(null)
const labelCopies = ref(1)
const labelUsePromo = ref(false)
const labelBusy = ref(false)

function openLabelModal(p: Product) {
  labelProduct.value = p
  labelCopies.value = 1
  labelUsePromo.value = false
  showLabelModal.value = true
}

async function printLabel() {
  if (!labelProduct.value) return
  labelBusy.value = true
  try {
    const resp = await http.get(`/api/products/${labelProduct.value.id}/label`, {
      params: { copies: labelCopies.value, promo: labelUsePromo.value },
      responseType: 'blob'
    })
    const url = URL.createObjectURL(new Blob([resp.data], { type: 'application/pdf' }))
    const win = window.open(url, '_blank')
    if (win) {
      win.addEventListener('load', () => { win.print() })
    }
    showLabelModal.value = false
  } catch {
    toast.error('Label generation failed')
  } finally {
    labelBusy.value = false
  }
}

onMounted(() => {
  void load()
  void loadSuppliers()
})
</script>

<template>
  <div class="stock-page">
    <McPageHeader title="Stock list" description="Full inventory. Use Import to load items from your Huntex workbook or CSV.">
      <template v-if="canManage" #actions>
        <McButton variant="primary" type="button" @click="openAdd">Add product</McButton>
        <McButton v-if="canExport" variant="secondary" type="button" @click="exportCsv">Export CSV</McButton>
      </template>
    </McPageHeader>

    <McAlert v-if="err" variant="error">{{ err }}</McAlert>

    <McCard :padded="false" title="Filters">
      <div class="stock-toolbar">
        <div class="stock-toolbar__search">
          <McField label="Search" for-id="stock-q">
            <input
              id="stock-q"
              v-model="q"
              type="search"
              placeholder="Any words, any order — matches name, SKU, barcode, category, supplier…"
            />
          </McField>
        </div>
        <label class="stock-toolbar__check">
          <input v-model="includeInactive" type="checkbox" />
          Include inactive
        </label>
        <div class="stock-toolbar__page">
          <span class="stock-toolbar__label">Rows</span>
          <select v-model.number="pageSize" class="stock-toolbar__select">
            <option :value="100">100</option>
            <option :value="250">250</option>
            <option :value="500">500</option>
            <option :value="1000">1000</option>
            <option :value="5000">5000</option>
          </select>
        </div>
        <div class="stock-toolbar__nav">
          <McButton variant="secondary" type="button" :disabled="busy || skip <= 0" @click="prevPage">Previous</McButton>
          <McButton
            variant="secondary"
            type="button"
            :disabled="busy || !page || skip + page.take >= page.total"
            @click="nextPage"
          >
            Next
          </McButton>
          <span class="stock-toolbar__meta">{{ pageLabel }}</span>
          <McSpinner v-if="busy" />
        </div>
      </div>
    </McCard>

    <McCard :padded="false" title="Products">
      <div class="stock-table-wrap">
        <table v-if="page?.items.length" class="stock-table mc-table">
          <thead>
            <tr>
              <th>SKU</th>
              <th>Barcode</th>
              <th>Name</th>
              <th>Mfr</th>
              <th>Type</th>
              <th>Category</th>
              <th>Supplier</th>
              <th>Cost</th>
              <th>Sell</th>
              <th>Owned</th>
              <th>Consign</th>
              <th>Status</th>
              <th v-if="canManage"></th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="p in page.items" :key="p.id">
              <td class="stock-mono">{{ p.sku }}</td>
              <td class="stock-mono">{{ p.barcode ?? '—' }}</td>
              <td class="stock-name">{{ p.name }}</td>
              <td>{{ p.manufacturer ?? '—' }}</td>
              <td>{{ p.itemType ?? '—' }}</td>
              <td>{{ p.category ?? '—' }}</td>
              <td>{{ p.supplierName ?? '—' }}</td>
              <td>{{ p.cost != null ? formatZAR(p.cost) : '—' }}</td>
              <td :class="{ 'stock-warn': !!p.warning }">
                <template v-if="p.specialPrice != null && p.specialPrice !== p.sellPrice">
                  <span class="stock-special-price">{{ formatZAR(p.specialPrice) }}</span>
                  <span class="stock-was-price">{{ formatZAR(p.sellPrice) }}</span>
                  <span class="stock-special-label">{{ p.specialLabel }}</span>
                </template>
                <template v-else>
                  {{ formatZAR(p.sellPrice) }}
                </template>
                <span v-if="p.warning" :title="p.warning" class="stock-warn-icon">⚠</span>
              </td>
              <td>
                <strong :class="{ 'stock-qty--low': p.qtyOnHand <= 3 }">{{ p.qtyOnHand }}</strong>
              </td>
              <td>
                <strong v-if="p.qtyConsignment > 0" class="stock-qty--consign">{{ p.qtyConsignment }}</strong>
                <span v-else class="stock-qty--none">—</span>
              </td>
              <td>
                <McBadge :variant="p.active ? 'success' : 'neutral'">{{ p.active ? 'Active' : 'Inactive' }}</McBadge>
              </td>
              <td v-if="canManage" class="stock-actions">
                <McButton variant="secondary" dense type="button" @click="openEdit(p)">Edit</McButton>
                <McButton variant="secondary" dense type="button" @click="openReceiptModal(p, 'OwnedIn')">+ Owned</McButton>
                <McButton variant="secondary" dense type="button" @click="openReceiptModal(p, 'ConsignmentIn')">+ Consign</McButton>
                <McButton v-if="p.qtyConsignment > 0" variant="secondary" dense type="button" @click="openReceiptModal(p, 'ConsignmentToStock')">Move</McButton>
                <McButton v-if="p.qtyConsignment > 0" variant="ghost" dense type="button" @click="openReceiptModal(p, 'ConsignmentReturn')">Return</McButton>
                <McButton variant="secondary" dense type="button" @click="openSpecialModal(p)">Special</McButton>
                <McButton variant="secondary" dense type="button" @click="openLabelModal(p)">Label</McButton>
                <McButton variant="ghost" dense type="button" @click="openHistory(p)">History</McButton>
                <McButton variant="ghost" dense type="button" @click="toggleActive(p)">
                  {{ p.active ? 'Deactivate' : 'Activate' }}
                </McButton>
              </td>
            </tr>
          </tbody>
        </table>
        <McEmptyState
          v-else-if="page && !busy"
          title="No products in this view"
          hint="Try clearing the search filter or import stock from the Import page."
        />
        <div v-else-if="busy" class="stock-loading">
          <McSpinner />
          <span>Loading…</span>
        </div>
      </div>
    </McCard>

    <Teleport to="body">
      <Transition name="stock-drawer-fade">
        <div v-if="showForm" class="stock-drawer-overlay" aria-hidden="true" @click.self="closeDrawer" />
      </Transition>
      <Transition name="stock-drawer-slide">
        <aside v-if="showForm" class="stock-drawer" role="dialog" aria-labelledby="stock-drawer-title">
          <header class="stock-drawer__head">
            <h2 id="stock-drawer-title" class="stock-drawer__title">{{ editId ? 'Edit product' : 'Add product' }}</h2>
            <button type="button" class="stock-drawer__close" aria-label="Close" @click="closeDrawer">×</button>
          </header>
          <div class="stock-drawer__body">
            <McAlert v-if="formErr" variant="error">{{ formErr }}</McAlert>
            <div class="stock-drawer__grid">
              <McField label="SKU" for-id="f-sku">
                <input id="f-sku" v-model="form.sku" required />
              </McField>
              <McField label="Barcode" for-id="f-bc">
                <input id="f-bc" v-model="form.barcode" />
              </McField>
            </div>
            <McField label="Name" for-id="f-name">
              <input id="f-name" v-model="form.name" required />
            </McField>
            <div class="stock-drawer__grid">
              <McField label="Manufacturer" for-id="f-mfr">
                <input id="f-mfr" v-model="form.manufacturer" placeholder="e.g. Hornady" />
              </McField>
              <McField label="Item type" for-id="f-type">
                <input id="f-type" v-model="form.itemType" placeholder="e.g. Bullet, Cap" />
              </McField>
            </div>
            <div class="stock-drawer__grid">
              <McField label="Category" for-id="f-cat">
                <input id="f-cat" v-model="form.category" />
              </McField>
              <McField label="Supplier" for-id="f-supplier">
                <select id="f-supplier" v-model="form.supplierId">
                  <option value="">— None —</option>
                  <option v-for="s in suppliers" :key="s.id" :value="s.id">{{ s.name }}</option>
                </select>
              </McField>
            </div>
            <div class="stock-drawer__grid">
              <McField label="Cost ex VAT (R)" for-id="f-cost">
                <input id="f-cost" v-model.number="form.cost" type="number" step="0.01" min="0" />
              </McField>
              <McField label="Sell price (R)" for-id="f-sell" :hint="!editId && !sellPriceManual ? 'Auto-calculated from cost' : ''">
                <input id="f-sell" v-model.number="form.sellPrice" type="number" step="0.01" min="0" @input="sellPriceManual = true" />
              </McField>
              <McField label="Qty on hand" for-id="f-qty">
                <input id="f-qty" v-model.number="form.qtyOnHand" type="number" step="1" min="0" />
              </McField>
            </div>
          </div>
          <footer class="stock-drawer__foot">
            <McButton variant="secondary" type="button" @click="closeDrawer">Cancel</McButton>
            <McButton variant="primary" type="button" :disabled="formBusy" @click="saveProduct">
              {{ editId ? 'Save changes' : 'Create' }}
            </McButton>
          </footer>
        </aside>
      </Transition>
    </Teleport>
    <!-- Stock receipt modal -->
    <McModal v-model="showReceiptModal" :title="receiptTypeLabel">
      <McAlert v-if="receiptErr" variant="error">{{ receiptErr }}</McAlert>
      <p class="receipt-product-name">{{ receiptProduct?.name }}</p>

      <McField
        v-if="receiptType !== 'OwnedIn'"
        label="Supplier"
        for-id="r-supplier"
      >
        <select id="r-supplier" v-model="receiptSupplierId" required>
          <option value="" disabled>Select supplier…</option>
          <template v-if="receiptType === 'ConsignmentToStock' || receiptType === 'ConsignmentReturn'">
            <option v-for="s in consignmentSummary" :key="s.supplierId" :value="s.supplierId">
              {{ s.supplierName }} ({{ s.onHand }} on hand)
            </option>
          </template>
          <template v-else>
            <option v-for="s in suppliers" :key="s.id" :value="s.id">{{ s.name }}</option>
          </template>
        </select>
      </McField>

      <McField v-if="receiptType === 'OwnedIn'" label="Supplier (optional)" for-id="r-supplier-opt">
        <select id="r-supplier-opt" v-model="receiptSupplierId">
          <option value="">— None —</option>
          <option v-for="s in suppliers" :key="s.id" :value="s.id">{{ s.name }}</option>
        </select>
      </McField>

      <McField label="Quantity" for-id="r-qty">
        <input id="r-qty" v-model.number="receiptQty" type="number" min="1" :max="receiptMaxQty" step="1" required />
      </McField>
      <p v-if="(receiptType === 'ConsignmentToStock' || receiptType === 'ConsignmentReturn') && receiptSupplierId" class="receipt-max-hint">
        Max: {{ receiptMaxQty }} units from this supplier
      </p>

      <McField label="Notes (optional)" for-id="r-notes">
        <input id="r-notes" v-model="receiptNotes" />
      </McField>

      <template #footer>
        <McButton variant="secondary" type="button" @click="showReceiptModal = false">Cancel</McButton>
        <McButton variant="primary" type="button" :disabled="receiptBusy || receiptQty < 1" @click="submitReceipt">
          <McSpinner v-if="receiptBusy" />
          <span v-else>Confirm</span>
        </McButton>
      </template>
    </McModal>

    <!-- Stock history drawer -->
    <Teleport to="body">
      <Transition name="stock-drawer-fade">
        <div v-if="showHistory" class="stock-drawer-overlay" aria-hidden="true" @click.self="showHistory = false" />
      </Transition>
      <Transition name="stock-drawer-slide">
        <aside v-if="showHistory" class="stock-drawer stock-drawer--wide" role="dialog" aria-labelledby="history-title">
          <header class="stock-drawer__head">
            <h2 id="history-title" class="stock-drawer__title">Stock history — {{ historyProduct?.name }}</h2>
            <button type="button" class="stock-drawer__close" aria-label="Close" @click="showHistory = false">×</button>
          </header>
          <div class="stock-drawer__body">
            <div v-if="historyBusy" class="stock-loading">
              <McSpinner /><span>Loading…</span>
            </div>
            <McEmptyState v-else-if="!historyReceipts.length" title="No stock movements" hint="Use the Receive, Move, or Return actions to create entries." />
            <table v-else class="mc-table history-table">
              <thead>
                <tr>
                  <th>Date</th>
                  <th>Type</th>
                  <th>Supplier</th>
                  <th>Qty</th>
                  <th>Notes</th>
                  <th>User</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="r in historyReceipts" :key="r.id">
                  <td class="stock-mono">{{ formatDate(r.createdAt) }}</td>
                  <td><McBadge :variant="receiptTypeBadge(r.type).variant">{{ receiptTypeBadge(r.type).label }}</McBadge></td>
                  <td>{{ r.supplierName ?? '—' }}</td>
                  <td><strong>{{ r.quantity }}</strong></td>
                  <td>{{ r.notes ?? '—' }}</td>
                  <td>{{ r.processedBy ?? '—' }}</td>
                </tr>
              </tbody>
            </table>
          </div>
        </aside>
      </Transition>
    </Teleport>

    <!-- Label print modal -->
    <McModal v-model="showLabelModal" title="Print product label">
      <p style="margin:0 0 0.75rem;font-size:0.9rem">
        <strong>{{ labelProduct?.name }}</strong><br />
        <span style="color:var(--mc-app-text-muted)">{{ labelProduct?.sku }}</span>
        <span style="margin-left:0.5rem">{{ labelProduct ? formatZAR(labelProduct.sellPrice) : '' }}</span>
      </p>
      <McField label="Number of copies" for-id="lbl-copies" hint="Brother QL-800 · DK-22205 62mm">
        <input id="lbl-copies" v-model.number="labelCopies" type="number" min="1" max="50" step="1" />
      </McField>
      <McCheckbox v-model="labelUsePromo" label="Apply active promotion / specials pricing" hint="Shows the discounted price with the original crossed out, and the promotion name" />
      <template #footer>
        <McButton variant="secondary" type="button" @click="showLabelModal = false">Cancel</McButton>
        <McButton variant="primary" type="button" :disabled="labelBusy" @click="printLabel">
          <McSpinner v-if="labelBusy" />
          <span v-else>Print</span>
        </McButton>
      </template>
    </McModal>

    <!-- Per-product specials modal -->
    <McModal v-model="showSpecialModal" :title="`Specials — ${specialProduct?.name ?? ''}`">
      <div v-if="specialBusy" style="padding:1rem;text-align:center"><McSpinner /></div>
      <template v-else>
        <div v-if="existingSpecials.length" style="margin-bottom:1rem">
          <h4 style="margin:0 0 0.5rem;font-size:0.85rem">Current specials</h4>
          <table class="mc-table" style="font-size:0.82rem">
            <thead><tr><th>Type</th><th>Value</th><th>Effective</th><th></th></tr></thead>
            <tbody>
              <tr v-for="s in existingSpecials" :key="s.id">
                <td>
                  <McBadge v-if="s.promotionName" variant="accent">{{ s.promotionName }}</McBadge>
                  <McBadge v-else variant="neutral">Standalone</McBadge>
                </td>
                <td>
                  <template v-if="s.specialPrice != null">{{ formatZAR(s.specialPrice) }}</template>
                  <template v-else-if="s.discountPercent != null">{{ s.discountPercent }}% off</template>
                </td>
                <td><strong>{{ formatZAR(s.effectivePrice) }}</strong></td>
                <td><McButton variant="ghost" dense type="button" @click="removeSpecial(s)">Remove</McButton></td>
              </tr>
            </tbody>
          </table>
        </div>
        <McAlert v-if="specialFormErr" variant="error">{{ specialFormErr }}</McAlert>
        <h4 style="margin:0 0 0.5rem;font-size:0.85rem">Add standalone special</h4>
        <div style="display:flex;gap:0.75rem;margin-bottom:0.5rem">
          <label style="display:flex;align-items:center;gap:0.3rem;font-size:0.85rem;cursor:pointer">
            <input v-model="specialForm.usePrice" type="radio" :value="true" /> Special price (R)
          </label>
          <label style="display:flex;align-items:center;gap:0.3rem;font-size:0.85rem;cursor:pointer">
            <input v-model="specialForm.usePrice" type="radio" :value="false" /> Discount %
          </label>
        </div>
        <McField v-if="specialForm.usePrice" label="Special price (R)" for-id="sp-price">
          <input id="sp-price" v-model.number="specialForm.specialPrice" type="number" step="0.01" min="0" />
        </McField>
        <McField v-else label="Discount %" for-id="sp-disc">
          <input id="sp-disc" v-model.number="specialForm.discountPercent" type="number" step="0.01" min="0" max="100" />
        </McField>
      </template>
      <template #footer>
        <McButton variant="secondary" type="button" @click="showSpecialModal = false">Close</McButton>
        <McButton variant="primary" type="button" :disabled="specialFormBusy" @click="saveStandaloneSpecial">Add special</McButton>
      </template>
    </McModal>
  </div>
</template>

<style scoped>
.stock-page {
  min-height: 100%;
}

.stock-toolbar {
  display: flex;
  flex-wrap: wrap;
  align-items: flex-end;
  gap: 1rem;
  padding: 1.15rem var(--mc-app-pad-card, 1.75rem);
}

.stock-toolbar__search {
  flex: 1 1 220px;
}

.stock-toolbar__search :deep(.mc-field) {
  margin-bottom: 0;
}

.stock-toolbar__check {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  font-weight: 500;
  padding-bottom: 0.35rem;
  cursor: pointer;
}

.stock-toolbar__page {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.stock-toolbar__label {
  font-size: 0.8rem;
  font-weight: 700;
  color: var(--mc-app-text-secondary, #333336);
}

.stock-toolbar__select {
  min-height: 44px;
  padding: 0 0.85rem;
  border-radius: 10px;
  border: 1.5px solid var(--mc-app-border-subtle, #c8c5bd);
  background: var(--mc-app-surface, #fff);
  color: var(--mc-app-text, #1a1a1c);
  transition: border-color 0.15s ease, box-shadow 0.15s ease;
}

.stock-toolbar__select:focus {
  outline: none;
  border-color: var(--mc-accent, #f47a20);
  box-shadow: inset 0 0 0 1px var(--mc-accent, #f47a20);
}

.stock-toolbar__nav {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.5rem;
  margin-left: auto;
}

.stock-toolbar__meta {
  font-size: 0.85rem;
  color: var(--mc-app-text-muted, #5c5a56);
  font-weight: 500;
}

.stock-table-wrap {
  overflow-x: auto;
  -webkit-overflow-scrolling: touch;
  padding: 0 24px 16px;
}

.stock-table {
  width: 100%;
  min-width: 1100px;
  font-size: 0.88rem;
}

.stock-mono {
  font-variant-numeric: tabular-nums;
  white-space: nowrap;
}

.stock-name {
  max-width: 200px;
  font-weight: 500;
}

.stock-warn {
  color: #b71c1c;
  font-weight: 600;
}

.stock-warn-icon {
  cursor: help;
  margin-left: 0.2rem;
}

.stock-special-price {
  display: block;
  font-weight: 700;
  color: #cc0000;
}

.stock-was-price {
  display: block;
  font-size: 0.8em;
  color: #999;
  text-decoration: line-through;
}

.stock-special-label {
  display: block;
  font-size: 0.7em;
  color: #cc0000;
  font-weight: 600;
}

.stock-qty--low {
  color: #e65100;
}

.stock-qty--consign {
  color: #1565c0;
}

.stock-qty--none {
  color: var(--mc-app-text-muted, #5c5a56);
}

.stock-actions {
  white-space: nowrap;
  display: flex;
  flex-wrap: wrap;
  gap: 0.25rem;
}

.stock-loading {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 0.75rem;
  padding: 3rem;
  color: var(--mc-app-text-muted, #5c5a56);
}

.stock-drawer-overlay {
  position: fixed;
  inset: 0;
  z-index: 10030;
  background: rgba(10, 10, 11, 0.45);
  backdrop-filter: blur(2px);
}

.stock-drawer {
  position: fixed;
  top: 0;
  right: 0;
  bottom: 0;
  z-index: 10031;
  width: min(480px, 100vw);
  background: var(--mc-app-surface, #fff);
  box-shadow: -8px 0 40px rgba(0, 0, 0, 0.2);
  display: flex;
  flex-direction: column;
  border-left: 1px solid var(--mc-app-border-soft, #ddd9d3);
}

.stock-drawer__head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 1.15rem 1.5rem;
  border-bottom: 1px solid var(--mc-app-border-faint, #eceae5);
  background: var(--mc-app-surface-2, #f9f8f6);
}

.stock-drawer__title {
  margin: 0;
  font-family: 'Barlow Condensed', sans-serif;
  font-size: 1.15rem;
  letter-spacing: 0.05em;
  text-transform: uppercase;
}

.stock-drawer__close {
  width: 44px;
  height: 44px;
  border: 1.5px solid var(--mc-app-border-faint, #eceae5);
  background: var(--mc-app-surface, #fff);
  border-radius: 10px;
  font-size: 1.35rem;
  line-height: 1;
  cursor: pointer;
  color: var(--mc-app-text-secondary, #333336);
  transition: background 0.15s ease, border-color 0.15s ease;
}

.stock-drawer__close:hover {
  background: var(--mc-app-surface-muted, #f0eeea);
  border-color: var(--mc-app-border-subtle, #c8c5bd);
}

.stock-drawer__body {
  flex: 1;
  overflow-y: auto;
  padding: 1.5rem;
}

.stock-drawer__grid {
  display: grid;
  grid-template-columns: 1fr;
  gap: 0;
}

@media (min-width: 400px) {
  .stock-drawer__grid {
    grid-template-columns: 1fr 1fr;
  }
}

.stock-drawer__foot {
  padding: 1.15rem 1.5rem;
  border-top: 1px solid var(--mc-app-border-faint, #eceae5);
  display: flex;
  justify-content: flex-end;
  gap: 0.6rem;
  flex-wrap: wrap;
  background: var(--mc-app-surface-2, #f9f8f6);
}

.stock-drawer-fade-enter-active,
.stock-drawer-fade-leave-active {
  transition: opacity 0.2s ease;
}
.stock-drawer-fade-enter-from,
.stock-drawer-fade-leave-to {
  opacity: 0;
}

.stock-drawer-slide-enter-active,
.stock-drawer-slide-leave-active {
  transition: transform 0.25s ease;
}
.stock-drawer-slide-enter-from,
.stock-drawer-slide-leave-to {
  transform: translateX(100%);
}

.stock-drawer--wide {
  width: min(680px, 100vw);
}

.history-table {
  width: 100%;
  font-size: 0.85rem;
}

.receipt-product-name {
  font-weight: 600;
  font-size: 1rem;
  margin: 0 0 1rem;
  color: var(--mc-app-heading, #0a0a0c);
}

.receipt-max-hint {
  font-size: 0.82rem;
  color: var(--mc-app-text-muted, #5c5a56);
  margin: -0.5rem 0 0.75rem;
}
</style>
