<script setup lang="ts">
/**
 * Find sale — till-side lookup for the returns counter.
 *
 * Most receipts are printed without customer details. Staff scan/type a SKU,
 * barcode, item name, invoice number or customer name to see what was paid
 * (including any discount) so a return can be handled without the paper slip.
 */
import { computed, nextTick, onMounted, ref } from 'vue'
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

const DEFAULT_LOOKBACK_DAYS = 90

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

function toDateStr(d: Date) {
  return d.toISOString().slice(0, 10)
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
    // Keep focus ready for the next scan / typed query.
    void nextTick(() => searchInput.value?.focus())
  }
}

function resetFilters() {
  applyDefaultDateRange()
  includeVoided.value = false
  if (canSearch.value) void search()
}

function receiptUrl(l: SaleLine) {
  return `/#/receipt/${l.publicToken}?auto=0`
}

function invoiceUrl(l: SaleLine) {
  return `/#/invoice/${l.publicToken}`
}

function fmtWhen(iso: string): string {
  const d = new Date(iso)
  return isNaN(d.getTime()) ? '—' : d.toLocaleString('en-ZA')
}

onMounted(() => {
  applyDefaultDateRange()
  void nextTick(() => searchInput.value?.focus())
})
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

    <McCard v-if="rows" :title="`Results (${rows.length})`">
      <McEmptyState
        v-if="rows.length === 0"
        title="No matching sales"
        message="Try the SKU on its own, part of the item name, or widen the date range under More filters."
      />
      <div v-else class="hist-table-wrap">
        <table class="mc-table">
          <thead>
            <tr>
              <th>When</th>
              <th>Invoice</th>
              <th>Customer</th>
              <th>Item</th>
              <th class="hist-num">Qty</th>
              <th class="hist-num">Retail</th>
              <th class="hist-num">Discount</th>
              <th class="hist-num">Paid each</th>
              <th class="hist-num">Line total</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="(l, i) in rows" :key="i" :class="{ 'hist-row--void': l.status === 'Voided' }">
              <td>{{ fmtWhen(l.createdAt) }}</td>
              <td class="hist-mono">
                {{ l.invoiceNumber }}
                <McBadge v-if="l.status === 'Voided'" variant="danger">Voided</McBadge>
              </td>
              <td>{{ l.customerName || '—' }}</td>
              <td>
                <span class="hist-item">{{ l.description }}</span>
                <span v-if="l.sku" class="hist-mono hist-item__sku">{{ l.sku }}</span>
              </td>
              <td class="hist-num">{{ formatNumber(l.quantity) }}</td>
              <td class="hist-num">{{ formatZAR(l.originalUnitPrice || l.unitPrice) }}</td>
              <td class="hist-num">
                <span v-if="saving(l) > 0" class="hist-disc">
                  −{{ formatZAR(saving(l)) }}<br />
                  <small>({{ savingPercent(l) }}%)</small>
                </span>
                <span v-else>—</span>
              </td>
              <td class="hist-num hist-paid">{{ formatZAR(l.effectiveUnitPrice) }}</td>
              <td class="hist-num">{{ formatZAR(l.lineTotal) }}</td>
              <td class="hist-actions">
                <a class="hist-link" :href="invoiceUrl(l)" target="_blank" rel="noreferrer">Invoice</a>
                <a class="hist-link" :href="receiptUrl(l)" target="_blank" rel="noreferrer">Receipt</a>
              </td>
            </tr>
          </tbody>
        </table>
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

.hist-till__hint {
  margin: 0.55rem 0 0;
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

.hist-table-wrap {
  overflow-x: auto;
  -webkit-overflow-scrolling: touch;
}

.hist-num {
  text-align: right;
  font-variant-numeric: tabular-nums;
  white-space: nowrap;
}

.hist-paid {
  font-weight: 700;
}

.hist-disc {
  color: #cc0000;
  font-weight: 600;
}

.hist-mono {
  font-family: var(--mc-app-mono, ui-monospace, SFMono-Regular, Menlo, monospace);
  font-size: 0.875rem;
}

.hist-item {
  display: block;
}

.hist-item__sku {
  display: block;
  color: var(--mc-app-text-muted, #5c5a56);
  font-size: 0.78rem;
}

.hist-row--void {
  opacity: 0.6;
}

.hist-actions {
  white-space: nowrap;
}

.hist-link {
  color: var(--mc-app-accent, #8a6d3b);
  font-size: 0.82rem;
  font-weight: 600;
  text-decoration: none;
}

.hist-link + .hist-link {
  margin-left: 0.6rem;
}

.hist-link:hover {
  text-decoration: underline;
}

@media (max-width: 640px) {
  .hist-till__search {
    flex-direction: column;
  }
}
</style>
