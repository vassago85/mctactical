<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { http } from '@/api/http'

const dto = ref({
  defaultMarginPercent: 50,
  defaultFixedMarkup: 0,
  useMarginPercent: true,
  roundSellToNearest: 0,
  hideCostForSalesRole: true
})
const err = ref<string | null>(null)
const ok = ref<string | null>(null)
const reroundBusy = ref(false)
const reroundMsg = ref<string | null>(null)

const pricingMode = computed({
  get: () => (dto.value.roundSellToNearest >= 10 ? 'huntex' : 'normal'),
  set: (v: string) => {
    dto.value.roundSellToNearest = v === 'huntex' ? 10 : 0
  }
})

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

async function reroundAll() {
  reroundBusy.value = true
  reroundMsg.value = null
  try {
    const { data } = await http.post('/api/settings/pricing/reround')
    reroundMsg.value = `Done — ${data.updated} of ${data.total} products re-rounded.`
  } catch (e: unknown) {
    const ax = e as { response?: { data?: { error?: string } } }
    reroundMsg.value = ax.response?.data?.error ?? 'Re-round failed'
  } finally {
    reroundBusy.value = false
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
          <input type="radio" value="normal" v-model="pricingMode" />
          <span><strong>Normal retail</strong> — sell prices as-is (no rounding)</span>
        </label>
        <label style="display: flex; align-items: center; gap: 0.5rem; cursor: pointer">
          <input type="radio" value="huntex" v-model="pricingMode" />
          <span><strong>Huntex pricing</strong> — round all sell prices up to the nearest R10</span>
        </label>
      </div>
      <p style="color: var(--mc-muted); font-size: 0.85rem; margin: 0.35rem 0 0">
        Huntex pricing rounds sell prices on import, new products, and price edits.
      </p>
    </div>
    <div v-if="pricingMode === 'huntex'" class="field" style="margin-top: 0.5rem">
      <button type="button" class="btn secondary" :disabled="reroundBusy" @click="reroundAll">
        Re-round all existing products to nearest R10
      </button>
      <p v-if="reroundMsg" style="color: var(--mc-muted); font-size: 0.85rem; margin: 0.35rem 0 0">
        {{ reroundMsg }}
      </p>
    </div>
    <hr style="border-color: var(--mc-border); margin: 1rem 0" />
    <label><input type="checkbox" v-model="dto.useMarginPercent" /> Use margin % (else fixed markup)</label>
    <div class="field">
      <label>Default margin % (on wholesale cost; 50 = list 1.5× cost)</label>
      <input type="number" v-model.number="dto.defaultMarginPercent" step="0.01" />
    </div>
    <div class="field">
      <label>Default fixed markup</label>
      <input type="number" v-model.number="dto.defaultFixedMarkup" step="0.01" />
    </div>
    <p style="color: var(--mc-muted); font-size: 0.88rem; margin: 0">
      Invoices do not add VAT — MC Tactical is not VAT registered.
    </p>
    <label><input type="checkbox" v-model="dto.hideCostForSalesRole" /> Hide cost for Sales role</label>
    <div style="margin-top: 1rem">
      <button type="button" class="btn" @click="save">Save</button>
    </div>
  </div>
</template>
