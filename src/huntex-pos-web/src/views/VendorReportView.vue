<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { http } from '@/api/http'
import { useToast } from '@/composables/useToast'
import { formatZAR, formatNumber } from '@/utils/format'
import McPageHeader from '@/components/ui/McPageHeader.vue'
import McCard from '@/components/ui/McCard.vue'
import McButton from '@/components/ui/McButton.vue'
import McField from '@/components/ui/McField.vue'
import McAlert from '@/components/ui/McAlert.vue'
import McSpinner from '@/components/ui/McSpinner.vue'
import McEmptyState from '@/components/ui/McEmptyState.vue'

type VendorProduct = {
  sku: string
  name: string
  sellPrice: number
  cost: number
  onHand: number
  onHandValue: number
  received: number
  movedToStock: number
  returned: number
  movedFromStock: number
  sold: number
  soldRevenue: number
}

type VendorSoldLine = {
  createdAt: string
  invoiceNumber: string
  sku: string
  description: string
  quantity: number
  unitPrice: number
  lineTotal: number
}

type VendorReport = {
  supplierId: string
  supplierName: string
  from: string | null
  to: string | null
  onHand: number
  onHandValue: number
  totalReceived: number
  totalSold: number
  totalSoldRevenue: number
  totalReturned: number
  products: VendorProduct[]
  soldLines: VendorSoldLine[]
}

const toast = useToast()
const report = ref<VendorReport | null>(null)
const busy = ref(false)
const err = ref<string | null>(null)

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

const fromDate = ref(defaultFrom())
const toDate = ref(defaultTo())

type DatePreset = { label: string; from: string; to: string }
const presets = computed<DatePreset[]>(() => {
  const now = new Date()
  const today = toDateStr(now)
  const d7 = new Date(now); d7.setDate(d7.getDate() - 7)
  const d30 = new Date(now); d30.setDate(d30.getDate() - 30)
  const ytdStart = new Date(now.getFullYear(), 0, 1)
  return [
    { label: 'Last 7 days', from: toDateStr(d7), to: today },
    { label: 'Last 30 days', from: toDateStr(d30), to: today },
    { label: 'Year to date', from: toDateStr(ytdStart), to: today }
  ]
})

function applyPreset(p: DatePreset) {
  fromDate.value = p.from
  toDate.value = p.to
  void load()
}

function buildDateParams(): Record<string, string> {
  const params: Record<string, string> = {}
  if (fromDate.value) params.from = new Date(fromDate.value).toISOString()
  if (toDate.value) {
    const end = new Date(toDate.value)
    end.setHours(23, 59, 59, 999)
    params.to = end.toISOString()
  }
  return params
}

async function load() {
  err.value = null
  busy.value = true
  try {
    const { data } = await http.get<VendorReport>('/api/reports/my-vendor', { params: buildDateParams() })
    report.value = data
  } catch (e: unknown) {
    const ax = e as { response?: { status?: number; data?: { error?: string } }; message?: string }
    if (ax.response?.status === 403) {
      err.value = 'Your account is not linked to a vendor. Ask an admin to assign you to a supplier on the Team page.'
    } else {
      err.value = ax.response?.data?.error ?? ax.message ?? 'Could not load vendor report'
      toast.error(err.value)
    }
  } finally {
    busy.value = false
  }
}

onMounted(load)

function csvEsc(s: string) {
  if (s.includes('"') || s.includes(',') || s.includes('\n'))
    return '"' + s.replace(/"/g, '""') + '"'
  return s
}

function downloadCsv(filename: string, rows: string[]) {
  const blob = new Blob(['\ufeff' + rows.join('\n')], { type: 'text/csv;charset=utf-8' })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = filename
  a.click()
  URL.revokeObjectURL(url)
  toast.success('CSV downloaded')
}

function safeName() {
  const s = report.value?.supplierName ?? 'vendor'
  return s.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '') || 'vendor'
}

function exportProductsCsv() {
  if (!report.value) return
  const rows = [
    ['SKU', 'Name', 'Retail', 'Cost', 'OnHand', 'OnHandValue', 'Received', 'MovedToStock', 'Returned', 'Sold', 'Revenue'].join(','),
    ...report.value.products.map(p => [
      csvEsc(p.sku), csvEsc(p.name),
      p.sellPrice.toFixed(2), p.cost.toFixed(2),
      p.onHand, p.onHandValue.toFixed(2),
      p.received, p.movedToStock, p.returned,
      p.sold, p.soldRevenue.toFixed(2)
    ].join(','))
  ]
  rows.push([
    '', '', '', '', report.value.onHand, report.value.onHandValue.toFixed(2),
    report.value.totalReceived, '', report.value.totalReturned,
    report.value.totalSold, report.value.totalSoldRevenue.toFixed(2)
  ].join(','))
  downloadCsv(`${safeName()}-products.csv`, rows)
}

function exportSoldLinesCsv() {
  if (!report.value) return
  const rows = [
    ['When', 'Invoice', 'SKU', 'Description', 'Qty', 'UnitPrice', 'LineTotal'].join(','),
    ...report.value.soldLines.map(l => [
      csvEsc(new Date(l.createdAt).toISOString()),
      csvEsc(l.invoiceNumber),
      csvEsc(l.sku),
      csvEsc(l.description),
      l.quantity,
      l.unitPrice.toFixed(2),
      l.lineTotal.toFixed(2)
    ].join(','))
  ]
  downloadCsv(`${safeName()}-sales.csv`, rows)
}
</script>

<template>
  <div class="vend-page">
    <McPageHeader :title="report ? `${report.supplierName} — vendor report` : 'My vendor report'">
      <template #default>
        Stock on hand and sales for your consigned products only. Totals are VAT-inclusive.
      </template>
      <template #actions>
        <McButton v-if="report && report.products.length" variant="secondary" type="button" @click="exportProductsCsv">Export products CSV</McButton>
        <McButton v-if="report && report.soldLines.length" variant="primary" type="button" @click="exportSoldLinesCsv">Export sales CSV</McButton>
      </template>
    </McPageHeader>

    <McCard title="Date range">
      <div class="vend-preset-row">
        <McButton v-for="p in presets" :key="p.label" variant="ghost" dense type="button" @click="applyPreset(p)">{{ p.label }}</McButton>
      </div>
      <div class="vend-date-row">
        <McField label="From" for-id="vend-from">
          <input id="vend-from" v-model="fromDate" type="date" />
        </McField>
        <McField label="To" for-id="vend-to">
          <input id="vend-to" v-model="toDate" type="date" />
        </McField>
        <McButton variant="primary" type="button" :disabled="busy" @click="load">
          <McSpinner v-if="busy" />
          <span v-else>Run report</span>
        </McButton>
      </div>
    </McCard>

    <McAlert v-if="err" variant="error">{{ err }}</McAlert>

    <template v-if="report">
      <div class="vend-kpi-row">
        <div class="vend-kpi">
          <span class="vend-kpi__label">On-hand (qty)</span>
          <strong class="vend-kpi__value">{{ formatNumber(report.onHand) }}</strong>
        </div>
        <div class="vend-kpi">
          <span class="vend-kpi__label">On-hand (retail value)</span>
          <strong class="vend-kpi__value">{{ formatZAR(report.onHandValue) }}</strong>
        </div>
        <div class="vend-kpi">
          <span class="vend-kpi__label">Sold (qty)</span>
          <strong class="vend-kpi__value">{{ formatNumber(report.totalSold) }}</strong>
        </div>
        <div class="vend-kpi">
          <span class="vend-kpi__label">Sold revenue</span>
          <strong class="vend-kpi__value">{{ formatZAR(report.totalSoldRevenue) }}</strong>
        </div>
      </div>

      <McCard title="Products">
        <div v-if="report.products.length > 0" class="vend-card-actions">
          <McButton variant="ghost" dense type="button" @click="exportProductsCsv">Export CSV</McButton>
        </div>
        <div v-if="report.products.length === 0">
          <McEmptyState title="No stock for this vendor yet" message="When consignment stock is recorded it will appear here." />
        </div>
        <div v-else class="vend-table-wrap">
          <table class="mc-table">
            <thead>
              <tr>
                <th>SKU</th>
                <th>Name</th>
                <th class="vend-num">Retail</th>
                <th class="vend-num">On hand</th>
                <th class="vend-num">Received</th>
                <th class="vend-num">Sold</th>
                <th class="vend-num">Returned</th>
                <th class="vend-num">Revenue</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="p in report.products" :key="p.sku">
                <td class="vend-mono">{{ p.sku }}</td>
                <td>{{ p.name }}</td>
                <td class="vend-num">{{ formatZAR(p.sellPrice) }}</td>
                <td class="vend-num">{{ formatNumber(p.onHand) }}</td>
                <td class="vend-num">{{ formatNumber(p.received) }}</td>
                <td class="vend-num">{{ formatNumber(p.sold) }}</td>
                <td class="vend-num">{{ formatNumber(p.returned) }}</td>
                <td class="vend-num">{{ formatZAR(p.soldRevenue) }}</td>
              </tr>
            </tbody>
          </table>
        </div>
      </McCard>

      <McCard title="Recent sales">
        <div v-if="report.soldLines.length > 0" class="vend-card-actions">
          <McButton variant="ghost" dense type="button" @click="exportSoldLinesCsv">Export CSV</McButton>
        </div>
        <div v-if="report.soldLines.length === 0">
          <McEmptyState title="No sales yet in this range" message="Pick a wider date range or check back after the show." />
        </div>
        <div v-else class="vend-table-wrap">
          <table class="mc-table">
            <thead>
              <tr>
                <th>When</th>
                <th>Invoice</th>
                <th>SKU</th>
                <th>Description</th>
                <th class="vend-num">Qty</th>
                <th class="vend-num">Unit</th>
                <th class="vend-num">Line</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="(l, i) in report.soldLines" :key="i">
                <td>{{ new Date(l.createdAt).toLocaleString() }}</td>
                <td class="vend-mono">{{ l.invoiceNumber }}</td>
                <td class="vend-mono">{{ l.sku }}</td>
                <td>{{ l.description }}</td>
                <td class="vend-num">{{ formatNumber(l.quantity) }}</td>
                <td class="vend-num">{{ formatZAR(l.unitPrice) }}</td>
                <td class="vend-num">{{ formatZAR(l.lineTotal) }}</td>
              </tr>
            </tbody>
          </table>
        </div>
      </McCard>
    </template>
  </div>
</template>

<style scoped>
.vend-page {
  min-height: 100%;
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.vend-preset-row {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
  margin-bottom: 0.75rem;
}

.vend-date-row {
  display: flex;
  flex-wrap: wrap;
  gap: 0.75rem;
  align-items: end;
}

.vend-kpi-row {
  display: flex;
  flex-wrap: wrap;
  gap: 1rem;
}

.vend-kpi {
  flex: 1 1 160px;
  background: var(--mc-app-surface, #fff);
  border: 1px solid var(--mc-app-border-soft, #ddd9d3);
  border-radius: var(--mc-app-radius-card, 18px);
  padding: 1rem 1.25rem;
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
}

.vend-kpi__label {
  font-size: 0.78rem;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.04em;
  color: var(--mc-app-text-muted, #5c5a56);
}

.vend-kpi__value {
  font-size: 1.35rem;
  font-weight: 700;
  color: var(--mc-app-heading, #0a0a0c);
  font-variant-numeric: tabular-nums;
}

.vend-table-wrap {
  overflow-x: auto;
  -webkit-overflow-scrolling: touch;
}

.vend-card-actions {
  display: flex;
  gap: 0.5rem;
  align-items: center;
  flex-wrap: wrap;
  margin-bottom: 0.5rem;
}

.vend-num {
  text-align: right;
  font-variant-numeric: tabular-nums;
}

.vend-mono {
  font-family: var(--mc-app-mono, ui-monospace, SFMono-Regular, Menlo, monospace);
  font-size: 0.875rem;
}
</style>
