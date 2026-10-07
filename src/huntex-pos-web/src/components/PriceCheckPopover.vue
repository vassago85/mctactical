<script setup lang="ts">
import { nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { http } from '@/api/http'
import { formatZAR } from '@/utils/format'
import McBadge from '@/components/ui/McBadge.vue'
import McSpinner from '@/components/ui/McSpinner.vue'
import { Tag, X } from 'lucide-vue-next'

export type PriceCheckProduct = {
  id: string
  sku: string
  barcode?: string | null
  name: string
  sellPrice: number
  qtyOnHand: number
  qtyConsignment?: number
  /** Null when the API hides cost for this user (Sales role + "Hide product cost" setting). */
  cost?: number | null
}

const props = defineProps<{
  open: boolean
  priceOf: (p: PriceCheckProduct) => { price: number; hasDiscount: boolean }
}>()

const emit = defineEmits<{ 'update:open': [value: boolean] }>()

const root = ref<HTMLElement | null>(null)
const input = ref<HTMLInputElement | null>(null)
const q = ref('')
const results = ref<PriceCheckProduct[]>([])
const busy = ref(false)
const searched = ref(false)

let timer: ReturnType<typeof setTimeout> | null = null
let requestSeq = 0

async function runSearch() {
  const term = q.value.trim()
  const seq = ++requestSeq
  if (!term) {
    results.value = []
    searched.value = false
    busy.value = false
    return
  }
  busy.value = true
  try {
    const { data } = await http.get<PriceCheckProduct[]>('/api/products', { params: { q: term, take: 20 } })
    if (seq === requestSeq) results.value = data
  } catch {
    if (seq === requestSeq) results.value = []
  } finally {
    if (seq === requestSeq) {
      busy.value = false
      searched.value = true
    }
  }
}

watch(q, () => {
  if (timer) clearTimeout(timer)
  timer = setTimeout(() => void runSearch(), 250)
})

function onKeydown(ev: KeyboardEvent) {
  if (ev.key === 'Enter') {
    ev.preventDefault()
    if (timer) clearTimeout(timer)
    void runSearch().then(() => input.value?.select())
  }
}

function close() {
  emit('update:open', false)
}

function onDocumentPointerDown(ev: PointerEvent) {
  if (props.open && root.value && !root.value.contains(ev.target as Node)) close()
}

function onDocumentKeydown(ev: KeyboardEvent) {
  if (props.open && ev.key === 'Escape') close()
}

watch(
  () => props.open,
  async (isOpen) => {
    if (!isOpen) return
    await nextTick()
    input.value?.focus()
    input.value?.select()
  },
  { immediate: true }
)

onMounted(() => {
  document.addEventListener('pointerdown', onDocumentPointerDown)
  document.addEventListener('keydown', onDocumentKeydown)
})
onBeforeUnmount(() => {
  document.removeEventListener('pointerdown', onDocumentPointerDown)
  document.removeEventListener('keydown', onDocumentKeydown)
  if (timer) clearTimeout(timer)
})
</script>

<template>
  <div ref="root" class="pc-wrap">
    <button
      type="button"
      class="pc-toggle"
      :class="{ 'pc-toggle--on': open }"
      :aria-expanded="open"
      aria-haspopup="dialog"
      title="Check a price and stock level without adding to the sale"
      @click="emit('update:open', !open)"
    >
      <Tag :size="16" />
      <span>Check price</span>
    </button>

    <div v-if="open" class="pc-pop" role="dialog" aria-label="Check price">
      <div class="pc-pop__head">
        <input
          id="price-check-search"
          ref="input"
          v-model="q"
          type="search"
          autocomplete="off"
          placeholder="Scan or type SKU / name…"
          class="pc-pop__input"
          @keydown="onKeydown"
        />
        <McSpinner v-if="busy" />
        <button type="button" class="pc-pop__close" aria-label="Close price check" @click="close"><X :size="16" /></button>
      </div>
      <p class="pc-pop__hint">Nothing is added to the sale.</p>

      <p v-if="searched && !busy && !results.length" class="pc-pop__empty">No matches.</p>
      <ul v-else-if="results.length" class="pc-list">
        <li v-for="p in results" :key="p.id" class="pc-item">
          <div class="pc-item__main">
            <p class="pc-item__name">{{ p.name }}</p>
            <p class="pc-item__meta">
              <span>{{ p.sku }}</span>
              <span v-if="p.barcode"> · {{ p.barcode }}</span>
              <span v-if="p.cost != null"> · Cost {{ formatZAR(p.cost) }}</span>
            </p>
          </div>
          <div class="pc-item__side">
            <template v-if="priceOf(p).hasDiscount">
              <span class="pc-item__price pc-item__price--sale">{{ formatZAR(priceOf(p).price) }}</span>
              <span class="pc-item__was">{{ formatZAR(p.sellPrice) }}</span>
            </template>
            <span v-else class="pc-item__price">{{ formatZAR(p.sellPrice) }}</span>
            <McBadge :variant="p.qtyOnHand > 0 ? 'success' : 'danger'">
              {{ p.qtyOnHand > 0 ? `In stock (${p.qtyOnHand})` : 'Out of stock' }}
            </McBadge>
            <span v-if="p.qtyConsignment" class="pc-item__consign">+{{ p.qtyConsignment }} consignment</span>
          </div>
        </li>
      </ul>
    </div>
  </div>
</template>

<style scoped>
.pc-wrap {
  position: relative;
  flex-shrink: 0;
}

.pc-toggle {
  display: inline-flex;
  align-items: center;
  gap: 0.35rem;
  padding: 0.55rem 0.85rem;
  border: 1.5px solid var(--mc-app-border-subtle, #c8c5bd);
  border-radius: 10px;
  background: var(--mc-app-surface, #fff);
  font-size: 0.88rem;
  font-weight: 600;
  color: var(--mc-app-text-secondary, #333336);
  cursor: pointer;
  transition: background 0.12s ease, border-color 0.12s ease, color 0.12s ease;
}
.pc-toggle:hover {
  background: var(--mc-app-surface-muted, #f6f5f1);
}
.pc-toggle--on {
  background: var(--mc-accent, #f47a20);
  border-color: var(--mc-accent, #f47a20);
  color: #fff;
}
.pc-toggle--on:hover {
  background: var(--mc-accent, #f47a20);
}

.pc-pop {
  position: absolute;
  top: calc(100% + 0.5rem);
  right: 0;
  z-index: 30;
  width: min(28rem, calc(100vw - 2rem));
  max-height: min(32rem, 70vh);
  display: flex;
  flex-direction: column;
  padding: 0.75rem;
  background: var(--mc-app-surface, #fff);
  border: 1px solid var(--mc-app-border-soft, #ddd9d3);
  border-radius: 14px;
  box-shadow: 0 12px 32px rgba(0, 0, 0, 0.14);
}

.pc-pop__head {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.pc-pop__input {
  flex: 1;
  min-width: 0;
  padding: 0.6rem 0.75rem;
  font-size: 1rem;
  border: 1.5px solid var(--mc-accent, #f47a20);
  border-radius: 10px;
}
.pc-pop__input:focus {
  outline: none;
}

.pc-pop__close {
  display: inline-flex;
  padding: 0.35rem;
  border: none;
  background: none;
  color: var(--mc-app-text-muted, #5c5a56);
  cursor: pointer;
  border-radius: 6px;
}
.pc-pop__close:hover {
  background: var(--mc-app-surface-muted, #f6f5f1);
}

.pc-pop__hint,
.pc-pop__empty {
  margin: 0.4rem 0 0;
  font-size: 0.8rem;
  color: var(--mc-app-text-muted, #5c5a56);
}

.pc-list {
  list-style: none;
  margin: 0.6rem 0 0;
  padding: 0;
  overflow-y: auto;
  display: flex;
  flex-direction: column;
  gap: 0.4rem;
}

.pc-item {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 0.75rem;
  padding: 0.6rem 0.7rem;
  background: var(--mc-app-surface-alt, #faf9f6);
  border: 1px solid var(--mc-app-border-faint, #eceae5);
  border-radius: 8px;
}

.pc-item__main {
  flex: 1;
  min-width: 0;
}
.pc-item__name {
  margin: 0;
  font-weight: 600;
  font-size: 0.92rem;
  line-height: 1.3;
}
.pc-item__meta {
  margin: 0.15rem 0 0;
  font-size: 0.78rem;
  color: var(--mc-app-text-muted, #777);
}

.pc-item__side {
  display: flex;
  flex-direction: column;
  align-items: flex-end;
  gap: 0.2rem;
  flex-shrink: 0;
}
.pc-item__price {
  font-weight: 700;
  font-size: 1.05rem;
}
.pc-item__price--sale {
  color: #cc0000;
}
.pc-item__was {
  font-size: 0.78rem;
  color: #999;
  text-decoration: line-through;
}
.pc-item__consign {
  font-size: 0.75rem;
  color: var(--mc-app-text-muted, #5c5a56);
}
</style>
