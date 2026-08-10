<script setup lang="ts">
/**
 * Find sale — till-side lookup for the returns counter.
 *
 * Most receipts are printed without customer details. Staff scan/type a SKU,
 * barcode, item name, invoice number or customer name to see what was paid
 * (including any discount) so a return can be handled without the paper slip.
 */
import { computed, nextTick, onMounted, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { http } from '@/api/http'
import { useToast } from '@/composables/useToast'
import { formatZAR, formatNumber } from '@/utils/format'
import McPageHeader from '@/components/ui/McPageHeader.vue'
import McCard from '@/components/ui/McCard.vue'
import McButton from '@/components/ui/McButton.vue'
import McField from '@/components/ui/McField.vue'
import McAlert from '@/components/ui/McAlert.vue'
import McBadge from '@/components/ui/McBadge.vue'
import McSpinner from '@/components/ui/McSpinner.vue'
import McEmptyState from '@/components/ui/McEmptyState.vue'

type SaleLine = {
  invoiceId: string
  invoiceNumber: string
  createdAt: string
  status: string
  customerName?: string | null
  paymentMethod: string
  publicToken: string
  productId: string
  sku?: string | null
  description: string
  quantity: number
  originalUnitPrice: number
  unitPrice: number
  lineDiscount: number
  lineTotal: number
  effectiveUnitPrice: number
}

type SaleGroup = {
  invoiceId: string
  invoiceNumber: string
  createdAt: string
  status: string
  customerName?: string | null
  paymentMethod: string
  publicToken: string
  lines: SaleLine[]
}

const DEFAULT_LOOKBACK_DAYS = 90

const route = useRoute()
const toast = useToast()
const q = ref('')
const fromDate = ref('')
const toDate = ref('')
const includeVoided = ref(false)
const showMoreFilters = ref(false)
const rows = ref<SaleLine[] | null>(null)
const busy = ref(false)
const err = ref<string | null>(null)
const searchInput = ref<HTMLInputElement | null>(null)

const canSearch = computed(() => q.value.trim().length >= 2)

/** Group flat API lines into receipt cards (order follows API: newest first). */
const groups = computed<SaleGroup[]>(() => {
  if (!rows.value?.length) return []
  const order: string[] = []
  const map = new Map<string, SaleGroup>()
  for (const l of rows.value) {
    let g = map.get(l.invoiceId)
    if (!g) {
      g = {
        invoiceId: l.invoiceId,
        invoiceNumber: l.invoiceNumber,
        createdAt: l.createdAt,
        status: l.status,
        customerName: l.customerName,
        paymentMethod: l.paymentMethod,
        publicToken: l.publicToken,
        lines: []
      }
      map.set(l.invoiceId, g)
      order.push(l.invoiceId)
    }
    g.lines.push(l)
  }
  return order.map((id) => map.get(id)!)
})

const resultsTitle = computed(() => {
  if (!rows.value) return ''
  const saleCount = groups.value.length
  const lineCount = rows.value.length
  if (saleCount === lineCount) return `Results (${saleCount})`
  return `Results (${saleCount} sale${saleCount === 1 ? '' : 's'}, ${lineCount} line${lineCount === 1 ? '' : 's'})`
})

/** Local calendar date — toISOString would shift the SA day either side of midnight. */
function toDateStr(d: Date) {
  const month = `${d.getMonth() + 1}`.padStart(2, '0')
  const day = `${d.getDate()}`.padStart(2, '0')
  return `${d.getFullYear()}-${month}-${day}`
}

/** Default window matches the 90-day returns policy on the receipt. */
function applyDefaultDateRange() {
  const end = new Date()
  const start = new Date()
  start.setDate(start.getDate() - DEFAULT_LOOKBACK_DAYS)
  fromDate.value = toDateStr(start)
  toDate.value = toDateStr(end)
}

const dateRangeHint = computed(() => {
  if (!fromDate.value && !toDate.value) return 'All dates'
  if (fromDate.value && toDate.value) return `${fromDate.value} → ${toDate.value}`
  if (fromDate.value) return `From ${fromDate.value}`
  return `Until ${toDate.value}`
})

/** Rand off retail on this line, however it was given (promotion or operator concession). */
function saving(l: SaleLine): number {
  const promo = l.originalUnitPrice > l.unitPrice
    ? (l.originalUnitPrice - l.unitPrice) * l.quantity
    : 0
  return Math.round((promo + l.lineDiscount) * 100) / 100
}

function savingPercent(l: SaleLine): number {
  const gross = l.lineTotal + saving(l)
  if (gross <= 0) return 0
  return Math.round(saving(l) / gross * 1000) / 10
}

async function search() {
  if (!canSearch.value) return
  err.value = null
  busy.value = true
  try {
    const params: Record<string, string | boolean | number> = { q: q.value.trim(), take: 200 }
    if (fromDate.value) params.from = new Date(fromDate.value).toISOString()
    if (toDate.value) {
      const end = new Date(toDate.value)
      end.setHours(23, 59, 59, 999)
      params.to = end.toISOString()
    }
    if (includeVoided.value) params.includeVoided = true

    const { data } = await http.get<SaleLine[]>('/api/invoices/search-lines', { params })
    rows.value = data
  } catch (e: unknown) {
    const ax = e as { response?: { data?: { error?: string } }; message?: string }
    err.value = ax.response?.data?.error ?? ax.message ?? 'Search failed'
    toast.error(err.value)
  } finally {
    busy.value = false
    void nextTick(() => searchInput.value?.focus())
  }
}

function resetFilters() {
  applyDefaultDateRange()
  includeVoided.value = false
  if (canSearch.value) void search()
}

function receiptUrl(g: SaleGroup) {
  return `/#/receipt/${g.publicToken}?auto=0`
}

function invoiceUrl(g: SaleGroup) {
  return `/#/invoice/${g.publicToken}`
}

function fmtWhen(iso: string): string {
  const d = new Date(iso)
  return isNaN(d.getTime()) ? '—' : d.toLocaleString('en-ZA')
}

onMounted(async () => {
  applyDefaultDateRange()
  const incoming = typeof route.query.q === 'string' ? route.query.q.trim() : ''
  if (incoming) q.value = incoming
  await nextTick()
  searchInput.value?.focus()
  if (canSearch.value) await search()
})

// POS "Scan to find" links here with ?q=. When the page is already mounted the
// component is reused, so re-run the search instead of showing stale results.
watch(
  () => route.query.q,
  async (incoming) => {
    const term = typeof incoming === 'string' ? incoming.trim() : ''
    if (!term || term === q.value.trim()) return
    q.value = term
    await nextTick()
    searchInput.value?.focus()
    if (canSearch.value) await search()
  }
)
</script>

<template>
  <div class="hist-page">
    <McPageHeader title="Find sale">
      <template #default>
        Look up what a customer paid when they have no receipt. Scan a barcode or type a
        SKU, item name, invoice number or customer name.
      </template>
    </McPageHeader>

    <McCard>
      <form class="hist-till" @submit.prevent="search">
        <div class="hist-till__search">
          <input
            id="hist-q"
            ref="searchInput"
            v-model="q"
            type="search"
            class="hist-till__input"
            placeholder="Scan barcode or type SKU / item / invoice…"
            autocomplete="off"
            enterkeyhint="search"
          />
          <McButton variant="primary" type="submit" :disabled="busy || !canSearch">
            <McSpinner v-if="busy" />
            <span v-else>Find</span>
          </McButton>
        </div>
        <p class="hist-till__help">No receipt? Scan the item or type the SKU.</p>
        <p class="hist-till__hint">
          Searching {{ dateRangeHint }}
          <span v-if="!canSearch"> · enter at least 2 characters</span>
        </p>

        <div class="hist-till__toggle-row">
          <button
            type="button"
            class="hist-till__more"
            :aria-expanded="showMoreFilters"
            @click="showMoreFilters = !showMoreFilters"
          >
            {{ showMoreFilters ? 'Hide filters' : 'More filters' }}
          </button>
        </div>

        <div v-if="showMoreFilters" class="hist-till__filters">
          <McField label="From" for-id="hist-from">
            <input id="hist-from" v-model="fromDate" type="date" />
          </McField>
          <McField label="To" for-id="hist-to">
            <input id="hist-to" v-model="toDate" type="date" />
          </McField>
          <label class="hist-check">
            <input v-model="includeVoided" type="checkbox" />
            Include voided sales
          </label>
          <McButton variant="ghost" dense type="button" @click="resetFilters">
            Reset to last {{ DEFAULT_LOOKBACK_DAYS }} days
          </McButton>
        </div>
      </form>
    </McCard>

    <McAlert v-if="err" variant="error">{{ err }}</McAlert>

    <McCard v-if="rows" :title="resultsTitle">
      <McEmptyState
        v-if="rows.length === 0"
        title="No matching sales"
        hint="Try the SKU on its own, part of the item name, or widen the date range under More filters."
      />
      <div v-else class="hist-groups">
        <article
          v-for="g in groups"
          :key="g.invoiceId"
          class="hist-receipt"
          :class="{ 'hist-receipt--void': g.status === 'Voided' }"
        >
          <header class="hist-receipt__head">
            <div class="hist-receipt__meta">
              <div class="hist-receipt__num">
                {{ g.invoiceNumber }}
                <McBadge v-if="g.status === 'Voided'" variant="danger">Voided</McBadge>
              </div>
              <div class="hist-receipt__when">{{ fmtWhen(g.createdAt) }}</div>
              <div class="hist-receipt__who">
                {{ g.customerName || 'No customer on receipt' }}
                <span v-if="g.paymentMethod"> · {{ g.paymentMethod }}</span>
              </div>
            </div>
            <div class="hist-receipt__actions">
              <a class="hist-action hist-action--primary" :href="receiptUrl(g)" target="_blank" rel="noreferrer">
                Reprint receipt
              </a>
              <a class="hist-action" :href="invoiceUrl(g)" target="_blank" rel="noreferrer">
                Open invoice
              </a>
            </div>
          </header>

          <ul class="hist-receipt__lines">
            <li v-for="(l, i) in g.lines" :key="i" class="hist-line">
              <div class="hist-line__main">
                <div class="hist-line__item">
                  <span class="hist-line__name">{{ l.description }}</span>
                  <span v-if="l.sku" class="hist-line__sku">{{ l.sku }}</span>
                  <span class="hist-line__qty">Qty {{ formatNumber(l.quantity) }}</span>
                </div>
                <div class="hist-line__paid">
                  <span class="hist-line__paid-label">Paid each</span>
                  <strong class="hist-line__paid-val">{{ formatZAR(l.effectiveUnitPrice) }}</strong>
                </div>
              </div>
              <div class="hist-line__foot">
                <McBadge v-if="saving(l) > 0" variant="warning">
                  Discount −{{ formatZAR(saving(l)) }} ({{ savingPercent(l) }}%)
                </McBadge>
                <span class="hist-line__retail">Retail {{ formatZAR(l.originalUnitPrice || l.unitPrice) }}</span>
                <span class="hist-line__total">Line {{ formatZAR(l.lineTotal) }}</span>
              </div>
            </li>
          </ul>
        </article>
      </div>
    </McCard>
  </div>
</template>

<style scoped>
.hist-page {
  min-height: 100%;
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.hist-till__search {
  display: flex;
  gap: 0.6rem;
  align-items: stretch;
}

.hist-till__input {
  flex: 1 1 auto;
  min-width: 0;
  font-size: 1.15rem;
  padding: 0.85rem 1rem;
  border: 1px solid var(--mc-app-border-soft, #ddd9d3);
  border-radius: 10px;
  background: var(--mc-app-surface, #fff);
  color: inherit;
}

.hist-till__input:focus {
  outline: 2px solid var(--mc-app-accent, #8a6d3b);
  outline-offset: 1px;
}

.hist-till__help {
  margin: 0.65rem 0 0;
  font-size: 0.95rem;
  font-weight: 600;
  color: var(--mc-app-heading, #0a0a0c);
}

.hist-till__hint {
  margin: 0.35rem 0 0;
  font-size: 0.82rem;
  color: var(--mc-app-text-muted, #5c5a56);
}

.hist-till__toggle-row {
  margin-top: 0.65rem;
}

.hist-till__more {
  appearance: none;
  border: none;
  background: none;
  padding: 0;
  font: inherit;
  font-size: 0.85rem;
  font-weight: 600;
  color: var(--mc-app-accent, #8a6d3b);
  cursor: pointer;
  text-decoration: underline;
}

.hist-till__filters {
  display: flex;
  flex-wrap: wrap;
  gap: 0.75rem;
  align-items: end;
  margin-top: 0.85rem;
  padding-top: 0.85rem;
  border-top: 1px solid var(--mc-app-border-soft, #ddd9d3);
}

.hist-check {
  display: inline-flex;
  align-items: center;
  gap: 0.4rem;
  font-size: 0.85rem;
  color: var(--mc-app-text-muted, #5c5a56);
  padding-bottom: 0.35rem;
}

.hist-groups {
  display: flex;
  flex-direction: column;
  gap: 0.85rem;
}

.hist-receipt {
  border: 1px solid var(--mc-app-border-soft, #ddd9d3);
  border-radius: 12px;
  background: var(--mc-app-surface, #fff);
  overflow: hidden;
}

.hist-receipt--void {
  opacity: 0.65;
}

.hist-receipt__head {
  display: flex;
  flex-wrap: wrap;
  gap: 0.75rem 1rem;
  justify-content: space-between;
  align-items: flex-start;
  padding: 0.85rem 1rem;
  border-bottom: 1px solid var(--mc-app-border-soft, #ddd9d3);
  background: var(--mc-app-bg, #f6f4f0);
}

.hist-receipt__num {
  display: flex;
  align-items: center;
  gap: 0.45rem;
  font-family: var(--mc-app-mono, ui-monospace, SFMono-Regular, Menlo, monospace);
  font-weight: 700;
  font-size: 1rem;
}

.hist-receipt__when,
.hist-receipt__who {
  font-size: 0.85rem;
  color: var(--mc-app-text-muted, #5c5a56);
  margin-top: 0.2rem;
}

.hist-receipt__actions {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
}

.hist-action {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-height: 40px;
  padding: 0 0.9rem;
  border-radius: 10px;
  font-size: 0.78rem;
  font-weight: 700;
  letter-spacing: 0.03em;
  text-transform: uppercase;
  text-decoration: none;
  border: 1px solid var(--mc-app-border-soft, #ddd9d3);
  color: var(--mc-app-heading, #0a0a0c);
  background: #fff;
}

.hist-action:hover {
  background: rgba(0, 0, 0, 0.04);
}

.hist-action--primary {
  border-color: var(--mc-app-accent, #f47a20);
  background: var(--mc-app-accent, #f47a20);
  color: #fff;
}

.hist-action--primary:hover {
  filter: brightness(0.95);
  background: var(--mc-app-accent, #f47a20);
}

.hist-receipt__lines {
  list-style: none;
  margin: 0;
  padding: 0;
}

.hist-line {
  padding: 0.85rem 1rem;
  border-bottom: 1px solid var(--mc-app-border-faint, #eceae5);
}

.hist-line:last-child {
  border-bottom: none;
}

.hist-line__main {
  display: flex;
  flex-wrap: wrap;
  gap: 0.75rem 1.25rem;
  justify-content: space-between;
  align-items: flex-start;
}

.hist-line__name {
  display: block;
  font-weight: 700;
  font-size: 1rem;
}

.hist-line__sku,
.hist-line__qty {
  display: inline-block;
  margin-top: 0.2rem;
  margin-right: 0.75rem;
  font-size: 0.8rem;
  color: var(--mc-app-text-muted, #5c5a56);
}

.hist-line__sku {
  font-family: var(--mc-app-mono, ui-monospace, SFMono-Regular, Menlo, monospace);
}

.hist-line__paid {
  text-align: right;
  min-width: 7.5rem;
}

.hist-line__paid-label {
  display: block;
  font-size: 0.72rem;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.04em;
  color: var(--mc-app-text-muted, #5c5a56);
}

.hist-line__paid-val {
  display: block;
  font-size: 1.45rem;
  font-weight: 800;
  font-variant-numeric: tabular-nums;
  line-height: 1.15;
  color: var(--mc-app-heading, #0a0a0c);
}

.hist-line__foot {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem 0.85rem;
  align-items: center;
  margin-top: 0.55rem;
  font-size: 0.8rem;
  color: var(--mc-app-text-muted, #5c5a56);
}

.hist-line__total {
  font-variant-numeric: tabular-nums;
  margin-left: auto;
}

@media (max-width: 640px) {
  .hist-till__search {
    flex-direction: column;
  }

  .hist-line__paid {
    text-align: left;
    width: 100%;
  }

  .hist-line__total {
    margin-left: 0;
  }
}
</style>
