<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { http } from '@/api/http'
import { useToast } from '@/composables/useToast'
import McPageHeader from '@/components/ui/McPageHeader.vue'
import McCard from '@/components/ui/McCard.vue'
import McButton from '@/components/ui/McButton.vue'
import McField from '@/components/ui/McField.vue'
import McAlert from '@/components/ui/McAlert.vue'
import McBadge from '@/components/ui/McBadge.vue'
import McSpinner from '@/components/ui/McSpinner.vue'
import McEmptyState from '@/components/ui/McEmptyState.vue'
import McModal from '@/components/ui/McModal.vue'
import BarcodeScanner from '@/components/BarcodeScanner.vue'
import { X } from 'lucide-vue-next'

const toast = useToast()

type Supplier = { id: string; name: string }
type BatchLine = {
  id: string
  productId: string
  sku: string
  barcode?: string | null
  productName: string
  expectedQty: number
  checkedQty: number
  notes?: string | null
}
type Batch = {
  id: string
  supplierId: string
  supplierName: string
  type: string
  status: string
  notes?: string | null
  createdBy?: string | null
  createdAt: string
  committedAt?: string | null
  lines: BatchLine[]
}
type ReturnableLine = {
  productId: string
  sku: string
  barcode?: string | null
  productName: string
  onHand: number
}

/* ── State ── */
const suppliers = ref<Supplier[]>([])
const batches = ref<Batch[]>([])
const activeBatch = ref<Batch | null>(null)
const busy = ref(false)
const err = ref<string | null>(null)

/* create form */
const showCreate = ref(false)
const createType = ref<'Receive' | 'Return'>('Receive')
const createSupplierId = ref('')
const createNotes = ref('')

/* scan */
const scanOpen = ref(false)
const lastScanResult = ref<string | null>(null)

/* import */
const showImport = ref(false)
const importFile = ref<File | null>(null)

/* product search (manual add) */
const showAddProduct = ref(false)
const addSearch = ref('')
const addResults = ref<{ id: string; sku: string; name: string }[]>([])
const addQty = ref(1)
const addBusy = ref(false)

/* returnable stock for return mode */
const returnableStock = ref<ReturnableLine[]>([])

/* ── Lifecycle ── */
onMounted(async () => {
  await Promise.all([loadSuppliers(), loadBatches()])
})

/* ── API calls ── */
async function loadSuppliers() {
  try {
    const { data } = await http.get<Supplier[]>('/api/suppliers')
    suppliers.value = data
  } catch { /* non-critical */ }
}

async function loadBatches() {
  busy.value = true
  try {
    const { data } = await http.get<Batch[]>('/api/consignment-batches')
    batches.value = data
  } catch (e: unknown) {
    handleErr(e)
  } finally {
    busy.value = false
  }
}

async function createBatch() {
  if (!createSupplierId.value) { err.value = 'Select a supplier.'; return }
  err.value = null
  busy.value = true
  try {
    const { data } = await http.post<Batch>('/api/consignment-batches', {
      type: createType.value,
      supplierId: createSupplierId.value,
      notes: createNotes.value || null
    })
    showCreate.value = false
    activeBatch.value = data

    if (createType.value === 'Return') {
      await loadReturnableStock(createSupplierId.value, data.id)
    }
    createSupplierId.value = ''
    createNotes.value = ''
  } catch (e: unknown) {
    handleErr(e)
  } finally {
    busy.value = false
  }
}

async function loadReturnableStock(supplierId: string, batchId: string) {
  try {
    const { data } = await http.get<ReturnableLine[]>('/api/consignment-batches/returnable-stock', {
      params: { supplierId }
    })
    returnableStock.value = data
    for (const item of data) {
      await http.post(`/api/consignment-batches/${batchId}/lines`, {
        productId: item.productId,
        expectedQty: item.onHand
      })
    }
    await refreshBatch(batchId)
  } catch (e: unknown) {
    handleErr(e)
  }
}

async function openBatch(b: Batch) {
  busy.value = true
  try {
    const { data } = await http.get<Batch>(`/api/consignment-batches/${b.id}`)
    activeBatch.value = data
  } catch (e: unknown) {
    handleErr(e)
  } finally {
    busy.value = false
  }
}

async function refreshBatch(id: string) {
  try {
    const { data } = await http.get<Batch>(`/api/consignment-batches/${id}`)
    activeBatch.value = data
  } catch { /* silent */ }
}

async function deleteBatch(id: string) {
  if (!confirm('Delete this draft batch?')) return
  try {
    await http.delete(`/api/consignment-batches/${id}`)
    activeBatch.value = null
    await loadBatches()
    toast.success('Batch deleted')
  } catch (e: unknown) {
    handleErr(e)
  }
}

async function removeLine(lineId: string) {
  if (!activeBatch.value) return
  try {
    const { data } = await http.delete<Batch>(`/api/consignment-batches/${activeBatch.value.id}/lines/${lineId}`)
    activeBatch.value = data
  } catch (e: unknown) {
    handleErr(e)
  }
}

async function updateLineQty(lineId: string, field: 'checkedQty' | 'expectedQty', value: number) {
  if (!activeBatch.value) return
  try {
    const body: Record<string, number> = {}
    body[field] = Math.max(0, value)
    const { data } = await http.put<Batch>(`/api/consignment-batches/${activeBatch.value.id}/lines/${lineId}`, body)
    activeBatch.value = data
  } catch (e: unknown) {
    handleErr(e)
  }
}

/* ── Scanning ── */
async function onScan(code: string) {
  if (!activeBatch.value) return
  scanOpen.value = false
  lastScanResult.value = null
  try {
    const { data } = await http.post<Batch>(`/api/consignment-batches/${activeBatch.value.id}/scan`, {
      barcode: code.trim(),
      qty: 1
    })
    activeBatch.value = data
    const found = data.lines.find(l => l.barcode === code.trim() || l.sku === code.trim())
    lastScanResult.value = found ? `${found.productName} — checked: ${found.checkedQty}` : 'Scanned'
    toast.success(lastScanResult.value)
  } catch (e: unknown) {
    handleErr(e)
    lastScanResult.value = 'Not found'
  }
}

/* ── Import CSV ── */
async function importCsv() {
  if (!activeBatch.value || !importFile.value) return
  const form = new FormData()
  form.append('file', importFile.value)
  busy.value = true
  try {
    const { data } = await http.post(`/api/consignment-batches/${activeBatch.value.id}/import`, form)
    activeBatch.value = (data as any).batch
    const nf = (data as any).notFound as string[]
    if (nf?.length) toast.warning(`${nf.length} SKUs not found: ${nf.slice(0, 5).join(', ')}${nf.length > 5 ? '…' : ''}`)
    else toast.success(`${(data as any).added} lines imported`)
    showImport.value = false
    importFile.value = null
  } catch (e: unknown) {
    handleErr(e)
  } finally {
    busy.value = false
  }
}

/* ── Manual add product ── */
async function searchProducts() {
  if (addSearch.value.length < 2) return
  addBusy.value = true
  try {
    const { data } = await http.get<any[]>('/api/products', { params: { q: addSearch.value, take: 20 } })
    addResults.value = data.map((p: any) => ({ id: p.id, sku: p.sku, name: p.name }))
  } catch { /* ignore */ } finally { addBusy.value = false }
}

async function addProduct(productId: string) {
  if (!activeBatch.value) return
  try {
    const { data } = await http.post<Batch>(`/api/consignment-batches/${activeBatch.value.id}/lines`, {
      productId,
      expectedQty: addQty.value
    })
    activeBatch.value = data
    toast.success('Product added')
    showAddProduct.value = false
    addSearch.value = ''
    addResults.value = []
    addQty.value = 1
  } catch (e: unknown) {
    handleErr(e)
  }
}

/* ── Commit ── */
async function commitBatch() {
  if (!activeBatch.value) return
  const label = activeBatch.value.type === 'Receive' ? 'receive' : 'return'
  if (!confirm(`Commit this ${label} batch? Stock levels will be updated.`)) return
  busy.value = true
  try {
    const { data } = await http.post<Batch>(`/api/consignment-batches/${activeBatch.value.id}/commit`)
    activeBatch.value = data
    toast.success('Batch committed — stock updated')
    await loadBatches()
  } catch (e: unknown) {
    handleErr(e)
  } finally {
    busy.value = false
  }
}

/* ── PDF ── */
async function downloadPdf() {
  if (!activeBatch.value) return
  try {
    const { data } = await http.get(`/api/consignment-batches/${activeBatch.value.id}/pdf`, { responseType: 'blob' })
    const url = URL.createObjectURL(data as Blob)
    window.open(url, '_blank')
  } catch (e: unknown) {
    handleErr(e)
  }
}

/* ── Helpers ── */
function handleErr(e: unknown) {
  const ax = e as { response?: { data?: { error?: string } } }
  const msg = ax.response?.data?.error ?? 'Operation failed'
  err.value = msg
  toast.error(msg)
}

function lineStatus(l: BatchLine): 'done' | 'partial' | 'extra' | 'pending' {
  if (l.expectedQty > 0 && l.checkedQty >= l.expectedQty) return 'done'
  if (l.checkedQty > 0 && l.expectedQty > 0) return 'partial'
  if (l.expectedQty === 0 && l.checkedQty > 0) return 'extra'
  return 'pending'
}

function statusVariant(s: string): 'success' | 'warning' | 'error' | 'neutral' {
  if (s === 'done') return 'success'
  if (s === 'partial') return 'warning'
  if (s === 'extra') return 'error'
  return 'neutral'
}

const totalExpected = computed(() => activeBatch.value?.lines.reduce((s, l) => s + l.expectedQty, 0) ?? 0)
const totalChecked = computed(() => activeBatch.value?.lines.reduce((s, l) => s + l.checkedQty, 0) ?? 0)
const progressPct = computed(() => totalExpected.value > 0 ? Math.min(100, Math.round(totalChecked.value / totalExpected.value * 100)) : 0)
const isDraft = computed(() => activeBatch.value?.status === 'Draft')
const isReceive = computed(() => activeBatch.value?.type === 'Receive')

function formatDate(iso: string) {
  return new Date(iso).toLocaleString('en-ZA', { dateStyle: 'short', timeStyle: 'short' })
}
</script>

<template>
  <McPageHeader title="Consignment Batches" subtitle="Receive and return consignment stock with verification" />

  <McAlert v-if="err" variant="error" style="margin-bottom: 1rem">{{ err }}</McAlert>

  <!-- Active batch view -->
  <template v-if="activeBatch">
    <McCard>
      <template #header>
        <div class="cb-batch-head">
          <div>
            <h3 class="cb-batch-title">
              {{ activeBatch.type === 'Receive' ? 'Receive from' : 'Return to' }}
              {{ activeBatch.supplierName }}
            </h3>
            <div class="cb-batch-meta">
              <McBadge :variant="activeBatch.status === 'Committed' ? 'success' : 'warning'">{{ activeBatch.status }}</McBadge>
              <span>{{ formatDate(activeBatch.createdAt) }}</span>
              <span v-if="activeBatch.notes" class="cb-notes">{{ activeBatch.notes }}</span>
            </div>
          </div>
          <div class="cb-batch-actions">
            <McButton variant="secondary" dense @click="activeBatch = null; loadBatches()">Back</McButton>
            <McButton variant="secondary" dense @click="downloadPdf">Download PDF</McButton>
            <template v-if="isDraft">
              <template v-if="isReceive">
                <McButton variant="secondary" dense @click="showImport = true">Import CSV</McButton>
                <McButton variant="secondary" dense @click="showAddProduct = true">+ Add product</McButton>
                <McButton variant="primary" dense @click="scanOpen = !scanOpen">
                  {{ scanOpen ? 'Stop scan' : 'Scan barcode' }}
                </McButton>
              </template>
              <template v-else>
                <McButton variant="secondary" dense @click="showAddProduct = true">+ Add product</McButton>
              </template>
              <McButton variant="primary" dense :disabled="busy || !activeBatch.lines.length" @click="commitBatch">
                Commit
              </McButton>
            </template>
          </div>
        </div>
      </template>

      <!-- Scanner -->
      <div v-if="scanOpen && isDraft" class="cb-scanner-wrap">
        <BarcodeScanner :active="scanOpen" @decode="onScan" />
        <p v-if="lastScanResult" class="cb-scan-result">{{ lastScanResult }}</p>
      </div>

      <!-- Progress bar (receive mode) -->
      <div v-if="isReceive && activeBatch.lines.length" class="cb-progress">
        <div class="cb-progress-bar">
          <div class="cb-progress-fill" :style="{ width: progressPct + '%' }" />
        </div>
        <span class="cb-progress-label">{{ totalChecked }} / {{ totalExpected }} checked ({{ progressPct }}%)</span>
      </div>

      <!-- Lines table -->
      <div class="cb-table-wrap">
        <table v-if="activeBatch.lines.length" class="mc-table cb-table">
          <thead>
            <tr>
              <th>SKU</th>
              <th>Product</th>
              <th class="text-center">Expected</th>
              <th v-if="isReceive" class="text-center">Received</th>
              <th class="text-center">Status</th>
              <th v-if="isDraft"></th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="l in activeBatch.lines" :key="l.id" :class="'cb-row--' + lineStatus(l)">
              <td class="cb-mono">{{ l.sku }}</td>
              <td>{{ l.productName }}</td>
              <td class="text-center">
                <template v-if="isDraft && !isReceive">
                  <input type="number" class="cb-qty-input" :value="l.expectedQty" min="0"
                    @change="updateLineQty(l.id, 'expectedQty', +($event.target as HTMLInputElement).value)" />
                </template>
                <template v-else>{{ l.expectedQty }}</template>
              </td>
              <td v-if="isReceive" class="text-center">
                <template v-if="isDraft">
                  <input type="number" class="cb-qty-input" :value="l.checkedQty" min="0"
                    @change="updateLineQty(l.id, 'checkedQty', +($event.target as HTMLInputElement).value)" />
                </template>
                <template v-else>{{ l.checkedQty }}</template>
              </td>
              <td class="text-center">
                <McBadge v-if="isReceive" :variant="statusVariant(lineStatus(l))">{{ lineStatus(l) }}</McBadge>
                <span v-else>{{ l.expectedQty }}</span>
              </td>
              <td v-if="isDraft" class="text-center">
                <button type="button" class="cb-remove-btn" title="Remove" @click="removeLine(l.id)"><X :size="16" /></button>
              </td>
            </tr>
          </tbody>
        </table>
        <McEmptyState v-else title="No items yet" hint="Import a CSV, scan barcodes, or add products manually." />
      </div>
    </McCard>
  </template>

  <!-- Batch list -->
  <template v-else>
    <div class="cb-toolbar">
      <McButton variant="primary" @click="showCreate = true">New batch</McButton>
    </div>

    <McCard>
      <div v-if="busy" class="cb-loading"><McSpinner /><span>Loading…</span></div>
      <table v-else-if="batches.length" class="mc-table">
        <thead>
          <tr>
            <th>Date</th>
            <th>Type</th>
            <th>Supplier</th>
            <th>Lines</th>
            <th>Status</th>
            <th></th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="b in batches" :key="b.id">
            <td class="cb-mono">{{ formatDate(b.createdAt) }}</td>
            <td><McBadge :variant="b.type === 'Receive' ? 'success' : 'warning'">{{ b.type }}</McBadge></td>
            <td>{{ b.supplierName }}</td>
            <td class="text-center">{{ b.lines.length }}</td>
            <td><McBadge :variant="b.status === 'Committed' ? 'success' : 'neutral'">{{ b.status }}</McBadge></td>
            <td>
              <McButton variant="secondary" dense @click="openBatch(b)">Open</McButton>
              <McButton v-if="b.status === 'Draft'" variant="secondary" dense style="margin-left: .25rem" @click="deleteBatch(b.id)">Delete</McButton>
            </td>
          </tr>
        </tbody>
      </table>
      <McEmptyState v-else title="No batches" hint="Create a new batch to receive or return consignment stock." />
    </McCard>
  </template>

  <!-- Create modal -->
  <McModal v-model="showCreate" title="New consignment batch">
    <McField label="Type" for-id="cb-type">
      <select id="cb-type" v-model="createType">
        <option value="Receive">Receive (check in)</option>
        <option value="Return">Return (send back)</option>
      </select>
    </McField>
    <McField label="Supplier" for-id="cb-supplier">
      <select id="cb-supplier" v-model="createSupplierId" required>
        <option value="" disabled>Select supplier…</option>
        <option v-for="s in suppliers" :key="s.id" :value="s.id">{{ s.name }}</option>
      </select>
    </McField>
    <McField label="Notes (optional)" for-id="cb-notes">
      <input id="cb-notes" v-model="createNotes" />
    </McField>
    <template #footer>
      <McButton variant="secondary" @click="showCreate = false">Cancel</McButton>
      <McButton variant="primary" :disabled="busy || !createSupplierId" @click="createBatch">Create</McButton>
    </template>
  </McModal>

  <!-- Import CSV modal -->
  <McModal v-model="showImport" title="Import CSV">
    <p style="margin-bottom: .75rem; font-size: .9rem; color: #666">
      CSV file with columns: SKU (or Code), Qty (or Quantity). One row per product.
    </p>
    <McField label="File" for-id="cb-file">
      <input id="cb-file" type="file" accept=".csv" @change="importFile = ($event.target as HTMLInputElement).files?.[0] ?? null" />
    </McField>
    <template #footer>
      <McButton variant="secondary" @click="showImport = false">Cancel</McButton>
      <McButton variant="primary" :disabled="busy || !importFile" @click="importCsv">
        <McSpinner v-if="busy" /><span v-else>Import</span>
      </McButton>
    </template>
  </McModal>

  <!-- Add product modal -->
  <McModal v-model="showAddProduct" title="Add product">
    <McField label="Search by SKU or name" for-id="cb-search">
      <input id="cb-search" v-model="addSearch" @input="searchProducts" placeholder="Type to search…" />
    </McField>
    <McField label="Quantity" for-id="cb-add-qty">
      <input id="cb-add-qty" v-model.number="addQty" type="number" min="1" step="1" />
    </McField>
    <div v-if="addBusy" class="cb-loading"><McSpinner /></div>
    <ul v-else-if="addResults.length" class="cb-search-results">
      <li v-for="p in addResults" :key="p.id" class="cb-search-item" @click="addProduct(p.id)">
        <strong>{{ p.sku }}</strong> — {{ p.name }}
      </li>
    </ul>
    <template #footer>
      <McButton variant="secondary" @click="showAddProduct = false">Close</McButton>
    </template>
  </McModal>
</template>

<style scoped>
.cb-toolbar { display: flex; gap: .75rem; margin-bottom: 1rem; }
.cb-batch-head { display: flex; justify-content: space-between; align-items: flex-start; flex-wrap: wrap; gap: .75rem; }
.cb-batch-title { margin: 0; font-size: 1.1rem; }
.cb-batch-meta { display: flex; align-items: center; gap: .5rem; font-size: .85rem; color: #777; margin-top: .25rem; }
.cb-batch-actions { display: flex; gap: .5rem; flex-wrap: wrap; }
.cb-notes { font-style: italic; }
.cb-mono { font-family: monospace; font-size: .85rem; }
.cb-loading { display: flex; align-items: center; gap: .5rem; padding: 2rem; justify-content: center; }

.cb-scanner-wrap { padding: 1rem 0; text-align: center; }
.cb-scan-result { margin-top: .5rem; font-weight: 600; color: #16a34a; }

.cb-progress { display: flex; align-items: center; gap: .75rem; padding: .75rem 0; }
.cb-progress-bar { flex: 1; height: 8px; background: #eee; border-radius: 4px; overflow: hidden; }
.cb-progress-fill { height: 100%; background: #F47A20; border-radius: 4px; transition: width .3s; }
.cb-progress-label { font-size: .85rem; color: #555; white-space: nowrap; }

.cb-table-wrap { overflow-x: auto; }
.cb-table { width: 100%; }
.cb-qty-input { width: 60px; text-align: center; padding: .25rem; border: 1px solid #ddd; border-radius: 4px; font-size: .9rem; }
.cb-remove-btn { display: inline-flex; align-items: center; justify-content: center; background: none; border: none; font-size: 1.2rem; color: #999; cursor: pointer; padding: .25rem .5rem; }
.cb-remove-btn:hover { color: #e11d48; }

.cb-row--done { background: #f0fdf4; }
.cb-row--partial { background: #fffbeb; }
.cb-row--extra { background: #fef2f2; }

.cb-search-results { list-style: none; padding: 0; margin: .5rem 0 0; max-height: 200px; overflow-y: auto; }
.cb-search-item { padding: .5rem .75rem; cursor: pointer; border-bottom: 1px solid #eee; font-size: .9rem; }
.cb-search-item:hover { background: #f5f5f5; }

.text-center { text-align: center; }
</style>
