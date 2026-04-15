<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { http } from '@/api/http'
import { useToast } from '@/composables/useToast'
import { formatZAR } from '@/utils/format'
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
  totalConsignmentQty: number; productCount: number
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
  qtySold: number; revenue: number; cost: number
}
type StockReport = {
  onHand: StockOnHandSummary
  consignmentBySupplier: ConsignmentBySupplier[]
  receivedInPeriod: StockMovementLine[]
  soldInPeriod: ProductSoldLine[]
}

const toast = useToast()
const invoices = ref<Row[]>([])
const daily = ref<Daily[]>([])
const err = ref<string | null>(null)

/* Stock report state */
const stockReport = ref<StockReport | null>(null)
const stockErr = ref<string | null>(null)
const stockBusy = ref(false)
const activeTab = ref<'sales' | 'stock'>('stock')

function defaultFrom() {
  const d = new Date()
  d.setDate(d.getDate() - 30)
  return d.toISOString().slice(0, 10)
}
function defaultTo() {
  return new Date().toISOString().slice(0, 10)
}

const stockFrom = ref(defaultFrom())
const stockTo = ref(defaultTo())

const expandedSuppliers = ref<Set<string>>(new Set())
function toggleSupplier(id: string) {
  if (expandedSuppliers.value.has(id)) expandedSuppliers.value.delete(id)
  else expandedSuppliers.value.add(id)
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

async function load() {
  err.value = null
  try {
    const [inv, d] = await Promise.all([
      http.get<Row[]>('/api/reports/invoices'),
      http.get<Daily[]>('/api/reports/daily?days=21')
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
  }
}

async function loadStockReport() {
  stockErr.value = null
  stockBusy.value = true
  try {
    const params: Record<string, string> = {}
    if (stockFrom.value) params.from = new Date(stockFrom.value).toISOString()
    if (stockTo.value) {
      const end = new Date(stockTo.value)
      end.setHours(23, 59, 59, 999)
      params.to = end.toISOString()
    }
    const { data } = await http.get<StockReport>('/api/reports/stock', { params })
    stockReport.value = data
  } catch (e: unknown) {
    const ax = e as { response?: { data?: { error?: string } }; message?: string }
    stockErr.value = ax.response?.data?.error ?? ax.message ?? 'Could not load stock report'
  } finally {
    stockBusy.value = false
  }
}

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

function formatDate(iso: string) {
  return new Date(iso).toLocaleDateString('en-ZA', { day: 'numeric', month: 'short', year: 'numeric' })
}

onMounted(() => {
  void load()
  void loadStockReport()
})

async function exportCsv() {
  try {
    const res = await http.get('/api/reports/invoices/export', { responseType: 'blob' })
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
</script>

<template>
  <div class="rep-page">
    <McPageHeader title="Reports">
      <template #actions>
        <McButton variant="secondary" type="button" @click="activeTab === 'sales' ? load() : loadStockReport()">Refresh</McButton>
        <McButton v-if="activeTab === 'sales'" variant="primary" type="button" @click="exportCsv">Export invoices CSV</McButton>
      </template>
    </McPageHeader>

    <div class="rep-tabs">
      <button type="button" class="rep-tab" :class="{ 'rep-tab--active': activeTab === 'stock' }" @click="activeTab = 'stock'">Stock report</button>
      <button type="button" class="rep-tab" :class="{ 'rep-tab--active': activeTab === 'sales' }" @click="activeTab = 'sales'">Sales report</button>
    </div>

    <!-- ── STOCK REPORT TAB ── -->
    <template v-if="activeTab === 'stock'">
      <McCard title="Date range">
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
            <strong class="rep-kpi__value">{{ stockReport.onHand.productCount }}</strong>
          </div>
          <div class="rep-kpi">
            <span class="rep-kpi__label">Owned stock (qty)</span>
            <strong class="rep-kpi__value">{{ stockReport.onHand.totalOwnedQty }}</strong>
          </div>
          <div class="rep-kpi">
            <span class="rep-kpi__label">Owned stock (value)</span>
            <strong class="rep-kpi__value">{{ formatZAR(stockReport.onHand.totalOwnedValue) }}</strong>
          </div>
          <div class="rep-kpi">
            <span class="rep-kpi__label">Consignment stock (qty)</span>
            <strong class="rep-kpi__value rep-kpi__value--blue">{{ stockReport.onHand.totalConsignmentQty }}</strong>
          </div>
          <div class="rep-kpi">
            <span class="rep-kpi__label">Sold in period (qty)</span>
            <strong class="rep-kpi__value">{{ totalSoldQty }}</strong>
          </div>
          <div class="rep-kpi">
            <span class="rep-kpi__label">Sold in period (revenue)</span>
            <strong class="rep-kpi__value">{{ formatZAR(totalSoldRevenue) }}</strong>
          </div>
          <div class="rep-kpi">
            <span class="rep-kpi__label">Received in period (qty)</span>
            <strong class="rep-kpi__value">{{ totalReceivedQty }}</strong>
          </div>
        </div>

        <!-- Consignment by supplier -->
        <McCard v-if="stockReport.consignmentBySupplier.length" title="Consignment stock by supplier">
          <div class="rep-table-wrap">
            <table class="mc-table">
              <thead>
                <tr>
                  <th>Supplier</th>
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
                    <td class="rep-num">{{ s.totalIn }}</td>
                    <td class="rep-num">{{ s.totalMovedToStock }}</td>
                    <td class="rep-num">{{ s.totalReturned }}</td>
                    <td class="rep-num"><strong>{{ s.onHand }}</strong></td>
                    <td>
                      <McButton variant="ghost" dense type="button" @click="toggleSupplier(s.supplierId)">
                        {{ expandedSuppliers.has(s.supplierId) ? 'Hide' : 'Details' }}
                      </McButton>
                    </td>
                  </tr>
                  <template v-if="expandedSuppliers.has(s.supplierId)">
                    <tr v-for="p in s.products" :key="p.sku" class="rep-detail-row">
                      <td class="rep-indent">{{ p.sku }} — {{ p.name }}</td>
                      <td class="rep-num">{{ p.totalIn }}</td>
                      <td class="rep-num">{{ p.totalMovedToStock }}</td>
                      <td class="rep-num">{{ p.totalReturned }}</td>
                      <td class="rep-num">{{ p.onHand }}</td>
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
                  <th class="rep-num">Cost</th>
                  <th class="rep-num">GP</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="p in stockReport.soldInPeriod" :key="p.sku">
                  <td class="rep-mono">{{ p.sku }}</td>
                  <td>{{ p.name }}</td>
                  <td class="rep-num">{{ p.qtySold }}</td>
                  <td class="rep-num">{{ formatZAR(p.revenue) }}</td>
                  <td class="rep-num">{{ formatZAR(p.cost) }}</td>
                  <td class="rep-num">{{ formatZAR(p.revenue - p.cost) }}</td>
                </tr>
              </tbody>
              <tfoot>
                <tr class="rep-total-row">
                  <td colspan="2"><strong>Totals</strong></td>
                  <td class="rep-num"><strong>{{ totalSoldQty }}</strong></td>
                  <td class="rep-num"><strong>{{ formatZAR(totalSoldRevenue) }}</strong></td>
                  <td class="rep-num"><strong>{{ formatZAR(stockReport.soldInPeriod.reduce((s, p) => s + p.cost, 0)) }}</strong></td>
                  <td class="rep-num"><strong>{{ formatZAR(totalSoldRevenue - stockReport.soldInPeriod.reduce((s, p) => s + p.cost, 0)) }}</strong></td>
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
                  <th>Supplier</th>
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
                  <td class="rep-num">{{ r.quantity }}</td>
                </tr>
              </tbody>
              <tfoot>
                <tr class="rep-total-row">
                  <td colspan="5"><strong>Total received</strong></td>
                  <td class="rep-num"><strong>{{ totalReceivedQty }}</strong></td>
                </tr>
              </tfoot>
            </table>
          </div>
        </McCard>

        <!-- Stock on hand detail -->
        <McCard title="Stock on hand (all active products)">
          <div class="rep-table-wrap">
            <table class="mc-table">
              <thead>
                <tr>
                  <th>SKU</th>
                  <th>Name</th>
                  <th>Supplier</th>
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
                  <td class="rep-num">{{ p.qtyOwned }}</td>
                  <td class="rep-num" :class="{ 'rep-blue': p.qtyConsignment > 0 }">{{ p.qtyConsignment || '—' }}</td>
                  <td class="rep-num">{{ formatZAR(p.cost) }}</td>
                  <td class="rep-num">{{ formatZAR(p.sellPrice) }}</td>
                  <td class="rep-num">{{ formatZAR(p.ownedValue) }}</td>
                </tr>
              </tbody>
              <tfoot>
                <tr class="rep-total-row">
                  <td colspan="3"><strong>Totals</strong></td>
                  <td class="rep-num"><strong>{{ stockReport.onHand.totalOwnedQty }}</strong></td>
                  <td class="rep-num"><strong>{{ stockReport.onHand.totalConsignmentQty }}</strong></td>
                  <td colspan="2"></td>
                  <td class="rep-num"><strong>{{ formatZAR(stockReport.onHand.totalOwnedValue) }}</strong></td>
                </tr>
              </tfoot>
            </table>
          </div>
        </McCard>
      </template>
    </template>

    <!-- ── SALES REPORT TAB ── -->
    <template v-if="activeTab === 'sales'">
      <McAlert v-if="err" variant="error">{{ err }}</McAlert>

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
                <td class="rep-num">{{ d.invoiceCount }}</td>
                <td class="rep-num">{{ formatZAR(d.grandTotal) }}</td>
              </tr>
            </tbody>
          </table>
        </div>
      </McCard>

      <McCard title="Recent invoices">
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
              <tr v-for="r in invoices" :key="r.id">
                <td class="rep-mono">{{ r.invoiceNumber }}</td>
                <td>{{ new Date(r.createdAt).toLocaleString() }}</td>
                <td>{{ r.customerName ?? '—' }}</td>
                <td class="rep-num">{{ formatZAR(r.grandTotal) }}</td>
                <td>{{ r.status }}</td>
                <td>
                  <McButton variant="secondary" type="button" @click="openPdf(r.id)">PDF</McButton>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </McCard>
    </template>
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
</style>
