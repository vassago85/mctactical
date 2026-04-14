<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { http } from '@/api/http'
import { useAuthStore } from '@/stores/auth'
import { useToast } from '@/composables/useToast'
import { formatZAR } from '@/utils/format'
import McPageHeader from '@/components/ui/McPageHeader.vue'
import McCard from '@/components/ui/McCard.vue'
import McButton from '@/components/ui/McButton.vue'
import McField from '@/components/ui/McField.vue'
import McAlert from '@/components/ui/McAlert.vue'
import McBadge from '@/components/ui/McBadge.vue'
import McSpinner from '@/components/ui/McSpinner.vue'
import McEmptyState from '@/components/ui/McEmptyState.vue'

type Product = {
  id: string
  sku: string
  barcode?: string | null
  name: string
  category?: string | null
  manufacturer?: string | null
  itemType?: string | null
  supplierName?: string | null
  cost?: number | null
  sellPrice: number
  qtyOnHand: number
  active: boolean
  warning?: string | null
}

type Page = { total: number; skip: number; take: number; items: Product[] }

const auth = useAuthStore()
const toast = useToast()
const q = ref('')
const includeInactive = ref(false)
const skip = ref(0)
const pageSize = ref(500)
const page = ref<Page | null>(null)
const busy = ref(false)
const err = ref<string | null>(null)

const canManage = computed(() => auth.hasRole('Admin', 'Owner', 'Dev'))
const canExport = canManage
const pageLabel = computed(() => {
  if (!page.value) return ''
  const from = page.value.total === 0 ? 0 : page.value.skip + 1
  const to = Math.min(page.value.skip + page.value.items.length, page.value.total)
  return `${from}–${to} of ${page.value.total}`
})

let debounce: ReturnType<typeof setTimeout> | null = null
watch([q, includeInactive], () => {
  skip.value = 0
  if (debounce) clearTimeout(debounce)
  debounce = setTimeout(() => void load(), 300)
})

watch([skip, pageSize], () => void load())
watch(pageSize, () => {
  skip.value = 0
})

async function load() {
  err.value = null
  busy.value = true
  try {
    const { data } = await http.get<Page>('/api/products/stocklist', {
      params: {
        q: q.value.trim() || undefined,
        includeInactive: includeInactive.value,
        skip: skip.value,
        take: pageSize.value
      }
    })
    page.value = data
  } catch {
    err.value = 'Could not load stock list'
    page.value = null
  } finally {
    busy.value = false
  }
}

function nextPage() {
  if (!page.value) return
  if (skip.value + page.value.take < page.value.total) skip.value += page.value.take
}

function prevPage() {
  skip.value = Math.max(0, skip.value - pageSize.value)
}

async function exportCsv() {
  try {
    const params: Record<string, string | boolean> = { includeInactive: includeInactive.value }
    if (q.value.trim()) params.q = q.value.trim()
    const res = await http.get('/api/products/stocklist/export', { params, responseType: 'blob' })
    const url = URL.createObjectURL(res.data)
    const a = document.createElement('a')
    a.href = url
    a.download = 'stocklist.csv'
    a.click()
    URL.revokeObjectURL(url)
    toast.success('CSV downloaded')
  } catch {
    toast.error('Export failed')
  }
}

const showForm = ref(false)
const editId = ref<string | null>(null)
const form = ref({
  sku: '',
  barcode: '',
  name: '',
  category: '',
  manufacturer: '',
  itemType: '',
  cost: 0,
  sellPrice: 0,
  qtyOnHand: 0
})
const formBusy = ref(false)
const formErr = ref<string | null>(null)
const sellPriceManual = ref(false)
let computeTimer: ReturnType<typeof setTimeout> | null = null

function closeDrawer() {
  showForm.value = false
}

function openAdd() {
  editId.value = null
  sellPriceManual.value = false
  form.value = { sku: '', barcode: '', name: '', category: '', manufacturer: '', itemType: '', cost: 0, sellPrice: 0, qtyOnHand: 0 }
  formErr.value = null
  showForm.value = true
}

function openEdit(p: Product) {
  editId.value = p.id
  sellPriceManual.value = true
  form.value = {
    sku: p.sku,
    barcode: p.barcode ?? '',
    name: p.name,
    category: p.category ?? '',
    manufacturer: p.manufacturer ?? '',
    itemType: p.itemType ?? '',
    cost: p.cost ?? 0,
    sellPrice: p.sellPrice,
    qtyOnHand: p.qtyOnHand
  }
  formErr.value = null
  showForm.value = true
}

watch(() => form.value.cost, (cost) => {
  if (sellPriceManual.value) return
  if (computeTimer) clearTimeout(computeTimer)
  if (!cost || cost <= 0) { form.value.sellPrice = 0; return }
  computeTimer = setTimeout(async () => {
    try {
      const { data } = await http.get<{ sellPrice: number }>('/api/settings/pricing/compute-sell', { params: { cost } })
      if (!sellPriceManual.value) form.value.sellPrice = data.sellPrice
    } catch { /* keep current value */ }
  }, 300)
})

async function saveProduct() {
  formErr.value = null
  formBusy.value = true
  try {
    if (editId.value) {
      await http.put(`/api/products/${editId.value}`, {
        sku: form.value.sku,
        barcode: form.value.barcode || null,
        name: form.value.name,
        category: form.value.category || null,
        manufacturer: form.value.manufacturer || null,
        itemType: form.value.itemType || null,
        cost: form.value.cost,
        sellPrice: form.value.sellPrice,
        qtyOnHand: form.value.qtyOnHand
      })
      toast.success('Product updated')
    } else {
      await http.post('/api/products', {
        sku: form.value.sku,
        barcode: form.value.barcode || null,
        name: form.value.name,
        category: form.value.category || null,
        manufacturer: form.value.manufacturer || null,
        itemType: form.value.itemType || null,
        cost: form.value.cost,
        sellPrice: form.value.sellPrice,
        qtyOnHand: form.value.qtyOnHand
      })
      toast.success('Product created')
    }
    showForm.value = false
    await load()
  } catch (e: unknown) {
    const ax = e as { response?: { data?: { error?: string } } }
    formErr.value = ax.response?.data?.error ?? 'Save failed'
  } finally {
    formBusy.value = false
  }
}

async function toggleActive(p: Product) {
  err.value = null
  try {
    await http.put(`/api/products/${p.id}`, { active: !p.active })
    toast.success(p.active ? 'Product deactivated' : 'Product activated')
    await load()
  } catch {
    err.value = 'Update failed'
    toast.error('Update failed')
  }
}

onMounted(() => void load())
</script>

<template>
  <div class="stock-page">
    <McPageHeader title="Stock list" description="Full inventory. Use Import to load items from your Huntex workbook or CSV.">
      <template v-if="canManage" #actions>
        <McButton variant="primary" type="button" @click="openAdd">Add product</McButton>
        <McButton v-if="canExport" variant="secondary" type="button" @click="exportCsv">Export CSV</McButton>
      </template>
    </McPageHeader>

    <McAlert v-if="err" variant="error">{{ err }}</McAlert>

    <McCard :padded="false" title="Filters">
      <div class="stock-toolbar">
        <div class="stock-toolbar__search">
          <McField label="Search" for-id="stock-q">
            <input
              id="stock-q"
              v-model="q"
              type="search"
              placeholder="Any words, any order — matches name, SKU, barcode, category, supplier…"
            />
          </McField>
        </div>
        <label class="stock-toolbar__check">
          <input v-model="includeInactive" type="checkbox" />
          Include inactive
        </label>
        <div class="stock-toolbar__page">
          <span class="stock-toolbar__label">Rows</span>
          <select v-model.number="pageSize" class="stock-toolbar__select">
            <option :value="100">100</option>
            <option :value="250">250</option>
            <option :value="500">500</option>
            <option :value="1000">1000</option>
            <option :value="5000">5000</option>
          </select>
        </div>
        <div class="stock-toolbar__nav">
          <McButton variant="secondary" type="button" :disabled="busy || skip <= 0" @click="prevPage">Previous</McButton>
          <McButton
            variant="secondary"
            type="button"
            :disabled="busy || !page || skip + page.take >= page.total"
            @click="nextPage"
          >
            Next
          </McButton>
          <span class="stock-toolbar__meta">{{ pageLabel }}</span>
          <McSpinner v-if="busy" />
        </div>
      </div>
    </McCard>

    <McCard :padded="false" title="Products">
      <div class="stock-table-wrap">
        <table v-if="page?.items.length" class="stock-table mc-table">
          <thead>
            <tr>
              <th>SKU</th>
              <th>Barcode</th>
              <th>Name</th>
              <th>Mfr</th>
              <th>Type</th>
              <th>Category</th>
              <th>Supplier</th>
              <th>Cost</th>
              <th>Sell</th>
              <th>Qty</th>
              <th>Status</th>
              <th v-if="canManage"></th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="p in page.items" :key="p.id">
              <td class="stock-mono">{{ p.sku }}</td>
              <td class="stock-mono">{{ p.barcode ?? '—' }}</td>
              <td class="stock-name">{{ p.name }}</td>
              <td>{{ p.manufacturer ?? '—' }}</td>
              <td>{{ p.itemType ?? '—' }}</td>
              <td>{{ p.category ?? '—' }}</td>
              <td>{{ p.supplierName ?? '—' }}</td>
              <td>{{ p.cost != null ? formatZAR(p.cost) : '—' }}</td>
              <td :class="{ 'stock-warn': !!p.warning }">
                {{ formatZAR(p.sellPrice) }}
                <span v-if="p.warning" :title="p.warning" class="stock-warn-icon">⚠</span>
              </td>
              <td>
                <strong :class="{ 'stock-qty--low': p.qtyOnHand <= 3 }">{{ p.qtyOnHand }}</strong>
              </td>
              <td>
                <McBadge :variant="p.active ? 'success' : 'neutral'">{{ p.active ? 'Active' : 'Inactive' }}</McBadge>
              </td>
              <td v-if="canManage" class="stock-actions">
                <McButton variant="secondary" dense type="button" @click="openEdit(p)">Edit</McButton>
                <McButton variant="ghost" dense type="button" @click="toggleActive(p)">
                  {{ p.active ? 'Deactivate' : 'Activate' }}
                </McButton>
              </td>
            </tr>
          </tbody>
        </table>
        <McEmptyState
          v-else-if="page && !busy"
          title="No products in this view"
          hint="Try clearing the search filter or import stock from the Import page."
        />
        <div v-else-if="busy" class="stock-loading">
          <McSpinner />
          <span>Loading…</span>
        </div>
      </div>
    </McCard>

    <Teleport to="body">
      <Transition name="stock-drawer-fade">
        <div v-if="showForm" class="stock-drawer-overlay" aria-hidden="true" @click.self="closeDrawer" />
      </Transition>
      <Transition name="stock-drawer-slide">
        <aside v-if="showForm" class="stock-drawer" role="dialog" aria-labelledby="stock-drawer-title">
          <header class="stock-drawer__head">
            <h2 id="stock-drawer-title" class="stock-drawer__title">{{ editId ? 'Edit product' : 'Add product' }}</h2>
            <button type="button" class="stock-drawer__close" aria-label="Close" @click="closeDrawer">×</button>
          </header>
          <div class="stock-drawer__body">
            <McAlert v-if="formErr" variant="error">{{ formErr }}</McAlert>
            <McField label="SKU" for-id="f-sku">
              <input id="f-sku" v-model="form.sku" required />
            </McField>
            <McField label="Barcode" for-id="f-bc">
              <input id="f-bc" v-model="form.barcode" />
            </McField>
            <McField label="Name" for-id="f-name">
              <input id="f-name" v-model="form.name" required />
            </McField>
            <div class="stock-drawer__grid">
              <McField label="Category" for-id="f-cat">
                <input id="f-cat" v-model="form.category" />
              </McField>
              <McField label="Manufacturer" for-id="f-mfr">
                <input id="f-mfr" v-model="form.manufacturer" placeholder="e.g. Hornady" />
              </McField>
              <McField label="Item type" for-id="f-type">
                <input id="f-type" v-model="form.itemType" placeholder="e.g. Bullet, Cap" />
              </McField>
            </div>
            <div class="stock-drawer__grid">
              <McField label="Cost (R)" for-id="f-cost">
                <input id="f-cost" v-model.number="form.cost" type="number" step="0.01" min="0" />
              </McField>
              <McField label="Sell price (R)" for-id="f-sell" :hint="sellPriceManual ? '' : 'Auto-calculated from cost'">
                <input id="f-sell" v-model.number="form.sellPrice" type="number" step="0.01" min="0" @input="sellPriceManual = true" />
              </McField>
              <McField label="Qty on hand" for-id="f-qty">
                <input id="f-qty" v-model.number="form.qtyOnHand" type="number" step="1" min="0" />
              </McField>
            </div>
          </div>
          <footer class="stock-drawer__foot">
            <McButton variant="secondary" type="button" @click="closeDrawer">Cancel</McButton>
            <McButton variant="primary" type="button" :disabled="formBusy" @click="saveProduct">
              {{ editId ? 'Save changes' : 'Create' }}
            </McButton>
          </footer>
        </aside>
      </Transition>
    </Teleport>
  </div>
</template>

<style scoped>
.stock-page {
  min-height: 100%;
}

.stock-toolbar {
  display: flex;
  flex-wrap: wrap;
  align-items: flex-end;
  gap: 1rem;
  padding: 1.15rem var(--mc-app-pad-card, 1.75rem);
}

.stock-toolbar__search {
  flex: 1 1 220px;
}

.stock-toolbar__search :deep(.mc-field) {
  margin-bottom: 0;
}

.stock-toolbar__check {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  font-weight: 500;
  padding-bottom: 0.35rem;
  cursor: pointer;
}

.stock-toolbar__page {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.stock-toolbar__label {
  font-size: 0.8rem;
  font-weight: 700;
  color: var(--mc-app-text-secondary, #333336);
}

.stock-toolbar__select {
  min-height: 44px;
  padding: 0 0.85rem;
  border-radius: 10px;
  border: 1.5px solid var(--mc-app-border-subtle, #c8c5bd);
  background: var(--mc-app-surface, #fff);
  color: var(--mc-app-text, #1a1a1c);
  transition: border-color 0.15s ease, box-shadow 0.15s ease;
}

.stock-toolbar__select:focus {
  outline: none;
  border-color: var(--mc-accent, #f47a20);
  box-shadow: 0 0 0 3px rgba(244, 122, 32, 0.2);
}

.stock-toolbar__nav {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.5rem;
  margin-left: auto;
}

.stock-toolbar__meta {
  font-size: 0.85rem;
  color: var(--mc-app-text-muted, #5c5a56);
  font-weight: 500;
}

.stock-table-wrap {
  overflow-x: auto;
  -webkit-overflow-scrolling: touch;
}

.stock-table {
  width: 100%;
  min-width: 900px;
  font-size: 0.88rem;
}

.stock-mono {
  font-variant-numeric: tabular-nums;
  white-space: nowrap;
}

.stock-name {
  max-width: 200px;
  font-weight: 500;
}

.stock-warn {
  color: #b71c1c;
  font-weight: 600;
}

.stock-warn-icon {
  cursor: help;
  margin-left: 0.2rem;
}

.stock-qty--low {
  color: #e65100;
}

.stock-actions {
  white-space: nowrap;
}

.stock-loading {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 0.75rem;
  padding: 3rem;
  color: var(--mc-app-text-muted, #5c5a56);
}

.stock-drawer-overlay {
  position: fixed;
  inset: 0;
  z-index: 10030;
  background: rgba(10, 10, 11, 0.45);
  backdrop-filter: blur(2px);
}

.stock-drawer {
  position: fixed;
  top: 0;
  right: 0;
  bottom: 0;
  z-index: 10031;
  width: min(480px, 100vw);
  background: var(--mc-app-surface, #fff);
  box-shadow: -8px 0 40px rgba(0, 0, 0, 0.2);
  display: flex;
  flex-direction: column;
  border-left: 1px solid var(--mc-app-border-soft, #ddd9d3);
}

.stock-drawer__head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 1.15rem 1.5rem;
  border-bottom: 1px solid var(--mc-app-border-faint, #eceae5);
  background: var(--mc-app-surface-2, #f9f8f6);
}

.stock-drawer__title {
  margin: 0;
  font-family: 'Barlow Condensed', sans-serif;
  font-size: 1.15rem;
  letter-spacing: 0.05em;
  text-transform: uppercase;
}

.stock-drawer__close {
  width: 44px;
  height: 44px;
  border: 1.5px solid var(--mc-app-border-faint, #eceae5);
  background: var(--mc-app-surface, #fff);
  border-radius: 10px;
  font-size: 1.35rem;
  line-height: 1;
  cursor: pointer;
  color: var(--mc-app-text-secondary, #333336);
  transition: background 0.15s ease, border-color 0.15s ease;
}

.stock-drawer__close:hover {
  background: var(--mc-app-surface-muted, #f0eeea);
  border-color: var(--mc-app-border-subtle, #c8c5bd);
}

.stock-drawer__body {
  flex: 1;
  overflow-y: auto;
  padding: 1.5rem;
}

.stock-drawer__grid {
  display: grid;
  grid-template-columns: 1fr;
  gap: 0;
}

@media (min-width: 400px) {
  .stock-drawer__grid {
    grid-template-columns: 1fr 1fr;
  }
}

.stock-drawer__foot {
  padding: 1.15rem 1.5rem;
  border-top: 1px solid var(--mc-app-border-faint, #eceae5);
  display: flex;
  justify-content: flex-end;
  gap: 0.6rem;
  flex-wrap: wrap;
  background: var(--mc-app-surface-2, #f9f8f6);
}

.stock-drawer-fade-enter-active,
.stock-drawer-fade-leave-active {
  transition: opacity 0.2s ease;
}
.stock-drawer-fade-enter-from,
.stock-drawer-fade-leave-to {
  opacity: 0;
}

.stock-drawer-slide-enter-active,
.stock-drawer-slide-leave-active {
  transition: transform 0.25s ease;
}
.stock-drawer-slide-enter-from,
.stock-drawer-slide-leave-to {
  transform: translateX(100%);
}
</style>
