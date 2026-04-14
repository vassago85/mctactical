<script setup lang="ts">
import { onMounted, ref } from 'vue'
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
</script>

<template>
  <h1>Pricing</h1>
  <p class="err" v-if="err">{{ err }}</p>
  <p v-if="ok" style="color: #a5d6a7">{{ ok }}</p>
  <div class="card">
    <label><input type="checkbox" v-model="dto.useMarginPercent" /> Use margin % (else fixed markup)</label>
    <div class="field">
      <label>Default margin % (on wholesale cost; 50 = list 1.5× cost)</label>
      <input type="number" v-model.number="dto.defaultMarginPercent" step="0.01" />
    </div>
    <div class="field">
      <label>Default fixed markup</label>
      <input type="number" v-model.number="dto.defaultFixedMarkup" step="0.01" />
    </div>
    <div class="field">
      <label>Round sell prices to nearest (R)</label>
      <select v-model.number="dto.roundSellToNearest">
        <option :value="0">No rounding (normal pricing)</option>
        <option :value="10">R10 (Huntex pricing)</option>
        <option :value="5">R5</option>
        <option :value="1">R1</option>
      </select>
      <p style="color: var(--mc-muted); font-size: 0.85rem; margin: 0.35rem 0 0">
        Huntex pricing rounds all sell prices up to the nearest R10. Applied on import and when computing from margin.
      </p>
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
