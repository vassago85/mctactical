<script setup lang="ts">
import { ref } from 'vue'
import { http } from '@/api/http'
import BarcodeScanner from '@/components/BarcodeScanner.vue'

type Product = { id: string; sku: string; barcode?: string | null; name: string; qtyOnHand: number }
type Session = {
  id: string
  name: string
  status: string
  lines: Array<{
    id: string
    productId: string
    productName: string
    sku: string
    qtyBefore: number
    qtyCounted: number
  }>
}

const sessionName = ref(`Count ${new Date().toLocaleDateString()}`)
const session = ref<Session | null>(null)
const q = ref('')
const results = ref<Product[]>([])
const countInput = ref(0)
const selectedProduct = ref<Product | null>(null)
const scanOpen = ref(false)
const err = ref<string | null>(null)
const busy = ref(false)

async function createSession() {
  err.value = null
  busy.value = true
  try {
    const { data } = await http.post<Session>('/api/stocktake/sessions', { name: sessionName.value })
    session.value = data
  } catch {
    err.value = 'Could not create session'
  } finally {
    busy.value = false
  }
}

async function search() {
  const s = q.value.trim()
  if (!s) {
    results.value = []
    return
  }
  const { data } = await http.get<Product[]>('/api/products', { params: { q: s, take: 30 } })
  results.value = data
}

async function pickProduct(p: Product) {
  selectedProduct.value = p
  countInput.value = p.qtyOnHand
  q.value = p.name
}

function onScan(code: string) {
  q.value = code.trim()
  scanOpen.value = false
  void (async () => {
    await search()
    const hit =
      results.value.find((p) => p.barcode === code.trim() || p.sku === code.trim()) ?? results.value[0]
    if (hit) await pickProduct(hit)
  })()
}

async function saveLine() {
  if (!session.value || !selectedProduct.value) return
  err.value = null
  busy.value = true
  try {
    await http.post(`/api/stocktake/sessions/${session.value.id}/lines`, {
      productId: selectedProduct.value.id,
      qtyCounted: countInput.value
    })
    const { data } = await http.get<Session>(`/api/stocktake/sessions/${session.value.id}`)
    session.value = data
  } catch (e: unknown) {
    const ax = e as { response?: { data?: { error?: string } } }
    err.value = ax.response?.data?.error ?? 'Save failed'
  } finally {
    busy.value = false
  }
}

async function postSession() {
  if (!session.value) return
  busy.value = true
  err.value = null
  try {
    await http.post(`/api/stocktake/sessions/${session.value.id}/post`)
    const { data } = await http.get<Session>(`/api/stocktake/sessions/${session.value.id}`)
    session.value = data
    alert('Stocktake posted. Quantities updated.')
  } catch (e: unknown) {
    const ax = e as { response?: { data?: { error?: string } } }
    err.value = ax.response?.data?.error ?? 'Post failed'
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <h1>Stocktake</h1>
  <p class="err" v-if="err">{{ err }}</p>

  <div v-if="!session" class="card">
    <div class="field">
      <label>Session name</label>
      <input v-model="sessionName" />
    </div>
    <button type="button" class="btn" :disabled="busy" @click="createSession">Start session</button>
  </div>

  <div v-else>
    <p>
      <strong>{{ session.name }}</strong> — {{ session.status }}
    </p>
    <div class="row" style="margin-bottom: 0.5rem">
      <button type="button" class="btn secondary" @click="scanOpen = !scanOpen">
        {{ scanOpen ? 'Hide scanner' : 'Scan barcode' }}
      </button>
    </div>
    <div v-if="scanOpen" class="card">
      <BarcodeScanner :active="scanOpen" @decode="onScan" />
    </div>
    <div class="field">
      <label>Find product</label>
      <input v-model="q" @keyup.enter="search" />
      <button type="button" class="btn secondary" @click="search">Search</button>
    </div>
    <div class="card">
      <table>
        <tbody>
          <tr v-for="p in results" :key="p.id" @click="pickProduct(p)" style="cursor: pointer">
            <td>{{ p.name }}</td>
            <td>{{ p.sku }}</td>
            <td>{{ p.qtyOnHand }}</td>
          </tr>
        </tbody>
      </table>
    </div>
    <div v-if="selectedProduct" class="card">
      <p>
        Counting: <strong>{{ selectedProduct.name }}</strong> (system qty {{ selectedProduct.qtyOnHand }})
      </p>
      <div class="field">
        <label>Counted quantity</label>
        <input type="number" v-model.number="countInput" min="0" />
      </div>
      <button type="button" class="btn" :disabled="busy" @click="saveLine">Save line</button>
    </div>
    <h2>Lines</h2>
    <div class="card">
      <table>
        <thead>
          <tr>
            <th>Product</th>
            <th>Before</th>
            <th>Counted</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="l in session.lines" :key="l.id">
            <td>{{ l.productName }}</td>
            <td>{{ l.qtyBefore }}</td>
            <td>{{ l.qtyCounted }}</td>
          </tr>
        </tbody>
      </table>
    </div>
    <button
      v-if="session.status === 'Draft'"
      type="button"
      class="btn danger"
      :disabled="busy"
      @click="postSession"
    >
      Post stocktake (Admin)
    </button>
    <p v-if="session.status === 'Draft'" class="err" style="margin-top: 0.5rem">
      Posting requires Admin / Owner / Dev role.
    </p>
  </div>
</template>
