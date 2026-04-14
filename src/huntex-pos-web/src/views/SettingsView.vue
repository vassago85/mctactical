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
import McCheckbox from '@/components/ui/McCheckbox.vue'
import McRadioCard from '@/components/ui/McRadioCard.vue'
import McActionBar from '@/components/ui/McActionBar.vue'

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
        <McRadioCard
          :model-value="dto.pricingMode"
          value="normal"
          title="Normal retail"
          description="Cost × 1.5, rounded up to nearest R10"
          @update:model-value="dto.pricingMode = $event"
        />
        <McRadioCard
          :model-value="dto.pricingMode"
          value="huntex"
          title="Huntex pricing"
          description="Normal sell ÷ 1.1, rounded up to nearest R10"
          @update:model-value="dto.pricingMode = $event"
        />
      </div>
      <p class="set-hint">
        Sell prices round up to R10. Warnings appear if sell is below distributor cost (ex-VAT + 15%).
      </p>
    </McCard>

    <McCard title="Default cost markup">
      <McCheckbox v-model="dto.useMarginPercent" label="Use margin %" hint="Otherwise a fixed markup in Rands is applied" />
      <McField label="Default margin % (50 = 1.5× cost)" for-id="set-margin">
        <input id="set-margin" v-model.number="dto.defaultMarginPercent" type="number" step="0.01" />
      </McField>
      <McField label="Default fixed markup (R)" for-id="set-fixed">
        <input id="set-fixed" v-model.number="dto.defaultFixedMarkup" type="number" step="0.01" />
      </McField>
    </McCard>

    <McCard title="POS display">
      <p class="set-hint" style="margin-top: 0">MC Tactical is not VAT registered — customer invoices do not add VAT.</p>
      <McCheckbox v-model="dto.hideCostForSalesRole" label="Hide product cost for Sales role" hint="Sales staff won't see cost or margin values" />
    </McCard>

    <McActionBar>
      <McButton variant="primary" type="button" @click="save">Save settings</McButton>
      <McButton variant="secondary" type="button" :disabled="recalcBusy" @click="showRecalcModal = true">
        Recalculate all product prices
      </McButton>
    </McActionBar>
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
  gap: 0.75rem;
}

.set-hint {
  margin: 0.75rem 0 0;
  font-size: 0.88rem;
  color: var(--mc-app-text-muted, #5c5a56);
  line-height: 1.5;
}

.set-recalc-msg {
  margin: 0.75rem 0 0;
  font-size: 0.88rem;
  color: var(--mc-app-text-muted, #5c5a56);
}
</style>
