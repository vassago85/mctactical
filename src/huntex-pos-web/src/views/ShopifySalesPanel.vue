<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { http } from '@/api/http'
import { formatZAR, formatNumber } from '@/utils/format'
import McCard from '@/components/ui/McCard.vue'
import McButton from '@/components/ui/McButton.vue'
import McField from '@/components/ui/McField.vue'
import McAlert from '@/components/ui/McAlert.vue'
import McSpinner from '@/components/ui/McSpinner.vue'

type Dashboard = {
  revenue: number
  orders: number
  units: number
  avgOrderValue: number
}

function toDateStr(d: Date) { return d.toISOString().slice(0, 10) }

const from = ref(toDateStr(new Date(Date.now() - 30 * 864e5)))
const to = ref(toDateStr(new Date()))
const dash = ref<Dashboard | null>(null)
const busy = ref(false)
const err = ref<string | null>(null)

async function load() {
  busy.value = true
  err.value = null
  try {
    const params: Record<string, string> = {}
    if (from.value) params.from = new Date(from.value).toISOString()
    if (to.value) { const e = new Date(to.value); e.setHours(23, 59, 59, 999); params.to = e.toISOString() }
    const { data } = await http.get<Dashboard>('/api/shopify/dashboard', { params })
    dash.value = data
  } catch (e) {
    const ax = e as { response?: { data?: { error?: string } }; message?: string }
    err.value = ax.response?.data?.error ?? ax.message ?? 'Could not load Shopify sales'
  } finally {
    busy.value = false
  }
}

onMounted(() => void load())
</script>

<template>
  <McCard title="Shopify sales">
    <div class="shp-period">
      <McField label="From" for-id="shp-from"><input id="shp-from" v-model="from" type="date" /></McField>
      <McField label="To" for-id="shp-to"><input id="shp-to" v-model="to" type="date" /></McField>
      <McButton variant="primary" type="button" :disabled="busy" @click="load">
        <McSpinner v-if="busy" />
        <span v-else>Run report</span>
      </McButton>
    </div>

    <McAlert v-if="err" variant="error">{{ err }}</McAlert>

    <div v-if="dash" class="shp-kpis">
      <div class="kpi kpi--accent">
        <span class="kpi__label">Shopify revenue (incl VAT)</span>
        <strong class="kpi__value">{{ formatZAR(dash.revenue) }}</strong>
        <span class="kpi__sub">selected period</span>
      </div>
      <div class="kpi">
        <span class="kpi__label">Orders</span>
        <strong class="kpi__value">{{ formatNumber(dash.orders) }}</strong>
        <span class="kpi__sub">selected period</span>
      </div>
      <div class="kpi">
        <span class="kpi__label">Items sold</span>
        <strong class="kpi__value">{{ formatNumber(dash.units) }}</strong>
        <span class="kpi__sub">units, selected period</span>
      </div>
      <div class="kpi">
        <span class="kpi__label">Avg order value</span>
        <strong class="kpi__value">{{ formatZAR(dash.avgOrderValue) }}</strong>
        <span class="kpi__sub">selected period</span>
      </div>
    </div>
    <p class="shp-note">
      Online orders are pulled in from Shopify under Settings → Integrations. Unlinked items and product matching live there too.
    </p>
  </McCard>
</template>

<style scoped>
.shp-period { display: flex; flex-wrap: wrap; align-items: flex-end; gap: 0.75rem; margin-bottom: 1rem; }
.shp-period :deep(.mc-field) { margin-bottom: 0; }
.shp-kpis { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: 0.75rem; margin: 0.5rem 0; }
.kpi { border: 1px solid var(--mc-app-border-soft, #e0ddd8); border-radius: 12px; padding: 0.85rem 1rem; background: var(--mc-app-surface, #fff); display: flex; flex-direction: column; gap: 0.15rem; }
.kpi--accent { border-color: var(--mc-accent, #f47a20); box-shadow: inset 3px 0 0 var(--mc-accent, #f47a20); }
.kpi__label { font-size: 0.72rem; letter-spacing: 0.05em; text-transform: uppercase; color: var(--mc-app-text-muted, #8a8780); }
.kpi__value { font-size: 1.4rem; font-weight: 700; color: var(--mc-app-text, #1a1a1c); font-variant-numeric: tabular-nums; }
.kpi__sub { font-size: 0.74rem; color: var(--mc-app-text-muted, #8a8780); }
.shp-note { margin: 0.75rem 0 0; font-size: 0.82rem; color: var(--mc-app-text-muted, #5c5a56); }
@media (max-width: 900px) { .shp-kpis { grid-template-columns: repeat(2, minmax(0, 1fr)); } }
</style>
