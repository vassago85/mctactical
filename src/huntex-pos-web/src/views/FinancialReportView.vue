<script setup lang="ts">
import { computed, nextTick, onMounted, ref } from 'vue'
import { http } from '@/api/http'
import { useBranding } from '@/composables/useBranding'
import { formatZAR, formatNumber } from '@/utils/format'
import McButton from '@/components/ui/McButton.vue'
import McField from '@/components/ui/McField.vue'
import McSpinner from '@/components/ui/McSpinner.vue'
import McMetricCard from '@/components/ui/McMetricCard.vue'
import { Eye, EyeOff } from 'lucide-vue-next'
import { usePrivacyMode } from '@/composables/usePrivacyMode'
import { Chart, registerables } from 'chart.js'

Chart.register(...registerables)

const { businessName, logoUrl } = useBranding()
const { privacyActive, toggle: togglePrivacy } = usePrivacyMode()

type ProductSoldLine = {
  sku: string; name: string
  qtySold: number; revenue: number; discount: number; costExVat: number; costInclVat: number
}
type StockReport = { soldInPeriod: ProductSoldLine[] }
type DailySummary = { date: string; invoiceCount: number; grandTotal: number; grossProfit: number }
type PaymentMethodBreakdown = { method: string; count: number; grandTotal: number }
type PaymentsSummary = { totalGrand: number; totalCount: number; byMethod: PaymentMethodBreakdown[] }

const busy = ref(false)
const err = ref<string | null>(null)
const stock = ref<StockReport | null>(null)
const daily = ref<DailySummary[]>([])
const payments = ref<PaymentsSummary | null>(null)

function toDateStr(d: Date) { return d.toISOString().slice(0, 10) }
function defaultFrom() { const d = new Date(); d.setDate(d.getDate() - 30); return toDateStr(d) }
function defaultTo() { return toDateStr(new Date()) }

const fromDate = ref(defaultFrom())
const toDate = ref(defaultTo())

function buildDateParams() {
  const params: Record<string, string> = {}
  if (fromDate.value) params.from = new Date(fromDate.value).toISOString()
  if (toDate.value) {
    const end = new Date(toDate.value)
    end.setHours(23, 59, 59, 999)
    params.to = end.toISOString()
  }
  return params
}

async function loadReport() {
  busy.value = true
  err.value = null
  try {
    const params = buildDateParams()
    const [s, d, p] = await Promise.all([
      http.get<StockReport>('/api/reports/stock', { params }),
      http.get<DailySummary[]>('/api/reports/daily', { params }),
      http.get<PaymentsSummary>('/api/reports/payments', { params })
    ])
    stock.value = s.data
    daily.value = d.data
    payments.value = p.data
    busy.value = false
    await nextTick()
    renderCharts()
  } catch {
    err.value = 'Failed to load report data'
  } finally {
    busy.value = false
  }
}

onMounted(loadReport)

const sold = computed(() => stock.value?.soldInPeriod ?? [])
const totalRevenue = computed(() => sold.value.reduce((s, p) => s + p.revenue, 0))
const totalDiscount = computed(() => sold.value.reduce((s, p) => s + p.discount, 0))
const totalCostInclVat = computed(() => sold.value.reduce((s, p) => s + p.costInclVat, 0))
const totalGP = computed(() => (totalRevenue.value - totalDiscount.value) - totalCostInclVat.value)
const gpMargin = computed(() => {
  const netRev = totalRevenue.value - totalDiscount.value
  return netRev > 0 ? (totalGP.value / netRev) * 100 : 0
})
const totalQtySold = computed(() => sold.value.reduce((s, p) => s + p.qtySold, 0))
const totalInvoices = computed(() => daily.value.reduce((s, d) => s + d.invoiceCount, 0))
const totalSalesGrand = computed(() => daily.value.reduce((s, d) => s + d.grandTotal, 0))
const avgOrderValue = computed(() => totalInvoices.value > 0 ? totalSalesGrand.value / totalInvoices.value : 0)

function formatPeriod() {
  const f = new Date(fromDate.value).toLocaleDateString('en-ZA', { day: 'numeric', month: 'long', year: 'numeric' })
  const t = new Date(toDate.value).toLocaleDateString('en-ZA', { day: 'numeric', month: 'long', year: 'numeric' })
  return `${f} — ${t}`
}

function doPrint() { window.print() }

const topProducts = computed(() =>
  [...sold.value].sort((a, b) => b.revenue - a.revenue).slice(0, 15)
)

// Charts
const revenueChartRef = ref<HTMLCanvasElement | null>(null)
const paymentChartRef = ref<HTMLCanvasElement | null>(null)
const topProductsChartRef = ref<HTMLCanvasElement | null>(null)
let revenueChart: Chart | null = null
let paymentChart: Chart | null = null
let topProductsChart: Chart | null = null

const COLORS = {
  orange: '#f47a20',
  orangeLight: 'rgba(244, 122, 32, 0.15)',
  green: '#2e7d32',
  greenLight: 'rgba(46, 125, 50, 0.12)',
  blue: '#1565c0',
  red: '#c62828',
  grey: '#888',
  payMethods: ['#f47a20', '#1565c0', '#2e7d32', '#7b1fa2', '#c62828']
}

function renderCharts() {
  renderRevenueChart()
  renderPaymentChart()
  renderTopProductsChart()
}

function renderRevenueChart() {
  if (!revenueChartRef.value || !daily.value.length) return
  if (revenueChart) revenueChart.destroy()

  const sorted = [...daily.value].sort((a, b) => a.date.localeCompare(b.date))
  const labels = sorted.map(d => {
    const dt = new Date(d.date)
    return dt.toLocaleDateString('en-ZA', { day: 'numeric', month: 'short' })
  })
  const data = sorted.map(d => d.grandTotal)
  const gpData = sorted.map(d => d.grossProfit)

  let cumulative = 0
  const cumulativeData = sorted.map(d => { cumulative += d.grandTotal; return cumulative })

  revenueChart = new Chart(revenueChartRef.value, {
    type: 'line',
    data: {
      labels,
      datasets: [
        {
          label: 'Daily revenue',
          data,
          borderColor: COLORS.orange,
          backgroundColor: COLORS.orangeLight,
          fill: true,
          tension: 0.3,
          pointRadius: data.length > 30 ? 0 : 3,
          pointHoverRadius: 5,
          borderWidth: 2.5,
          yAxisID: 'y'
        },
        {
          label: 'Daily GP',
          data: gpData,
          borderColor: COLORS.blue,
          backgroundColor: 'rgba(21, 101, 192, 0.08)',
          fill: true,
          tension: 0.3,
          pointRadius: data.length > 30 ? 0 : 3,
          pointHoverRadius: 5,
          borderWidth: 2,
          yAxisID: 'y'
        },
        {
          label: 'Cumulative',
          data: cumulativeData,
          borderColor: COLORS.green,
          backgroundColor: 'transparent',
          borderDash: [6, 3],
          tension: 0.3,
          pointRadius: 0,
          borderWidth: 1.5,
          yAxisID: 'y1'
        }
      ]
    },
    options: {
      responsive: true,
      maintainAspectRatio: false,
      interaction: { mode: 'index', intersect: false },
      plugins: {
        legend: { position: 'top', labels: { usePointStyle: true, padding: 16, font: { size: 11 } } },
        tooltip: {
          callbacks: {
            label: (ctx) => `${ctx.dataset.label}: R${(ctx.parsed.y ?? 0).toLocaleString('en-ZA', { minimumFractionDigits: 2 })}`
          }
        }
      },
      scales: {
        x: { grid: { display: false }, ticks: { font: { size: 10 }, maxRotation: 45 } },
        y: {
          position: 'left',
          grid: { color: 'rgba(0,0,0,0.06)' },
          ticks: { font: { size: 10 }, callback: (v) => `R${Number(v).toLocaleString()}` }
        },
        y1: {
          position: 'right',
          grid: { drawOnChartArea: false },
          ticks: { font: { size: 10 }, callback: (v) => `R${Number(v).toLocaleString()}` }
        }
      }
    }
  })
}

function renderPaymentChart() {
  if (!paymentChartRef.value || !payments.value?.byMethod.length) return
  if (paymentChart) paymentChart.destroy()

  const methods = payments.value.byMethod
  paymentChart = new Chart(paymentChartRef.value, {
    type: 'doughnut',
    data: {
      labels: methods.map(m => m.method),
      datasets: [{
        data: methods.map(m => m.grandTotal),
        backgroundColor: COLORS.payMethods.slice(0, methods.length),
        borderWidth: 2,
        borderColor: '#fff'
      }]
    },
    options: {
      responsive: true,
      maintainAspectRatio: false,
      cutout: '60%',
      plugins: {
        legend: { position: 'bottom', labels: { usePointStyle: true, padding: 14, font: { size: 11 } } },
        tooltip: {
          callbacks: {
            label: (ctx) => {
              const total = methods.reduce((s, m) => s + m.grandTotal, 0)
              const pct = total > 0 ? ((ctx.parsed / total) * 100).toFixed(1) : '0'
              return `${ctx.label}: R${ctx.parsed.toLocaleString('en-ZA', { minimumFractionDigits: 2 })} (${pct}%)`
            }
          }
        }
      }
    }
  })
}

function renderTopProductsChart() {
  if (!topProductsChartRef.value || !topProducts.value.length) return
  if (topProductsChart) topProductsChart.destroy()

  const top10 = topProducts.value.slice(0, 10)
  const labels = top10.map(p => p.name.length > 22 ? p.name.slice(0, 20) + '…' : p.name)
  const revenues = top10.map(p => p.revenue)
  const gps = top10.map(p => (p.revenue - p.discount) - p.costInclVat)

  topProductsChart = new Chart(topProductsChartRef.value, {
    type: 'bar',
    data: {
      labels,
      datasets: [
        {
          label: 'Revenue',
          data: revenues,
          backgroundColor: COLORS.orangeLight,
          borderColor: COLORS.orange,
          borderWidth: 1.5,
          borderRadius: 4
        },
        {
          label: 'Gross Profit',
          data: gps,
          backgroundColor: COLORS.greenLight,
          borderColor: COLORS.green,
          borderWidth: 1.5,
          borderRadius: 4
        }
      ]
    },
    options: {
      responsive: true,
      maintainAspectRatio: false,
      indexAxis: 'y',
      plugins: {
        legend: { position: 'top', labels: { usePointStyle: true, padding: 16, font: { size: 11 } } },
        tooltip: {
          callbacks: {
            label: (ctx) => `${ctx.dataset.label}: R${(ctx.parsed.x ?? 0).toLocaleString('en-ZA', { minimumFractionDigits: 2 })}`
          }
        }
      },
      scales: {
        x: {
          grid: { color: 'rgba(0,0,0,0.06)' },
          ticks: { font: { size: 10 }, callback: (v) => `R${Number(v).toLocaleString()}` }
        },
        y: { grid: { display: false }, ticks: { font: { size: 10 } } }
      }
    }
  })
}
</script>

<template>
  <div class="fr">
    <!-- Controls — hidden when printing -->
    <div class="fr-controls no-print">
      <McField label="From" for-id="fr-from">
        <input id="fr-from" v-model="fromDate" type="date" />
      </McField>
      <McField label="To" for-id="fr-to">
        <input id="fr-to" v-model="toDate" type="date" />
      </McField>
      <McButton variant="primary" type="button" :disabled="busy" @click="loadReport">
        <McSpinner v-if="busy" />
        <span v-else>Generate</span>
      </McButton>
      <McButton variant="secondary" type="button" :disabled="busy || !stock" @click="doPrint">Print / Save PDF</McButton>
      <button
        type="button"
        class="fr-privacy"
        :class="{ 'fr-privacy--on': privacyActive }"
        :aria-pressed="privacyActive"
        :title="privacyActive ? 'Show numbers' : 'Hide numbers (blur)'"
        @click="togglePrivacy"
      >
        <component :is="privacyActive ? EyeOff : Eye" :size="16" />
        <span>{{ privacyActive ? 'Numbers hidden' : 'Privacy mode' }}</span>
      </button>
    </div>

    <div v-if="err" class="fr-err no-print">{{ err }}</div>

    <div v-if="busy" class="fr-loading"><McSpinner /> Loading report…</div>

    <!-- Report body -->
    <div v-if="stock && !busy" class="fr-report">
      <!-- Header -->
      <header class="fr-header">
        <div class="fr-header__brand">
          <img v-if="logoUrl" :src="logoUrl" :alt="businessName" class="fr-header__logo" width="180" height="54" />
          <h1 v-else class="fr-header__name">{{ businessName }}</h1>
        </div>
        <div class="fr-header__meta">
          <h2 class="fr-header__title">Financial Overview</h2>
          <p class="fr-header__period">{{ formatPeriod() }}</p>
          <p class="fr-header__gen">Generated {{ new Date().toLocaleDateString('en-ZA', { day: 'numeric', month: 'long', year: 'numeric', hour: '2-digit', minute: '2-digit' }) }}</p>
        </div>
      </header>

      <hr class="fr-rule" />

      <!-- KPI row 1 — headline metrics -->
      <section class="fr-kpis fr-kpis--lg">
        <McMetricCard
          label="Revenue (incl VAT)"
          :value="formatZAR(totalRevenue)"
          variant="accent"
          sensitive
        />
        <McMetricCard
          label="Gross Profit"
          :value="formatZAR(totalGP)"
          variant="accent"
          sensitive
        />
        <McMetricCard
          label="GP Margin"
          :value="`${gpMargin.toFixed(1)}%`"
          variant="accent"
          sensitive
        />
        <McMetricCard
          label="Discounts Given"
          :value="formatZAR(totalDiscount)"
          variant="warning"
          sensitive
        />
      </section>

      <!-- KPI row 2 — counts -->
      <section class="fr-kpis fr-kpis--sm">
        <McMetricCard
          label="Invoices"
          :value="formatNumber(totalInvoices)"
          size="compact"
          sensitive
        />
        <McMetricCard
          label="Items Sold"
          :value="formatNumber(totalQtySold)"
          size="compact"
          sensitive
        />
        <McMetricCard
          label="Avg Order Value"
          :value="formatZAR(avgOrderValue)"
          size="compact"
          sensitive
        />
        <McMetricCard
          label="Grand Total (paid)"
          :value="formatZAR(totalSalesGrand)"
          size="compact"
          sensitive
        />
        <McMetricCard
          label="Wholesale + VAT"
          :value="formatZAR(totalCostInclVat)"
          variant="muted"
          size="compact"
          sensitive
        />
      </section>

      <!-- Charts row -->
      <section class="fr-charts">
        <div class="fr-chart-card fr-chart-card--wide">
          <h3 class="fr-chart-card__title">Daily Revenue / GP / Cumulative</h3>
          <div class="fr-chart-wrap fr-chart-wrap--line sensitive-chart">
            <canvas ref="revenueChartRef"></canvas>
          </div>
        </div>
        <div class="fr-chart-card">
          <h3 class="fr-chart-card__title">Payment Methods</h3>
          <div class="fr-chart-wrap fr-chart-wrap--donut sensitive-chart">
            <canvas ref="paymentChartRef"></canvas>
          </div>
        </div>
      </section>

      <!-- Top products bar chart -->
      <section v-if="topProducts.length" class="fr-chart-card fr-chart-card--full">
        <h3 class="fr-chart-card__title">Top Products — Revenue vs GP</h3>
        <div class="fr-chart-wrap fr-chart-wrap--bar sensitive-chart">
          <canvas ref="topProductsChartRef"></canvas>
        </div>
      </section>

      <!-- Products table -->
      <section v-if="topProducts.length" class="fr-section">
        <h3 class="fr-section__title">Product Detail</h3>
        <table class="fr-table">
          <thead>
            <tr>
              <th>SKU</th>
              <th>Product</th>
              <th class="fr-r">Qty</th>
              <th class="fr-r">Revenue</th>
              <th class="fr-r">Discount</th>
              <th class="fr-r">Wholesale + VAT</th>
              <th class="fr-r">GP</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="p in topProducts" :key="p.sku">
              <td class="fr-mono">{{ p.sku }}</td>
              <td>{{ p.name }}</td>
              <td class="fr-r"><span class="sensitive-value">{{ formatNumber(p.qtySold) }}</span></td>
              <td class="fr-r"><span class="sensitive-value">{{ formatZAR(p.revenue) }}</span></td>
              <td class="fr-r"><span class="sensitive-value">{{ p.discount ? formatZAR(p.discount) : '—' }}</span></td>
              <td class="fr-r"><span class="sensitive-value">{{ formatZAR(p.costInclVat) }}</span></td>
              <td class="fr-r"><span class="sensitive-value">{{ formatZAR((p.revenue - p.discount) - p.costInclVat) }}</span></td>
            </tr>
          </tbody>
          <tfoot>
            <tr>
              <td colspan="2"><strong>All products</strong></td>
              <td class="fr-r"><strong class="sensitive-value">{{ formatNumber(totalQtySold) }}</strong></td>
              <td class="fr-r"><strong class="sensitive-value">{{ formatZAR(totalRevenue) }}</strong></td>
              <td class="fr-r"><strong class="sensitive-value">{{ formatZAR(totalDiscount) }}</strong></td>
              <td class="fr-r"><strong class="sensitive-value">{{ formatZAR(totalCostInclVat) }}</strong></td>
              <td class="fr-r"><strong class="sensitive-value">{{ formatZAR(totalGP) }}</strong></td>
            </tr>
          </tfoot>
        </table>
      </section>

      <!-- Daily table -->
      <section v-if="daily.length" class="fr-section">
        <h3 class="fr-section__title">Daily Breakdown</h3>
        <table class="fr-table">
          <thead>
            <tr><th>Date</th><th class="fr-r">Invoices</th><th class="fr-r">Total</th></tr>
          </thead>
          <tbody>
            <tr v-for="d in daily" :key="d.date">
              <td>{{ d.date }}</td>
              <td class="fr-r"><span class="sensitive-value">{{ formatNumber(d.invoiceCount) }}</span></td>
              <td class="fr-r"><span class="sensitive-value">{{ formatZAR(d.grandTotal) }}</span></td>
            </tr>
          </tbody>
          <tfoot>
            <tr>
              <td><strong>Total</strong></td>
              <td class="fr-r"><strong class="sensitive-value">{{ formatNumber(totalInvoices) }}</strong></td>
              <td class="fr-r"><strong class="sensitive-value">{{ formatZAR(totalSalesGrand) }}</strong></td>
            </tr>
          </tfoot>
        </table>
      </section>

      <!-- Footer -->
      <footer class="fr-footer">
        <p>GP = (Revenue − Discounts) − Wholesale cost incl. VAT &nbsp;|&nbsp; {{ businessName }} — VAT reg. All amounts in ZAR.</p>
      </footer>
    </div>
  </div>
</template>

<style scoped>
.no-print { }
@media print { .no-print { display: none !important; } }

/* ── Page ────────────────────────────────────────────────────────────── */
.fr { max-width: 1060px; margin: 0 auto; padding: 1.5rem; }

.fr-controls {
  display: flex; flex-wrap: wrap; align-items: flex-end; gap: 1rem;
  margin-bottom: 1.5rem; padding: 1rem 1.25rem;
  background: var(--mc-app-surface, #fff);
  border: 1px solid var(--mc-app-border-soft, #ddd9d3);
  border-radius: var(--mc-app-radius-card, 18px);
}
.fr-controls :deep(.mc-field) { margin-bottom: 0; }

.fr-privacy {
  display: inline-flex;
  align-items: center;
  gap: 0.4rem;
  padding: 0.55rem 0.9rem;
  border-radius: 10px;
  border: 1.5px solid var(--mc-app-border-subtle, #c8c5bd);
  background: var(--mc-app-surface, #fff);
  color: var(--mc-app-text-secondary, #333336);
  font-size: 0.86rem;
  font-weight: 600;
  cursor: pointer;
  margin-left: auto;
  transition: background 0.12s ease, border-color 0.12s ease, color 0.12s ease;
}
.fr-privacy:hover { border-color: var(--mc-accent, #f47a20); }
.fr-privacy--on {
  background: var(--mc-accent, #f47a20);
  border-color: var(--mc-accent, #f47a20);
  color: #fff;
}

.fr-err { padding: 0.75rem 1rem; background: #fdecea; color: #b71c1c; border-radius: 8px; margin-bottom: 1rem; }
.fr-loading { display: flex; align-items: center; gap: 0.75rem; justify-content: center; padding: 3rem; color: var(--mc-app-text-muted, #5c5a56); }

/* ── Report body ─────────────────────────────────────────────────────── */
.fr-report {
  background: #fff; color: #1a1a1c;
  border-radius: 14px; padding: 2.5rem;
  border: 1px solid var(--mc-app-border-soft, #ddd9d3);
  box-shadow: 0 1px 4px rgba(0,0,0,0.04);
}

/* ── Header ──────────────────────────────────────────────────────────── */
.fr-header { display: flex; justify-content: space-between; align-items: flex-start; gap: 1.5rem; flex-wrap: wrap; }
.fr-header__logo { height: 54px; width: auto; object-fit: contain; }
.fr-header__name { font-family: 'Barlow Condensed', sans-serif; font-size: 1.75rem; font-weight: 700; margin: 0; }
.fr-header__meta { text-align: right; }
.fr-header__title { font-family: 'Barlow Condensed', sans-serif; font-size: 1.4rem; font-weight: 700; margin: 0; text-transform: uppercase; letter-spacing: 0.06em; color: #0a0a0b; }
.fr-header__period { margin: 0.25rem 0 0; font-size: 0.95rem; font-weight: 600; color: #333; }
.fr-header__gen { margin: 0.1rem 0 0; font-size: 0.75rem; color: #999; }
.fr-rule { border: none; border-top: 2.5px solid #0a0a0b; margin: 1.25rem 0 1.75rem; }

/* ── KPIs ────────────────────────────────────────────────────────────── */
.fr-kpis {
  display: grid;
  gap: 0.875rem;
  margin-bottom: 1.25rem;
  grid-template-columns: repeat(4, minmax(0, 1fr));
}
.fr-kpis--lg { margin-bottom: 1rem; }
.fr-kpis--sm {
  grid-template-columns: repeat(5, minmax(0, 1fr));
  margin-bottom: 1.75rem;
}

/* ── Chart cards ─────────────────────────────────────────────────────── */
.fr-charts {
  display: grid;
  grid-template-columns: minmax(0, 2fr) minmax(0, 1fr);
  gap: 1rem;
  margin-bottom: 1.25rem;
}

.fr-chart-card {
  border: 1px solid var(--mc-app-border-soft, #e0ddd8);
  border-radius: 14px;
  padding: 1.1rem 1.25rem 1.25rem;
  background: var(--mc-app-surface, #fff);
  box-shadow: var(--mc-app-shadow-sm, 0 1px 3px rgba(0, 0, 0, 0.04));
}
.fr-chart-card--full { margin-bottom: 1.75rem; }
.fr-chart-card__title {
  font-family: 'Barlow Condensed', sans-serif;
  font-size: 0.9rem;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.07em;
  color: var(--mc-app-text-secondary, #555);
  margin: 0 0 0.75rem;
  padding-bottom: 0.4rem;
  border-bottom: 1.5px solid var(--mc-app-border-faint, #eceae5);
}

.fr-chart-wrap { transition: filter 0.18s ease; }
.fr-chart-wrap--line { height: 280px; }
.fr-chart-wrap--donut { height: 280px; display: flex; align-items: center; justify-content: center; }
.fr-chart-wrap--bar { height: 340px; }

/* ── Sections + tables ───────────────────────────────────────────────── */
.fr-section { margin-bottom: 2rem; }
.fr-section__title {
  font-family: 'Barlow Condensed', sans-serif; font-size: 0.95rem; font-weight: 700;
  text-transform: uppercase; letter-spacing: 0.05em; color: #333;
  border-bottom: 2px solid #e0ddd8; padding-bottom: 0.35rem; margin: 0 0 0.75rem;
}

.fr-table { width: 100%; border-collapse: collapse; font-size: 0.82rem; }
.fr-table th { text-align: left; font-size: 0.68rem; font-weight: 700; text-transform: uppercase; letter-spacing: 0.04em; color: #666; padding: 0.4rem 0.5rem; border-bottom: 1.5px solid #c8c5bd; }
.fr-table td { padding: 0.3rem 0.5rem; border-bottom: 1px solid #eceae5; color: #222; }
.fr-table tbody tr:hover td { background: #f5f4f1; }
.fr-table tfoot td { border-top: 2px solid #c8c5bd; border-bottom: none; background: #f5f4f2; }
.fr-r { text-align: right; font-variant-numeric: tabular-nums; }
.fr-mono { font-weight: 600; font-variant-numeric: tabular-nums; }

/* ── Footer ──────────────────────────────────────────────────────────── */
.fr-footer { margin-top: 2rem; padding-top: 0.75rem; border-top: 1px solid #e0ddd8; font-size: 0.72rem; color: #aaa; text-align: center; }
.fr-footer p { margin: 0; }

/* ── Print ───────────────────────────────────────────────────────────── */
@media print {
  @page { size: A4 landscape; margin: 12mm; }
  .fr { max-width: none; padding: 0; }
  .fr-report { border: none; border-radius: 0; padding: 0; box-shadow: none; }
  .fr-chart-card, .fr-table tfoot td { -webkit-print-color-adjust: exact; print-color-adjust: exact; }
  .fr-charts { grid-template-columns: minmax(0, 2fr) minmax(0, 1fr); }
  .fr-chart-wrap--line { height: 200px; }
  .fr-chart-wrap--donut { height: 200px; }
  .fr-chart-wrap--bar { height: 250px; }
  .fr-section { page-break-inside: avoid; }
}

/* ── Responsive ──────────────────────────────────────────────────────── */
@media screen and (max-width: 1100px) {
  .fr-kpis--sm { grid-template-columns: repeat(3, minmax(0, 1fr)); }
}
@media screen and (max-width: 800px) {
  .fr-report { padding: 1.25rem; }
  .fr-kpis { grid-template-columns: repeat(2, minmax(0, 1fr)); }
  .fr-kpis--sm { grid-template-columns: repeat(2, minmax(0, 1fr)); }
  .fr-charts { grid-template-columns: 1fr; }
  .fr-header { flex-direction: column; }
  .fr-header__meta { text-align: left; }
  .fr-privacy { margin-left: 0; }
}
@media screen and (max-width: 480px) {
  .fr-kpis { grid-template-columns: 1fr; }
  .fr-kpis--sm { grid-template-columns: 1fr; }
}
</style>
