<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { http } from '@/api/http'
import { formatZAR, formatNumber } from '@/utils/format'
import { commissionSummary, type CommissionBasis } from '@/composables/useSalespeople'
import McCard from '@/components/ui/McCard.vue'
import McButton from '@/components/ui/McButton.vue'
import McField from '@/components/ui/McField.vue'
import McAlert from '@/components/ui/McAlert.vue'
import McBadge from '@/components/ui/McBadge.vue'
import McSpinner from '@/components/ui/McSpinner.vue'
import McEmptyState from '@/components/ui/McEmptyState.vue'
import { ChevronLeft, ChevronRight } from 'lucide-vue-next'

type ReportRow = {
  salespersonId: string | null
  name: string
  isActive: boolean
  salesCount: number
  grossSalesInclVat: number
  returnsInclVat: number
  netSalesInclVat: number
  netSalesExVat: number
  grossProfitExVat: number
  commissionPercent: number
  commissionBasis: CommissionBasis
  commission: number
}

function monthValue(d: Date): string {
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}`
}

const month = ref(monthValue(new Date()))
const rows = ref<ReportRow[]>([])
const busy = ref(false)
const err = ref<string | null>(null)
const loadedMonth = ref<string | null>(null)

/** Local-time month bounds so a sale at 23:30 on the last day lands in the right month. */
function monthRange(value: string): { from: Date; to: Date } {
  const [y, m] = value.split('-').map(Number)
  return { from: new Date(y, m - 1, 1), to: new Date(y, m, 0, 23, 59, 59, 999) }
}

const monthLabel = computed(() => {
  if (!loadedMonth.value) return ''
  return monthRange(loadedMonth.value).from.toLocaleDateString('en-ZA', { month: 'long', year: 'numeric' })
})

const totals = computed(() => ({
  salesCount: rows.value.reduce((s, r) => s + r.salesCount, 0),
  netSalesInclVat: rows.value.reduce((s, r) => s + r.netSalesInclVat, 0),
  netSalesExVat: rows.value.reduce((s, r) => s + r.netSalesExVat, 0),
  grossProfitExVat: rows.value.reduce((s, r) => s + r.grossProfitExVat, 0),
  commission: rows.value.reduce((s, r) => s + r.commission, 0)
}))

async function load() {
  if (!month.value) return
  busy.value = true
  err.value = null
  try {
    const { from, to } = monthRange(month.value)
    const { data } = await http.get<{ rows: ReportRow[] }>('/api/reports/salespeople', {
      params: { from: from.toISOString(), to: to.toISOString() }
    })
    rows.value = data.rows
    loadedMonth.value = month.value
  } catch (e: unknown) {
    const ax = e as { response?: { data?: { error?: string } } }
    err.value = ax.response?.data?.error ?? 'Could not load the salesperson report.'
  } finally {
    busy.value = false
  }
}

function shiftMonth(delta: number) {
  const { from } = monthRange(month.value)
  month.value = monthValue(new Date(from.getFullYear(), from.getMonth() + delta, 1))
  void load()
}

function commissionNote(r: ReportRow): string {
  if (!r.salespersonId) return '—'
  return commissionSummary(r.commissionPercent, r.commissionBasis)
}

function exportCsv() {
  const header = ['Salesperson', 'Sales', 'Gross sales incl VAT', 'Returns incl VAT', 'Net sales incl VAT', 'Net sales excl VAT', 'Gross profit excl VAT', 'Commission rate', 'Commission']
  const esc = (v: string) => (/[",\n]/.test(v) ? `"${v.replace(/"/g, '""')}"` : v)
  const lines = rows.value.map(r => [
    r.name,
    String(r.salesCount),
    r.grossSalesInclVat.toFixed(2),
    r.returnsInclVat.toFixed(2),
    r.netSalesInclVat.toFixed(2),
    r.netSalesExVat.toFixed(2),
    r.grossProfitExVat.toFixed(2),
    commissionNote(r),
    r.commission.toFixed(2)
  ].map(esc).join(','))
  const blob = new Blob([[header.join(','), ...lines].join('\n')], { type: 'text/csv' })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = `salespeople-${loadedMonth.value}.csv`
  a.click()
  URL.revokeObjectURL(url)
}

onMounted(load)
</script>

<template>
  <McCard title="Month">
    <div class="spr-controls">
      <McButton variant="secondary" type="button" aria-label="Previous month" @click="shiftMonth(-1)"><ChevronLeft :size="18" /></McButton>
      <McField label="Month" for-id="spr-month">
        <input id="spr-month" v-model="month" type="month" @change="load" />
      </McField>
      <McButton variant="secondary" type="button" aria-label="Next month" @click="shiftMonth(1)"><ChevronRight :size="18" /></McButton>
      <McButton variant="primary" type="button" :disabled="busy" @click="load">
        <McSpinner v-if="busy" />
        <span v-else>Run report</span>
      </McButton>
      <McButton v-if="rows.length" variant="secondary" type="button" @click="exportCsv">Export CSV</McButton>
    </div>
    <p class="spr-note">
      Returns are deducted from the original salesperson in the month the item came back, so a month you've
      already paid out never changes. Voided sales are excluded. “Unassigned” covers older sales and online orders.
    </p>
  </McCard>

  <McAlert v-if="err" variant="error">{{ err }}</McAlert>

  <McCard v-if="loadedMonth" :title="`Salespeople — ${monthLabel}`">
    <McEmptyState
      v-if="!rows.length"
      title="No sales this month"
      hint="Add salespeople under Manage → Salespeople, then pick one at checkout for each sale."
    />
    <div v-else class="spr-table-wrap">
      <table class="mc-table">
        <thead>
          <tr>
            <th>Salesperson</th>
            <th class="spr-num">Sales</th>
            <th class="spr-num">Net sales incl. VAT</th>
            <th class="spr-num">Net sales excl. VAT</th>
            <th class="spr-num">Gross profit excl. VAT</th>
            <th>Commission rate</th>
            <th class="spr-num">Commission</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="r in rows" :key="r.salespersonId ?? 'unassigned'" :class="{ 'spr-row--muted': !r.salespersonId }">
            <td>
              <strong>{{ r.name }}</strong>
              <McBadge v-if="r.salespersonId && !r.isActive" variant="neutral" class="spr-badge">Inactive</McBadge>
            </td>
            <td class="spr-num">{{ formatNumber(r.salesCount) }}</td>
            <td class="spr-num">
              {{ formatZAR(r.netSalesInclVat) }}
              <small v-if="r.returnsInclVat" class="spr-returns">after {{ formatZAR(r.returnsInclVat) }} returns</small>
            </td>
            <td class="spr-num">{{ formatZAR(r.netSalesExVat) }}</td>
            <td class="spr-num">{{ formatZAR(r.grossProfitExVat) }}</td>
            <td class="spr-basis">{{ commissionNote(r) }}</td>
            <td class="spr-num"><strong>{{ r.salespersonId ? formatZAR(r.commission) : '—' }}</strong></td>
          </tr>
        </tbody>
        <tfoot>
          <tr>
            <th>Total</th>
            <th class="spr-num">{{ formatNumber(totals.salesCount) }}</th>
            <th class="spr-num">{{ formatZAR(totals.netSalesInclVat) }}</th>
            <th class="spr-num">{{ formatZAR(totals.netSalesExVat) }}</th>
            <th class="spr-num">{{ formatZAR(totals.grossProfitExVat) }}</th>
            <th></th>
            <th class="spr-num">{{ formatZAR(totals.commission) }}</th>
          </tr>
        </tfoot>
      </table>
    </div>
  </McCard>
</template>

<style scoped>
.spr-controls {
  display: flex;
  flex-wrap: wrap;
  align-items: flex-end;
  gap: 0.75rem;
}
.spr-controls :deep(.mc-field) {
  margin-bottom: 0;
}
.spr-note {
  margin: 0.85rem 0 0;
  font-size: 0.85rem;
  color: var(--mc-app-text-muted, #5c5a56);
}
.spr-table-wrap {
  overflow-x: auto;
  -webkit-overflow-scrolling: touch;
}
.spr-num {
  text-align: right;
  font-variant-numeric: tabular-nums;
  white-space: nowrap;
}
.spr-returns {
  display: block;
  font-size: 0.75rem;
  color: var(--mc-app-text-muted, #5c5a56);
}
.spr-basis {
  font-size: 0.85rem;
  color: var(--mc-app-text-secondary, #555);
}
.spr-badge {
  margin-left: 0.4rem;
}
.spr-row--muted td {
  color: var(--mc-app-text-muted, #5c5a56);
}
</style>
