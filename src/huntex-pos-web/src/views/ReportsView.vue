<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { http } from '@/api/http'
import { useAuthStore } from '@/stores/auth'
import { useToast } from '@/composables/useToast'
import { formatZAR, formatNumber } from '@/utils/format'
import McPageHeader from '@/components/ui/McPageHeader.vue'
import McCard from '@/components/ui/McCard.vue'
import McButton from '@/components/ui/McButton.vue'
import McField from '@/components/ui/McField.vue'
import McAlert from '@/components/ui/McAlert.vue'
import McBadge from '@/components/ui/McBadge.vue'
import McSpinner from '@/components/ui/McSpinner.vue'

type Row = {
  id: string
  invoiceNumber: string
  status: string
  grandTotal: number
  createdAt: string
  customerName?: string | null
  voidReason?: string | null
  voidedAt?: string | null
  voidedByName?: string | null
}
type Daily = { date: string; invoiceCount: number; grandTotal: number }

/* Stock report types */
type StockOnHandLine = {
  sku: string; name: string; supplierName?: string | null
  qtyOwned: number; qtyConsignment: number
  cost: number; sellPrice: number; ownedValue: number
}
type StockOnHandSummary = {
  totalOwnedQty: number; totalOwnedValue: number
  totalConsignmentQty: number; totalConsignmentValue: number
  productCount: number
  lines: StockOnHandLine[]
}
type ConsignmentProductLine = {
  sku: string; name: string
  onHand: number; totalIn: number; totalMovedToStock: number; totalReturned: number
}
type ConsignmentBySupplier = {
  supplierId: string; supplierName: string
  totalIn: number; totalMovedToStock: number; totalReturned: number; onHand: number
  products: ConsignmentProductLine[]
}
type StockMovementLine = {
  sku: string; name: string; supplierName?: string | null
  type: string; quantity: number; createdAt: string
}
type ProductSoldLine = {
  sku: string; name: string
  qtySold: number; revenue: number; costExVat: number; costInclVat: number
}
type StockReport = {
  onHand: StockOnHandSummary
  consignmentBySupplier: ConsignmentBySupplier[]
  receivedInPeriod: StockMovementLine[]
  soldInPeriod: ProductSoldLine[]
}

/* Consignment report types */
type ConsignmentProductReport = {
  sku: string; name: string; sellPrice: number; cost: number
  onHand: number; onHandValue: number; received: number
  movedToStock: number; returned: number; movedFromStock: number
  sold: number; soldRevenue: number
}
type ConsignmentSupplierReport = {
  supplierId: string; supplierName: string
  onHand: number; onHandValue: number
  totalReceived: number; totalReceivedValue: number
  totalSold: number; totalSoldRevenue: number
  totalMovedToStock: number; totalReturned: number; totalMovedFromStock: number
  products: ConsignmentProductReport[]
}
type ConsignmentReport = {
  suppliers: ConsignmentSupplierReport[]
  totalOnHand: number; totalOnHandValue: number
  totalReceived: number; totalReceivedValue: number
  totalSold: number; totalSoldRevenue: number
  totalReturned: number; totalMovedFromStock: number
}
type Supplier = { id: string; name: string }

const auth = useAuthStore()
const toast = useToast()
const invoices = ref<Row[]>([])
const daily = ref<Daily[]>([])
const err = ref<string | null>(null)
const salesBusy = ref(false)

const isDev = computed(() => auth.hasRole('Dev', 'Owner'))
const canReverse = computed(() => auth.hasRole('Owner', 'Admin', 'Dev'))

const reverseTarget = ref<Row | null>(null)
const reverseReason = ref('')
const reverseBusy = ref(false)

function openReverse(r: Row) {
  reverseTarget.value = r
  reverseReason.value = ''
}
function closeReverse() {
  if (reverseBusy.value) return
  reverseTarget.value = null
  reverseReason.value = ''
}
async function confirmReverse() {
  const target = reverseTarget.value
  if (!target) return
  const reason = reverseReason.value.trim()
  if (reason.length < 3) {
    toast.error('Reason is required (min 3 characters)')
    return
  }
  reverseBusy.value = true
  try {
    await http.post(`/api/invoices/${target.id}/void`, { reason })
    toast.success(`Invoice ${target.invoiceNumber} reversed — stock returned`)
    reverseTarget.value = null
    reverseReason.value = ''
    await loadSales()
  } catch (e: unknown) {
    const msg = (e as { response?: { data?: { error?: string } } })?.response?.data?.error ?? 'Failed to reverse invoice'
    toast.error(msg)
  } finally {
    reverseBusy.value = false
  }
}

/* Stock report state */
const stockReport = ref<StockReport | null>(null)
const stockErr = ref<string | null>(null)
const stockBusy = ref(false)
const activeTab = ref<'sales' | 'stock' | 'consignment'>('stock')

/* Consignment report state */
const consignReport = ref<ConsignmentReport | null>(null)
const consignErr = ref<string | null>(null)
const consignBusy = ref(false)
const consignFrom = ref(defaultFrom())
const consignTo = ref(defaultTo())
const consignSupplierId = ref('')
const supplierList = ref<Supplier[]>([])
const expandedConsignSuppliers = ref<Set<string>>(new Set())

const showSohDetail = ref(false)

/* Purge state */
const showPurgeConfirm = ref(false)
const purging = ref(false)

function toDateStr(d: Date) {
  return d.toISOString().slice(0, 10)
}
function defaultFrom() {
  const d = new Date()
  d.setDate(d.getDate() - 30)
  return toDateStr(d)
}
function defaultTo() {
  return toDateStr(new Date())
}

const stockFrom = ref(defaultFrom())
const stockTo = ref(defaultTo())
const salesFrom = ref(defaultFrom())
const salesTo = ref(defaultTo())

type DatePreset = { label: string; from: string; to: string }
function getPresets(): DatePreset[] {
  const now = new Date()
  const today = toDateStr(now)

  const d7 = new Date(now); d7.setDate(d7.getDate() - 7)
  const d30 = new Date(now); d30.setDate(d30.getDate() - 30)

  const lastMonthEnd = new Date(now.getFullYear(), now.getMonth(), 0)
  const lastMonthStart = new Date(lastMonthEnd.getFullYear(), lastMonthEnd.getMonth(), 1)

  const curQ = Math.floor(now.getMonth() / 3)
  const lastQEnd = new Date(now.getFullYear(), curQ * 3, 0)
  const lastQStart = new Date(lastQEnd.getFullYear(), lastQEnd.getMonth() - 2, 1)

  const ytdStart = new Date(now.getFullYear(), 0, 1)

  return [
    { label: 'Last 7 days', from: toDateStr(d7), to: today },
    { label: 'Last 30 days', from: toDateStr(d30), to: today },
    { label: 'Last month', from: toDateStr(lastMonthStart), to: toDateStr(lastMonthEnd) },
    { label: 'Last quarter', from: toDateStr(lastQStart), to: toDateStr(lastQEnd) },
    { label: 'Year to date', from: toDateStr(ytdStart), to: today },
  ]
}
const presets = getPresets()

function applySalesPreset(p: DatePreset) {
  salesFrom.value = p.from
  salesTo.value = p.to
  void loadSales()
}
function applyStockPreset(p: DatePreset) {
  stockFrom.value = p.from
  stockTo.value = p.to
  void loadStockReport()
}
function applyConsignPreset(p: DatePreset) {
  consignFrom.value = p.from
  consignTo.value = p.to
  void loadConsignmentReport()
}

const expandedSuppliers = ref<Set<string>>(new Set())
function toggleSupplier(id: string) {
  if (expandedSuppliers.value.has(id)) expandedSuppliers.value.delete(id)
  else expandedSuppliers.value.add(id)
}
function toggleConsignSupplier(id: string) {
  if (expandedConsignSuppliers.value.has(id)) expandedConsignSuppliers.value.delete(id)
  else expandedConsignSuppliers.value.add(id)
}

async function openPdf(id: string) {
  try {
    const res = await http.get(`/api/invoices/${id}/pdf`, { responseType: 'blob' })
    const url = URL.createObjectURL(res.data)
    window.open(url, '_blank', 'noopener')
  } catch {
    toast.error('Could not open PDF')
  }
}

function buildDateParams(from: string, to: string): Record<string, string> {
  const params: Record<string, string> = {}
  if (from) params.from = new Date(from).toISOString()
  if (to) {
    const end = new Date(to)
    end.setHours(23, 59, 59, 999)
    params.to = end.toISOString()
  }
  return params
}

async function loadSales() {
  err.value = null
  salesBusy.value = true
  try {
    const params = buildDateParams(salesFrom.value, salesTo.value)
    const [inv, d] = await Promise.all([
      http.get<Row[]>('/api/reports/invoices', { params }),
      http.get<Daily[]>('/api/reports/daily', { params })
    ])
    invoices.value = inv.data
    daily.value = d.data
  } catch (e: unknown) {
    const ax = e as { response?: { status?: number; data?: unknown; statusText?: string }; message?: string }
    const status = ax.response?.status
    const rawData = ax.response?.data
    const detail = typeof rawData === 'object' && rawData !== null && 'error' in rawData
      ? (rawData as { error?: string }).error
      : typeof rawData === 'string' ? rawData : null
    if (status === 401 || status === 403) {
      err.value = 'You do not have permission to view reports. Only Admin / Owner roles can access this page.'
    } else if (status) {
      err.value = `Reports failed (${status}): ${detail || ax.response?.statusText || 'Unknown error'}`
    } else {
      err.value = `Cannot reach the API: ${ax.message || 'Network error'}`
    }
  } finally {
    salesBusy.value = false
  }
}

async function loadStockReport() {
  stockErr.value = null
  stockBusy.value = true
  try {
    const params = buildDateParams(stockFrom.value, stockTo.value)
    const { data } = await http.get<StockReport>('/api/reports/stock', { params })
    stockReport.value = data
  } catch (e: unknown) {
    const ax = e as { response?: { data?: { error?: string } }; message?: string }
    stockErr.value = ax.response?.data?.error ?? ax.message ?? 'Could not load stock report'
  } finally {
    stockBusy.value = false
  }
}

const salesGrandTotal = computed(() => daily.value.reduce((s, d) => s + d.grandTotal, 0))
const salesInvoiceCount = computed(() => daily.value.reduce((s, d) => s + d.invoiceCount, 0))

const totalSoldQty = computed(() => stockReport.value?.soldInPeriod.reduce((s, p) => s + p.qtySold, 0) ?? 0)
const totalSoldRevenue = computed(() => stockReport.value?.soldInPeriod.reduce((s, p) => s + p.revenue, 0) ?? 0)
const totalReceivedQty = computed(() => stockReport.value?.receivedInPeriod.reduce((s, r) => s + r.quantity, 0) ?? 0)

function receiptTypeLabel(type: string) {
  switch (type) {
    case 'OwnedIn': return 'Owned'
    case 'ConsignmentIn': return 'Consignment'
    default: return type
  }
}

async function loadSuppliers() {
  try {
    const { data } = await http.get<Supplier[]>('/api/suppliers')
    supplierList.value = data
  } catch { /* non-critical */ }
}

async function loadConsignmentReport() {
  consignErr.value = null
  consignBusy.value = true
  try {
    const params: Record<string, string> = { ...buildDateParams(consignFrom.value, consignTo.value) }
    if (consignSupplierId.value) params.supplierId = consignSupplierId.value
    const { data } = await http.get<ConsignmentReport>('/api/reports/consignment', { params })
    consignReport.value = data
  } catch (e: unknown) {
    const ax = e as { response?: { data?: { error?: string } }; message?: string }
    consignErr.value = ax.response?.data?.error ?? ax.message ?? 'Could not load consignment report'
  } finally {
    consignBusy.value = false
  }
}

function formatDate(iso: string) {
  return new Date(iso).toLocaleDateString('en-ZA', { day: 'numeric', month: 'short', year: 'numeric' })
}

onMounted(() => {
  void loadSales()
  void loadStockReport()
  void loadSuppliers()
  void loadConsignmentReport()
})

async function exportCsv() {
  try {
    const params = buildDateParams(salesFrom.value, salesTo.value)
    const res = await http.get('/api/reports/invoices/export', { params, responseType: 'blob' })
    const url = URL.createObjectURL(res.data)
    const a = document.createElement('a')
    a.href = url
    a.download = 'invoices.csv'
    a.click()
    URL.revokeObjectURL(url)
    toast.success('Invoices exported')
  } catch {
    toast.error('Export failed')
  }
}

function exportSohCsv() {
  if (!stockReport.value) return
  const lines = stockReport.value.onHand.lines
  const rows = [
    ['SKU', 'Name', 'Wholesaler', 'Owned', 'Consignment', 'Cost', 'Sell', 'Owned Value'].join(','),
    ...lines.map(p => [
      csvEsc(p.sku), csvEsc(p.name), csvEsc(p.supplierName ?? ''),
      p.qtyOwned, p.qtyConsignment, p.cost.toFixed(2), p.sellPrice.toFixed(2), p.ownedValue.toFixed(2)
    ].join(','))
  ]
  const blob = new Blob([rows.join('\n')], { type: 'text/csv' })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = 'stock-on-hand.csv'
  a.click()
  URL.revokeObjectURL(url)
  toast.success('CSV downloaded')
}

function csvEsc(s: string) {
  if (s.includes('"') || s.includes(',') || s.includes('\n'))
    return '"' + s.replace(/"/g, '""') + '"'
  return s
}

async function purgeData() {
  purging.value = true
  try {
    await http.post('/api/reports/purge')
    toast.success('All transactional data purged')
    showPurgeConfirm.value = false
    invoices.value = []
    daily.value = []
    stockReport.value = null
    void loadSales()
    void loadStockReport()
  } catch (e: unknown) {
    const ax = e as { response?: { data?: { error?: string } }; message?: string }
    toast.error(ax.response?.data?.error ?? ax.message ?? 'Purge failed')
  } finally {
    purging.value = false
  }
}
</script>

<template>
  <div class="rep-page">
    <McPageHeader title="Reports">
      <template #actions>
        <McButton variant="secondary" type="button" @click="activeTab === 'sales' ? loadSales() : activeTab === 'consignment' ? loadConsignmentReport() : loadStockReport()">Refresh</McButton>
        <McButton v-if="activeTab === 'sales'" variant="primary" type="button" @click="exportCsv">Export invoices CSV</McButton>
        <McButton v-if="isDev" variant="danger" type="button" @click="showPurgeConfirm = true">Purge all data</McButton>
      </template>
    </McPageHeader>

    <!-- Purge confirmation dialog -->
    <div v-if="showPurgeConfirm" class="rep-overlay" @click.self="showPurgeConfirm = false">
      <McCard title="Purge all transactional data?" class="rep-dialog">
        <p>This will <strong>permanently delete</strong> all invoices, stock receipts, stocktake sessions, and invoice PDFs. Product quantities will be reset to zero.</p>
        <p style="margin-top: 0.5rem; color: var(--mc-app-text-muted, #5c5a56);">Products, wholesalers, users, and settings are not affected.</p>
        <div class="rep-dialog__actions">
          <McButton variant="secondary" type="button" @click="showPurgeConfirm = false">Cancel</McButton>
          <McButton variant="danger" type="button" :disabled="purging" @click="purgeData">
            <McSpinner v-if="purging" />
            <span v-else>Yes, purge everything</span>
          </McButton>
        </div>
      </McCard>
    </div>

    <div class="rep-tabs">
      <button type="button" class="rep-tab" :class="{ 'rep-tab--active': activeTab === 'stock' }" @click="activeTab = 'stock'">Stock report</button>
      <button type="button" class="rep-tab" :class="{ 'rep-tab--active': activeTab === 'consignment' }" @click="activeTab = 'consignment'">Consignment</button>
      <button type="button" class="rep-tab" :class="{ 'rep-tab--active': activeTab === 'sales' }" @click="activeTab = 'sales'">Sales report</button>
    </div>

    <!-- ── STOCK REPORT TAB ── -->
    <template v-if="activeTab === 'stock'">
      <McCard title="Date range">
        <div class="rep-preset-row">
          <McButton v-for="p in presets" :key="p.label" variant="ghost" dense type="button" @click="applyStockPreset(p)">{{ p.label }}</McButton>
        </div>
        <div class="rep-date-row">
          <McField label="From" for-id="sr-from">
            <input id="sr-from" v-model="stockFrom" type="date" />
          </McField>
          <McField label="To" for-id="sr-to">
            <input id="sr-to" v-model="stockTo" type="date" />
          </McField>
          <McButton variant="primary" type="button" :disabled="stockBusy" @click="loadStockReport">
            <McSpinner v-if="stockBusy" />
            <span v-else>Run report</span>
          </McButton>
        </div>
      </McCard>

      <McAlert v-if="stockErr" variant="error">{{ stockErr }}</McAlert>

      <template v-if="stockReport">
        <!-- On-hand summary cards -->
        <div class="rep-kpi-row">
          <div class="rep-kpi">
            <span class="rep-kpi__label">Products</span>
            <strong class="rep-kpi__value">{{ formatNumber(stockReport.onHand.productCount) }}</strong>
          </div>
          <div class="rep-kpi">
            <span class="rep-kpi__label">Owned stock (qty)</span>
            <strong class="rep-kpi__value">{{ formatNumber(stockReport.onHand.totalOwnedQty) }}</strong>
          </div>
          <div class="rep-kpi">
            <span class="rep-kpi__label">Owned stock (value ex VAT)</span>
            <strong class="rep-kpi__value">{{ formatZAR(stockReport.onHand.totalOwnedValue) }}</strong>
          </div>
          <div class="rep-kpi">
            <span class="rep-kpi__label">Consignment stock (qty)</span>
            <strong class="rep-kpi__value rep-kpi__value--blue">{{ formatNumber(stockReport.onHand.totalConsignmentQty) }}</strong>
          </div>
          <div class="rep-kpi">
            <span class="rep-kpi__label">Consignment stock (value incl.)</span>
            <strong class="rep-kpi__value rep-kpi__value--blue">{{ formatZAR(stockReport.onHand.totalConsignmentValue) }}</strong>
          </div>
          <div class="rep-kpi">
            <span class="rep-kpi__label">Sold in period (qty)</span>
            <strong class="rep-kpi__value">{{ formatNumber(totalSoldQty) }}</strong>
          </div>
          <div class="rep-kpi">
            <span class="rep-kpi__label">Sold in period (revenue)</span>
            <strong class="rep-kpi__value">{{ formatZAR(totalSoldRevenue) }}</strong>
          </div>
          <div class="rep-kpi">
            <span class="rep-kpi__label">Received in period (qty)</span>
            <strong class="rep-kpi__value">{{ formatNumber(totalReceivedQty) }}</strong>
          </div>
        </div>

        <!-- Consignment by wholesaler -->
        <McCard v-if="stockReport.consignmentBySupplier.length" title="Consignment stock by wholesaler">
          <div class="rep-table-wrap">
            <table class="mc-table">
              <thead>
                <tr>
                  <th>Wholesaler</th>
                  <th class="rep-num">Received</th>
                  <th class="rep-num">Moved to stock</th>
                  <th class="rep-num">Returned</th>
                  <th class="rep-num">On hand</th>
                  <th></th>
                </tr>
              </thead>
              <tbody>
                <template v-for="s in stockReport.consignmentBySupplier" :key="s.supplierId">
                  <tr>
                    <td class="rep-bold">{{ s.supplierName }}</td>
                    <td class="rep-num">{{ formatNumber(s.totalIn) }}</td>
                    <td class="rep-num">{{ formatNumber(s.totalMovedToStock) }}</td>
                    <td class="rep-num">{{ formatNumber(s.totalReturned) }}</td>
                    <td class="rep-num"><strong>{{ formatNumber(s.onHand) }}</strong></td>
                    <td>
                      <McButton variant="ghost" dense type="button" @click="toggleSupplier(s.supplierId)">
                        {{ expandedSuppliers.has(s.supplierId) ? 'Hide' : 'Details' }}
                      </McButton>
                    </td>
                  </tr>
                  <template v-if="expandedSuppliers.has(s.supplierId)">
                    <tr v-for="p in s.products" :key="p.sku" class="rep-detail-row">
                      <td class="rep-indent">{{ p.sku }} — {{ p.name }}</td>
                      <td class="rep-num">{{ formatNumber(p.totalIn) }}</td>
                      <td class="rep-num">{{ formatNumber(p.totalMovedToStock) }}</td>
                      <td class="rep-num">{{ formatNumber(p.totalReturned) }}</td>
                      <td class="rep-num">{{ formatNumber(p.onHand) }}</td>
                      <td></td>
                    </tr>
                  </template>
                </template>
              </tbody>
            </table>
          </div>
        </McCard>

        <!-- Sold in period -->
        <McCard v-if="stockReport.soldInPeriod.length" title="Products sold in period">
          <div class="rep-table-wrap">
            <table class="mc-table">
              <thead>
                <tr>
                  <th>SKU</th>
                  <th>Name</th>
                  <th class="rep-num">Qty sold</th>
                  <th class="rep-num">Revenue</th>
                  <th class="rep-num">Cost ex VAT</th>
                  <th class="rep-num">Cost incl VAT</th>
                  <th class="rep-num">GP</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="p in stockReport.soldInPeriod" :key="p.sku">
                  <td class="rep-mono">{{ p.sku }}</td>
                  <td>{{ p.name }}</td>
                  <td class="rep-num">{{ formatNumber(p.qtySold) }}</td>
                  <td class="rep-num">{{ formatZAR(p.revenue) }}</td>
                  <td class="rep-num">{{ formatZAR(p.costExVat) }}</td>
                  <td class="rep-num">{{ formatZAR(p.costInclVat) }}</td>
                  <td class="rep-num">{{ formatZAR(p.revenue - p.costInclVat) }}</td>
                </tr>
              </tbody>
              <tfoot>
                <tr class="rep-total-row">
                  <td colspan="2"><strong>Totals</strong></td>
                  <td class="rep-num"><strong>{{ formatNumber(totalSoldQty) }}</strong></td>
                  <td class="rep-num"><strong>{{ formatZAR(totalSoldRevenue) }}</strong></td>
                  <td class="rep-num"><strong>{{ formatZAR(stockReport.soldInPeriod.reduce((s, p) => s + p.costExVat, 0)) }}</strong></td>
                  <td class="rep-num"><strong>{{ formatZAR(stockReport.soldInPeriod.reduce((s, p) => s + p.costInclVat, 0)) }}</strong></td>
                  <td class="rep-num"><strong>{{ formatZAR(totalSoldRevenue - stockReport.soldInPeriod.reduce((s, p) => s + p.costInclVat, 0)) }}</strong></td>
                </tr>
              </tfoot>
            </table>
          </div>
        </McCard>

        <!-- Received in period -->
        <McCard v-if="stockReport.receivedInPeriod.length" title="Stock received in period">
          <div class="rep-table-wrap">
            <table class="mc-table">
              <thead>
                <tr>
                  <th>Date</th>
                  <th>SKU</th>
                  <th>Name</th>
                  <th>Wholesaler</th>
                  <th>Type</th>
                  <th class="rep-num">Qty</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="(r, i) in stockReport.receivedInPeriod" :key="i">
                  <td class="rep-mono">{{ formatDate(r.createdAt) }}</td>
                  <td class="rep-mono">{{ r.sku }}</td>
                  <td>{{ r.name }}</td>
                  <td>{{ r.supplierName ?? '—' }}</td>
                  <td><McBadge :variant="r.type === 'OwnedIn' ? 'success' : 'warning'">{{ receiptTypeLabel(r.type) }}</McBadge></td>
                  <td class="rep-num">{{ formatNumber(r.quantity) }}</td>
                </tr>
              </tbody>
              <tfoot>
                <tr class="rep-total-row">
                  <td colspan="5"><strong>Total received</strong></td>
                  <td class="rep-num"><strong>{{ formatNumber(totalReceivedQty) }}</strong></td>
                </tr>
              </tfoot>
            </table>
          </div>
        </McCard>

        <!-- Stock on hand detail (collapsed by default) -->
        <McCard title="Stock on hand (all active products)">
          <div style="display:flex;gap:0.75rem;align-items:center;flex-wrap:wrap;margin-bottom:0.5rem">
            <McButton variant="secondary" dense type="button" @click="showSohDetail = !showSohDetail">
              {{ showSohDetail ? 'Hide detail' : 'Show detail' }} ({{ stockReport.onHand.lines.length }} products)
            </McButton>
            <McButton variant="ghost" dense type="button" @click="exportSohCsv">Export CSV</McButton>
          </div>

          <div v-if="showSohDetail" class="rep-table-wrap">
            <table class="mc-table">
              <thead>
                <tr>
                  <th>SKU</th>
                  <th>Name</th>
                  <th>Wholesaler</th>
                  <th class="rep-num">Owned</th>
                  <th class="rep-num">Consign</th>
                  <th class="rep-num">Cost</th>
                  <th class="rep-num">Sell</th>
                  <th class="rep-num">Owned value</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="p in stockReport.onHand.lines" :key="p.sku">
                  <td class="rep-mono">{{ p.sku }}</td>
                  <td>{{ p.name }}</td>
                  <td>{{ p.supplierName ?? '—' }}</td>
                  <td class="rep-num">{{ formatNumber(p.qtyOwned) }}</td>
                  <td class="rep-num" :class="{ 'rep-blue': p.qtyConsignment > 0 }">{{ p.qtyConsignment ? formatNumber(p.qtyConsignment) : '—' }}</td>
                  <td class="rep-num">{{ formatZAR(p.cost) }}</td>
                  <td class="rep-num">{{ formatZAR(p.sellPrice) }}</td>
                  <td class="rep-num">{{ formatZAR(p.ownedValue) }}</td>
                </tr>
              </tbody>
              <tfoot>
                <tr class="rep-total-row">
                  <td colspan="3"><strong>Totals</strong></td>
                  <td class="rep-num"><strong>{{ formatNumber(stockReport.onHand.totalOwnedQty) }}</strong></td>
                  <td class="rep-num"><strong>{{ formatNumber(stockReport.onHand.totalConsignmentQty) }}</strong></td>
                  <td colspan="2"></td>
                  <td class="rep-num"><strong>{{ formatZAR(stockReport.onHand.totalOwnedValue) }}</strong></td>
                </tr>
              </tfoot>
            </table>
          </div>
        </McCard>
      </template>
    </template>

    <!-- ── CONSIGNMENT REPORT TAB ── -->
    <template v-if="activeTab === 'consignment'">
      <McCard title="Consignment report filters">
        <div class="rep-preset-row">
          <McButton v-for="p in presets" :key="p.label" variant="ghost" dense type="button" @click="applyConsignPreset(p)">{{ p.label }}</McButton>
        </div>
        <div class="rep-date-row">
          <McField label="Wholesaler" for-id="cr-supplier">
            <select id="cr-supplier" v-model="consignSupplierId" style="min-width: 180px">
              <option value="">All wholesalers</option>
              <option v-for="s in supplierList" :key="s.id" :value="s.id">{{ s.name }}</option>
            </select>
          </McField>
          <McField label="Sold from" for-id="cr-from">
            <input id="cr-from" v-model="consignFrom" type="date" />
          </McField>
          <McField label="Sold to" for-id="cr-to">
            <input id="cr-to" v-model="consignTo" type="date" />
          </McField>
          <McButton variant="primary" type="button" :disabled="consignBusy" @click="loadConsignmentReport">
            <McSpinner v-if="consignBusy" />
            <span v-else>Run report</span>
          </McButton>
        </div>
      </McCard>

      <McAlert v-if="consignErr" variant="error">{{ consignErr }}</McAlert>

      <template v-if="consignReport">
        <div class="rep-kpi-row">
          <div class="rep-kpi">
            <span class="rep-kpi__label">Consignment on hand</span>
            <strong class="rep-kpi__value rep-kpi__value--blue">{{ formatNumber(consignReport.totalOnHand) }}</strong>
          </div>
          <div class="rep-kpi">
            <span class="rep-kpi__label">On hand value (sell)</span>
            <strong class="rep-kpi__value rep-kpi__value--blue">{{ formatZAR(consignReport.totalOnHandValue) }}</strong>
          </div>
          <div class="rep-kpi">
            <span class="rep-kpi__label">Total received</span>
            <strong class="rep-kpi__value">{{ formatNumber(consignReport.totalReceived) }}</strong>
          </div>
          <div class="rep-kpi">
            <span class="rep-kpi__label">Received value (sell)</span>
            <strong class="rep-kpi__value">{{ formatZAR(consignReport.totalReceivedValue) }}</strong>
          </div>
          <div class="rep-kpi">
            <span class="rep-kpi__label">Sold in period (qty)</span>
            <strong class="rep-kpi__value">{{ formatNumber(consignReport.totalSold) }}</strong>
          </div>
          <div class="rep-kpi">
            <span class="rep-kpi__label">Sold in period (revenue)</span>
            <strong class="rep-kpi__value">{{ formatZAR(consignReport.totalSoldRevenue) }}</strong>
          </div>
          <div class="rep-kpi">
            <span class="rep-kpi__label">Returned to wholesaler</span>
            <strong class="rep-kpi__value">{{ formatNumber(consignReport.totalReturned) }}</strong>
          </div>
        </div>

        <McCard v-for="s in consignReport.suppliers" :key="s.supplierId" :title="s.supplierName">
          <div class="rep-kpi-row" style="margin-bottom: 0.75rem">
            <div class="rep-kpi rep-kpi--sm">
              <span class="rep-kpi__label">On hand</span>
              <strong class="rep-kpi__value rep-kpi__value--blue">{{ formatNumber(s.onHand) }}</strong>
            </div>
            <div class="rep-kpi rep-kpi--sm">
              <span class="rep-kpi__label">On hand value</span>
              <strong class="rep-kpi__value rep-kpi__value--blue">{{ formatZAR(s.onHandValue) }}</strong>
            </div>
            <div class="rep-kpi rep-kpi--sm">
              <span class="rep-kpi__label">Received</span>
              <strong class="rep-kpi__value">{{ formatNumber(s.totalReceived) }}</strong>
            </div>
            <div class="rep-kpi rep-kpi--sm">
              <span class="rep-kpi__label">Moved to stock</span>
              <strong class="rep-kpi__value">{{ formatNumber(s.totalMovedToStock) }}</strong>
            </div>
            <div class="rep-kpi rep-kpi--sm">
              <span class="rep-kpi__label">Returned</span>
              <strong class="rep-kpi__value">{{ formatNumber(s.totalReturned) }}</strong>
            </div>
            <div class="rep-kpi rep-kpi--sm">
              <span class="rep-kpi__label">Sold (period)</span>
              <strong class="rep-kpi__value">{{ formatNumber(s.totalSold) }}</strong>
            </div>
            <div class="rep-kpi rep-kpi--sm">
              <span class="rep-kpi__label">Revenue (period)</span>
              <strong class="rep-kpi__value">{{ formatZAR(s.totalSoldRevenue) }}</strong>
            </div>
          </div>

          <McButton variant="ghost" dense type="button" @click="toggleConsignSupplier(s.supplierId)" style="margin-bottom: 0.5rem">
            {{ expandedConsignSuppliers.has(s.supplierId) ? 'Hide product detail' : 'Show product detail' }}
          </McButton>

          <div v-if="expandedConsignSuppliers.has(s.supplierId)" class="rep-table-wrap">
            <table class="mc-table">
              <thead>
                <tr>
                  <th>SKU</th>
                  <th>Name</th>
                  <th class="rep-num">Cost</th>
                  <th class="rep-num">Sell</th>
                  <th class="rep-num">Received</th>
                  <th class="rep-num">Moved→Stock</th>
                  <th class="rep-num">Stock→Consign</th>
                  <th class="rep-num">Returned</th>
                  <th class="rep-num">On hand</th>
                  <th class="rep-num">On hand value</th>
                  <th class="rep-num">Sold</th>
                  <th class="rep-num">Revenue</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="p in s.products" :key="p.sku">
                  <td class="rep-mono">{{ p.sku }}</td>
                  <td>{{ p.name }}</td>
                  <td class="rep-num">{{ formatZAR(p.cost) }}</td>
                  <td class="rep-num">{{ formatZAR(p.sellPrice) }}</td>
                  <td class="rep-num">{{ formatNumber(p.received) }}</td>
                  <td class="rep-num">{{ formatNumber(p.movedToStock) }}</td>
                  <td class="rep-num">{{ p.movedFromStock ? formatNumber(p.movedFromStock) : '—' }}</td>
                  <td class="rep-num">{{ formatNumber(p.returned) }}</td>
                  <td class="rep-num"><strong :class="{ 'rep-blue': p.onHand > 0 }">{{ formatNumber(p.onHand) }}</strong></td>
                  <td class="rep-num">{{ formatZAR(p.onHandValue) }}</td>
                  <td class="rep-num">{{ formatNumber(p.sold) }}</td>
                  <td class="rep-num">{{ formatZAR(p.soldRevenue) }}</td>
                </tr>
              </tbody>
              <tfoot>
                <tr class="rep-total-row">
                  <td colspan="4"><strong>Totals</strong></td>
                  <td class="rep-num"><strong>{{ formatNumber(s.totalReceived) }}</strong></td>
                  <td class="rep-num"><strong>{{ formatNumber(s.totalMovedToStock) }}</strong></td>
                  <td class="rep-num"><strong>{{ s.totalMovedFromStock ? formatNumber(s.totalMovedFromStock) : '—' }}</strong></td>
                  <td class="rep-num"><strong>{{ formatNumber(s.totalReturned) }}</strong></td>
                  <td class="rep-num"><strong>{{ formatNumber(s.onHand) }}</strong></td>
                  <td class="rep-num"><strong>{{ formatZAR(s.onHandValue) }}</strong></td>
                  <td class="rep-num"><strong>{{ formatNumber(s.totalSold) }}</strong></td>
                  <td class="rep-num"><strong>{{ formatZAR(s.totalSoldRevenue) }}</strong></td>
                </tr>
              </tfoot>
            </table>
          </div>
        </McCard>

        <McCard v-if="!consignReport.suppliers.length" title="No consignment data">
          <p>No consignment stock movements found{{ consignSupplierId ? ' for the selected wholesaler' : '' }}.</p>
        </McCard>
      </template>
    </template>

    <!-- ── SALES REPORT TAB ── -->
    <template v-if="activeTab === 'sales'">
      <McCard title="Date range">
        <div class="rep-preset-row">
          <McButton v-for="p in presets" :key="p.label" variant="ghost" dense type="button" @click="applySalesPreset(p)">{{ p.label }}</McButton>
        </div>
        <div class="rep-date-row">
          <McField label="From" for-id="sl-from">
            <input id="sl-from" v-model="salesFrom" type="date" />
          </McField>
          <McField label="To" for-id="sl-to">
            <input id="sl-to" v-model="salesTo" type="date" />
          </McField>
          <McButton variant="primary" type="button" :disabled="salesBusy" @click="loadSales">
            <McSpinner v-if="salesBusy" />
            <span v-else>Run report</span>
          </McButton>
        </div>
      </McCard>

      <McAlert v-if="err" variant="error">{{ err }}</McAlert>

      <div class="rep-kpi-row">
        <div class="rep-kpi">
          <span class="rep-kpi__label">Invoices</span>
          <strong class="rep-kpi__value">{{ formatNumber(salesInvoiceCount) }}</strong>
        </div>
        <div class="rep-kpi">
          <span class="rep-kpi__label">Total sales</span>
          <strong class="rep-kpi__value">{{ formatZAR(salesGrandTotal) }}</strong>
        </div>
      </div>

      <McCard title="Daily totals (final invoices)">
        <div class="rep-table-wrap">
          <table class="mc-table">
            <thead>
              <tr>
                <th>Date</th>
                <th class="rep-num">Count</th>
                <th class="rep-num">Total</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="d in daily" :key="d.date">
                <td>{{ d.date }}</td>
                <td class="rep-num">{{ formatNumber(d.invoiceCount) }}</td>
                <td class="rep-num">{{ formatZAR(d.grandTotal) }}</td>
              </tr>
            </tbody>
            <tfoot v-if="daily.length">
              <tr class="rep-total-row">
                <td><strong>Total</strong></td>
                <td class="rep-num"><strong>{{ formatNumber(salesInvoiceCount) }}</strong></td>
                <td class="rep-num"><strong>{{ formatZAR(salesGrandTotal) }}</strong></td>
              </tr>
            </tfoot>
          </table>
        </div>
      </McCard>

      <McCard title="Invoices">
        <div class="rep-table-wrap">
          <table class="mc-table">
            <thead>
              <tr>
                <th>Invoice</th>
                <th>When</th>
                <th>Customer</th>
                <th class="rep-num">Total</th>
                <th>Status</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="r in invoices" :key="r.id" :class="{ 'rep-row--voided': r.status === 'Voided' }">
                <td class="rep-mono">{{ r.invoiceNumber }}</td>
                <td>{{ new Date(r.createdAt).toLocaleString() }}</td>
                <td>{{ r.customerName ?? '—' }}</td>
                <td class="rep-num">{{ formatZAR(r.grandTotal) }}</td>
                <td>
                  <McBadge v-if="r.status === 'Voided'" variant="danger" :title="r.voidReason ? `${r.voidReason}${r.voidedByName ? ' — ' + r.voidedByName : ''}${r.voidedAt ? ' (' + new Date(r.voidedAt).toLocaleString() + ')' : ''}` : undefined">Reversed</McBadge>
                  <span v-else>{{ r.status }}</span>
                </td>
                <td>
                  <div class="rep-row-actions">
                    <McButton variant="secondary" type="button" @click="openPdf(r.id)">PDF</McButton>
                    <McButton
                      v-if="canReverse && r.status !== 'Voided'"
                      variant="danger"
                      type="button"
                      dense
                      @click="openReverse(r)"
                    >Reverse</McButton>
                  </div>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </McCard>
    </template>

    <div v-if="reverseTarget" class="rep-overlay" @click.self="closeReverse">
      <McCard :title="`Reverse invoice ${reverseTarget.invoiceNumber}?`" class="rep-dialog">
        <p>
          This will mark the invoice as <strong>Reversed</strong> and
          <strong>return {{ reverseTarget.customerName ? 'the items from ' + reverseTarget.customerName : 'all line items' }} to stock</strong>.
          The invoice record, line items, and PDF are kept for audit.
        </p>
        <McField label="Reason" for-id="rev-reason">
          <textarea
            id="rev-reason"
            v-model="reverseReason"
            rows="3"
            maxlength="500"
            placeholder="e.g. Customer returned items, duplicate sale, wrong product rang up…"
            :disabled="reverseBusy"
          ></textarea>
        </McField>
        <div class="rep-dialog__actions">
          <McButton variant="secondary" type="button" :disabled="reverseBusy" @click="closeReverse">Cancel</McButton>
          <McButton variant="danger" type="button" :disabled="reverseBusy || reverseReason.trim().length < 3" @click="confirmReverse">
            <McSpinner v-if="reverseBusy" />
            <span v-else>Reverse & return stock</span>
          </McButton>
        </div>
      </McCard>
    </div>
  </div>
</template>

<style scoped>
.rep-page {
  min-height: 100%;
}

.rep-tabs {
  display: flex;
  gap: 0;
  margin-bottom: 1.25rem;
  border-bottom: 2px solid var(--mc-app-border-faint, #eceae5);
}

.rep-tab {
  padding: 0.75rem 1.5rem;
  border: none;
  background: none;
  font-weight: 600;
  font-size: 0.95rem;
  color: var(--mc-app-text-muted, #5c5a56);
  cursor: pointer;
  border-bottom: 3px solid transparent;
  margin-bottom: -2px;
  transition: color 0.15s, border-color 0.15s;
}

.rep-tab:hover {
  color: var(--mc-app-text, #1a1a1c);
}

.rep-tab--active {
  color: var(--mc-accent, #f47a20);
  border-bottom-color: var(--mc-accent, #f47a20);
}

.rep-date-row {
  display: flex;
  flex-wrap: wrap;
  align-items: flex-end;
  gap: 1rem;
}

.rep-date-row :deep(.mc-field) {
  margin-bottom: 0;
}

.rep-kpi-row {
  display: flex;
  flex-wrap: wrap;
  gap: 1rem;
  margin-bottom: 1.25rem;
}

.rep-kpi {
  flex: 1 1 140px;
  background: var(--mc-app-surface, #fff);
  border: 1px solid var(--mc-app-border-soft, #ddd9d3);
  border-radius: var(--mc-app-radius-card, 18px);
  padding: 1rem 1.25rem;
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
}

.rep-kpi__label {
  font-size: 0.78rem;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.04em;
  color: var(--mc-app-text-muted, #5c5a56);
}

.rep-kpi__value {
  font-size: 1.5rem;
  font-weight: 700;
  color: var(--mc-app-heading, #0a0a0c);
  font-variant-numeric: tabular-nums;
}

.rep-kpi__value--blue {
  color: #1565c0;
}

.rep-kpi--sm {
  padding: 0.625rem 0.875rem;
}

.rep-kpi--sm .rep-kpi__value {
  font-size: 1.15rem;
}

.rep-table-wrap {
  overflow-x: auto;
  -webkit-overflow-scrolling: touch;
}

.rep-num {
  text-align: right;
  font-variant-numeric: tabular-nums;
}

.rep-mono {
  font-variant-numeric: tabular-nums;
  font-weight: 600;
}

.rep-bold {
  font-weight: 700;
}

.rep-blue {
  color: #1565c0;
  font-weight: 600;
}

.rep-detail-row td {
  background: var(--mc-app-surface-2, #f9f8f6);
  font-size: 0.85rem;
}

.rep-indent {
  padding-left: 2rem !important;
}

.rep-total-row td {
  border-top: 2px solid var(--mc-app-border-subtle, #c8c5bd);
  background: var(--mc-app-surface-2, #f9f8f6);
}

.rep-preset-row {
  display: flex;
  flex-wrap: wrap;
  gap: 0.375rem;
  margin-bottom: 0.75rem;
}

.rep-row-actions {
  display: flex;
  gap: 0.375rem;
  justify-content: flex-end;
}

.rep-row--voided td {
  color: var(--mc-app-text-muted, #5c5a56);
  text-decoration: line-through;
  text-decoration-color: rgba(200, 50, 50, 0.55);
}

.rep-row--voided td:last-child,
.rep-row--voided td:nth-child(5) {
  text-decoration: none;
}

.rep-overlay {
  position: fixed;
  inset: 0;
  z-index: 999;
  background: rgba(0, 0, 0, 0.45);
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 1rem;
}

.rep-dialog {
  max-width: 480px;
  width: 100%;
}

.rep-dialog__actions {
  display: flex;
  justify-content: flex-end;
  gap: 0.75rem;
  margin-top: 1.25rem;
}
</style>
