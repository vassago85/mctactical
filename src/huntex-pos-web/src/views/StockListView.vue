<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
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
import McFilterToolbar from '@/components/ui/McFilterToolbar.vue'
import BarcodeScanner from '@/components/BarcodeScanner.vue'
import { AlertTriangle, MoreHorizontal, X, Star, ScanLine } from 'lucide-vue-next'

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
  supplierDiscountPercent?: number
  effectiveCost?: number | null
  sellPrice: number
  qtyOnHand: number
  qtyConsignment: number
  active: boolean
  warning?: string | null
  specialPrice?: number | null
  specialLabel?: string | null
  pricingMethod?: string
  customMarkupPercent?: number | null
  fixedSellPrice?: number | null
  minSellPrice?: number | null
  priceLocked?: boolean
  pricingSource?: string | null
  minAllowedPrice?: number | null
}

type Page = { total: number; skip: number; take: number; items: Product[] }

type StockReceipt = {
  id: string
  productId: string
  supplierId?: string | null
  supplierName?: string | null
  type: string
  quantity: number
  costPrice?: number | null
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
const actionsMenuId = ref<string | null>(null)
function toggleActionsMenu(id: string) {
  actionsMenuId.value = actionsMenuId.value === id ? null : id
}

const canManage = computed(() => auth.hasRole('Admin', 'Owner', 'Dev'))
const canExport = canManage
const filterSpecials = ref(false)
const filterSupplierId = ref<string>('')
const filterInStockOnly = ref(false)
const pageLabel = computed(() => {
  if (!page.value) return ''
  const from = page.value.total === 0 ? 0 : page.value.skip + 1
  const to = Math.min(page.value.skip + page.value.items.length, page.value.total)
  return `${from}–${to} of ${page.value.total}`
})

const visibleItems = computed(() => {
  const items = page.value?.items ?? []
  if (!filterInStockOnly.value) return items
  return items.filter((p) => p.qtyOnHand + p.qtyConsignment > 0)
})

let debounce: ReturnType<typeof setTimeout> | null = null
watch([q, includeInactive, filterSupplierId], () => {
  skip.value = 0
  if (debounce) clearTimeout(debounce)
  debounce = setTimeout(() => void load(), 300)
})
watch(filterSpecials, () => {
  skip.value = 0
  void load()
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
        hasSpecial: filterSpecials.value || undefined,
        supplierId: filterSupplierId.value || undefined,
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
  supplierDiscountPercent: 0,
  sellPrice: 0,
  qtyOnHand: 0,
  pricingMethod: 'default' as 'default' | 'custom_markup' | 'fixed_price',
  customMarkupPercent: null as number | null,
  fixedSellPrice: null as number | null,
  minSellPrice: null as number | null,
  priceLocked: false
})
const formBusy = ref(false)
const formErr = ref<string | null>(null)
const sellPriceManual = ref(false)

const formEffectiveCost = computed(() => {
  const cost = Number(form.value.cost) || 0
  const disc = Number(form.value.supplierDiscountPercent) || 0
  if (cost <= 0) return 0
  return Math.round(cost * (1 - disc / 100) * 100) / 100
})

// Only Owner/Dev can correct qty-on-hand from the edit drawer (audited via adjust-stock endpoint).
// Admin/Sales must go through Stocktake or consignment receipts instead.
const canAdjustStock = computed(() => auth.hasRole('Owner', 'Dev'))
const originalQtyOnHand = ref(0)
const adjustReason = ref('')

interface PricingPreview {
  sellPrice: number
  minAllowedPrice: number
  source: string
  pricingMethod: string
  effectiveMarkupPercent: number
  effectiveMaxDiscountPercent: number
  effectiveRoundToNearest: number
  effectiveMinMarginPercent: number | null
}
const pricingPreview = ref<PricingPreview | null>(null)
const previewBusy = ref(false)
let previewTimer: ReturnType<typeof setTimeout> | null = null
let computeTimer: ReturnType<typeof setTimeout> | null = null

function closeDrawer() {
  showForm.value = false
  showBarcodeScanner.value = false
  barcodeConflict.value = null
}

// ── Barcode scanner state (Edit-product drawer) ──────────────────────────
// Camera-based scan to fill the Barcode field. The form still requires an
// explicit Save click, so the operator can review before persisting.
const showBarcodeScanner = ref(false)
const barcodeConflict = ref<{ sku: string; name: string } | null>(null)
let barcodeLookupTimer: ReturnType<typeof setTimeout> | null = null

function openBarcodeScanner() {
  showBarcodeScanner.value = true
}
function closeBarcodeScanner() {
  showBarcodeScanner.value = false
}
function onBarcodeScanned(value: string) {
  const trimmed = (value ?? '').trim()
  if (!trimmed) return
  form.value.barcode = trimmed
  closeBarcodeScanner()
  toast.success(`Scanned ${trimmed}`)
  void checkBarcodeConflict(trimmed)
}

// Hits the existing GET /api/products?barcode=X. Filters out the
// currently-edited product so re-saving an existing barcode doesn't warn.
async function checkBarcodeConflict(barcode: string) {
  barcodeConflict.value = null
  if (!barcode) return
  try {
    const { data } = await http.get<Product[]>('/api/products', {
      params: { barcode, take: 5 }
    })
    const other = (data ?? []).find((p) => p.id !== editId.value && p.barcode === barcode)
    if (other) {
      barcodeConflict.value = { sku: other.sku, name: other.name }
    }
  } catch {
    /* network blip — let the backend's uniqueness rule catch it on save */
  }
}

// Re-check whenever the barcode input itself changes (typing or paste). Debounced.
watch(
  () => form.value.barcode,
  (val) => {
    if (barcodeLookupTimer) clearTimeout(barcodeLookupTimer)
    const v = (val ?? '').trim()
    if (!v) {
      barcodeConflict.value = null
      return
    }
    barcodeLookupTimer = setTimeout(() => void checkBarcodeConflict(v), 350)
  }
)

function openAdd() {
  editId.value = null
  sellPriceManual.value = false
  form.value = {
    sku: '', barcode: '', name: '', category: '', manufacturer: '', itemType: '',
    supplierId: '', cost: 0, supplierDiscountPercent: 0, sellPrice: 0, qtyOnHand: 0,
    pricingMethod: 'default', customMarkupPercent: null, fixedSellPrice: null,
    minSellPrice: null, priceLocked: false
  }
  originalQtyOnHand.value = 0
  adjustReason.value = ''
  pricingPreview.value = null
  formErr.value = null
  showForm.value = true
  schedulePreview()
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
    supplierDiscountPercent: p.supplierDiscountPercent ?? 0,
    sellPrice: p.sellPrice,
    qtyOnHand: p.qtyOnHand,
    pricingMethod: (p.pricingMethod as 'default' | 'custom_markup' | 'fixed_price') ?? 'default',
    customMarkupPercent: p.customMarkupPercent ?? null,
    fixedSellPrice: p.fixedSellPrice ?? null,
    minSellPrice: p.minSellPrice ?? null,
    priceLocked: p.priceLocked ?? false
  }
  originalQtyOnHand.value = p.qtyOnHand
  adjustReason.value = ''
  pricingPreview.value = null
  formErr.value = null
  showForm.value = true
  schedulePreview()
}

function schedulePreview() {
  if (previewTimer) clearTimeout(previewTimer)
  previewTimer = setTimeout(() => { void runPreview() }, 250)
}

async function runPreview() {
  previewBusy.value = true
  try {
    const { data } = await http.post<PricingPreview>('/api/pricing-rules/preview', {
      cost: Number(form.value.cost) || 0,
      category: form.value.category || null,
      manufacturer: form.value.manufacturer || null,
      supplierId: form.value.supplierId || null,
      pricingMethod: form.value.pricingMethod,
      customMarkupPercent: form.value.customMarkupPercent,
      fixedSellPrice: form.value.fixedSellPrice,
      minSellPrice: form.value.minSellPrice
    })
    pricingPreview.value = data
    if (!sellPriceManual.value && data.sellPrice > 0) {
      form.value.sellPrice = data.sellPrice
    }
  } catch {
    /* keep current preview */
  } finally {
    previewBusy.value = false
  }
}

watch(() => [
  form.value.cost,
  form.value.category,
  form.value.manufacturer,
  form.value.supplierId,
  form.value.pricingMethod,
  form.value.customMarkupPercent,
  form.value.fixedSellPrice,
  form.value.minSellPrice
], () => {
  if (!showForm.value) return
  schedulePreview()
}, { deep: true })

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
    const costVal = Number(form.value.cost) || 0
    const sellVal = Number(form.value.sellPrice) || 0
    const payload: Record<string, unknown> = {
      sku: form.value.sku,
      barcode: form.value.barcode || null,
      name: form.value.name,
      category: form.value.category || null,
      manufacturer: form.value.manufacturer || null,
      itemType: form.value.itemType || null,
      supplierId: form.value.supplierId || null,
      cost: costVal > 0 ? costVal : null,
      supplierDiscountPercent: Number(form.value.supplierDiscountPercent) || 0,
      sellPrice: sellVal > 0 ? sellVal : null,
      pricingMethod: form.value.pricingMethod,
      customMarkupPercent: form.value.customMarkupPercent,
      fixedSellPrice: form.value.fixedSellPrice,
      minSellPrice: form.value.minSellPrice,
      priceLocked: form.value.priceLocked
    }
    if (editId.value) {
      // Validate qty adjustment up-front so we don't half-save the product.
      const qtyChanged = canAdjustStock.value && form.value.qtyOnHand !== originalQtyOnHand.value
      const trimmedReason = adjustReason.value.trim()
      if (qtyChanged) {
        if (form.value.qtyOnHand < 0) {
          formErr.value = 'Quantity on hand cannot be negative.'
          formBusy.value = false
          return
        }
        if (!trimmedReason) {
          formErr.value = 'A reason is required when changing quantity on hand.'
          formBusy.value = false
          return
        }
      }

      await http.put(`/api/products/${editId.value}`, payload)

      if (qtyChanged) {
        await http.post(`/api/products/${editId.value}/adjust-stock`, {
          newQtyOnHand: form.value.qtyOnHand,
          reason: trimmedReason
        })
        toast.success(`Product updated · stock adjusted by ${form.value.qtyOnHand - originalQtyOnHand.value}`)
      } else {
        toast.success('Product updated')
      }
    } else {
      payload.qtyOnHand = form.value.qtyOnHand
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
const receiptType = ref<'OwnedIn' | 'ConsignmentIn' | 'ConsignmentToStock' | 'ConsignmentReturn' | 'StockToConsignment'>('OwnedIn')
const receiptSupplierId = ref('')
const receiptQty = ref(1)
const receiptCostPrice = ref(0)
const receiptNotes = ref('')
const receiptBusy = ref(false)
const receiptErr = ref<string | null>(null)
const consignmentSummary = ref<ConsignmentSummaryLine[]>([])

function openReceiptModal(p: Product, type: typeof receiptType.value) {
  receiptProduct.value = p
  receiptType.value = type
  receiptSupplierId.value = ''
  receiptQty.value = 1
  receiptCostPrice.value = p.cost ?? 0
  receiptNotes.value = ''
  receiptErr.value = null
  consignmentSummary.value = []
  showReceiptModal.value = true
  if (type === 'ConsignmentToStock' || type === 'ConsignmentReturn' || type === 'StockToConsignment') {
    void loadConsignmentSummary(p.id, p.supplierId ?? undefined)
  }
}

async function loadConsignmentSummary(productId: string, productSupplierId?: string) {
  try {
    const { data } = await http.get<ConsignmentSummaryLine[]>(`/api/products/${productId}/consignment-summary`)
    consignmentSummary.value = data.filter(s => s.onHand > 0)
    if (consignmentSummary.value.length === 1) {
      receiptSupplierId.value = consignmentSummary.value[0].supplierId
    } else if (consignmentSummary.value.length === 0 && productSupplierId) {
      receiptSupplierId.value = productSupplierId
    }
  } catch { /* non-critical */ }
}

const receiptMaxQty = computed(() => {
  if (receiptType.value === 'StockToConsignment') return receiptProduct.value?.qtyOnHand ?? 0
  if (receiptType.value !== 'ConsignmentToStock' && receiptType.value !== 'ConsignmentReturn') return 999999
  const line = consignmentSummary.value.find(s => s.supplierId === receiptSupplierId.value)
  if (line) return line.onHand
  return receiptProduct.value?.qtyConsignment ?? 0
})

const receiptTypeLabel = computed(() => {
  switch (receiptType.value) {
    case 'OwnedIn': return 'Receive owned stock'
    case 'ConsignmentIn': return 'Receive consignment'
    case 'ConsignmentToStock': return 'Move consignment → owned stock'
    case 'StockToConsignment': return 'Move owned stock → consignment'
    case 'ConsignmentReturn': return 'Return to wholesaler'
    default: return 'Stock movement'
  }
})

async function submitReceipt() {
  receiptErr.value = null
  if (!receiptProduct.value) return
  if (receiptType.value !== 'OwnedIn' && !receiptSupplierId.value) {
    receiptErr.value = 'Please select a wholesaler.'
    return
  }
  receiptBusy.value = true
  try {
    await http.post(`/api/products/${receiptProduct.value.id}/stock-receipts`, {
      type: receiptType.value,
      supplierId: receiptSupplierId.value || null,
      quantity: receiptQty.value,
      costPrice: receiptCostPrice.value > 0 ? receiptCostPrice.value : null,
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
  } catch (e: unknown) {
    const ax = e as { response?: { data?: { error?: string } } }
    toast.error(ax.response?.data?.error ?? 'Could not load history')
  } finally {
    historyBusy.value = false
  }
}

function receiptTypeBadge(type: string): { label: string; variant: 'success' | 'neutral' | 'warning' | 'error' } {
  switch (type) {
    case 'OwnedIn': return { label: 'Received (owned)', variant: 'success' }
    case 'ConsignmentIn': return { label: 'Received (consignment)', variant: 'warning' }
    case 'ConsignmentToStock': return { label: 'Consign → Stock', variant: 'success' }
    case 'StockToConsignment': return { label: 'Stock → Consign', variant: 'warning' }
    case 'ConsignmentReturn': return { label: 'Returned', variant: 'error' }
    case 'Adjustment': return { label: 'Adjustment', variant: 'neutral' }
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
  } catch (e: unknown) {
    const ax = e as { response?: { data?: { error?: string } } }
    specialFormErr.value = ax.response?.data?.error ?? 'Could not load specials'
  }
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

async function toggleSpecialActive(s: ProductSpecialItem) {
  try {
    await http.put(`/api/promotions/specials/${s.id}`, { isActive: !s.isActive })
    toast.success(s.isActive ? 'Special deactivated' : 'Special activated')
    if (specialProduct.value) await openSpecialModal(specialProduct.value)
    await load()
  } catch { toast.error('Update failed') }
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
  } catch (e: unknown) {
    const ax = e as { response?: { data?: Blob | Record<string, string> } }
    let msg = 'Label generation failed'
    try {
      if (ax.response?.data instanceof Blob) {
        const text = await ax.response.data.text()
        const json = JSON.parse(text)
        if (json.error) msg += ': ' + json.error
      }
    } catch { /* ignore parse errors */ }
    toast.error(msg)
  } finally {
    labelBusy.value = false
  }
}

function closeActionsMenu(e: MouseEvent) {
  if (actionsMenuId.value && !(e.target as HTMLElement)?.closest('.stock-actions-more'))
    actionsMenuId.value = null
}
onMounted(() => {
  void load()
  void loadSuppliers()
  document.addEventListener('click', closeActionsMenu)
})
onUnmounted(() => document.removeEventListener('click', closeActionsMenu))
</script>

<template>
  <div class="stock-page">
    <McPageHeader title="Stock list" description="Full inventory. Use Import to load items from your Huntex workbook or CSV.">
      <template v-if="canManage" #actions>
        <McButton variant="primary" type="button" @click="openAdd">Add product</McButton>
        <RouterLink to="/receiving" custom v-slot="{ navigate }">
          <McButton variant="secondary" type="button" @click="navigate">Receive stock</McButton>
        </RouterLink>
        <RouterLink to="/stock/labels" custom v-slot="{ navigate }">
          <McButton variant="secondary" type="button" @click="navigate">Bulk labels</McButton>
        </RouterLink>
        <McButton v-if="canExport" variant="secondary" type="button" @click="exportCsv">Export CSV</McButton>
      </template>
    </McPageHeader>

    <McAlert v-if="err" variant="error">{{ err }}</McAlert>

    <McFilterToolbar sticky>
      <input
        v-model="q"
        type="search"
        placeholder="Search name, SKU, barcode, category, wholesaler…"
        class="stock-filter-search"
        aria-label="Search inventory"
      />
      <select v-model="filterSupplierId" class="stock-filter-supplier" aria-label="Filter by wholesaler">
        <option value="">All wholesalers</option>
        <option v-for="s in suppliers" :key="s.id" :value="s.id">{{ s.name }}</option>
      </select>
      <button
        type="button"
        class="stock-filter-toggle"
        :class="{ 'stock-filter-toggle--on': filterInStockOnly }"
        @click="filterInStockOnly = !filterInStockOnly"
      >In stock</button>
      <button
        type="button"
        class="stock-filter-toggle"
        :class="{ 'stock-filter-toggle--on': filterSpecials }"
        @click="filterSpecials = !filterSpecials"
      >
        <component :is="filterSpecials ? X : Star" :size="14" />
        Specials
      </button>
      <button
        type="button"
        class="stock-filter-toggle"
        :class="{ 'stock-filter-toggle--on': includeInactive }"
        @click="includeInactive = !includeInactive"
      >Inactive</button>

      <template #actions>
        <div class="stock-filter-rows">
          <span class="stock-filter-rows__label">Rows</span>
          <select v-model.number="pageSize" aria-label="Rows per page">
            <option :value="100">100</option>
            <option :value="250">250</option>
            <option :value="500">500</option>
            <option :value="1000">1000</option>
            <option :value="5000">5000</option>
          </select>
        </div>
        <div class="stock-filter-pager">
          <McButton variant="secondary" dense type="button" :disabled="busy || skip <= 0" @click="prevPage">Prev</McButton>
          <span class="stock-filter-pager__meta">{{ pageLabel }}</span>
          <McButton
            variant="secondary"
            dense
            type="button"
            :disabled="busy || !page || skip + page.take >= page.total"
            @click="nextPage"
          >Next</McButton>
          <McSpinner v-if="busy" />
        </div>
      </template>
    </McFilterToolbar>

    <McCard :padded="false" title="Products">
      <div class="stock-table-wrap">
        <table v-if="visibleItems.length" class="stock-table mc-table">
          <thead>
            <tr>
              <th>Product</th>
              <th>Wholesaler</th>
              <th class="text-right">Cost</th>
              <th class="text-right">Sell</th>
              <th class="text-right">Promo</th>
              <th class="text-center">Owned</th>
              <th class="text-center">Consign</th>
              <th>Status</th>
              <th v-if="canManage"></th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="p in visibleItems" :key="p.id">
              <td class="stock-product">
                <div class="stock-product__name">{{ p.name }}</div>
                <div class="stock-product__meta">
                  <span class="stock-product__sku">{{ p.sku }}</span>
                  <span v-if="p.barcode" class="stock-product__barcode">· {{ p.barcode }}</span>
                </div>
              </td>
              <td>{{ p.supplierName ?? '—' }}</td>
              <td class="text-right">
                <template v-if="p.cost != null && p.cost > 0">
                  {{ formatZAR(p.cost) }}
                  <span v-if="p.supplierDiscountPercent && p.supplierDiscountPercent > 0" class="stock-supplier-disc">
                    &rarr; {{ formatZAR(p.effectiveCost ?? p.cost) }} (−{{ p.supplierDiscountPercent }}%)
                  </span>
                </template>
                <template v-else>—</template>
              </td>
              <td class="text-right" :class="{ 'stock-warn': !!p.warning }">
                {{ formatZAR(p.sellPrice) }}
                <AlertTriangle v-if="p.warning" :title="p.warning" class="stock-warn-icon" :size="14" />
              </td>
              <td class="text-right">
                <template v-if="p.specialPrice != null && p.specialPrice !== p.sellPrice">
                  <span class="stock-special-price">{{ formatZAR(p.specialPrice) }}</span>
                  <span class="stock-special-label">{{ p.specialLabel }}</span>
                </template>
                <span v-else class="stock-qty--none">—</span>
              </td>
              <td class="text-center">
                <strong :class="{ 'stock-qty--low': p.qtyOnHand <= 3 && p.qtyOnHand > 0, 'stock-qty--out': p.qtyOnHand < 1 }">{{ p.qtyOnHand }}</strong>
              </td>
              <td class="text-center">
                <strong v-if="p.qtyConsignment > 0" class="stock-qty--consign">{{ p.qtyConsignment }}</strong>
                <span v-else class="stock-qty--none">—</span>
              </td>
              <td>
                <div class="stock-status-stack">
                  <McBadge :variant="p.active ? 'success' : 'neutral'">{{ p.active ? 'Active' : 'Inactive' }}</McBadge>
                  <McBadge v-if="p.qtyOnHand < 1" variant="danger">Out</McBadge>
                  <McBadge v-else-if="p.qtyOnHand <= 3" variant="warning">Low</McBadge>
                  <McBadge v-if="p.specialPrice != null && p.specialPrice !== p.sellPrice" variant="accent">Special</McBadge>
                  <McBadge v-if="p.qtyConsignment > 0" variant="neutral">Consign</McBadge>
                </div>
              </td>
              <td v-if="canManage" class="stock-actions">
                <McButton variant="secondary" dense type="button" @click="openEdit(p)">Edit</McButton>
                <McButton variant="secondary" dense type="button" @click="openLabelModal(p)">Label</McButton>
                <div class="stock-actions-more">
                  <button type="button" class="stock-actions-toggle" @click="toggleActionsMenu(p.id)"><MoreHorizontal :size="16" /></button>
                  <div v-if="actionsMenuId === p.id" class="stock-actions-dropdown">
                    <button type="button" @click="openReceiptModal(p, 'OwnedIn'); actionsMenuId = null">+ Owned stock</button>
                    <button type="button" @click="openReceiptModal(p, 'ConsignmentIn'); actionsMenuId = null">+ Consignment</button>
                    <button v-if="p.qtyConsignment > 0" type="button" @click="openReceiptModal(p, 'ConsignmentToStock'); actionsMenuId = null">Consign → Stock</button>
                    <button v-if="p.qtyOnHand > 0" type="button" @click="openReceiptModal(p, 'StockToConsignment'); actionsMenuId = null">Stock → Consign</button>
                    <button v-if="p.qtyConsignment > 0" type="button" @click="openReceiptModal(p, 'ConsignmentReturn'); actionsMenuId = null">Return consignment</button>
                    <button type="button" @click="openSpecialModal(p); actionsMenuId = null">Manage specials</button>
                    <button type="button" @click="openHistory(p); actionsMenuId = null">View history</button>
                    <button type="button" @click="toggleActive(p); actionsMenuId = null">
                      {{ p.active ? 'Deactivate' : 'Activate' }}
                    </button>
                  </div>
                </div>
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
            <button type="button" class="stock-drawer__close" aria-label="Close" @click="closeDrawer"><X :size="20" /></button>
          </header>
          <div class="stock-drawer__body">
            <McAlert v-if="formErr" variant="error">{{ formErr }}</McAlert>
            <div class="stock-drawer__grid">
              <McField label="SKU" for-id="f-sku">
                <input id="f-sku" v-model="form.sku" required />
              </McField>
              <McField label="EAN-13 SKU" for-id="f-bc" hint="Auto-generated on first label print if left blank">
                <div class="stock-barcode-row">
                  <input id="f-bc" v-model="form.barcode" placeholder="Type or scan…" />
                  <button
                    type="button"
                    class="stock-barcode-scan"
                    title="Scan a barcode with the camera"
                    @click="openBarcodeScanner"
                  >
                    <ScanLine :size="16" aria-hidden="true" />
                    <span>Scan</span>
                  </button>
                </div>
                <McAlert
                  v-if="barcodeConflict"
                  variant="warning"
                  class="stock-barcode-conflict"
                >
                  This barcode is already on
                  <strong>{{ barcodeConflict.name }}</strong> (SKU
                  <code>{{ barcodeConflict.sku }}</code>). Saving will move the
                  barcode to this product.
                </McAlert>
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
              <McField label="Wholesaler" for-id="f-supplier">
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
              <McField label="Supplier discount %" for-id="f-supdisc" hint="0 = no discount">
                <input id="f-supdisc" v-model.number="form.supplierDiscountPercent" type="number" step="1" min="0" max="100" />
              </McField>
              <div v-if="form.supplierDiscountPercent > 0" class="stock-drawer__effective-cost">
                Effective cost: <strong>{{ formatZAR(formEffectiveCost) }}</strong>
              </div>
              <McField label="Sell price (R)" for-id="f-sell" :hint="!editId && !sellPriceManual ? 'Auto-calculated from cost' : ''">
                <input id="f-sell" v-model.number="form.sellPrice" type="number" step="0.01" min="0" @input="sellPriceManual = true" />
              </McField>
              <McField v-if="!editId" label="Initial stock qty" for-id="f-qty">
                <input id="f-qty" v-model.number="form.qtyOnHand" type="number" step="1" min="0" />
              </McField>
            </div>

            <div v-if="editId && canAdjustStock" class="stock-drawer__adjust">
              <header class="stock-drawer__adjust-head">
                <h3 class="stock-drawer__adjust-title">Quantity on hand</h3>
                <p class="stock-drawer__adjust-hint">
                  Owner / Dev only. Changes here are written to the product's stock movement history with
                  the reason below. For routine receiving use the stock receipts action instead.
                </p>
              </header>
              <div class="stock-drawer__grid">
                <McField label="New qty on hand" for-id="f-qty-edit" :hint="`Current: ${originalQtyOnHand}`">
                  <input id="f-qty-edit" v-model.number="form.qtyOnHand" type="number" step="1" min="0" />
                </McField>
                <McField
                  label="Reason"
                  for-id="f-qty-reason"
                  :hint="form.qtyOnHand !== originalQtyOnHand ? 'Required — explain why qty is being corrected.' : 'Only needed when qty changes.'"
                >
                  <input
                    id="f-qty-reason"
                    v-model="adjustReason"
                    type="text"
                    maxlength="500"
                    placeholder="e.g. Stocktake variance, damaged, found unit"
                    :required="form.qtyOnHand !== originalQtyOnHand"
                  />
                </McField>
              </div>
              <p v-if="form.qtyOnHand !== originalQtyOnHand" class="stock-drawer__adjust-delta">
                Adjustment: {{ form.qtyOnHand - originalQtyOnHand > 0 ? '+' : '' }}{{ form.qtyOnHand - originalQtyOnHand }}
              </p>
            </div>

            <section class="pricing-section">
              <header class="pricing-section__head">
                <h3 class="pricing-section__title">Pricing</h3>
                <p v-if="pricingPreview" class="pricing-section__source">{{ pricingPreview.source }}</p>
              </header>

              <McField label="Pricing method" for-id="f-method">
                <select id="f-method" v-model="form.pricingMethod">
                  <option value="default">Default (use pricing rules)</option>
                  <option value="custom_markup">Custom markup %</option>
                  <option value="fixed_price">Fixed sell price</option>
                </select>
              </McField>

              <div v-if="form.pricingMethod === 'custom_markup'" class="stock-drawer__grid">
                <McField label="Custom markup % (cost × 1 + markup/100)" for-id="f-cmp">
                  <input id="f-cmp" v-model.number="form.customMarkupPercent" type="number" step="0.01" />
                </McField>
              </div>

              <div v-if="form.pricingMethod === 'fixed_price'" class="stock-drawer__grid">
                <McField label="Fixed sell price (R)" for-id="f-fsp">
                  <input id="f-fsp" v-model.number="form.fixedSellPrice" type="number" step="0.01" min="0" />
                </McField>
              </div>

              <div class="stock-drawer__grid">
                <McField label="Minimum sell price (R)" for-id="f-minp" hint="Hard floor — sell price never goes below this.">
                  <input id="f-minp" v-model.number="form.minSellPrice" type="number" step="0.01" min="0" />
                </McField>
                <McField label="&nbsp;" for-id="f-lock">
                  <label class="pricing-lock">
                    <input id="f-lock" v-model="form.priceLocked" type="checkbox" />
                    <span>Lock price (never auto-recalculate)</span>
                  </label>
                </McField>
              </div>

              <div v-if="pricingPreview" class="pricing-preview">
                <div>
                  <span class="pricing-preview__label">Preview sell</span>
                  <strong>{{ formatZAR(pricingPreview.sellPrice) }}</strong>
                </div>
                <div>
                  <span class="pricing-preview__label">Min allowed</span>
                  <strong>{{ pricingPreview.minAllowedPrice > 0 ? formatZAR(pricingPreview.minAllowedPrice) : '—' }}</strong>
                </div>
                <div>
                  <span class="pricing-preview__label">Markup</span>
                  <strong>{{ pricingPreview.effectiveMarkupPercent }}%</strong>
                </div>
                <div>
                  <span class="pricing-preview__label">Max discount</span>
                  <strong>{{ pricingPreview.effectiveMaxDiscountPercent }}%</strong>
                </div>
              </div>
            </section>
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
        label="Wholesaler"
        for-id="r-supplier"
      >
        <select id="r-supplier" v-model="receiptSupplierId" required>
          <option value="" disabled>Select wholesaler…</option>
          <template v-if="(receiptType === 'ConsignmentToStock' || receiptType === 'ConsignmentReturn') && consignmentSummary.length > 0">
            <option v-for="s in consignmentSummary" :key="s.supplierId" :value="s.supplierId">
              {{ s.supplierName }} ({{ s.onHand }} on hand)
            </option>
          </template>
          <template v-else>
            <option v-for="s in suppliers" :key="s.id" :value="s.id">
              {{ s.name }}
              <template v-if="receiptProduct?.supplierId === s.id"> (assigned)</template>
            </option>
          </template>
        </select>
      </McField>

      <McField v-if="receiptType === 'OwnedIn'" label="Wholesaler (optional)" for-id="r-supplier-opt">
        <select id="r-supplier-opt" v-model="receiptSupplierId">
          <option value="">— None —</option>
          <option v-for="s in suppliers" :key="s.id" :value="s.id">{{ s.name }}</option>
        </select>
      </McField>

      <McField label="Quantity" for-id="r-qty">
        <input id="r-qty" v-model.number="receiptQty" type="number" min="1" :max="receiptMaxQty" step="1" required />
      </McField>
      <p v-if="(receiptType === 'ConsignmentToStock' || receiptType === 'ConsignmentReturn') && receiptSupplierId" class="receipt-max-hint">
        Max: {{ receiptMaxQty }} consignment units{{ consignmentSummary.find(s => s.supplierId === receiptSupplierId) ? ' from this wholesaler' : '' }}
      </p>
      <p v-if="receiptType === 'StockToConsignment'" class="receipt-max-hint">
        Max: {{ receiptMaxQty }} owned units available
      </p>

      <McField label="Purchase price / Cost ex VAT (R)" for-id="r-cost" hint="Updates the product cost price. Leave unchanged if price hasn't changed.">
        <input id="r-cost" v-model.number="receiptCostPrice" type="number" step="0.01" min="0" />
      </McField>

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
            <button type="button" class="stock-drawer__close" aria-label="Close" @click="showHistory = false"><X :size="20" /></button>
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
                  <th>Wholesaler</th>
                  <th>Qty</th>
                  <th>Cost</th>
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
                  <td>{{ r.costPrice ? formatZAR(r.costPrice) : '—' }}</td>
                  <td>{{ r.notes ?? '—' }}</td>
                  <td>{{ r.processedBy ?? '—' }}</td>
                </tr>
              </tbody>
            </table>
          </div>
        </aside>
      </Transition>
    </Teleport>

    <!-- Barcode scanner modal (drawer scan button) -->
    <McModal v-model="showBarcodeScanner" title="Scan barcode">
      <p class="stock-barcode-help">
        Point the camera at the product barcode. The value will fill the field
        and you'll need to click <strong>Save</strong> to apply.
      </p>
      <div class="stock-barcode-scanner">
        <BarcodeScanner :active="showBarcodeScanner" @decode="onBarcodeScanned" />
      </div>
      <template #footer>
        <McButton variant="secondary" type="button" @click="closeBarcodeScanner">Cancel</McButton>
      </template>
    </McModal>

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
        <div v-if="existingSpecials.length" style="margin-bottom:1.25rem">
          <h4 style="margin:0 0 0.5rem;font-size:0.85rem">Current specials</h4>
          <div v-for="s in existingSpecials" :key="s.id" class="special-card" :class="{ 'special-card--inactive': !s.isActive }">
            <div class="special-card__row">
              <McBadge v-if="s.promotionName" variant="accent">{{ s.promotionName }}</McBadge>
              <McBadge v-else variant="neutral">Standalone</McBadge>
              <McBadge :variant="s.isActive ? 'success' : 'neutral'">{{ s.isActive ? 'Active' : 'Inactive' }}</McBadge>
            </div>
            <div class="special-card__price">
              <span v-if="s.specialPrice != null">Special price: <strong>{{ formatZAR(s.specialPrice) }}</strong></span>
              <span v-else-if="s.discountPercent != null">Discount: <strong>{{ s.discountPercent }}%</strong></span>
              <span> → Effective: <strong>{{ formatZAR(s.effectivePrice) }}</strong></span>
            </div>
            <div class="special-card__actions">
              <McButton :variant="s.isActive ? 'secondary' : 'primary'" dense type="button" @click="toggleSpecialActive(s)">
                {{ s.isActive ? 'Deactivate' : 'Activate' }}
              </McButton>
              <McButton variant="ghost" dense type="button" @click="removeSpecial(s)">Remove</McButton>
            </div>
          </div>
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
.special-card {
  border: 1px solid var(--mc-app-border-soft, #ddd9d3);
  border-radius: 10px;
  padding: 0.75rem;
  margin-bottom: 0.5rem;
  background: var(--mc-app-surface-2, #f9f8f6);
}

.special-card--inactive {
  opacity: 0.6;
}

.special-card__row {
  display: flex;
  gap: 0.5rem;
  align-items: center;
  margin-bottom: 0.4rem;
}

.special-card__price {
  font-size: 0.85rem;
  margin-bottom: 0.5rem;
}

.special-card__actions {
  display: flex;
  gap: 0.5rem;
}

.stock-page {
  min-height: 100%;
}

/* ── Filter toolbar inputs ────────────────────────────────────────────── */
.stock-filter-search {
  flex: 1 1 240px;
  min-width: 200px;
}
.stock-filter-supplier {
  flex: 0 1 200px;
  min-width: 140px;
}
.stock-filter-toggle {
  display: inline-flex;
  align-items: center;
  gap: 0.3rem;
  height: 36px;
  padding: 0 0.85rem;
  border-radius: 8px;
  border: 1.5px solid var(--mc-app-border-subtle, #c8c5bd);
  background: var(--mc-app-surface, #fff);
  color: var(--mc-app-text-secondary, #333336);
  font-size: 0.85rem;
  font-weight: 600;
  cursor: pointer;
  white-space: nowrap;
  transition: border-color 0.12s ease, background 0.12s ease, color 0.12s ease;
}
.stock-filter-toggle:hover {
  border-color: var(--mc-accent, #f47a20);
}
.stock-filter-toggle--on {
  border-color: var(--mc-accent, #f47a20);
  background: var(--mc-accent, #f47a20);
  color: #fff;
}
.stock-filter-toggle--on:hover { background: #d96a15; border-color: #d96a15; }

.stock-filter-rows {
  display: flex;
  align-items: center;
  gap: 0.4rem;
  font-size: 0.8rem;
  color: var(--mc-app-text-muted, #5c5a56);
}
.stock-filter-rows__label {
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.06em;
}
.stock-filter-rows select {
  height: 32px;
  padding: 0 0.4rem;
  border-radius: 6px;
  border: 1px solid var(--mc-app-border-soft, #ddd9d3);
  background: var(--mc-app-surface, #fff);
  font-size: 0.85rem;
}
.stock-filter-pager {
  display: flex;
  align-items: center;
  gap: 0.4rem;
}
.stock-filter-pager__meta {
  font-size: 0.78rem;
  color: var(--mc-app-text-muted, #5c5a56);
  font-weight: 600;
  white-space: nowrap;
  font-variant-numeric: tabular-nums;
}

.stock-table-wrap {
  overflow-x: auto;
  -webkit-overflow-scrolling: touch;
  padding: 0 24px 16px;
}

.stock-table {
  width: 100%;
  min-width: 820px;
  font-size: 0.88rem;
}

.text-right { text-align: right; }
.text-center { text-align: center; }

/* Combined name/SKU/barcode cell */
.stock-product {
  max-width: 22rem;
  line-height: 1.3;
}
.stock-product__name {
  font-weight: 700;
  color: var(--mc-app-heading, #0a0a0c);
}
.stock-product__meta {
  display: block;
  font-size: 0.74rem;
  color: var(--mc-app-text-muted, #5c5a56);
  font-variant-numeric: tabular-nums;
  margin-top: 0.1rem;
}
.stock-product__sku { font-weight: 600; }

.stock-status-stack {
  display: flex;
  flex-wrap: wrap;
  gap: 0.3rem;
}

.stock-warn {
  color: #b71c1c;
  font-weight: 600;
}

.stock-warn-icon {
  cursor: help;
  margin-left: 0.2rem;
  vertical-align: middle;
  display: inline-block;
}

.stock-special-price {
  display: block;
  font-weight: 700;
  color: #cc0000;
}

.stock-special-label {
  display: block;
  font-size: 0.7em;
  color: #cc0000;
  font-weight: 600;
}

.stock-supplier-disc {
  display: block;
  font-size: 0.75em;
  color: var(--mc-app-accent, #0a7e3d);
  font-weight: 600;
}

.stock-qty--low {
  color: #e65100;
}

.stock-qty--out {
  color: #c62828;
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
  align-items: center;
  gap: 0.25rem;
}

.stock-actions-more {
  position: relative;
}

.stock-actions-toggle {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  background: none;
  border: 1px solid var(--mc-app-border, #ddd);
  border-radius: 4px;
  cursor: pointer;
  font-size: 1.1rem;
  padding: 4px 8px;
  line-height: 1;
  color: var(--mc-app-text, #333);
}
.stock-actions-toggle:hover { background: var(--mc-app-bg-hover, #f0f0f0); }

.stock-actions-dropdown {
  position: absolute;
  right: 0;
  top: 100%;
  z-index: 100;
  background: #fff;
  border: 1px solid var(--mc-app-border, #ddd);
  border-radius: 6px;
  box-shadow: 0 4px 16px rgba(0,0,0,0.12);
  min-width: 180px;
  padding: 4px 0;
}
.stock-actions-dropdown button {
  display: block;
  width: 100%;
  text-align: left;
  background: none;
  border: none;
  padding: 6px 14px;
  font-size: 0.84rem;
  cursor: pointer;
  color: var(--mc-app-text, #333);
}
.stock-actions-dropdown button:hover {
  background: var(--mc-app-bg-hover, #f5f5f5);
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

.stock-drawer__effective-cost {
  grid-column: 1 / -1;
  padding: 0.35rem 0.75rem;
  margin-bottom: 0.5rem;
  font-size: 0.85rem;
  color: var(--mc-app-text-secondary, #665);
  background: var(--mc-app-surface-2, #f5f3ef);
  border-radius: 6px;
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

.stock-drawer__adjust {
  margin-top: 1rem;
  padding: 1rem;
  border: 1px solid var(--mc-app-border-faint, #eceae5);
  border-radius: 12px;
  background: var(--mc-app-surface-2, #f9f8f6);
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}
.stock-drawer__adjust-head {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
}
.stock-drawer__adjust-title {
  margin: 0;
  font-size: 0.95rem;
  font-weight: 600;
  color: var(--mc-app-text, #1b1b1b);
}
.stock-drawer__adjust-hint {
  margin: 0;
  font-size: 0.78rem;
  color: var(--mc-app-text-muted, #6b6b6b);
  line-height: 1.35;
}
.stock-drawer__adjust-delta {
  margin: 0;
  font-size: 0.85rem;
  font-weight: 600;
  color: var(--mc-accent, #a0570c);
}

.pricing-section {
  margin-top: 1rem;
  padding: 1rem;
  border: 1px solid var(--mc-app-border-faint, #eceae5);
  border-radius: 12px;
  background: var(--mc-app-surface-2, #f9f8f6);
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}
.pricing-section__head {
  display: flex;
  justify-content: space-between;
  align-items: baseline;
  gap: 0.75rem;
  flex-wrap: wrap;
}
.pricing-section__title {
  margin: 0;
  font-size: 1rem;
  font-weight: 700;
  color: var(--mc-app-text, #1a1a1c);
}
.pricing-section__source {
  margin: 0;
  font-size: 0.8rem;
  color: var(--mc-app-text-muted, #5c5a56);
  font-weight: 500;
}
.pricing-lock {
  display: inline-flex;
  gap: 0.45rem;
  align-items: center;
  font-size: 0.88rem;
  color: var(--mc-app-text-secondary, #333336);
  padding-top: 0.35rem;
}
.pricing-preview {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(110px, 1fr));
  gap: 0.5rem 1rem;
  padding: 0.75rem;
  border: 1px solid var(--mc-app-border-faint, #eceae5);
  border-radius: 10px;
  background: #fff;
}
.pricing-preview__label {
  display: block;
  font-size: 0.7rem;
  letter-spacing: 0.08em;
  text-transform: uppercase;
  color: var(--mc-app-text-muted, #5c5a56);
  margin-bottom: 0.15rem;
}
.pricing-preview strong {
  font-size: 0.95rem;
  color: var(--mc-app-text, #1a1a1c);
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

/* ── Barcode scan-to-fill (drawer) ───────────────────────────────────── */
.stock-barcode-row {
  display: flex;
  gap: 0.5rem;
  align-items: stretch;
}
.stock-barcode-row > input {
  flex: 1 1 auto;
  min-width: 0;
}
.stock-barcode-scan {
  flex: 0 0 auto;
  display: inline-flex;
  align-items: center;
  gap: 0.4rem;
  padding: 0 0.85rem;
  border: 1.5px solid var(--mc-accent, #f47a20);
  background: var(--mc-app-surface, #fff);
  color: var(--mc-accent, #f47a20);
  border-radius: 10px;
  font-size: 0.85rem;
  font-weight: 700;
  letter-spacing: 0.04em;
  cursor: pointer;
  transition: background 0.12s ease, color 0.12s ease;
}
.stock-barcode-scan:hover {
  background: var(--mc-accent, #f47a20);
  color: #fff;
}
.stock-barcode-conflict {
  margin-top: 0.5rem;
}
.stock-barcode-help {
  margin: 0 0 0.75rem;
  font-size: 0.9rem;
  color: var(--mc-app-text-muted, #5c5a56);
}
.stock-barcode-scanner {
  border: 1px solid var(--mc-app-border-soft, #e5e2dc);
  border-radius: 12px;
  overflow: hidden;
  background: #000;
  aspect-ratio: 4 / 3;
  display: flex;
  align-items: center;
  justify-content: center;
}
.stock-barcode-scanner :deep(video) {
  width: 100%;
  height: 100%;
  object-fit: cover;
}
</style>
