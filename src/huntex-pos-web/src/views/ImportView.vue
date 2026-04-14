<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { http } from '@/api/http'

type Supplier = { id: string; name: string }
type Preset = { id: string; supplierId: string; name: string; mapping: Record<string, string | undefined> }
type PreviewRow = {
  rowIndex: number
  sku: string
  name: string
  cost: number
  sellPrice: number
  qtyOnHand: number
  error?: string | null
}

const suppliers = ref<Supplier[]>([])
const supplierId = ref('')
const sheetName = ref('huntex 2026')
const huntexFile = ref<File | null>(null)
const huntexPreview = ref<PreviewRow[]>([])
const huntexWarnings = ref<string[]>([])
const wholesalerFile = ref<File | null>(null)
const mappingJson = ref(
  JSON.stringify(
    { sku: 'SKU', barcode: 'Barcode', name: 'Description', cost: 'Cost', sellPrice: 'RRP', qtyOnHand: 'Qty' },
    null,
    2
  )
)
const wholesalerPreview = ref<PreviewRow[]>([])
const err = ref<string | null>(null)
const busy = ref(false)
const presets = ref<Preset[]>([])

async function loadPresets() {
  if (!supplierId.value) return
  const { data } = await http.get<Preset[]>('/api/imports/presets', { params: { supplierId: supplierId.value } })
  presets.value = data
}

onMounted(async () => {
  const { data } = await http.get<Supplier[]>('/api/suppliers')
  suppliers.value = data
  if (data[0]) supplierId.value = data[0].id
  await loadPresets().catch(() => {})
})

async function applyPreset(p: Preset) {
  mappingJson.value = JSON.stringify(p.mapping, null, 2)
}

async function savePreset() {
  const name = prompt('Preset name?')
  if (!name || !supplierId.value) return
  let mapping: Record<string, string | undefined>
  try {
    mapping = JSON.parse(mappingJson.value) as Record<string, string | undefined>
  } catch {
    err.value = 'Mapping JSON is invalid'
    return
  }
  await http.post('/api/imports/presets', { supplierId: supplierId.value, name, mapping })
  await loadPresets()
}

async function previewHuntex() {
  err.value = null
  if (!huntexFile.value) return
  busy.value = true
  try {
    const fd = new FormData()
    fd.append('file', huntexFile.value)
    fd.append('sheetName', sheetName.value)
    if (supplierId.value) fd.append('supplierId', supplierId.value)
    fd.append('commit', 'false')
    const { data } = await http.post<{ preview: PreviewRow[]; warnings: string[] }>('/api/imports/huntex', fd)
    huntexPreview.value = data.preview
    huntexWarnings.value = data.warnings ?? []
  } catch {
    err.value = 'Huntex preview failed'
  } finally {
    busy.value = false
  }
}

async function commitHuntex() {
  err.value = null
  if (!huntexFile.value) return
  busy.value = true
  try {
    const fd = new FormData()
    fd.append('file', huntexFile.value)
    fd.append('sheetName', sheetName.value)
    if (supplierId.value) fd.append('supplierId', supplierId.value)
    fd.append('commit', 'true')
    const { data } = await http.post('/api/imports/huntex', fd)
    alert(`Imported ${data.imported} rows`)
    huntexPreview.value = []
  } catch {
    err.value = 'Huntex import failed'
  } finally {
    busy.value = false
  }
}

async function previewWholesaler() {
  err.value = null
  if (!wholesalerFile.value || !supplierId.value) return
  busy.value = true
  try {
    const fd = new FormData()
    fd.append('file', wholesalerFile.value)
    fd.append('supplierId', supplierId.value)
    fd.append('mappingJson', mappingJson.value)
    fd.append('commit', 'false')
    const { data } = await http.post<{ preview: PreviewRow[] }>('/api/imports/wholesaler', fd)
    wholesalerPreview.value = data.preview
  } catch {
    err.value = 'Wholesaler preview failed (check mapping JSON matches CSV headers or use column letters A,B,…)'
  } finally {
    busy.value = false
  }
}

async function commitWholesaler() {
  err.value = null
  if (!wholesalerFile.value || !supplierId.value) return
  busy.value = true
  try {
    const fd = new FormData()
    fd.append('file', wholesalerFile.value)
    fd.append('supplierId', supplierId.value)
    fd.append('mappingJson', mappingJson.value)
    fd.append('commit', 'true')
    const { data } = await http.post('/api/imports/wholesaler', fd)
    alert(`Imported ${data.imported} rows`)
    wholesalerPreview.value = []
  } catch {
    err.value = 'Wholesaler import failed'
  } finally {
    busy.value = false
  }
}

async function addSupplier() {
  const name = prompt('Supplier name?')
  if (!name) return
  const { data } = await http.post<Supplier>('/api/suppliers', { name })
  suppliers.value.push(data)
  supplierId.value = data.id
}
</script>

<template>
  <h1>Stock import</h1>
  <p style="color: var(--mc-muted); font-size: 0.9rem">
    After importing, open <RouterLink to="/stock">Stock list</RouterLink> to review everything in the database.
  </p>
  <p class="err" v-if="err">{{ err }}</p>

  <div class="card">
    <h2>Supplier</h2>
    <div class="row">
      <select v-model="supplierId" @change="loadPresets">
        <option v-for="s in suppliers" :key="s.id" :value="s.id">{{ s.name }}</option>
      </select>
      <button type="button" class="btn secondary" @click="addSupplier">New supplier</button>
    </div>
    <div v-if="presets.length" class="row" style="margin-top: 0.5rem; flex-wrap: wrap">
      <span style="width: 100%; font-size: 0.9rem">Mapping presets:</span>
      <button v-for="p in presets" :key="p.id" type="button" class="btn secondary" @click="applyPreset(p)">
        {{ p.name }}
      </button>
      <button type="button" class="btn secondary" @click="savePreset">Save current mapping…</button>
    </div>
  </div>

  <div class="card">
    <h2>Huntex workbook or CSV</h2>
    <p>
      Upload <strong>.xlsx</strong> / <strong>.xlsm</strong> (sheet name defaults to <code>huntex 2026</code>) or
      <strong>.csv</strong> with the same headers (first row).
      <a href="/api/imports/example-csv" download>Download example CSV</a> to see the expected format.
    </p>
    <div class="field">
      <label>Sheet name (Excel only)</label>
      <input v-model="sheetName" />
    </div>
    <input type="file" accept=".xlsx,.xlsm,.csv" @change="huntexFile = ($event.target as HTMLInputElement).files?.[0] ?? null" />
    <div class="row" style="margin-top: 0.75rem">
      <button type="button" class="btn secondary" :disabled="busy" @click="previewHuntex">Preview</button>
      <button type="button" class="btn" :disabled="busy" @click="commitHuntex">Commit import</button>
    </div>
    <p v-for="w in huntexWarnings" :key="w" class="err">{{ w }}</p>
    <table v-if="huntexPreview.length">
      <thead>
        <tr>
          <th>#</th>
          <th>SKU</th>
          <th>Name</th>
          <th>Cost</th>
          <th>Sell</th>
          <th>Qty</th>
          <th>Err</th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="r in huntexPreview.slice(0, 80)" :key="r.rowIndex">
          <td>{{ r.rowIndex }}</td>
          <td>{{ r.sku }}</td>
          <td>{{ r.name }}</td>
          <td>{{ r.cost }}</td>
          <td>{{ r.sellPrice }}</td>
          <td>{{ r.qtyOnHand }}</td>
          <td>{{ r.error }}</td>
        </tr>
      </tbody>
    </table>
  </div>

  <div class="card">
    <h2>Wholesaler CSV / Excel</h2>
    <p>
      Provide column mapping JSON: header names or Excel columns (<code>A</code>, <code>B</code>, …). Example uses
      typical header names — adjust to your file.
    </p>
    <textarea v-model="mappingJson" rows="8" style="width: 100%; font-family: monospace" />
    <input type="file" accept=".csv,.xlsx,.xlsm" @change="wholesalerFile = ($event.target as HTMLInputElement).files?.[0] ?? null" />
    <div class="row" style="margin-top: 0.75rem">
      <button type="button" class="btn secondary" :disabled="busy" @click="previewWholesaler">Preview</button>
      <button type="button" class="btn" :disabled="busy" @click="commitWholesaler">Commit import</button>
    </div>
    <table v-if="wholesalerPreview.length">
      <thead>
        <tr>
          <th>#</th>
          <th>SKU</th>
          <th>Name</th>
          <th>Cost</th>
          <th>Sell</th>
          <th>Qty</th>
          <th>Err</th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="r in wholesalerPreview.slice(0, 80)" :key="r.rowIndex">
          <td>{{ r.rowIndex }}</td>
          <td>{{ r.sku }}</td>
          <td>{{ r.name }}</td>
          <td>{{ r.cost }}</td>
          <td>{{ r.sellPrice }}</td>
          <td>{{ r.qtyOnHand }}</td>
          <td>{{ r.error }}</td>
        </tr>
      </tbody>
    </table>
  </div>
</template>
