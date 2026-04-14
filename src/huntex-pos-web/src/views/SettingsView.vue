<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { http } from '@/api/http'
import { useToast } from '@/composables/useToast'
import McPageHeader from '@/components/ui/McPageHeader.vue'
import McCard from '@/components/ui/McCard.vue'
import McButton from '@/components/ui/McButton.vue'
import McField from '@/components/ui/McField.vue'
import McAlert from '@/components/ui/McAlert.vue'
import McModal from '@/components/ui/McModal.vue'

const toast = useToast()
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
const showRecalcModal = ref(false)

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
    toast.success('Pricing settings saved')
  } catch {
    err.value = 'Save failed'
    toast.error('Save failed')
  }
}

async function runRecalculate() {
  recalcBusy.value = true
  recalcMsg.value = null
  try {
    const { data } = await http.post('/api/settings/pricing/recalculate')
    let msg = `Done — ${data.updated} of ${data.total} products recalculated.`
    if (data.belowDistributorCost > 0) msg += ` ${data.belowDistributorCost} products below distributor cost.`
    recalcMsg.value = msg
    toast.success('Recalculation finished')
  } catch (e: unknown) {
    const ax = e as { response?: { data?: { error?: string } } }
    recalcMsg.value = ax.response?.data?.error ?? 'Recalculate failed'
    toast.error(recalcMsg.value)
  } finally {
    recalcBusy.value = false
    showRecalcModal.value = false
  }
}
</script>

<template>
  <div class="set-page">
    <McPageHeader
      title="Pricing"
      description="List prices and rounding rules. Invoices remain VAT-free for customers. Recalculate applies current rules to all active products from cost."
    />

    <McAlert v-if="err" variant="error">{{ err }}</McAlert>
    <McAlert v-if="ok" variant="success">{{ ok }}</McAlert>

    <McCard title="Retail mode">
      <div class="set-radio-group">
        <label class="set-radio">
          <input v-model="dto.pricingMode" type="radio" value="normal" />
          <span><strong>Normal retail</strong> — cost × 1.5, rounded up to nearest R10</span>
        </label>
        <label class="set-radio">
          <input v-model="dto.pricingMode" type="radio" value="huntex" />
          <span><strong>Huntex pricing</strong> — normal sell ÷ 1.1, rounded up to nearest R10</span>
        </label>
      </div>
      <p class="set-hint">
        Sell prices round up to R10. Warnings appear if sell is below distributor cost (ex-VAT + 15%).
      </p>
    </McCard>

    <McCard title="Default cost markup">
      <label class="set-check">
        <input v-model="dto.useMarginPercent" type="checkbox" />
        Use margin % (otherwise fixed markup in R)
      </label>
      <McField label="Default margin % (50 = 1.5× cost)" for-id="set-margin">
        <input id="set-margin" v-model.number="dto.defaultMarginPercent" type="number" step="0.01" />
      </McField>
      <McField label="Default fixed markup (R)" for-id="set-fixed">
        <input id="set-fixed" v-model.number="dto.defaultFixedMarkup" type="number" step="0.01" />
      </McField>
    </McCard>

    <McCard title="POS display">
      <p class="set-hint">MC Tactical is not VAT registered — customer invoices do not add VAT.</p>
      <label class="set-check">
        <input v-model="dto.hideCostForSalesRole" type="checkbox" />
        Hide product cost for Sales role
      </label>
    </McCard>

    <div class="set-actions">
      <McButton variant="primary" type="button" @click="save">Save settings</McButton>
      <McButton variant="secondary" type="button" :disabled="recalcBusy" @click="showRecalcModal = true">
        Recalculate all product prices
      </McButton>
    </div>
    <p v-if="recalcMsg" class="set-recalc-msg">{{ recalcMsg }}</p>

    <McModal v-model="showRecalcModal" title="Recalculate all prices?">
      <p>
        This updates every <strong>active</strong> product’s sell price from its cost using the current margin / mode. It
        cannot be undone automatically.
      </p>
      <template #footer>
        <McButton variant="secondary" type="button" @click="showRecalcModal = false">Cancel</McButton>
        <McButton variant="primary" type="button" :disabled="recalcBusy" @click="runRecalculate">Recalculate</McButton>
      </template>
    </McModal>
  </div>
</template>

<style scoped>
.set-page {
  min-height: 100%;
}

.set-radio-group {
  display: flex;
  flex-direction: column;
  gap: 0.65rem;
}

.set-radio {
  display: flex;
  align-items: flex-start;
  gap: 0.65rem;
  cursor: pointer;
  line-height: 1.45;
}

.set-radio input {
  margin-top: 0.2rem;
  width: 1.1rem;
  height: 1.1rem;
}

.set-hint {
  margin: 0.75rem 0 0;
  font-size: 0.88rem;
  color: #7a7874;
  line-height: 1.45;
}

.set-check {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  margin-bottom: 1rem;
  font-weight: 500;
  cursor: pointer;
}

.set-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 0.65rem;
  margin-top: 0.5rem;
}

.set-recalc-msg {
  margin: 0.75rem 0 0;
  font-size: 0.88rem;
  color: #5c5a56;
}
</style>
