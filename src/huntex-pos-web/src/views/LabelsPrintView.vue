<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { http } from '@/api/http'
import { useToast } from '@/composables/useToast'
import { formatZAR } from '@/utils/format'
import McPage from '@/components/ui/McPage.vue'
import McPageHeader from '@/components/ui/McPageHeader.vue'
import McCard from '@/components/ui/McCard.vue'
import McField from '@/components/ui/McField.vue'
import McButton from '@/components/ui/McButton.vue'
import McBadge from '@/components/ui/McBadge.vue'
import McAlert from '@/components/ui/McAlert.vue'
import McSpinner from '@/components/ui/McSpinner.vue'
import McEmptyState from '@/components/ui/McEmptyState.vue'
import McCheckbox from '@/components/ui/McCheckbox.vue'
import McStickyActionBar from '@/components/ui/McStickyActionBar.vue'
import { Printer, Search, X } from 'lucide-vue-next'

type Supplier = { id: string; name: string }

type Product = {
  id: string
  sku: string
  barcode?: string | null
  name: string
  category?: string | null
  manufacturer?: string | null
  supplierId?: string | null
  supplierName?: string | null
  sellPrice: number
  qtyOnHand: number
  qtyConsignment: number
  active: boolean
  specialPrice?: number | null
  specialLabel?: string | null
}

type Page = { total: number; skip: number; take: number; items: Product[] }

const toast = useToast()

// Search + filter state
const q = ref('')
const supplierId = ref<string>('')
const onlyInStock = ref(true)
const onlyActive = ref(true)
const onlyWithSpecials = ref(false)
const pageSize = ref(200)
const skip = ref(0)
const page = ref<Page | null>(null)
const busy = ref(false)
const err = ref<string | null>(null)

const suppliers = ref<Supplier[]>([])

// Selection (keyed by productId so selections survive pagination & filter changes)
const selectedIds = ref<Set<string>>(new Set())

// Print options
const usePromo = ref(false)
type CopyMode = 'one' | 'fixed' | 'qty'
const copyMode = ref<CopyMode>('one')
const fixedCopies = ref(1)
const maxCopiesPerProduct = ref(50)
const printing = ref(false)

async function loadSuppliers() {
  try {
    const { data } = await http.get<Supplier[]>('/api/suppliers')
    suppliers.value = data
  } catch {
    /* non-critical */
  }
}

async function load() {
  err.value = null
  busy.value = true
  try {
    const { data } = await http.get<Page>('/api/products/stocklist', {
      params: {
        q: q.value.trim() || undefined,
        supplierId: supplierId.value || undefined,
        includeInactive: !onlyActive.value,
        hasSpecial: onlyWithSpecials.value || undefined,
        skip: skip.value,
        take: pageSize.value
      }
    })
    if (onlyInStock.value) {
      data.items = data.items.filter(p => p.qtyOnHand > 0)
    }
    page.value = data
  } catch {
    err.value = 'Could not load products'
    page.value = null
  } finally {
    busy.value = false
  }
}

let debounce: ReturnType<typeof setTimeout> | null = null
watch([q, supplierId, onlyActive, onlyInStock, onlyWithSpecials], () => {
  skip.value = 0
  if (debounce) clearTimeout(debounce)
  debounce = setTimeout(() => void load(), 250)
})
watch([skip, pageSize], () => void load())

onMounted(() => {
  void loadSuppliers()
  void load()
})

const visibleIds = computed(() => (page.value?.items ?? []).map(p => p.id))
const allVisibleSelected = computed(
  () => visibleIds.value.length > 0 && visibleIds.value.every(id => selectedIds.value.has(id))
)
const someVisibleSelected = computed(
  () => visibleIds.value.some(id => selectedIds.value.has(id))
)

function toggleProduct(id: string) {
  const next = new Set(selectedIds.value)
  if (next.has(id)) next.delete(id)
  else next.add(id)
  selectedIds.value = next
}

function toggleAllVisible() {
  const next = new Set(selectedIds.value)
  if (allVisibleSelected.value) {
    for (const id of visibleIds.value) next.delete(id)
  } else {
    for (const id of visibleIds.value) next.add(id)
  }
  selectedIds.value = next
}

function clearSelection() {
  selectedIds.value = new Set()
}

async function selectAllMatching() {
  // Pull a bigger page matching current filters and add every id to the selection.
  try {
    busy.value = true
    const { data } = await http.get<Page>('/api/products/stocklist', {
      params: {
        q: q.value.trim() || undefined,
        supplierId: supplierId.value || undefined,
        includeInactive: !onlyActive.value,
        hasSpecial: onlyWithSpecials.value || undefined,
        skip: 0,
        take: 10_000
      }
    })
    const next = new Set(selectedIds.value)
    for (const p of data.items) {
      if (onlyInStock.value && p.qtyOnHand <= 0) continue
      next.add(p.id)
    }
    selectedIds.value = next
    toast.success(`Selected ${next.size} item${next.size === 1 ? '' : 's'}`)
  } catch {
    toast.error('Could not expand selection')
  } finally {
    busy.value = false
  }
}

// Build a live count of labels that would print given current mode.
// We can only estimate within the currently loaded page(s). For accuracy we
// fetch qty data from the selection when needed.
const selectionPreview = ref<{ products: number; labels: number; missingQty: number } | null>(null)

async function refreshPreview() {
  const ids = Array.from(selectedIds.value)
  if (ids.length === 0) {
    selectionPreview.value = null
    return
  }

  if (copyMode.value === 'one') {
    selectionPreview.value = { products: ids.length, labels: ids.length, missingQty: 0 }
    return
  }
  if (copyMode.value === 'fixed') {
    const n = Math.max(1, Number(fixedCopies.value) || 1)
    selectionPreview.value = { products: ids.length, labels: ids.length * n, missingQty: 0 }
    return
  }

  // qty mode — need on-hand values; use the loaded products as a best effort.
  const loaded = new Map<string, number>()
  for (const p of page.value?.items ?? []) loaded.set(p.id, p.qtyOnHand)
  let labels = 0
  let missing = 0
  let productsWithStock = 0
  const cap = Math.max(1, Number(maxCopiesPerProduct.value) || 50)
  for (const id of ids) {
    const qty = loaded.get(id)
    if (qty == null) { missing++; continue }
    if (qty <= 0) continue
    productsWithStock++
    labels += Math.min(qty, cap)
  }
  selectionPreview.value = { products: productsWithStock, labels, missingQty: missing }
}

watch([selectedIds, copyMode, fixedCopies, maxCopiesPerProduct], () => {
  void refreshPreview()
}, { deep: true })

async function printLabels() {
  const ids = Array.from(selectedIds.value)
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

const pageInfo = computed(() => {
  if (!page.value) return ''
  const shown = page.value.items.length
  const total = page.value.total
  return onlyInStock.value
    ? `${shown} with stock · ${total} matching total`
    : `${shown} of ${total} matching`
})

function effectivePrice(p: Product): { now: number; was?: number } {
  if (p.specialPrice != null && p.specialPrice < p.sellPrice) {
    return { now: p.specialPrice, was: p.sellPrice }
  }
  return { now: p.sellPrice }
}
</script>

<template>
  <McPage width="xl">
    <McPageHeader title="Print labels" description="Select products and print barcode labels in bulk. Sized for the Brother QL-800 with 62mm tape.">
      <template #actions>
        <McButton variant="secondary" dense type="button" :disabled="!selectedIds.size" @click="clearSelection">
          <X :size="14" /> Clear selection
        </McButton>
      </template>
    </McPageHeader>

    <McAlert v-if="err" variant="error">{{ err }}</McAlert>

    <McCard>
      <div class="lp-filters">
        <McField label="Search" for-id="lp-q" class="lp-filters__search">
          <div class="lp-search-wrap">
            <Search :size="14" class="lp-search-icon" />
            <input
              id="lp-q"
              v-model="q"
              type="search"
              placeholder="Name, SKU, barcode, category…"
              autocomplete="off"
            />
          </div>
        </McField>
        <McField label="Wholesaler" for-id="lp-supplier">
          <select id="lp-supplier" v-model="supplierId">
            <option value="">All wholesalers</option>
            <option v-for="s in suppliers" :key="s.id" :value="s.id">{{ s.name }}</option>
          </select>
        </McField>
        <div class="lp-filters__toggles">
          <McCheckbox v-model="onlyInStock" label="Only in-stock" hint="Hide products with 0 on hand" />
          <McCheckbox v-model="onlyActive" label="Only active" />
          <McCheckbox v-model="onlyWithSpecials" label="Only on special" />
        </div>
      </div>
    </McCard>

    <McCard>
      <header class="lp-table-head">
        <div class="lp-table-head__left">
          <label class="lp-select-all">
            <input
              type="checkbox"
              :checked="allVisibleSelected"
              :indeterminate.prop="!allVisibleSelected && someVisibleSelected"
              @change="toggleAllVisible"
            />
            <span>Select all on this page</span>
          </label>
          <McButton variant="ghost" dense type="button" :disabled="busy" @click="selectAllMatching">
            Select all matching
          </McButton>
        </div>
        <p class="lp-table-head__right">{{ pageInfo }}</p>
      </header>

      <div v-if="busy && !page" class="lp-loading"><McSpinner /> <span>Loading…</span></div>
      <McEmptyState
        v-else-if="!page?.items.length"
        title="No products match"
        hint="Try a different search term or clear the filters."
      />
      <div v-else class="lp-list">
        <label
          v-for="p in page.items"
          :key="p.id"
          class="lp-row"
          :class="{ 'lp-row--selected': selectedIds.has(p.id) }"
        >
          <input type="checkbox" :checked="selectedIds.has(p.id)" @change="toggleProduct(p.id)" />
          <div class="lp-row__body">
            <div class="lp-row__main">
              <strong class="lp-row__name">{{ p.name }}</strong>
              <span class="lp-row__sku">{{ p.sku }}<span v-if="p.barcode"> · {{ p.barcode }}</span></span>
              <span v-if="p.supplierName" class="lp-row__sup">{{ p.supplierName }}</span>
            </div>
            <div class="lp-row__meta">
              <McBadge :variant="p.qtyOnHand > 0 ? (p.qtyOnHand <= 3 ? 'warning' : 'success') : 'error'">
                Stock {{ p.qtyOnHand }}
              </McBadge>
              <McBadge v-if="p.specialLabel" variant="accent">{{ p.specialLabel }}</McBadge>
              <span class="lp-row__price">
                <span v-if="effectivePrice(p).was" class="lp-row__was">{{ formatZAR(effectivePrice(p).was!) }}</span>
                <strong>{{ formatZAR(effectivePrice(p).now) }}</strong>
              </span>
            </div>
          </div>
        </label>
      </div>
    </McCard>

    <McCard>
      <h3 class="lp-section-title">Print options</h3>
      <div class="lp-options">
        <fieldset class="lp-copy-mode">
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
              class="lp-copy-num"
            />
          </label>
          <label>
            <input v-model="copyMode" type="radio" value="qty" />
            <span>One per unit on hand</span>
          </label>
          <div v-if="copyMode === 'qty'" class="lp-qty-cap">
            <McField label="Max labels per product" for-id="lp-cap" hint="Safety cap (1–200).">
              <input
                id="lp-cap"
                v-model.number="maxCopiesPerProduct"
                type="number"
                min="1"
                max="200"
                step="1"
              />
            </McField>
          </div>
        </fieldset>

        <div class="lp-pricing-opt">
          <McCheckbox
            v-model="usePromo"
            label="Use active promotion / specials"
            hint="Shows discounted price with the original crossed out and the promotion name."
          />
        </div>
      </div>
    </McCard>

    <McStickyActionBar>
      <div class="lp-bar">
        <div class="lp-bar__info">
          <strong>{{ selectedIds.size }}</strong> selected
          <span v-if="selectionPreview" class="lp-bar__dim">
            · about {{ selectionPreview.labels }} label{{ selectionPreview.labels === 1 ? '' : 's' }}
            <span v-if="copyMode === 'qty' && selectionPreview.missingQty > 0">
              ({{ selectionPreview.missingQty }} not on this page)
            </span>
          </span>
        </div>
        <div class="lp-bar__actions">
          <McButton variant="secondary" type="button" :disabled="!selectedIds.size" @click="clearSelection">
            Clear
          </McButton>
          <McButton
            variant="primary"
            type="button"
            :disabled="!selectedIds.size || printing"
            @click="printLabels"
          >
            <McSpinner v-if="printing" />
            <template v-else>
              <Printer :size="16" />
              Print labels
            </template>
          </McButton>
        </div>
      </div>
    </McStickyActionBar>
  </McPage>
</template>

<style scoped>
.lp-filters {
  display: grid;
  grid-template-columns: 2fr 1fr;
  gap: 1rem 1.25rem;
  align-items: end;
}
.lp-filters__toggles {
  grid-column: 1 / -1;
  display: flex;
  flex-wrap: wrap;
  gap: 0.25rem 1.5rem;
}
@media (max-width: 720px) {
  .lp-filters {
    grid-template-columns: 1fr;
  }
}

.lp-search-wrap {
  position: relative;
  display: flex;
  align-items: center;
}
.lp-search-icon {
  position: absolute;
  left: 0.65rem;
  color: var(--mc-app-text-muted, #6b6b6b);
  pointer-events: none;
}
.lp-search-wrap input {
  padding-left: 2rem;
  width: 100%;
}

.lp-table-head {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 1rem;
  flex-wrap: wrap;
  margin-bottom: 0.75rem;
  padding-bottom: 0.75rem;
  border-bottom: 1px solid var(--mc-app-border-faint, #eceae5);
}
.lp-table-head__left {
  display: flex;
  gap: 1rem;
  align-items: center;
  flex-wrap: wrap;
}
.lp-table-head__right {
  margin: 0;
  font-size: 0.8rem;
  color: var(--mc-app-text-muted, #6b6b6b);
}
.lp-select-all {
  display: inline-flex;
  align-items: center;
  gap: 0.5rem;
  font-size: 0.85rem;
  font-weight: 600;
  color: var(--mc-app-text-secondary, #333);
  cursor: pointer;
}

.lp-loading {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  padding: 1.25rem;
  color: var(--mc-app-text-muted, #6b6b6b);
}

.lp-list {
  display: flex;
  flex-direction: column;
  gap: 0.35rem;
  max-height: 60vh;
  overflow-y: auto;
}
.lp-row {
  display: flex;
  gap: 0.75rem;
  padding: 0.6rem 0.75rem;
  border: 1px solid var(--mc-app-border-faint, #eceae5);
  border-radius: 10px;
  cursor: pointer;
  background: var(--mc-app-surface, #fff);
  transition: background 0.12s ease, border-color 0.12s ease;
}
.lp-row:hover {
  background: var(--mc-app-surface-2, #f9f8f6);
}
.lp-row--selected {
  background: rgba(244, 122, 32, 0.08);
  border-color: var(--mc-accent, #f47a20);
}
.lp-row > input[type='checkbox'] {
  margin-top: 0.25rem;
  width: 1.15rem;
  height: 1.15rem;
  accent-color: var(--mc-accent, #f47a20);
}
.lp-row__body {
  flex: 1;
  display: grid;
  grid-template-columns: 1fr auto;
  gap: 0.25rem 1rem;
  align-items: center;
}
@media (max-width: 640px) {
  .lp-row__body {
    grid-template-columns: 1fr;
  }
}
.lp-row__main {
  display: flex;
  flex-direction: column;
  gap: 0.1rem;
  min-width: 0;
}
.lp-row__name {
  font-size: 0.9rem;
  color: var(--mc-app-text, #1b1b1b);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.lp-row__sku {
  font-size: 0.78rem;
  color: var(--mc-app-text-muted, #6b6b6b);
  font-variant-numeric: tabular-nums;
}
.lp-row__sup {
  font-size: 0.75rem;
  color: var(--mc-app-text-muted, #6b6b6b);
}
.lp-row__meta {
  display: flex;
  gap: 0.5rem;
  align-items: center;
  flex-wrap: wrap;
}
.lp-row__price {
  font-size: 0.88rem;
  font-weight: 600;
  color: var(--mc-app-text, #1b1b1b);
  display: inline-flex;
  align-items: baseline;
  gap: 0.35rem;
}
.lp-row__was {
  color: var(--mc-app-text-muted, #6b6b6b);
  text-decoration: line-through;
  font-weight: 500;
  font-size: 0.78rem;
}

.lp-section-title {
  margin: 0 0 0.75rem;
  font-size: 0.95rem;
  font-weight: 700;
  letter-spacing: 0.03em;
  text-transform: uppercase;
  color: var(--mc-app-heading, #0a0a0c);
}
.lp-options {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 1.25rem;
}
@media (max-width: 720px) {
  .lp-options {
    grid-template-columns: 1fr;
  }
}
.lp-copy-mode {
  border: 1px solid var(--mc-app-border-faint, #eceae5);
  border-radius: 10px;
  padding: 0.75rem 1rem;
  margin: 0;
  display: flex;
  flex-direction: column;
  gap: 0.45rem;
}
.lp-copy-mode legend {
  font-size: 0.78rem;
  font-weight: 700;
  letter-spacing: 0.04em;
  text-transform: uppercase;
  color: var(--mc-app-text-muted, #6b6b6b);
  padding: 0 0.35rem;
}
.lp-copy-mode label {
  display: flex;
  align-items: center;
  gap: 0.6rem;
  font-size: 0.9rem;
  color: var(--mc-app-text-secondary, #333);
  cursor: pointer;
}
.lp-copy-num {
  width: 5rem;
  margin-left: auto;
}
.lp-qty-cap {
  margin-top: 0.35rem;
}
.lp-pricing-opt {
  align-self: start;
}

.lp-bar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 1rem;
  flex-wrap: wrap;
  width: 100%;
}
.lp-bar__info {
  font-size: 0.95rem;
  color: var(--mc-app-text, #1b1b1b);
}
.lp-bar__dim {
  color: var(--mc-app-text-muted, #6b6b6b);
  font-weight: 500;
}
.lp-bar__actions {
  display: flex;
  gap: 0.6rem;
  flex-wrap: wrap;
}
</style>
