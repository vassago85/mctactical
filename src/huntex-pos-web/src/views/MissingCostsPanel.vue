<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { http } from '@/api/http'
import { useAuthStore } from '@/stores/auth'
import { useToast } from '@/composables/useToast'
import { formatZAR, formatNumber } from '@/utils/format'
import McCard from '@/components/ui/McCard.vue'
import McButton from '@/components/ui/McButton.vue'
import McField from '@/components/ui/McField.vue'
import McAlert from '@/components/ui/McAlert.vue'
import McBadge from '@/components/ui/McBadge.vue'
import McSpinner from '@/components/ui/McSpinner.vue'
import McEmptyState from '@/components/ui/McEmptyState.vue'

type MissingCostRow = {
  key: string
  isUnlinkedShopify: boolean
  productId: string | null
  shopifyVariantId: number | null
  sku: string | null
  name: string
  qtySold: number
  saleCount: number
  revenue: number
  lastSoldAt: string
}

const auth = useAuthStore()
const toast = useToast()
const canLinkShopify = computed(() => auth.hasRole('Owner', 'Dev'))

const fromDate = ref('')
const toDate = ref('')
const rows = ref<MissingCostRow[]>([])
const costs = ref<Record<string, string | number>>({})
const busy = ref(false)
const saving = ref(false)
const loaded = ref(false)
const err = ref<string | null>(null)

const totalRevenue = computed(() => rows.value.reduce((s, r) => s + r.revenue, 0))

const entries = computed(() =>
  rows.value
    .map(r => ({ key: r.key, raw: String(costs.value[r.key] ?? '').trim() }))
    .filter(e => e.raw !== '')
    .map(e => ({ key: e.key, costExVat: Number(e.raw.replace(',', '.')) }))
)
const invalidEntry = computed(() => entries.value.some(e => !Number.isFinite(e.costExVat) || e.costExVat <= 0))

function params() {
  const p: Record<string, string> = {}
  if (fromDate.value) p.from = new Date(`${fromDate.value}T00:00:00`).toISOString()
  if (toDate.value) p.to = new Date(`${toDate.value}T23:59:59.999`).toISOString()
  return p
}

async function load() {
  busy.value = true
  err.value = null
  try {
    const { data } = await http.get<MissingCostRow[]>('/api/missing-costs', { params: params() })
    rows.value = data
    loaded.value = true
  } catch (e: unknown) {
    const ax = e as { response?: { data?: { error?: string } } }
    err.value = ax.response?.data?.error ?? 'Could not load items with missing costs.'
  } finally {
    busy.value = false
  }
}

function clearDates() {
  fromDate.value = ''
  toDate.value = ''
  void load()
}

async function save() {
  if (!entries.value.length || invalidEntry.value) return
  saving.value = true
  err.value = null
  try {
    const { data } = await http.post<{ productsUpdated: number; linesUpdated: number }>('/api/missing-costs', { items: entries.value })
    const items = entries.value.length
    toast.success(`Saved ${items} cost${items === 1 ? '' : 's'} · ${data.linesUpdated} past sale line${data.linesUpdated === 1 ? '' : 's'} updated`)
    costs.value = {}
    await load()
  } catch (e: unknown) {
    const ax = e as { response?: { data?: { error?: string } } }
    err.value = ax.response?.data?.error ?? 'Could not save costs.'
  } finally {
    saving.value = false
  }
}

function fmtDate(iso: string) {
  return new Date(iso).toLocaleDateString('en-ZA', { day: 'numeric', month: 'short', year: 'numeric' })
}

onMounted(load)
</script>

<template>
  <McCard title="Items sold with no cost">
    <p class="mcost-note">
      These items were sold while their cost was R0, so reports count the whole sale as profit.
      Enter the cost <strong>excluding VAT</strong>. It is saved on the product for future sales and filled in on
      every past sale that had no cost. Sales that already have a cost are not changed, and selling prices stay as they are.
    </p>
    <div class="mcost-controls">
      <McField label="Sold from" for-id="mcost-from">
        <input id="mcost-from" v-model="fromDate" type="date" @change="load" />
      </McField>
      <McField label="Sold to" for-id="mcost-to">
        <input id="mcost-to" v-model="toDate" type="date" @change="load" />
      </McField>
      <McButton v-if="fromDate || toDate" variant="ghost" type="button" @click="clearDates">All time</McButton>
      <McButton variant="secondary" type="button" :disabled="busy" @click="load">
        <McSpinner v-if="busy" />
        <span v-else>Refresh</span>
      </McButton>
    </div>
  </McCard>

  <McAlert v-if="err" variant="error">{{ err }}</McAlert>

  <McCard v-if="loaded" :title="rows.length ? `${formatNumber(rows.length)} item${rows.length === 1 ? '' : 's'} · ${formatZAR(totalRevenue)} counted as 100% profit` : 'Missing costs'">
    <McEmptyState
      v-if="!rows.length"
      title="Every sold item has a cost"
      hint="Gross profit in the reports is based on real costs for this period."
    />
    <template v-else>
      <div class="mcost-table-wrap">
        <table class="mc-table">
          <thead>
            <tr>
              <th>Item</th>
              <th class="mcost-num">Qty sold</th>
              <th class="mcost-num">Revenue</th>
              <th>Last sold</th>
              <th class="mcost-cost">Cost ex VAT (each)</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="r in rows" :key="r.key" class="mcost-row">
              <td>
                <strong>{{ r.name }}</strong>
                <span v-if="r.sku" class="mcost-sku">{{ r.sku }}</span>
                <div v-if="r.isUnlinkedShopify" class="mcost-online">
                  <McBadge variant="warning">Online item, not linked</McBadge>
                  <RouterLink v-if="canLinkShopify" class="mcost-link" to="/settings/shopify">Link to product</RouterLink>
                </div>
              </td>
              <td class="mcost-num">{{ formatNumber(r.qtySold) }}</td>
              <td class="mcost-num">{{ formatZAR(r.revenue) }}</td>
              <td>{{ fmtDate(r.lastSoldAt) }}</td>
              <td class="mcost-cost">
                <input
                  v-model="costs[r.key]"
                  type="number"
                  inputmode="decimal"
                  min="0.01"
                  step="0.01"
                  placeholder="0.00"
                  :aria-label="`Cost excluding VAT for ${r.name}`"
                />
              </td>
            </tr>
          </tbody>
        </table>
      </div>
      <p v-if="invalidEntry" class="mcost-invalid">Costs must be more than R0.</p>
      <div class="mcost-actions">
        <McButton variant="primary" type="button" :disabled="saving || !entries.length || invalidEntry" @click="save">
          <McSpinner v-if="saving" />
          <span v-else-if="entries.length">Save {{ entries.length }} cost{{ entries.length === 1 ? '' : 's' }}</span>
          <span v-else>Enter a cost to save</span>
        </McButton>
      </div>
    </template>
  </McCard>
</template>

<style scoped>
.mcost-note {
  margin: 0 0 0.85rem;
  font-size: 0.875rem;
  color: var(--mc-app-text-muted, #5c5a56);
}
.mcost-controls {
  display: flex;
  flex-wrap: wrap;
  align-items: flex-end;
  gap: 0.75rem;
}
.mcost-controls :deep(.mc-field) {
  margin-bottom: 0;
}
.mcost-table-wrap {
  overflow-x: auto;
  -webkit-overflow-scrolling: touch;
}
.mcost-num {
  text-align: right;
  font-variant-numeric: tabular-nums;
  white-space: nowrap;
}
.mcost-sku {
  display: block;
  font-size: 0.78rem;
  color: var(--mc-app-text-muted, #5c5a56);
  font-family: ui-monospace, SFMono-Regular, Menlo, monospace;
}
.mcost-online {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.5rem;
  margin-top: 0.3rem;
}
.mcost-link {
  font-size: 0.82rem;
  font-weight: 600;
  color: var(--mc-accent, #f47a20);
}
.mcost-cost {
  width: 9.5rem;
  text-align: right;
}
.mcost-cost input {
  width: 100%;
  min-height: 2.5rem;
  text-align: right;
  font-variant-numeric: tabular-nums;
}
.mcost-invalid {
  margin: 0.75rem 0 0;
  color: var(--mc-danger, #c62828);
  font-size: 0.875rem;
}
.mcost-actions {
  display: flex;
  justify-content: flex-end;
  margin-top: 1rem;
}
</style>
