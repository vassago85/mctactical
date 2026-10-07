<script setup lang="ts">
import { computed, ref } from 'vue'
import { http } from '@/api/http'
import { useToast } from '@/composables/useToast'
import McModal from '@/components/ui/McModal.vue'
import McField from '@/components/ui/McField.vue'
import McButton from '@/components/ui/McButton.vue'
import McSpinner from '@/components/ui/McSpinner.vue'
import McCheckbox from '@/components/ui/McCheckbox.vue'
import { Printer } from 'lucide-vue-next'

const props = defineProps<{
  modelValue: boolean
  productIds: string[]
  /** On-hand quantities for products the caller has loaded, used to estimate "one per unit" counts. */
  qtyById: Map<string, number>
}>()

const emit = defineEmits<{
  'update:modelValue': [v: boolean]
  printed: []
}>()

const toast = useToast()

type CopyMode = 'one' | 'fixed' | 'qty'
const copyMode = ref<CopyMode>('one')
const fixedCopies = ref(1)
const maxCopiesPerProduct = ref(50)
const usePromo = ref(false)
const printing = ref(false)

const preview = computed(() => {
  const ids = props.productIds
  switch (copyMode.value) {
    case 'one':
      return { labels: ids.length, missingQty: 0 }
    case 'fixed':
      return { labels: ids.length * Math.max(1, Number(fixedCopies.value) || 1), missingQty: 0 }
    case 'qty': {
      const cap = Math.max(1, Number(maxCopiesPerProduct.value) || 50)
      let labels = 0
      let missingQty = 0
      for (const id of ids) {
        const qty = props.qtyById.get(id)
        if (qty == null) { missingQty++; continue }
        if (qty > 0) labels += Math.min(qty, cap)
      }
      return { labels, missingQty }
    }
    default: {
      const exhaustive: never = copyMode.value
      return exhaustive
    }
  }
})

function close() {
  emit('update:modelValue', false)
}

async function printLabels() {
  const ids = props.productIds
  if (ids.length === 0) {
    toast.error('Select at least one product first')
    return
  }

  const payload: Record<string, unknown> = {
    productIds: ids,
    usePromo: usePromo.value
  }
  if (copyMode.value === 'fixed') {
    payload.copiesPerProduct = Math.max(1, Math.min(50, Number(fixedCopies.value) || 1))
  } else if (copyMode.value === 'qty') {
    payload.copiesFromQtyOnHand = true
    payload.maxCopiesPerProduct = Math.max(1, Math.min(200, Number(maxCopiesPerProduct.value) || 50))
  }

  printing.value = true
  try {
    const resp = await http.post('/api/products/labels', payload, { responseType: 'blob' })
    const count = resp.headers?.['x-label-count']
    const skipped = resp.headers?.['x-products-skipped-no-stock']
    const url = URL.createObjectURL(new Blob([resp.data], { type: 'application/pdf' }))
    const win = window.open(url, '_blank')
    if (win) win.addEventListener('load', () => { win.print() })
    const parts: string[] = []
    if (count) parts.push(`${count} label${count === '1' ? '' : 's'}`)
    if (skipped && Number(skipped) > 0) parts.push(`${skipped} skipped (no stock)`)
    toast.success(parts.length ? `Sent to printer — ${parts.join(', ')}` : 'Sent to printer')
    emit('printed')
    close()
  } catch (e: unknown) {
    const ax = e as { response?: { data?: Blob | Record<string, string> } }
    let msg = 'Label generation failed'
    try {
      if (ax.response?.data instanceof Blob) {
        const text = await ax.response.data.text()
        const json = JSON.parse(text)
        if (json.error) msg += ': ' + json.error
      }
    } catch { /* ignore */ }
    toast.error(msg)
  } finally {
    printing.value = false
  }
}
</script>

<template>
  <McModal :model-value="modelValue" title="Print labels" @update:model-value="emit('update:modelValue', $event)">
    <p class="lpd-lead">
      {{ productIds.length }} product{{ productIds.length === 1 ? '' : 's' }} selected · about
      <strong>{{ preview.labels }}</strong> label{{ preview.labels === 1 ? '' : 's' }}
      <span v-if="copyMode === 'qty' && preview.missingQty > 0" class="lpd-dim">
        ({{ preview.missingQty }} not on this page, counted when printed)
      </span>
    </p>
    <p class="lpd-dim">Sized for the Brother QL-800 with 62mm tape.</p>

    <fieldset class="lpd-copy-mode">
      <legend>Copies per product</legend>
      <label>
        <input v-model="copyMode" type="radio" value="one" />
        <span>One label per product</span>
      </label>
      <label>
        <input v-model="copyMode" type="radio" value="fixed" />
        <span>Fixed number per product</span>
        <input
          v-if="copyMode === 'fixed'"
          v-model.number="fixedCopies"
          type="number"
          min="1"
          max="50"
          step="1"
          class="lpd-copy-num"
          aria-label="Labels per product"
        />
      </label>
      <label>
        <input v-model="copyMode" type="radio" value="qty" />
        <span>One per unit on hand</span>
      </label>
      <McField v-if="copyMode === 'qty'" label="Max labels per product" for-id="lpd-cap" hint="Safety cap (1–200).">
        <input id="lpd-cap" v-model.number="maxCopiesPerProduct" type="number" min="1" max="200" step="1" />
      </McField>
    </fieldset>

    <McCheckbox
      v-model="usePromo"
      label="Use active promotion / specials"
      hint="Shows discounted price with the original crossed out and the promotion name."
    />

    <template #footer>
      <McButton variant="secondary" type="button" @click="close">Cancel</McButton>
      <McButton variant="primary" type="button" :disabled="!productIds.length || printing" @click="printLabels">
        <McSpinner v-if="printing" />
        <template v-else>
          <Printer :size="16" />
          Print labels
        </template>
      </McButton>
    </template>
  </McModal>
</template>

<style scoped>
.lpd-lead {
  margin: 0 0 0.25rem;
  font-size: 0.95rem;
}
.lpd-dim {
  margin: 0 0 1rem;
  font-size: 0.82rem;
  color: var(--mc-app-text-muted, #5c5a56);
}
.lpd-copy-mode {
  display: flex;
  flex-direction: column;
  gap: 0.55rem;
  margin: 0 0 1rem;
  padding: 0.85rem 1rem;
  border: 1px solid var(--mc-app-border-faint, #eceae5);
  border-radius: 10px;
}
.lpd-copy-mode legend {
  padding: 0 0.35rem;
  font-size: 0.8rem;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.04em;
  color: var(--mc-app-text-muted, #5c5a56);
}
.lpd-copy-mode label {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  font-size: 0.92rem;
  cursor: pointer;
}
.lpd-copy-num {
  width: 5rem;
  margin-left: 0.25rem;
}
</style>
