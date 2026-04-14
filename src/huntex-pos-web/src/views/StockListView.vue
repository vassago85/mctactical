<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { http } from '@/api/http'
import { useAuthStore } from '@/stores/auth'

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
}

type Page = { total: number; skip: number; take: number; items: Product[] }

const auth = useAuthStore()
const q = ref('')
const includeInactive = ref(false)
const skip = ref(0)
const pageSize = ref(500)
const page = ref<Page | null>(null)
const busy = ref(false)
const err = ref<string | null>(null)

const canManage = computed(() => auth.hasRole('Admin', 'Owner', 'Dev'))
const canExport = canManage
const totalPages = computed(() => (page.value ? Math.ceil(page.value.total / page.value.take) : 0))
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
  const params: Record<string, string | boolean> = { includeInactive: includeInactive.value }
  if (q.value.trim()) params.q = q.value.trim()
  const res = await http.get('/api/products/stocklist/export', { params, responseType: 'blob' })
  const url = URL.createObjectURL(res.data)
  const a = document.createElement('a')
  a.href = url
  a.download = 'stocklist.csv'
  a.click()
  URL.revokeObjectURL(url)
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
const formOk = ref<string | null>(null)

function openAdd() {
  editId.value = null
  form.value = { sku: '', barcode: '', name: '', category: '', manufacturer: '', itemType: '', cost: 0, sellPrice: 0, qtyOnHand: 0 }
  formErr.value = null
  formOk.value = null
  showForm.value = true
}

function openEdit(p: Product) {
  editId.value = p.id
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
  formOk.value = null
  showForm.value = true
}

async function saveProduct() {
  formErr.value = null
  formOk.value = null
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
      formOk.value = 'Product updated'
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
      formOk.value = 'Product created'
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
    await load()
  } catch {
    err.value = 'Update failed'
  }
}

onMounted(() => void load())
</script>

<template>
  <h1>Stock list</h1>
  <p style="color: var(--mc-muted); font-size: 0.9rem">
    Full inventory from the database. Import your Excel under <strong>Import</strong> (Huntex workbook) to load items first.
  </p>
  <p class="err" v-if="err">{{ err }}</p>

  <div class="card">
    <div class="row" style="margin-bottom: 0.75rem">
      <div class="field" style="flex: 1; min-width: 200px; margin-bottom: 0">
        <label>Search SKU, barcode, name, category</label>
        <input v-model="q" placeholder="Filter…" />
      </div>
      <label style="display: flex; align-items: center; gap: 0.35rem; margin-top: 1.5rem">
        <input type="checkbox" v-model="includeInactive" />
        Include inactive
      </label>
    </div>
    <div class="row">
      <label style="display: flex; align-items: center; gap: 0.35rem">
        Rows per page
        <select v-model.number="pageSize" style="width: 6rem">
          <option :value="100">100</option>
          <option :value="250">250</option>
          <option :value="500">500</option>
          <option :value="1000">1000</option>
          <option :value="5000">5000</option>
        </select>
      </label>
      <button type="button" class="btn secondary" :disabled="busy || skip <= 0" @click="prevPage">Previous</button>
      <button
        type="button"
        class="btn secondary"
        :disabled="busy || !page || skip + page.take >= page.total"
        @click="nextPage"
      >
        Next
      </button>
      <span style="color: var(--mc-muted)">{{ pageLabel }}</span>
      <button v-if="canManage" type="button" class="btn" style="margin-left: auto" @click="openAdd">Add product</button>
      <button v-if="canExport" type="button" class="btn secondary" @click="exportCsv">CSV</button>
    </div>
  </div>

  <div v-if="showForm" class="card">
    <h2>{{ editId ? 'Edit product' : 'Add product' }}</h2>
    <p class="err" v-if="formErr">{{ formErr }}</p>
    <p v-if="formOk" style="color: #a5d6a7">{{ formOk }}</p>
    <div class="row" style="flex-wrap: wrap; gap: 0.75rem">
      <div class="field" style="flex: 1; min-width: 120px">
        <label>SKU</label>
        <input v-model="form.sku" required />
      </div>
      <div class="field" style="flex: 1; min-width: 120px">
        <label>Barcode</label>
        <input v-model="form.barcode" />
      </div>
      <div class="field" style="flex: 2; min-width: 200px">
        <label>Name</label>
        <input v-model="form.name" required />
      </div>
      <div class="field" style="flex: 1; min-width: 120px">
        <label>Category</label>
        <input v-model="form.category" />
      </div>
      <div class="field" style="flex: 1; min-width: 120px">
        <label>Manufacturer</label>
        <input v-model="form.manufacturer" placeholder="e.g. Hornady" />
      </div>
      <div class="field" style="flex: 1; min-width: 120px">
        <label>Item type</label>
        <input v-model="form.itemType" placeholder="e.g. Bullet, Cap, Brass" />
      </div>
    </div>
    <div class="row" style="flex-wrap: wrap; gap: 0.75rem">
      <div class="field" style="flex: 1; min-width: 100px">
        <label>Cost (R)</label>
        <input type="number" v-model.number="form.cost" step="0.01" min="0" />
      </div>
      <div class="field" style="flex: 1; min-width: 100px">
        <label>Sell price (R)</label>
        <input type="number" v-model.number="form.sellPrice" step="0.01" min="0" />
      </div>
      <div class="field" style="flex: 1; min-width: 100px">
        <label>Qty on hand</label>
        <input type="number" v-model.number="form.qtyOnHand" step="1" min="0" />
      </div>
    </div>
    <div class="row" style="gap: 0.5rem; margin-top: 0.5rem">
      <button type="button" class="btn" :disabled="formBusy" @click="saveProduct">
        {{ editId ? 'Update' : 'Create' }}
      </button>
      <button type="button" class="btn secondary" @click="showForm = false">Cancel</button>
    </div>
  </div>

  <div class="card" style="overflow-x: auto">
    <table>
      <thead>
        <tr>
          <th>SKU</th>
          <th>Barcode</th>
          <th>Name</th>
          <th>Manufacturer</th>
          <th>Type</th>
          <th>Category</th>
          <th>Supplier</th>
          <th>Cost</th>
          <th>Sell</th>
          <th>Qty</th>
          <th>Active</th>
          <th v-if="canManage"></th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="p in page?.items ?? []" :key="p.id">
          <td>{{ p.sku }}</td>
          <td>{{ p.barcode }}</td>
          <td>{{ p.name }}</td>
          <td>{{ p.manufacturer }}</td>
          <td>{{ p.itemType }}</td>
          <td>{{ p.category }}</td>
          <td>{{ p.supplierName }}</td>
          <td>{{ p.cost != null ? p.cost.toFixed(2) : '—' }}</td>
          <td>{{ p.sellPrice.toFixed(2) }}</td>
          <td>{{ p.qtyOnHand }}</td>
          <td>{{ p.active ? 'Yes' : 'No' }}</td>
          <td v-if="canManage" class="row" style="gap: 0.25rem">
            <button type="button" class="btn secondary" style="padding: 0.2rem 0.5rem; font-size: 0.8rem" @click="openEdit(p)">Edit</button>
            <button type="button" class="btn secondary" style="padding: 0.2rem 0.5rem; font-size: 0.8rem" @click="toggleActive(p)">{{ p.active ? 'Deactivate' : 'Activate' }}</button>
          </td>
        </tr>
      </tbody>
    </table>
    <p v-if="page && !page.items.length" style="color: var(--mc-muted)">No products yet — use Import to load your stock list.</p>
  </div>
</template>
