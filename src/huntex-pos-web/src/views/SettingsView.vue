<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { http } from '@/api/http'

const dto = ref({
  defaultMarginPercent: 50,
  defaultFixedMarkup: 0,
  useMarginPercent: true,
  pricingMode: 'normal',
  hideCostForSalesRole: true
})
const err = ref<string | null>(null)
const ok = ref<string | null>(null)
const recalcBusy = ref(false)
const recalcMsg = ref<string | null>(null)

async function load() {
  const { data } = await http.get('/api/settings/pricing')
  dto.value = { ...data }
}

onMounted(() => void load().catch(() => (err.value = 'Load failed')))

async function save() {
  err.value = null
  ok.value = null
  try {
    await http.put('/api/settings/pricing', dto.value)
    ok.value = 'Saved'
  } catch {
    err.value = 'Save failed'
  }
}

async function recalculateAll() {
  if (!confirm('Recalculate ALL active product sell prices from cost using current settings? This cannot be undone.'))
    return
  recalcBusy.value = true
  recalcMsg.value = null
  try {
    const { data } = await http.post('/api/settings/pricing/recalculate')
    let msg = `Done — ${data.updated} of ${data.total} products recalculated.`
    if (data.belowDistributorCost > 0)
      msg += ` ⚠ ${data.belowDistributorCost} products below distributor cost.`
    recalcMsg.value = msg
  } catch (e: unknown) {
    const ax = e as { response?: { data?: { error?: string } } }
    recalcMsg.value = ax.response?.data?.error ?? 'Recalculate failed'
  } finally {
    recalcBusy.value = false
  }
}
</script>

<template>
  <h1>Pricing</h1>
  <p class="err" v-if="err">{{ err }}</p>
  <p v-if="ok" style="color: #a5d6a7">{{ ok }}</p>
  <div class="card">
    <div class="field">
      <label style="font-weight: 600; font-size: 1rem">Pricing mode</label>
      <div style="display: flex; flex-direction: column; gap: 0.5rem; margin-top: 0.35rem">
        <label style="display: flex; align-items: center; gap: 0.5rem; cursor: pointer">
          <input type="radio" value="normal" v-model="dto.pricingMode" />
          <span><strong>Normal retail</strong> — cost × 1.5, rounded up to nearest R10</span>
        </label>
        <label style="display: flex; align-items: center; gap: 0.5rem; cursor: pointer">
          <input type="radio" value="huntex" v-model="dto.pricingMode" />
          <span><strong>Huntex pricing</strong> — normal sell ÷ 1.1, rounded up to nearest R10</span>
        </label>
      </div>
      <p style="color: var(--mc-muted); font-size: 0.85rem; margin: 0.5rem 0 0">
        All sell prices are always rounded up to the nearest R10.<br />
        A warning appears if the sell price is below the distributor cost (ex-VAT + 15%).
      </p>
    </div>
    <hr style="border-color: var(--mc-border); margin: 1rem 0" />
    <label><input type="checkbox" v-model="dto.useMarginPercent" /> Use margin % (else fixed markup)</label>
    <div class="field">
      <label>Default margin % (on wholesale cost; 50 = list 1.5× cost)</label>
      <input type="number" v-model.number="dto.defaultMarginPercent" step="0.01" />
    </div>
    <div class="field">
      <label>Default fixed markup (R)</label>
      <input type="number" v-model.number="dto.defaultFixedMarkup" step="0.01" />
    </div>
    <hr style="border-color: var(--mc-border); margin: 1rem 0" />
    <p style="color: var(--mc-muted); font-size: 0.88rem; margin: 0">
      Invoices do not add VAT — MC Tactical is not VAT registered.
    </p>
    <label><input type="checkbox" v-model="dto.hideCostForSalesRole" /> Hide cost for Sales role</label>
    <div style="margin-top: 1rem; display: flex; gap: 0.75rem; align-items: center; flex-wrap: wrap">
      <button type="button" class="btn" @click="save">Save</button>
      <button type="button" class="btn secondary" :disabled="recalcBusy" @click="recalculateAll">
        Recalculate all product prices
      </button>
    </div>
    <p v-if="recalcMsg" style="color: var(--mc-muted); font-size: 0.85rem; margin: 0.5rem 0 0">
      {{ recalcMsg }}
    </p>
  </div>
</template>
