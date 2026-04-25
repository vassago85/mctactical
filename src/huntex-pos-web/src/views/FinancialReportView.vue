<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { http } from '@/api/http'
import { useBranding } from '@/composables/useBranding'
import { formatZAR, formatNumber } from '@/utils/format'
import McButton from '@/components/ui/McButton.vue'
import McField from '@/components/ui/McField.vue'
import McSpinner from '@/components/ui/McSpinner.vue'

const { businessName, logoUrl } = useBranding()

type ProductSoldLine = {
  sku: string; name: string
  qtySold: number; revenue: number; discount: number; costExVat: number; costInclVat: number
}
type StockReport = {
  soldInPeriod: ProductSoldLine[]
}
type DailySummary = { date: string; invoiceCount: number; grandTotal: number }
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
const totalCostEx = computed(() => sold.value.reduce((s, p) => s + p.costExVat, 0))
const totalCostInclVat = computed(() => sold.value.reduce((s, p) => s + p.costInclVat, 0))
const totalGP = computed(() => (totalRevenue.value - totalDiscount.value) / 1.15 - totalCostEx.value)
const gpMargin = computed(() => {
  const netRev = (totalRevenue.value - totalDiscount.value) / 1.15
  return netRev > 0 ? (totalGP.value / netRev) * 100 : 0
})
const totalQtySold = computed(() => sold.value.reduce((s, p) => s + p.qtySold, 0))
const totalInvoices = computed(() => daily.value.reduce((s, d) => s + d.invoiceCount, 0))
const totalSalesGrand = computed(() => daily.value.reduce((s, d) => s + d.grandTotal, 0))

function formatPeriod() {
  const f = new Date(fromDate.value).toLocaleDateString('en-ZA', { day: 'numeric', month: 'long', year: 'numeric' })
  const t = new Date(toDate.value).toLocaleDateString('en-ZA', { day: 'numeric', month: 'long', year: 'numeric' })
  return `${f} — ${t}`
}

function doPrint() { window.print() }

const topProducts = computed(() =>
  [...sold.value].sort((a, b) => b.revenue - a.revenue).slice(0, 15)
)
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
      <McButton variant="secondary" type="button" :disabled="busy || !stock" @click="doPrint">Print / PDF</McButton>
    </div>

    <div v-if="err" class="fr-err no-print">{{ err }}</div>

    <!-- Printable report body -->
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

      <hr class="fr-divider" />

      <!-- KPI summary -->
      <section class="fr-kpis">
        <div class="fr-kpi">
          <span class="fr-kpi__label">Revenue (incl VAT)</span>
          <strong class="fr-kpi__value">{{ formatZAR(totalRevenue) }}</strong>
        </div>
        <div class="fr-kpi">
          <span class="fr-kpi__label">Discounts Given</span>
          <strong class="fr-kpi__value fr-kpi__value--warn">{{ formatZAR(totalDiscount) }}</strong>
        </div>
        <div class="fr-kpi">
          <span class="fr-kpi__label">Wholesale + VAT</span>
          <strong class="fr-kpi__value">{{ formatZAR(totalCostInclVat) }}</strong>
        </div>
        <div class="fr-kpi fr-kpi--accent">
          <span class="fr-kpi__label">Gross Profit</span>
          <strong class="fr-kpi__value">{{ formatZAR(totalGP) }}</strong>
        </div>
        <div class="fr-kpi">
          <span class="fr-kpi__label">GP Margin</span>
          <strong class="fr-kpi__value">{{ gpMargin.toFixed(1) }}%</strong>
        </div>
        <div class="fr-kpi">
          <span class="fr-kpi__label">Invoices</span>
          <strong class="fr-kpi__value">{{ formatNumber(totalInvoices) }}</strong>
        </div>
        <div class="fr-kpi">
          <span class="fr-kpi__label">Items Sold</span>
          <strong class="fr-kpi__value">{{ formatNumber(totalQtySold) }}</strong>
        </div>
        <div class="fr-kpi">
          <span class="fr-kpi__label">Grand Total (paid)</span>
          <strong class="fr-kpi__value">{{ formatZAR(totalSalesGrand) }}</strong>
        </div>
      </section>

      <!-- Payment breakdown -->
      <section v-if="payments && payments.byMethod.length" class="fr-section">
        <h3 class="fr-section__title">Payment Methods</h3>
        <table class="fr-table">
          <thead>
            <tr><th>Method</th><th class="fr-r">Invoices</th><th class="fr-r">Total</th></tr>
          </thead>
          <tbody>
            <tr v-for="b in payments.byMethod" :key="b.method">
              <td>{{ b.method }}</td>
              <td class="fr-r">{{ formatNumber(b.count) }}</td>
              <td class="fr-r">{{ formatZAR(b.grandTotal) }}</td>
            </tr>
          </tbody>
          <tfoot>
            <tr>
              <td><strong>Total</strong></td>
              <td class="fr-r"><strong>{{ formatNumber(payments.totalCount) }}</strong></td>
              <td class="fr-r"><strong>{{ formatZAR(payments.totalGrand) }}</strong></td>
            </tr>
          </tfoot>
        </table>
      </section>

      <!-- Daily breakdown -->
      <section v-if="daily.length" class="fr-section">
        <h3 class="fr-section__title">Daily Sales</h3>
        <table class="fr-table">
          <thead>
            <tr><th>Date</th><th class="fr-r">Invoices</th><th class="fr-r">Total</th></tr>
          </thead>
          <tbody>
            <tr v-for="d in daily" :key="d.date">
              <td>{{ d.date }}</td>
              <td class="fr-r">{{ formatNumber(d.invoiceCount) }}</td>
              <td class="fr-r">{{ formatZAR(d.grandTotal) }}</td>
            </tr>
          </tbody>
          <tfoot>
            <tr>
              <td><strong>Total</strong></td>
              <td class="fr-r"><strong>{{ formatNumber(totalInvoices) }}</strong></td>
              <td class="fr-r"><strong>{{ formatZAR(totalSalesGrand) }}</strong></td>
            </tr>
          </tfoot>
        </table>
      </section>

      <!-- Top products -->
      <section v-if="topProducts.length" class="fr-section fr-section--break">
        <h3 class="fr-section__title">Top Products by Revenue</h3>
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
              <td class="fr-r">{{ formatNumber(p.qtySold) }}</td>
              <td class="fr-r">{{ formatZAR(p.revenue) }}</td>
              <td class="fr-r">{{ p.discount ? formatZAR(p.discount) : '—' }}</td>
              <td class="fr-r">{{ formatZAR(p.costInclVat) }}</td>
              <td class="fr-r">{{ formatZAR((p.revenue - p.discount) / 1.15 - p.costExVat) }}</td>
            </tr>
          </tbody>
          <tfoot>
            <tr>
              <td colspan="2"><strong>Totals (all products)</strong></td>
              <td class="fr-r"><strong>{{ formatNumber(totalQtySold) }}</strong></td>
              <td class="fr-r"><strong>{{ formatZAR(totalRevenue) }}</strong></td>
              <td class="fr-r"><strong>{{ formatZAR(totalDiscount) }}</strong></td>
              <td class="fr-r"><strong>{{ formatZAR(totalCostInclVat) }}</strong></td>
              <td class="fr-r"><strong>{{ formatZAR(totalGP) }}</strong></td>
            </tr>
          </tfoot>
        </table>
      </section>

      <!-- GP formula note -->
      <footer class="fr-footer">
        <p>GP = (Revenue − Discounts) ÷ 1.15 − Wholesale cost</p>
        <p>{{ businessName }} — VAT reg. All amounts in ZAR.</p>
      </footer>
    </div>

    <div v-if="busy" class="fr-loading"><McSpinner /> Loading report…</div>
  </div>
</template>

<style scoped>
/* ── Print control ──────────────────────────────────────────────────── */
.no-print { }
@media print {
  .no-print { display: none !important; }
}

/* ── Page wrapper ───────────────────────────────────────────────────── */
.fr {
  max-width: 960px;
  margin: 0 auto;
  padding: 1.5rem;
}

.fr-controls {
  display: flex;
  flex-wrap: wrap;
  align-items: flex-end;
  gap: 1rem;
  margin-bottom: 1.5rem;
  padding: 1rem 1.25rem;
  background: var(--mc-app-surface, #fff);
  border: 1px solid var(--mc-app-border-soft, #ddd9d3);
  border-radius: var(--mc-app-radius-card, 18px);
}

.fr-controls :deep(.mc-field) { margin-bottom: 0; }

.fr-err {
  padding: 0.75rem 1rem;
  background: #fdecea;
  color: #b71c1c;
  border-radius: 8px;
  margin-bottom: 1rem;
}

.fr-loading {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  justify-content: center;
  padding: 3rem;
  color: var(--mc-app-text-muted, #5c5a56);
}

/* ── Report body ────────────────────────────────────────────────────── */
.fr-report {
  background: #fff;
  color: #1a1a1c;
  border-radius: 12px;
  padding: 2.5rem 2.5rem 1.5rem;
  border: 1px solid var(--mc-app-border-soft, #ddd9d3);
}

/* ── Header ─────────────────────────────────────────────────────────── */
.fr-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  gap: 1.5rem;
  flex-wrap: wrap;
}

.fr-header__logo {
  height: 54px;
  width: auto;
  object-fit: contain;
}

.fr-header__name {
  font-family: 'Barlow Condensed', sans-serif;
  font-size: 1.75rem;
  font-weight: 700;
  margin: 0;
  color: #0a0a0b;
}

.fr-header__meta { text-align: right; }

.fr-header__title {
  font-family: 'Barlow Condensed', sans-serif;
  font-size: 1.5rem;
  font-weight: 700;
  margin: 0;
  color: #0a0a0b;
  text-transform: uppercase;
  letter-spacing: 0.06em;
}

.fr-header__period {
  margin: 0.25rem 0 0;
  font-size: 0.95rem;
  font-weight: 600;
  color: #333;
}

.fr-header__gen {
  margin: 0.15rem 0 0;
  font-size: 0.78rem;
  color: #888;
}

.fr-divider {
  border: none;
  border-top: 2px solid #0a0a0b;
  margin: 1.25rem 0 1.5rem;
}

/* ── KPIs ───────────────────────────────────────────────────────────── */
.fr-kpis {
  display: grid;
  grid-template-columns: repeat(4, 1fr);
  gap: 1rem;
  margin-bottom: 2rem;
}

.fr-kpi {
  padding: 0.875rem 1rem;
  border: 1.5px solid #e0ddd8;
  border-radius: 10px;
  display: flex;
  flex-direction: column;
  gap: 0.15rem;
}

.fr-kpi--accent {
  border-color: #f47a20;
  background: #fef7f0;
}

.fr-kpi__label {
  font-size: 0.7rem;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  color: #777;
}

.fr-kpi__value {
  font-size: 1.25rem;
  font-weight: 700;
  color: #0a0a0b;
  font-variant-numeric: tabular-nums;
}

.fr-kpi__value--warn { color: #c45f18; }

/* ── Sections ───────────────────────────────────────────────────────── */
.fr-section { margin-bottom: 2rem; }

.fr-section__title {
  font-family: 'Barlow Condensed', sans-serif;
  font-size: 1.05rem;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  color: #333;
  border-bottom: 2px solid #e0ddd8;
  padding-bottom: 0.35rem;
  margin: 0 0 0.75rem;
}

/* ── Tables ──────────────────────────────────────────────────────────── */
.fr-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.85rem;
}

.fr-table th {
  text-align: left;
  font-size: 0.72rem;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.04em;
  color: #555;
  padding: 0.4rem 0.5rem;
  border-bottom: 1.5px solid #c8c5bd;
}

.fr-table td {
  padding: 0.35rem 0.5rem;
  border-bottom: 1px solid #eceae5;
  color: #222;
}

.fr-table tfoot td {
  border-top: 2px solid #c8c5bd;
  border-bottom: none;
  background: #faf9f6;
}

.fr-r { text-align: right; font-variant-numeric: tabular-nums; }
.fr-mono { font-weight: 600; font-variant-numeric: tabular-nums; }

/* ── Footer ─────────────────────────────────────────────────────────── */
.fr-footer {
  margin-top: 2rem;
  padding-top: 1rem;
  border-top: 1px solid #e0ddd8;
  font-size: 0.75rem;
  color: #999;
  text-align: center;
}

.fr-footer p { margin: 0.15rem 0; }

/* ── Print overrides ────────────────────────────────────────────────── */
@media print {
  @page {
    size: A4;
    margin: 15mm 12mm;
  }

  .fr {
    max-width: none;
    padding: 0;
  }

  .fr-report {
    border: none;
    border-radius: 0;
    padding: 0;
    box-shadow: none;
  }

  .fr-kpis { grid-template-columns: repeat(4, 1fr); }

  .fr-kpi {
    border: 1px solid #ccc;
    -webkit-print-color-adjust: exact;
    print-color-adjust: exact;
  }

  .fr-kpi--accent {
    background: #fef7f0 !important;
    -webkit-print-color-adjust: exact;
    print-color-adjust: exact;
  }

  .fr-table tfoot td {
    background: #f5f4f2 !important;
    -webkit-print-color-adjust: exact;
    print-color-adjust: exact;
  }

  .fr-section--break { page-break-before: auto; }
}

/* ── Responsive ─────────────────────────────────────────────────────── */
@media screen and (max-width: 700px) {
  .fr-report { padding: 1.25rem; }
  .fr-kpis { grid-template-columns: repeat(2, 1fr); }
  .fr-header { flex-direction: column; }
  .fr-header__meta { text-align: left; }
}
</style>
