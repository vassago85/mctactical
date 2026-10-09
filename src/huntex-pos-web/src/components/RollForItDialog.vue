<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, ref, watch } from 'vue'
import { http } from '@/api/http'
import McModal from '@/components/ui/McModal.vue'
import McField from '@/components/ui/McField.vue'
import McButton from '@/components/ui/McButton.vue'
import McSpinner from '@/components/ui/McSpinner.vue'
import McAlert from '@/components/ui/McAlert.vue'

type RollStage = 'form' | 'rolling' | 'result'

interface RollStatus {
  enabled: boolean
  open?: boolean
  reason?: string | null
  minOrder?: number
  minOrderFormatted?: string
  maxPayout?: number
  maxPayoutFormatted?: string
  odds?: string
  disclaimer?: string
  headline?: string
  animation?: { dieColor: string; numberColor: string; accentColor: string; durationMs: number }
  nextRoll?: { startsAt: string; startsIn: number; label: string } | null
  currencySymbol?: string
}

interface RollResult {
  won: boolean
  displayedNumber: number
  pickedNumber: number
  payout: number
  payoutFormatted: string
  message: string
  rollId: number
  repeat: boolean
}

const props = defineProps<{
  modelValue: boolean
  cartSubtotalCents: number
  customerEmail: string
  customerPhone?: string
}>()

const emit = defineEmits<{
  'update:modelValue': [v: boolean]
  applied: [payload: { rollId: number; payout: number; payoutFormatted: string; displayedNumber: number }]
}>()

const stage = ref<RollStage>('form')
const email = ref('')
const phone = ref('')
const pickedNumber = ref<number | null>(null)
const loading = ref(false)
const error = ref<string | null>(null)
const status = ref<RollStatus | null>(null)
const statusError = ref<string | null>(null)
const result = ref<RollResult | null>(null)
const dieHost = ref<HTMLElement | null>(null)
let die3d: RFIDie3DInstance | null = null
let scriptEl: HTMLScriptElement | null = null

const belowMinimum = computed(() =>
  !!status.value?.minOrder && props.cartSubtotalCents < status.value.minOrder,
)
const canRoll = computed(() =>
  stage.value === 'form' && !loading.value && !belowMinimum.value &&
  (!!email.value.trim() || !!phone.value.trim()) &&
  pickedNumber.value !== null && (status.value?.open ?? false),
)

watch(() => props.modelValue, async (open) => {
  if (!open) return
  stage.value = 'form'
  result.value = null
  error.value = null
  pickedNumber.value = null
  email.value = props.customerEmail?.trim() || ''
  phone.value = props.customerPhone?.trim() || ''
  await fetchStatus()
  await nextTick()
  await ensureDie()
})

onBeforeUnmount(() => disposeDie())

async function fetchStatus() {
  statusError.value = null
  status.value = null
  try {
    const { data } = await http.get('/api/rollforit/status')
    if (!data.enabled) {
      statusError.value = 'Roll4It is not configured on this till. Ask a manager to set it up.'
      return
    }
    // Server wraps the real status under `.status` when enabled is true.
    const s = data.status ?? data
    status.value = { enabled: true, ...s }
  } catch (e) {
    const err = e as { response?: { data?: { message?: string } } }
    statusError.value = err.response?.data?.message ?? 'Could not reach Roll4It right now.'
  }
}

async function ensureDie() {
  if (!dieHost.value) return
  if (!window.RFIDie3D) {
    await loadScript('/rfi/rfi-die3d.js')
  }
  if (!window.RFIDie3D || !dieHost.value) return
  disposeDie()
  // The storefront widget passes a `size` and colour palette; the till is roomier
  // so we go a little bigger. Colours come from the Shopify admin so merchants can
  // keep brand parity between online and in-store experiences.
  die3d = window.RFIDie3D.create(dieHost.value, {
    size: 180,
    dieColor: status.value?.animation?.dieColor ?? '#f59e0b',
    numberColor: status.value?.animation?.numberColor ?? '#111111',
    accentColor: status.value?.animation?.accentColor ?? '#f59e0b',
  })
  die3d?.show(1)
}

function disposeDie() {
  die3d?.dispose()
  die3d = null
}

function loadScript(src: string): Promise<void> {
  return new Promise((resolve, reject) => {
    scriptEl = document.querySelector<HTMLScriptElement>(`script[data-rfi-die="${src}"]`)
    if (scriptEl && window.RFIDie3D) return resolve()
    scriptEl = document.createElement('script')
    scriptEl.src = src
    scriptEl.async = true
    scriptEl.dataset.rfiDie = src
    scriptEl.onload = () => resolve()
    scriptEl.onerror = () => reject(new Error('Failed to load the die bundle.'))
    document.head.appendChild(scriptEl)
  })
}

async function doRoll() {
  if (!canRoll.value || pickedNumber.value === null) return
  loading.value = true
  error.value = null
  stage.value = 'rolling'

  try {
    const { data } = await http.post<RollResult>('/api/rollforit/roll', {
      email: email.value.trim() || null,
      phone: phone.value.trim() || null,
      pickedNumber: pickedNumber.value,
      subtotalCents: props.cartSubtotalCents,
      currency: 'ZAR',
    })

    // Animate to the server-chosen face, then show the result so the customer sees it decided
    // on-screen rather than in a toast. The die rolls free-form, then settles to the picked face.
    const duration = status.value?.animation?.durationMs ?? 2200
    die3d?.start()
    await new Promise((resolve) => setTimeout(resolve, duration))
    await die3d?.settle(data.displayedNumber)
    result.value = data
    stage.value = 'result'
  } catch (e) {
    const err = e as { response?: { data?: { message?: string; detail?: string } } }
    error.value = err.response?.data?.message ?? err.response?.data?.detail ?? 'Could not roll right now.'
    stage.value = 'form'
    die3d?.show(1)
  } finally {
    loading.value = false
  }
}

function applyWin() {
  if (!result.value || !result.value.won) return
  emit('applied', {
    rollId: result.value.rollId,
    payout: result.value.payout / 100,
    payoutFormatted: result.value.payoutFormatted,
    displayedNumber: result.value.displayedNumber,
  })
  close()
}

function close() {
  emit('update:modelValue', false)
}
</script>

<template>
  <McModal :model-value="modelValue" title="Roll4It" @update:model-value="close">
    <div class="rfi-pos">
      <div v-if="statusError" class="rfi-pos__banner rfi-pos__banner--error">{{ statusError }}</div>

      <template v-else-if="status">
        <div v-if="status.headline" class="rfi-pos__headline">{{ status.headline }}</div>
        <div v-if="status.odds" class="rfi-pos__odds">{{ status.odds }}</div>

        <div v-if="!status.open" class="rfi-pos__banner rfi-pos__banner--muted">
          {{ status.reason === 'upcoming'
            ? ('The next roll opens ' + (status.nextRoll?.label ?? 'soon') + '.')
            : status.reason === 'sold_out'
              ? 'Today\'s prizes have all been won. Try again next promo day.'
              : 'The promotion is not running right now.' }}
        </div>

        <div v-if="belowMinimum" class="rfi-pos__banner rfi-pos__banner--muted">
          Add at least {{ status.minOrderFormatted }} to the sale before offering a roll.
        </div>

        <div class="rfi-pos__die" :class="{ 'rfi-pos__die--rolling': stage === 'rolling' }">
          <div ref="dieHost" class="rfi-pos__die-host"></div>
          <div v-if="stage === 'rolling'" class="rfi-pos__rolling">Rolling…</div>
        </div>

        <div v-if="stage === 'form'" class="rfi-pos__body">
          <div class="rfi-pos__cols">
            <McField label="Email (optional if phone)" for-id="rfi-email">
              <input id="rfi-email" v-model="email" type="email" autocomplete="email" />
            </McField>
            <McField label="Cell phone (optional if email)" for-id="rfi-phone">
              <input id="rfi-phone" v-model="phone" type="tel" autocomplete="tel" placeholder="082 123 4567" />
            </McField>
          </div>
          <p class="rfi-pos__hint">
            We only use this to make sure the same person doesn't roll twice today. It's stored as a one-way hash.
          </p>

          <div class="rfi-pos__pick-label">Pick a number</div>
          <div class="rfi-pos__pick">
            <button
              v-for="n in 10"
              :key="n"
              type="button"
              class="rfi-pos__num"
              :class="{ 'rfi-pos__num--on': pickedNumber === n }"
              :aria-pressed="pickedNumber === n"
              @click="pickedNumber = n"
            >{{ n }}</button>
          </div>

          <McAlert v-if="error" variant="error">{{ error }}</McAlert>

          <div class="rfi-pos__disclaimer" v-if="status.disclaimer">{{ status.disclaimer }}</div>
        </div>

        <div v-else-if="stage === 'result' && result" class="rfi-pos__body">
          <div class="rfi-pos__outcome" :class="result.won ? 'rfi-pos__outcome--win' : 'rfi-pos__outcome--loss'">
            <div class="rfi-pos__outcome-head">
              {{ result.repeat ? 'Already rolled today' : (result.won ? 'Winner!' : 'No prize today') }}
            </div>
            <div v-if="result.won" class="rfi-pos__outcome-amount">{{ result.payoutFormatted }} off this sale</div>
            <div class="rfi-pos__outcome-message">{{ result.message }}</div>
            <div v-if="result.repeat" class="rfi-pos__outcome-note">
              This customer rolled earlier today. The result above is their original roll.
              {{ result.won ? 'If an online code was already used, the till can still apply the win here instead.' : '' }}
            </div>
          </div>
        </div>
      </template>
    </div>

    <template #footer>
      <div class="rfi-pos__footer">
        <McButton variant="secondary" type="button" @click="close">
          {{ stage === 'result' && result?.won ? 'Skip' : 'Close' }}
        </McButton>

        <McButton
          v-if="stage === 'form'"
          variant="primary"
          type="button"
          :disabled="!canRoll"
          @click="doRoll"
        >
          <McSpinner v-if="loading" />
          <span v-else>Roll the die</span>
        </McButton>

        <McButton
          v-else-if="stage === 'result' && result?.won"
          variant="primary"
          type="button"
          @click="applyWin"
        >Apply {{ result.payoutFormatted }} to the sale</McButton>
      </div>
    </template>
  </McModal>
</template>

<style scoped>
.rfi-pos { display: flex; flex-direction: column; gap: 12px; }
.rfi-pos__banner {
  padding: 10px 12px;
  border-radius: 6px;
  font-size: 0.95em;
}
.rfi-pos__banner--muted { background: rgba(255,255,255,0.05); color: #d4d4d8; }
.rfi-pos__banner--error { background: rgba(220, 38, 38, 0.15); color: #fecaca; }
.rfi-pos__headline { font-size: 1.1em; font-weight: 600; }
.rfi-pos__odds { color: #a1a1aa; font-size: 0.9em; }
.rfi-pos__die {
  display: flex;
  justify-content: center;
  align-items: center;
  min-height: 190px;
  padding: 8px 0;
  position: relative;
}
.rfi-pos__die-host { width: 180px; height: 180px; display: flex; align-items: center; justify-content: center; }
.rfi-pos__rolling {
  position: absolute;
  bottom: 0;
  font-size: 0.9em;
  color: #a1a1aa;
}
.rfi-pos__body { display: flex; flex-direction: column; gap: 10px; }
.rfi-pos__cols {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 10px;
}
@media (max-width: 540px) {
  .rfi-pos__cols { grid-template-columns: 1fr; }
}
.rfi-pos__hint { font-size: 0.85em; color: #a1a1aa; margin: 0; }
.rfi-pos__pick-label { font-weight: 600; margin-top: 4px; }
.rfi-pos__pick {
  display: grid;
  grid-template-columns: repeat(5, 1fr);
  gap: 6px;
}
.rfi-pos__num {
  padding: 10px 0;
  font-size: 1.1em;
  font-weight: 700;
  border: 1px solid rgba(255,255,255,0.15);
  background: rgba(255,255,255,0.04);
  color: inherit;
  border-radius: 6px;
  cursor: pointer;
}
.rfi-pos__num--on {
  background: #f59e0b;
  color: #111;
  border-color: #f59e0b;
}
.rfi-pos__disclaimer { font-size: 0.8em; color: #71717a; }
.rfi-pos__outcome {
  padding: 14px;
  border-radius: 8px;
  text-align: center;
}
.rfi-pos__outcome--win { background: rgba(16, 185, 129, 0.15); border: 1px solid rgba(16, 185, 129, 0.4); }
.rfi-pos__outcome--loss { background: rgba(255,255,255,0.04); border: 1px solid rgba(255,255,255,0.1); }
.rfi-pos__outcome-head { font-size: 1.3em; font-weight: 700; }
.rfi-pos__outcome-amount { font-size: 1.6em; font-weight: 800; margin: 4px 0; }
.rfi-pos__outcome-message { color: #d4d4d8; font-size: 0.95em; }
.rfi-pos__outcome-note { color: #a1a1aa; font-size: 0.85em; margin-top: 6px; }
.rfi-pos__footer { display: flex; justify-content: flex-end; gap: 8px; }
</style>
