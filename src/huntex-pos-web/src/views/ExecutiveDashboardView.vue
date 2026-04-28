<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref } from 'vue'
import { http } from '@/api/http'
import { Chart, registerables } from 'chart.js'
import { Eye, EyeOff, RefreshCw, ArrowUp, ArrowDown, Minus, AlertTriangle, ShoppingBag, FileText, Package, Slash, ArrowRight } from 'lucide-vue-next'

import { formatZAR, formatNumber } from '@/utils/format'
import { useBranding } from '@/composables/useBranding'
import { usePrivacyMode } from '@/composables/usePrivacyMode'

import McPageHeader from '@/components/ui/McPageHeader.vue'
import McButton from '@/components/ui/McButton.vue'
import McField from '@/components/ui/McField.vue'
import McSpinner from '@/components/ui/McSpinner.vue'
import McSkeleton from '@/components/ui/McSkeleton.vue'
import McEmptyState from '@/components/ui/McEmptyState.vue'
import McAlert from '@/components/ui/McAlert.vue'
import McMetricCard from '@/components/ui/McMetricCard.vue'

Chart.register(...registerables)

// ─────────────────────────── Types (mirror DashboardDtos) ────────────────────
type TrendPoint = { date: string; total: number }
type Deltas = {
  periodSales: number | null
  periodGrossProfit: number | null
  avgBasket: number | null
  itemsSold: number | null
  invoiceCount: number | null
}
type Kpis = {
  todaySales: number
  monthSales: number
  grossProfit: number
  grossProfitPct: number
  avgBasket: number
  itemsSold: number
  invoiceCount: number
  lowStockCount: number
  periodSales: number
  periodGrossProfit: number
  deltas: Deltas
}
type PaymentMethod = { method: string; count: number; total: number; pct: number }
type Category = { category: string; qty: number; revenue: number }
type TopProduct = { sku: string; name: string; qty: number; revenue: number }
type LowStock = { productId: string; sku: string; name: string; qtyOnHand: number; supplierName: string | null }
type Activity = {
  type: 'sale' | 'void' | 'quote-created' | 'quote-converted' | 'restock' | 'restock-out' | 'stocktake'
  ts: string
  actor: string | null
  summary: string
  link: string | null
}
type Overview = {
  from: string
  to: string
  kpis: Kpis
  salesTrend: { current: TrendPoint[]; previous: TrendPoint[] }
  paymentMethods: PaymentMethod[]
  topCategories: Category[]
  topProducts: TopProduct[]
  lowStockAlerts: LowStock[]
  recentActivity: Activity[]
}

// ─────────────────────────── State ───────────────────────────────────────────
const { businessName } = useBranding()
const { privacyActive, toggle: togglePrivacy } = usePrivacyMode()

const busy = ref(false)
const err = ref<string | null>(null)
const data = ref<Overview | null>(null)

function toDateStr(d: Date) { return d.toISOString().slice(0, 10) }
function defaultFrom() { const d = new Date(); d.setDate(d.getDate() - 29); return toDateStr(d) }
function defaultTo() { return toDateStr(new Date()) }

const fromDate = ref(defaultFrom())
const toDate = ref(defaultTo())

const periodLabel = computed(() => {
  if (!fromDate.value || !toDate.value) return ''
  const f = new Date(fromDate.value).toLocaleDateString('en-ZA', { day: 'numeric', month: 'short', year: 'numeric' })
  const t = new Date(toDate.value).toLocaleDateString('en-ZA', { day: 'numeric', month: 'short', year: 'numeric' })
  return `${f} — ${t}`
})

function applyPreset(preset: '7d' | '30d' | '90d' | 'mtd' | 'ytd') {
  const today = new Date()
  if (preset === 'mtd') {
    fromDate.value = toDateStr(new Date(today.getFullYear(), today.getMonth(), 1))
    toDate.value = toDateStr(today)
  } else if (preset === 'ytd') {
    fromDate.value = toDateStr(new Date(today.getFullYear(), 0, 1))
    toDate.value = toDateStr(today)
  } else {
    const days = preset === '7d' ? 6 : preset === '30d' ? 29 : 89
    const start = new Date(today)
    start.setDate(start.getDate() - days)
    fromDate.value = toDateStr(start)
    toDate.value = toDateStr(today)
  }
  void load()
}

// ─────────────────────────── Load ────────────────────────────────────────────
function buildParams() {
  const p: Record<string, string> = {}
  if (fromDate.value) p.from = new Date(fromDate.value).toISOString()
  if (toDate.value) {
    const end = new Date(toDate.value)
    end.setHours(23, 59, 59, 999)
    p.to = end.toISOString()
  }
  return p
}

async function load() {
  busy.value = true
  err.value = null
  try {
    const { data: payload } = await http.get<Overview>('/api/dashboard/overview', { params: buildParams() })
    data.value = payload
    await nextTick()
    renderTrendChart()
    renderPaymentChart()
    renderCategoriesChart()
  } catch (e: any) {
    err.value = e?.response?.data?.error ?? 'Failed to load dashboard'
  } finally {
    busy.value = false
  }
}

onMounted(load)
onBeforeUnmount(() => {
  trendChart?.destroy()
  paymentChart?.destroy()
  categoriesChart?.destroy()
})

// ─────────────────────────── Charts ──────────────────────────────────────────
const trendChartRef = ref<HTMLCanvasElement | null>(null)
const paymentChartRef = ref<HTMLCanvasElement | null>(null)
const categoriesChartRef = ref<HTMLCanvasElement | null>(null)
let trendChart: Chart | null = null
let paymentChart: Chart | null = null
let categoriesChart: Chart | null = null

const COLORS = {
  orange: '#f47a20',
  orangeFill: 'rgba(244, 122, 32, 0.16)',
  grey: '#9ca3af',
  greyFill: 'rgba(156, 163, 175, 0.18)',
  // Payment method colours intentionally match other reports for cross-page consistency.
  pay: ['#f47a20', '#1565c0', '#2e7d32', '#7b1fa2', '#c62828', '#0891b2', '#475569']
}

function renderTrendChart() {
  if (!trendChartRef.value || !data.value) return
  trendChart?.destroy()

  const cur = data.value.salesTrend.current
  const prev = data.value.salesTrend.previous
  const labels = cur.map(p => new Date(p.date).toLocaleDateString('en-ZA', { day: 'numeric', month: 'short' }))

  trendChart = new Chart(trendChartRef.value, {
    type: 'line',
    data: {
      labels,
      datasets: [
        {
          label: 'This period',
          data: cur.map(p => p.total),
          borderColor: COLORS.orange,
          backgroundColor: COLORS.orangeFill,
          fill: true,
          tension: 0.3,
          pointRadius: cur.length > 30 ? 0 : 3,
          pointHoverRadius: 5,
          borderWidth: 2.5
        },
        {
          label: 'Previous period',
          data: prev.map(p => p.total),
          borderColor: COLORS.grey,
          backgroundColor: 'transparent',
          borderDash: [5, 4],
          tension: 0.3,
          pointRadius: 0,
          borderWidth: 1.5
        }
      ]
    },
    options: {
      responsive: true,
      maintainAspectRatio: false,
      interaction: { mode: 'index', intersect: false },
      plugins: {
        legend: { position: 'top', labels: { usePointStyle: true, padding: 14, font: { size: 11 } } },
        tooltip: {
          callbacks: {
            label: (ctx) => `${ctx.dataset.label}: R${(ctx.parsed.y ?? 0).toLocaleString('en-ZA', { minimumFractionDigits: 2 })}`
          }
        }
      },
      scales: {
        x: { grid: { display: false }, ticks: { font: { size: 10 }, maxRotation: 45, autoSkip: true, maxTicksLimit: 12 } },
        y: { grid: { color: 'rgba(0,0,0,0.06)' }, ticks: { font: { size: 10 }, callback: (v) => `R${Number(v).toLocaleString()}` } }
      }
    }
  })
}

function renderPaymentChart() {
  if (!paymentChartRef.value || !data.value || !data.value.paymentMethods.length) return
  paymentChart?.destroy()

  const pm = data.value.paymentMethods
  paymentChart = new Chart(paymentChartRef.value, {
    type: 'doughnut',
    data: {
      labels: pm.map(p => p.method),
      datasets: [{
        data: pm.map(p => p.total),
        backgroundColor: pm.map((_, i) => COLORS.pay[i % COLORS.pay.length]),
        borderWidth: 2,
        borderColor: '#fff'
      }]
    },
    options: {
      responsive: true,
      maintainAspectRatio: false,
      cutout: '62%',
      plugins: {
        legend: { position: 'bottom', labels: { usePointStyle: true, padding: 12, font: { size: 11 } } },
        tooltip: {
          callbacks: {
            label: (ctx) => {
              const v = ctx.parsed as unknown as number
              return ` ${ctx.label}: R${(v ?? 0).toLocaleString('en-ZA', { minimumFractionDigits: 2 })}`
            }
          }
        }
      }
    }
  })
}

function renderCategoriesChart() {
  if (!categoriesChartRef.value || !data.value || !data.value.topCategories.length) return
  categoriesChart?.destroy()

  const cats = data.value.topCategories
  categoriesChart = new Chart(categoriesChartRef.value, {
    type: 'bar',
    data: {
      labels: cats.map(c => c.category),
      datasets: [{
        label: 'Revenue',
        data: cats.map(c => c.revenue),
        backgroundColor: COLORS.orange,
        borderRadius: 6,
        maxBarThickness: 22
      }]
    },
    options: {
      indexAxis: 'y',
      responsive: true,
      maintainAspectRatio: false,
      plugins: {
        legend: { display: false },
        tooltip: {
          callbacks: {
            label: (ctx) => ` R${(ctx.parsed.x ?? 0).toLocaleString('en-ZA', { minimumFractionDigits: 2 })}`
          }
        }
      },
      scales: {
        x: { grid: { color: 'rgba(0,0,0,0.06)' }, ticks: { font: { size: 10 }, callback: (v) => `R${Number(v).toLocaleString()}` } },
        y: { grid: { display: false }, ticks: { font: { size: 11 } } }
      }
    }
  })
}

// ─────────────────────────── KPI helpers ─────────────────────────────────────
type DeltaKey = keyof Deltas

function deltaText(key: DeltaKey): string {
  const v = data.value?.kpis.deltas?.[key]
  if (v === null || v === undefined) return '—'
  const sign = v > 0 ? '+' : ''
  return `${sign}${v.toFixed(1)}%`
}
function deltaTone(key: DeltaKey): 'up' | 'down' | 'flat' {
  const v = data.value?.kpis.deltas?.[key]
  if (v === null || v === undefined || Math.abs(v) < 0.05) return 'flat'
  return v > 0 ? 'up' : 'down'
}

function formatRelative(ts: string): string {
  const now = Date.now()
  const t = new Date(ts).getTime()
  const diff = Math.max(0, now - t)
  const m = Math.floor(diff / 60000)
  if (m < 1) return 'just now'
  if (m < 60) return `${m}m ago`
  const h = Math.floor(m / 60)
  if (h < 24) return `${h}h ago`
  const d = Math.floor(h / 24)
  if (d < 7) return `${d}d ago`
  return new Date(ts).toLocaleDateString('en-ZA', { day: 'numeric', month: 'short' })
}

function activityIcon(type: Activity['type']) {
  if (type === 'sale') return ShoppingBag
  if (type === 'void') return Slash
  if (type === 'quote-created' || type === 'quote-converted') return FileText
  if (type === 'restock' || type === 'restock-out') return Package
  return ArrowRight
}
</script>

<template>
  <div class="exec-dashboard">
    <McPageHeader :title="`${businessName} — Dashboard`" :subtitle="periodLabel">
      <template #actions>
        <McButton variant="ghost" type="button" :title="privacyActive ? 'Show amounts' : 'Hide amounts'" @click="togglePrivacy">
          <component :is="privacyActive ? EyeOff : Eye" :size="16" />
          <span>{{ privacyActive ? 'Show' : 'Hide' }} amounts</span>
        </McButton>
        <McButton variant="ghost" type="button" :disabled="busy" @click="load">
          <RefreshCw :size="16" :class="{ 'exec-spin': busy }" />
          <span>Refresh</span>
        </McButton>
      </template>
    </McPageHeader>

    <!-- Filter strip -->
    <div class="exec-filters">
      <div class="exec-filters__dates">
        <McField label="From" for-id="exec-from">
          <input id="exec-from" v-model="fromDate" type="date" @change="load" />
        </McField>
        <McField label="To" for-id="exec-to">
          <input id="exec-to" v-model="toDate" type="date" @change="load" />
        </McField>
      </div>
      <div class="exec-filters__presets" role="group" aria-label="Date presets">
        <button type="button" class="exec-preset" @click="applyPreset('7d')">7 d</button>
        <button type="button" class="exec-preset" @click="applyPreset('30d')">30 d</button>
        <button type="button" class="exec-preset" @click="applyPreset('90d')">90 d</button>
        <button type="button" class="exec-preset" @click="applyPreset('mtd')">MTD</button>
        <button type="button" class="exec-preset" @click="applyPreset('ytd')">YTD</button>
      </div>
    </div>

    <McAlert v-if="err" variant="error" class="exec-error">{{ err }}</McAlert>

    <!-- Loading skeleton -->
    <div v-if="busy && !data" class="exec-loading">
      <McSkeleton :lines="2" />
      <McSkeleton :lines="6" />
    </div>

    <template v-else-if="data">
      <!-- KPI strip ─────────────────────────────────────────────────────────-->
      <div class="exec-kpis">
        <McMetricCard
          label="Today sales"
          :value="formatZAR(data.kpis.todaySales)"
          variant="accent"
          sensitive
        />
        <McMetricCard
          label="Month sales"
          :value="formatZAR(data.kpis.monthSales)"
          sensitive
        />
        <McMetricCard
          label="Gross profit"
          :value="formatZAR(data.kpis.grossProfit)"
          variant="success"
          :hint="deltaText('periodGrossProfit')"
          sensitive
        />
        <McMetricCard
          label="GP %"
          :value="`${data.kpis.grossProfitPct.toFixed(1)}%`"
        />
        <McMetricCard
          label="Avg basket"
          :value="formatZAR(data.kpis.avgBasket)"
          :hint="deltaText('avgBasket')"
          sensitive
        />
        <McMetricCard
          label="Items sold"
          :value="formatNumber(data.kpis.itemsSold)"
          :hint="deltaText('itemsSold')"
        />
        <McMetricCard
          label="Invoices"
          :value="formatNumber(data.kpis.invoiceCount)"
          :hint="deltaText('invoiceCount')"
        />
        <McMetricCard
          label="Low stock"
          :value="formatNumber(data.kpis.lowStockCount)"
          :variant="data.kpis.lowStockCount > 0 ? 'warning' : 'muted'"
          hint="≤ 5 on hand"
        />
      </div>

      <!-- Period delta pills (small, optional, below KPIs) -->
      <div class="exec-deltas" v-if="data.kpis.deltas.periodSales !== null">
        <span class="exec-delta-pill" :class="`exec-delta-pill--${deltaTone('periodSales')}`">
          <component :is="deltaTone('periodSales') === 'up' ? ArrowUp : deltaTone('periodSales') === 'down' ? ArrowDown : Minus" :size="12" />
          Period sales {{ deltaText('periodSales') }} vs previous {{ data.salesTrend.current.length }} days
        </span>
      </div>

      <!-- Charts row ────────────────────────────────────────────────────────-->
      <div class="exec-charts">
        <div class="exec-card exec-card--span2">
          <div class="exec-card__head">
            <span>Sales trend</span>
            <span class="exec-card__meta">vs previous {{ data.salesTrend.current.length }} days</span>
          </div>
          <div class="exec-card__body exec-card__body--chart">
            <McEmptyState v-if="!data.salesTrend.current.some(p => p.total > 0)" title="No sales in this period" />
            <canvas v-else ref="trendChartRef"></canvas>
          </div>
        </div>

        <div class="exec-card">
          <div class="exec-card__head"><span>Payment methods</span></div>
          <div class="exec-card__body exec-card__body--chart">
            <McEmptyState v-if="!data.paymentMethods.length" title="No sales yet" />
            <canvas v-else ref="paymentChartRef"></canvas>
          </div>
        </div>

        <div class="exec-card">
          <div class="exec-card__head"><span>Top categories</span></div>
          <div class="exec-card__body exec-card__body--chart">
            <McEmptyState v-if="!data.topCategories.length" title="No category data" hint="Tag products with a category to see this chart." />
            <canvas v-else ref="categoriesChartRef"></canvas>
          </div>
        </div>
      </div>

      <!-- Tables row ────────────────────────────────────────────────────────-->
      <div class="exec-tables">
        <!-- Top products -->
        <div class="exec-card">
          <div class="exec-card__head">
            <span>Top products</span>
            <span class="exec-card__meta">{{ data.topProducts.length }}</span>
          </div>
          <div class="exec-card__body">
            <McEmptyState v-if="!data.topProducts.length" title="No products sold" />
            <table v-else class="exec-table">
              <thead>
                <tr><th>Product</th><th class="exec-num">Sold</th><th class="exec-num">Revenue</th></tr>
              </thead>
              <tbody>
                <tr v-for="p in data.topProducts" :key="p.sku">
                  <td>
                    <div class="exec-cell-strong">{{ p.name }}</div>
                    <div class="exec-cell-meta">{{ p.sku }}</div>
                  </td>
                  <td class="exec-num">{{ formatNumber(p.qty) }}</td>
                  <td class="exec-num sensitive-value">{{ formatZAR(p.revenue) }}</td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>

        <!-- Low stock -->
        <div class="exec-card">
          <div class="exec-card__head">
            <span><AlertTriangle :size="14" class="exec-head-icon" /> Low stock alerts</span>
            <span class="exec-card__meta">{{ data.kpis.lowStockCount }}</span>
          </div>
          <div class="exec-card__body">
            <McEmptyState v-if="!data.lowStockAlerts.length" title="All stock above threshold" hint="No active products at or below 5 on hand." />
            <table v-else class="exec-table">
              <thead>
                <tr><th>Product</th><th class="exec-num">On hand</th></tr>
              </thead>
              <tbody>
                <tr v-for="p in data.lowStockAlerts" :key="p.productId" :class="{ 'exec-row--danger': p.qtyOnHand <= 0 }">
                  <td>
                    <div class="exec-cell-strong">{{ p.name }}</div>
                    <div class="exec-cell-meta">{{ p.sku }}<template v-if="p.supplierName"> · {{ p.supplierName }}</template></div>
                  </td>
                  <td class="exec-num">
                    <span class="exec-stock-pill" :class="{ 'exec-stock-pill--out': p.qtyOnHand <= 0 }">
                      {{ p.qtyOnHand }}
                    </span>
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>

        <!-- Recent activity -->
        <div class="exec-card">
          <div class="exec-card__head"><span>Recent activity</span></div>
          <div class="exec-card__body">
            <McEmptyState v-if="!data.recentActivity.length" title="Nothing happening yet" />
            <ul v-else class="exec-feed">
              <li v-for="(a, idx) in data.recentActivity" :key="idx" class="exec-feed__item">
                <span class="exec-feed__icon" :class="`exec-feed__icon--${a.type}`">
                  <component :is="activityIcon(a.type)" :size="14" />
                </span>
                <div class="exec-feed__body">
                  <div class="exec-feed__summary">{{ a.summary }}</div>
                  <div class="exec-feed__meta">
                    <span>{{ formatRelative(a.ts) }}</span>
                    <span v-if="a.actor"> · {{ a.actor }}</span>
                  </div>
                </div>
              </li>
            </ul>
          </div>
        </div>
      </div>
    </template>

    <McEmptyState
      v-else-if="!busy && !err"
      title="No data"
      hint="Try a wider date range."
    />

    <McSpinner v-if="busy && data" class="exec-spin-overlay" />
  </div>
</template>

<style scoped>
.exec-dashboard {
  display: flex;
  flex-direction: column;
  gap: 1rem;
  padding-bottom: 2rem;
}

/* ── Filter strip ─────────────────────────────────────────────────────────── */
.exec-filters {
  display: flex;
  flex-wrap: wrap;
  align-items: flex-end;
  gap: 1rem 1.5rem;
  background: var(--mc-app-surface, #fff);
  border: 1px solid var(--mc-app-border-soft, #ddd9d3);
  border-radius: 14px;
  padding: 0.85rem 1rem;
}
.exec-filters__dates {
  display: flex;
  flex-wrap: wrap;
  gap: 0.75rem;
}
.exec-filters__dates :deep(.mc-field) { min-width: 9rem; }
.exec-filters__presets {
  display: inline-flex;
  flex-wrap: wrap;
  gap: 0.4rem;
}
.exec-preset {
  appearance: none;
  background: var(--mc-app-surface-2, #faf9f6);
  border: 1px solid var(--mc-app-border-soft, #ddd9d3);
  border-radius: 999px;
  padding: 0.4rem 0.85rem;
  font-size: 0.78rem;
  font-weight: 600;
  color: var(--mc-app-text-secondary, #5c5a56);
  cursor: pointer;
  transition: background 0.15s, border-color 0.15s, color 0.15s;
}
.exec-preset:hover {
  background: var(--mc-app-surface, #fff);
  border-color: var(--mc-app-accent, #f47a20);
  color: var(--mc-app-accent, #f47a20);
}

.exec-error { margin: 0; }
.exec-loading { display: flex; flex-direction: column; gap: 1rem; }

/* ── KPI strip ────────────────────────────────────────────────────────────── */
.exec-kpis {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 0.7rem;
}
@media (min-width: 720px) {
  .exec-kpis { grid-template-columns: repeat(4, minmax(0, 1fr)); }
}
@media (min-width: 1280px) {
  .exec-kpis { grid-template-columns: repeat(8, minmax(0, 1fr)); }
}

/* ── Period delta pill row ────────────────────────────────────────────────── */
.exec-deltas { display: flex; flex-wrap: wrap; gap: 0.5rem; }
.exec-delta-pill {
  display: inline-flex;
  align-items: center;
  gap: 0.4rem;
  padding: 0.3rem 0.75rem;
  border-radius: 999px;
  font-size: 0.78rem;
  font-weight: 600;
  border: 1px solid;
}
.exec-delta-pill--up   { color: #065f46; border-color: #a7f3d0; background: #ecfdf5; }
.exec-delta-pill--down { color: #991b1b; border-color: #fecaca; background: #fef2f2; }
.exec-delta-pill--flat { color: #44403c; border-color: #ddd9d3; background: #faf9f6; }

/* ── Cards ────────────────────────────────────────────────────────────────── */
.exec-card {
  background: var(--mc-app-surface, #fff);
  border: 1px solid var(--mc-app-border-soft, #ddd9d3);
  border-radius: 14px;
  display: flex;
  flex-direction: column;
  min-width: 0;
  overflow: hidden;
}
.exec-card__head {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 0.5rem;
  padding: 0.75rem 1rem;
  border-bottom: 1px solid var(--mc-app-border-faint, #eceae5);
  font-size: 0.78rem;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.06em;
  color: var(--mc-app-text-secondary, #5c5a56);
}
.exec-card__head > span:first-child { display: inline-flex; align-items: center; gap: 0.4rem; }
.exec-head-icon { color: #b45309; }
.exec-card__meta {
  font-size: 0.7rem;
  font-weight: 600;
  color: var(--mc-app-text-muted, #8a8884);
  text-transform: none;
  letter-spacing: 0;
}
.exec-card__body { padding: 0.85rem 1rem 1rem; min-height: 0; }
.exec-card__body--chart { height: 280px; padding: 0.85rem 1rem; }
.exec-card__body--chart > canvas { width: 100% !important; height: 100% !important; }

/* ── Charts grid ──────────────────────────────────────────────────────────── */
.exec-charts {
  display: grid;
  grid-template-columns: 1fr;
  gap: 0.85rem;
}
@media (min-width: 1100px) {
  .exec-charts {
    grid-template-columns: minmax(0, 2fr) minmax(0, 1fr);
    grid-template-rows: auto auto;
    gap: 0.85rem;
  }
  .exec-card--span2 { grid-column: 1; grid-row: 1 / span 2; }
}

/* ── Tables grid ──────────────────────────────────────────────────────────── */
.exec-tables {
  display: grid;
  grid-template-columns: 1fr;
  gap: 0.85rem;
}
@media (min-width: 900px)  { .exec-tables { grid-template-columns: repeat(2, minmax(0, 1fr)); } }
@media (min-width: 1280px) { .exec-tables { grid-template-columns: repeat(3, minmax(0, 1fr)); } }

.exec-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.85rem;
}
.exec-table th {
  text-align: left;
  font-weight: 700;
  font-size: 0.72rem;
  text-transform: uppercase;
  letter-spacing: 0.06em;
  color: var(--mc-app-text-secondary, #5c5a56);
  padding: 0.5rem 0.5rem;
  border-bottom: 1px solid var(--mc-app-border-faint, #eceae5);
}
.exec-table td {
  padding: 0.55rem 0.5rem;
  border-bottom: 1px solid var(--mc-app-border-faint, #eceae5);
  vertical-align: top;
}
.exec-table tr:last-child td { border-bottom: 0; }
.exec-cell-strong { font-weight: 600; color: var(--mc-app-heading, #0a0a0c); line-height: 1.25; }
.exec-cell-meta { font-size: 0.72rem; color: var(--mc-app-text-secondary, #7a7874); margin-top: 0.1rem; }
.exec-num { text-align: right; font-variant-numeric: tabular-nums; white-space: nowrap; }

.exec-stock-pill {
  display: inline-flex;
  min-width: 2.2rem;
  justify-content: center;
  padding: 0.15rem 0.5rem;
  border-radius: 999px;
  background: #fffbeb;
  color: #92400e;
  border: 1px solid #fde68a;
  font-weight: 700;
  font-size: 0.78rem;
}
.exec-stock-pill--out {
  background: #fef2f2;
  color: #991b1b;
  border-color: #fecaca;
}
.exec-row--danger { background: rgba(254, 202, 202, 0.18); }

/* ── Recent activity feed ────────────────────────────────────────────────── */
.exec-feed { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; }
.exec-feed__item {
  display: flex;
  align-items: flex-start;
  gap: 0.6rem;
  padding: 0.55rem 0;
  border-bottom: 1px solid var(--mc-app-border-faint, #eceae5);
}
.exec-feed__item:last-child { border-bottom: 0; }
.exec-feed__icon {
  flex: 0 0 auto;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 28px;
  height: 28px;
  border-radius: 50%;
  background: var(--mc-app-surface-2, #faf9f6);
  color: var(--mc-app-text-secondary, #5c5a56);
  border: 1px solid var(--mc-app-border-faint, #eceae5);
}
.exec-feed__icon--sale { background: #fff7ed; color: #9a3c00; border-color: #fed7aa; }
.exec-feed__icon--void { background: #fef2f2; color: #991b1b; border-color: #fecaca; }
.exec-feed__icon--quote-created,
.exec-feed__icon--quote-converted { background: #eff6ff; color: #1e40af; border-color: #bfdbfe; }
.exec-feed__icon--restock,
.exec-feed__icon--restock-out { background: #ecfdf5; color: #065f46; border-color: #a7f3d0; }

.exec-feed__body { min-width: 0; flex: 1; }
.exec-feed__summary {
  font-size: 0.86rem;
  line-height: 1.3;
  color: var(--mc-app-heading, #0a0a0c);
  word-break: break-word;
}
.exec-feed__meta {
  font-size: 0.74rem;
  color: var(--mc-app-text-secondary, #7a7874);
  margin-top: 0.15rem;
}

/* ── Misc ─────────────────────────────────────────────────────────────────── */
.exec-spin { animation: exec-spin 1s linear infinite; }
@keyframes exec-spin { to { transform: rotate(360deg); } }
.exec-spin-overlay { position: fixed; bottom: 1rem; right: 1rem; }
</style>
