<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
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
const route = useRoute()
const router = useRouter()

type BatchType = 'Receive' | 'Return' | 'OwnedReceive'
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
  unitCost?: number | null
  currentProductCost: number
  unitCostChanged: boolean
}
type Batch = {
  id: string
  supplierId: string
  supplierName: string
  type: BatchType
  status: string
  notes?: string | null
  createdBy?: string | null
  createdAt: string
  committedAt?: string | null
  sourceDocumentRef?: string | null
  hasSourceDocument?: boolean
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
const createType = ref<BatchType>('Receive')
const createSupplierId = ref('')
const createNotes = ref('')
const createSourceDocRef = ref('')

/* scan */
const scanOpen = ref(false)
const lastScanResult = ref<string | null>(null)

/* import */
const showImport = ref(false)
const importFile = ref<File | null>(null)

/* pdf import */
const showPdfImport = ref(false)
const pdfFile = ref<File | null>(null)
const pdfPreview = ref<{ parsed: number; notFound: string[]; unparsedLines: string[]; rawText?: string | null } | null>(null)

/* product search (manual add) */
const showAddProduct = ref(false)
const addSearch = ref('')
const addResults = ref<{ id: string; sku: string; name: string }[]>([])
const addQty = ref(1)
const addUnitCost = ref<number | null>(null)
const addBusy = ref(false)

/* inline create product (when scan/pdf returns not-found) */
const showInlineCreate = ref(false)
const inlineSku = ref('')
const inlineName = ref('')
const inlineBarcode = ref('')
const inlineCost = ref<number | null>(null)
const inlineQty = ref(1)

/* returnable stock for return mode */
const returnableStock = ref<ReturnableLine[]>([])

/* commit confirmation */
const showCommit = ref(false)
const commitUpdateCatalog = ref(false)

/* ── Lifecycle ── */
onMounted(async () => {
  await Promise.all([loadSuppliers(), loadBatches()])
  const qType = route.query.type
  if (typeof qType === 'string' && ['Receive', 'Return', 'OwnedReceive'].includes(qType)) {
    createType.value = qType as BatchType
    showCreate.value = true
    void router.replace({ query: {} })
  }
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
  if (!createSupplierId.value) { err.value = 'Select a wholesaler.'; return }
  err.value = null
  busy.value = true
  try {
    const { data } = await http.post<Batch>('/api/consignment-batches', {
      type: createType.value,
      supplierId: createSupplierId.value,
      notes: createNotes.value || null,
      sourceDocumentRef: createSourceDocRef.value || null
    })
    showCreate.value = false
    activeBatch.value = data

    if (createType.value === 'Return') {
      await loadReturnableStock(createSupplierId.value, data.id)
    }
    createSupplierId.value = ''
    createNotes.value = ''
    createSourceDocRef.value = ''
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

async function updateLineUnitCost(lineId: string, value: number) {
  if (!activeBatch.value) return
  try {
    const { data } = await http.put<Batch>(`/api/consignment-batches/${activeBatch.value.id}/lines/${lineId}`, {
      unitCost: Math.max(0, value)
    })
    activeBatch.value = data
  } catch (e: unknown) {
    handleErr(e)
  }
}

/* ── Scanning ── */
async function onScan(code: string) {
  if (!activeBatch.value) return
  scanOpen.value = false
  const trimmed = code.trim()
  lastScanResult.value = null
  try {
    const { data } = await http.post<Batch>(`/api/consignment-batches/${activeBatch.value.id}/scan`, {
      barcode: trimmed,
      qty: 1
    })
    activeBatch.value = data
    const found = data.lines.find(l => l.barcode === trimmed || l.sku === trimmed)
    lastScanResult.value = found ? `${found.productName} — checked: ${found.checkedQty}` : 'Scanned'
    toast.success(lastScanResult.value)
  } catch (e: unknown) {
    const ax = e as { response?: { status?: number; data?: { error?: string; barcode?: string } } }
    if (ax.response?.status === 404) {
      inlineSku.value = ax.response?.data?.barcode ?? trimmed
      inlineName.value = ''
      inlineBarcode.value = inlineSku.value
      inlineCost.value = null
      inlineQty.value = 1
      showInlineCreate.value = true
      toast.warning('Product not found — fill in details to create it.')
    } else {
      handleErr(e)
      lastScanResult.value = 'Not found'
    }
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
    activeBatch.value = (data as { batch: Batch }).batch
    const nf = (data as { notFound?: string[] }).notFound ?? []
    if (nf.length) toast.warning(`${nf.length} SKUs not found: ${nf.slice(0, 5).join(', ')}${nf.length > 5 ? '…' : ''}`)
    else toast.success(`${(data as { added?: number }).added ?? 0} lines imported`)
    showImport.value = false
    importFile.value = null
  } catch (e: unknown) {
    handleErr(e)
  } finally {
    busy.value = false
  }
}

/* ── Import PDF ── */
async function importPdf() {
  if (!activeBatch.value || !pdfFile.value) return
  const form = new FormData()
  form.append('file', pdfFile.value)
  busy.value = true
  try {
    const { data } = await http.post<{ batch: Batch; parsed: number; notFound: string[]; unparsedLines: string[]; rawText?: string | null }>(
      `/api/consignment-batches/${activeBatch.value.id}/import-pdf`, form)
    activeBatch.value = data.batch
    pdfPreview.value = { parsed: data.parsed, notFound: data.notFound, unparsedLines: data.unparsedLines, rawText: data.rawText }
    toast.success(`${data.parsed} lines imported from PDF`)
  } catch (e: unknown) {
    handleErr(e)
  } finally {
    busy.value = false
  }
}

function pdfImportCreateForSku(sku: string) {
  inlineSku.value = sku
  inlineName.value = ''
  inlineBarcode.value = sku
  inlineCost.value = null
  inlineQty.value = 1
  showInlineCreate.value = true
}

/* ── Manual add product ── */
async function searchProducts() {
  if (addSearch.value.length < 2) return
  addBusy.value = true
  try {
    const { data } = await http.get<Array<{ id: string; sku: string; name: string }>>('/api/products', { params: { q: addSearch.value, take: 20 } })
    addResults.value = data.map((p) => ({ id: p.id, sku: p.sku, name: p.name }))
  } catch { /* ignore */ } finally { addBusy.value = false }
}

async function addProduct(productId: string) {
  if (!activeBatch.value) return
  try {
    const body: { productId: string; expectedQty: number; unitCost?: number } = {
      productId,
      expectedQty: addQty.value
    }
    if (isOwnedReceive.value && addUnitCost.value !== null && addUnitCost.value >= 0) {
      body.unitCost = addUnitCost.value
    }
    const { data } = await http.post<Batch>(`/api/consignment-batches/${activeBatch.value.id}/lines`, body)
    activeBatch.value = data
    toast.success('Product added')
    showAddProduct.value = false
    addSearch.value = ''
    addResults.value = []
    addQty.value = 1
    addUnitCost.value = null
  } catch (e: unknown) {
    handleErr(e)
  }
}

async function createInlineProduct() {
  if (!activeBatch.value) return
  if (!inlineSku.value.trim() || !inlineName.value.trim()) {
    toast.error('SKU and name are required.')
    return
  }
  try {
    const { data } = await http.post<Batch>(
      `/api/consignment-batches/${activeBatch.value.id}/lines-inline-create`,
      {
        sku: inlineSku.value.trim(),
        name: inlineName.value.trim(),
        barcode: inlineBarcode.value.trim() || null,
        unitCost: inlineCost.value ?? null,
        qty: inlineQty.value
      }
    )
    activeBatch.value = data
    toast.success(`Created ${inlineSku.value} and added to batch.`)
    showInlineCreate.value = false
  } catch (e: unknown) {
    handleErr(e)
  }
}

/* ── Commit ── */
const lines = computed(() => activeBatch.value?.lines ?? [])
const changedCostLinesCount = computed(() => lines.value.filter(l => l.unitCost != null && l.unitCost > 0 && l.unitCost !== l.currentProductCost).length)

function openCommit() {
  commitUpdateCatalog.value = changedCostLinesCount.value > 0
  showCommit.value = true
}

async function commitBatch() {
  if (!activeBatch.value) return
  busy.value = true
  try {
    const { data } = await http.post<Batch>(
      `/api/consignment-batches/${activeBatch.value.id}/commit`,
      null,
      { params: { updateCosts: commitUpdateCatalog.value } }
    )
    activeBatch.value = data
    toast.success('Batch committed — stock updated')
    showCommit.value = false
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

async function downloadSourcePdf() {
  if (!activeBatch.value) return
  try {
    const { data } = await http.get(`/api/consignment-batches/${activeBatch.value.id}/source-document`, { responseType: 'blob' })
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

function typeLabel(t: BatchType): string {
  if (t === 'Receive') return 'Consignment in'
  if (t === 'Return') return 'Consignment return'
  return 'Owned stock in'
}

function typeVariant(t: BatchType): 'success' | 'warning' | 'error' | 'info' | 'neutral' {
  if (t === 'Receive') return 'info'
  if (t === 'Return') return 'warning'
  return 'success'
}

const totalExpected = computed(() => activeBatch.value?.lines.reduce((s, l) => s + l.expectedQty, 0) ?? 0)
const totalChecked = computed(() => activeBatch.value?.lines.reduce((s, l) => s + l.checkedQty, 0) ?? 0)
const progressPct = computed(() => totalExpected.value > 0 ? Math.min(100, Math.round(totalChecked.value / totalExpected.value * 100)) : 0)
const isDraft = computed(() => activeBatch.value?.status === 'Draft')
const isReceive = computed(() => activeBatch.value?.type === 'Receive' || activeBatch.value?.type === 'OwnedReceive')
const isOwnedReceive = computed(() => activeBatch.value?.type === 'OwnedReceive')

function formatDate(iso: string) {
  return new Date(iso).toLocaleString('en-ZA', { dateStyle: 'short', timeStyle: 'short' })
}

function money(n: number | null | undefined): string {
  if (n == null) return '—'
  return new Intl.NumberFormat('en-ZA', { style: 'currency', currency: 'ZAR', minimumFractionDigits: 2 }).format(n)
}
</script>

<template>
  <McPageHeader title="Stock Batches" subtitle="Receive, return, and process owned stock from wholesalers" />

  <McAlert v-if="err" variant="error" style="margin-bottom: 1rem">{{ err }}</McAlert>

  <!-- Active batch view -->
  <template v-if="activeBatch">
    <McCard>
      <template #header>
        <div class="cb-batch-head">
          <div>
            <h3 class="cb-batch-title">
              {{ isOwnedReceive ? 'Owned stock from' : (activeBatch.type === 'Receive' ? 'Receive from' : 'Return to') }}
              {{ activeBatch.supplierName }}
            </h3>
            <div class="cb-batch-meta">
              <McBadge :variant="activeBatch.status === 'Committed' ? 'success' : 'warning'">{{ activeBatch.status }}</McBadge>
              <McBadge :variant="typeVariant(activeBatch.type)">{{ typeLabel(activeBatch.type) }}</McBadge>
              <span>{{ formatDate(activeBatch.createdAt) }}</span>
              <span v-if="activeBatch.sourceDocumentRef" class="cb-notes">Ref: {{ activeBatch.sourceDocumentRef }}</span>
              <span v-if="activeBatch.notes" class="cb-notes">{{ activeBatch.notes }}</span>
            </div>
          </div>
          <div class="cb-batch-actions">
            <McButton variant="secondary" dense @click="activeBatch = null; loadBatches()">Back</McButton>
            <McButton variant="secondary" dense @click="downloadPdf">Checklist PDF</McButton>
            <McButton v-if="activeBatch.hasSourceDocument" variant="secondary" dense @click="downloadSourcePdf">Source PDF</McButton>
            <template v-if="isDraft">
              <template v-if="isReceive">
                <McButton variant="secondary" dense @click="showImport = true">Import CSV</McButton>
                <McButton v-if="isOwnedReceive" variant="secondary" dense @click="showPdfImport = true; pdfPreview = null">Import PDF</McButton>
                <McButton variant="secondary" dense @click="showAddProduct = true">+ Add product</McButton>
                <McButton variant="primary" dense @click="scanOpen = !scanOpen">
                  {{ scanOpen ? 'Stop scan' : 'Scan barcode' }}
                </McButton>
              </template>
              <template v-else>
                <McButton variant="secondary" dense @click="showAddProduct = true">+ Add product</McButton>
              </template>
              <McButton variant="primary" dense :disabled="busy || !activeBatch.lines.length" @click="openCommit">
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
              <th v-if="isOwnedReceive" class="text-right">Unit cost</th>
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
              <td v-if="isOwnedReceive" class="text-right">
                <template v-if="isDraft">
                  <input type="number" class="cb-cost-input" :value="l.unitCost ?? ''" min="0" step="0.01"
                    :placeholder="money(l.currentProductCost)"
                    @change="updateLineUnitCost(l.id, +($event.target as HTMLInputElement).value)" />
                  <span v-if="l.unitCost != null && l.currentProductCost > 0 && l.unitCost !== l.currentProductCost" class="cb-cost-change" :title="`Current catalog cost: ${money(l.currentProductCost)}`">Δ</span>
                </template>
                <template v-else>{{ money(l.unitCost) }}</template>
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
        <McEmptyState v-else title="No items yet" hint="Import a CSV or PDF, scan barcodes, or add products manually." />
      </div>
    </McCard>
  </template>

  <!-- Batch list -->
  <template v-else>
    <div class="cb-toolbar">
      <McButton variant="primary" @click="createType = 'OwnedReceive'; showCreate = true">Receive stock (owned)</McButton>
      <McButton variant="secondary" @click="createType = 'Receive'; showCreate = true">Consignment receive</McButton>
      <McButton variant="secondary" @click="createType = 'Return'; showCreate = true">Consignment return</McButton>
    </div>

    <McCard>
      <div v-if="busy" class="cb-loading"><McSpinner /><span>Loading…</span></div>
      <table v-else-if="batches.length" class="mc-table">
        <thead>
          <tr>
            <th>Date</th>
            <th>Type</th>
            <th>Wholesaler</th>
            <th>Ref</th>
            <th>Lines</th>
            <th>Status</th>
            <th></th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="b in batches" :key="b.id">
            <td class="cb-mono">{{ formatDate(b.createdAt) }}</td>
            <td><McBadge :variant="typeVariant(b.type)">{{ typeLabel(b.type) }}</McBadge></td>
            <td>{{ b.supplierName }}</td>
            <td class="cb-mono">{{ b.sourceDocumentRef ?? '—' }}</td>
            <td class="text-center">{{ b.lines.length }}</td>
            <td><McBadge :variant="b.status === 'Committed' ? 'success' : 'neutral'">{{ b.status }}</McBadge></td>
            <td>
              <McButton variant="secondary" dense @click="openBatch(b)">Open</McButton>
              <McButton v-if="b.status === 'Draft'" variant="secondary" dense style="margin-left: .25rem" @click="deleteBatch(b.id)">Delete</McButton>
            </td>
          </tr>
        </tbody>
      </table>
      <McEmptyState v-else title="No batches" hint="Create a new batch to receive or return stock." />
    </McCard>
  </template>

  <!-- Create modal -->
  <McModal v-model="showCreate" title="New stock batch">
    <McField label="Type" for-id="cb-type">
      <select id="cb-type" v-model="createType">
        <option value="OwnedReceive">Receive owned stock (I bought this from wholesaler)</option>
        <option value="Receive">Consignment receive (wholesaler's stock on loan)</option>
        <option value="Return">Consignment return (send back to wholesaler)</option>
      </select>
    </McField>
    <McField label="Wholesaler" for-id="cb-supplier">
      <select id="cb-supplier" v-model="createSupplierId" required>
        <option value="" disabled>Select wholesaler…</option>
        <option v-for="s in suppliers" :key="s.id" :value="s.id">{{ s.name }}</option>
      </select>
    </McField>
    <McField v-if="createType === 'OwnedReceive'" label="Supplier invoice / delivery note ref (optional)" for-id="cb-src-ref">
      <input id="cb-src-ref" v-model="createSourceDocRef" placeholder="e.g. INV-12345" />
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
      CSV with columns: SKU (or Code), Qty (or Quantity). Optional: Cost / Unit Cost / Price (for owned stock).
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

  <!-- Import PDF modal -->
  <McModal v-model="showPdfImport" title="Import supplier invoice PDF">
    <p style="margin-bottom: .75rem; font-size: .9rem; color: #666">
      Upload the supplier's delivery note or invoice PDF. We'll extract SKU, qty, and unit cost where possible.
      Any SKUs not in the catalog will be listed below so you can create them.
    </p>
    <McField label="PDF file" for-id="cb-pdf-file">
      <input id="cb-pdf-file" type="file" accept="application/pdf" @change="pdfFile = ($event.target as HTMLInputElement).files?.[0] ?? null" />
    </McField>

    <div v-if="pdfPreview" class="cb-pdf-preview">
      <p><strong>{{ pdfPreview.parsed }}</strong> lines imported.</p>
      <div v-if="pdfPreview.notFound.length">
        <p style="margin-top: .5rem"><strong>{{ pdfPreview.notFound.length }}</strong> SKUs not in catalog:</p>
        <ul class="cb-nf-list">
          <li v-for="sku in pdfPreview.notFound" :key="sku">
            <span class="cb-mono">{{ sku }}</span>
            <McButton variant="secondary" dense style="margin-left: .5rem" @click="pdfImportCreateForSku(sku)">Create product</McButton>
          </li>
        </ul>
      </div>
      <details v-if="pdfPreview.unparsedLines.length" style="margin-top: .75rem">
        <summary>{{ pdfPreview.unparsedLines.length }} lines couldn't be parsed (raw)</summary>
        <pre class="cb-raw">{{ pdfPreview.unparsedLines.join('\n') }}</pre>
      </details>
      <details v-if="pdfPreview.rawText" style="margin-top: .5rem">
        <summary>Raw extracted text</summary>
        <pre class="cb-raw">{{ pdfPreview.rawText }}</pre>
      </details>
    </div>

    <template #footer>
      <McButton variant="secondary" @click="showPdfImport = false; pdfFile = null; pdfPreview = null">Close</McButton>
      <McButton variant="primary" :disabled="busy || !pdfFile" @click="importPdf">
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
    <McField v-if="isOwnedReceive" label="Unit cost (optional)" for-id="cb-add-cost">
      <input id="cb-add-cost" v-model.number="addUnitCost" type="number" min="0" step="0.01" placeholder="e.g. 49.50" />
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

  <!-- Inline create product modal -->
  <McModal v-model="showInlineCreate" title="Create new product">
    <p style="font-size: .9rem; color: #666; margin-bottom: .5rem">
      This product isn't in the catalog yet. Enter the minimum details to create it and add it to this batch.
      Supplier will be set to <strong>{{ activeBatch?.supplierName }}</strong>.
    </p>
    <McField label="SKU" for-id="cb-inline-sku">
      <input id="cb-inline-sku" v-model="inlineSku" required />
    </McField>
    <McField label="Product name" for-id="cb-inline-name">
      <input id="cb-inline-name" v-model="inlineName" required placeholder="e.g. Red Widget 500ml" />
    </McField>
    <McField label="Barcode (defaults to SKU if blank)" for-id="cb-inline-barcode">
      <input id="cb-inline-barcode" v-model="inlineBarcode" />
    </McField>
    <McField label="Unit cost (optional)" for-id="cb-inline-cost">
      <input id="cb-inline-cost" v-model.number="inlineCost" type="number" min="0" step="0.01" />
    </McField>
    <McField label="Quantity on this batch" for-id="cb-inline-qty">
      <input id="cb-inline-qty" v-model.number="inlineQty" type="number" min="1" step="1" />
    </McField>
    <template #footer>
      <McButton variant="secondary" @click="showInlineCreate = false">Cancel</McButton>
      <McButton variant="primary" :disabled="busy || !inlineSku.trim() || !inlineName.trim()" @click="createInlineProduct">Create and add</McButton>
    </template>
  </McModal>

  <!-- Commit confirmation modal -->
  <McModal v-model="showCommit" title="Commit batch">
    <p v-if="activeBatch">
      Commit this
      <strong>{{ typeLabel(activeBatch.type) }}</strong>
      batch? Stock levels will be updated and the batch will become read-only.
    </p>
    <ul class="cb-commit-summary">
      <li><strong>{{ activeBatch?.lines.length }}</strong> line(s)</li>
      <li><strong>{{ totalChecked }}</strong> units {{ activeBatch?.type === 'Return' ? 'returning' : 'receiving' }}</li>
    </ul>
    <div v-if="isOwnedReceive && changedCostLinesCount > 0" class="cb-cost-update">
      <p><strong>{{ changedCostLinesCount }}</strong> product(s) have a different unit cost from the catalog.</p>
      <label class="cb-checkbox">
        <input type="checkbox" v-model="commitUpdateCatalog" />
        Update catalog cost to the new price
      </label>
    </div>
    <template #footer>
      <McButton variant="secondary" :disabled="busy" @click="showCommit = false">Cancel</McButton>
      <McButton variant="primary" :disabled="busy" @click="commitBatch">
        <McSpinner v-if="busy" /><span v-else>Commit</span>
      </McButton>
    </template>
  </McModal>
</template>

<style scoped>
.cb-toolbar { display: flex; gap: .75rem; margin-bottom: 1rem; flex-wrap: wrap; }
.cb-batch-head { display: flex; justify-content: space-between; align-items: flex-start; flex-wrap: wrap; gap: .75rem; }
.cb-batch-title { margin: 0; font-size: 1.1rem; }
.cb-batch-meta { display: flex; align-items: center; gap: .5rem; font-size: .85rem; color: #777; margin-top: .25rem; flex-wrap: wrap; }
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
.cb-cost-input { width: 90px; text-align: right; padding: .25rem; border: 1px solid #ddd; border-radius: 4px; font-size: .9rem; }
.cb-cost-change { margin-left: .25rem; color: #F47A20; font-weight: 700; cursor: help; }
.cb-remove-btn { display: inline-flex; align-items: center; justify-content: center; background: none; border: none; font-size: 1.2rem; color: #999; cursor: pointer; padding: .25rem .5rem; }
.cb-remove-btn:hover { color: #e11d48; }

.cb-row--done { background: #f0fdf4; }
.cb-row--partial { background: #fffbeb; }
.cb-row--extra { background: #fef2f2; }

.cb-search-results { list-style: none; padding: 0; margin: .5rem 0 0; max-height: 200px; overflow-y: auto; }
.cb-search-item { padding: .5rem .75rem; cursor: pointer; border-bottom: 1px solid #eee; font-size: .9rem; }
.cb-search-item:hover { background: #f5f5f5; }

.cb-commit-summary { margin: .75rem 0 1rem; padding: .5rem 1rem; background: #f9fafb; border-radius: 6px; }
.cb-cost-update { margin-top: .75rem; padding: .75rem; background: #fff7ed; border-left: 3px solid #F47A20; border-radius: 4px; }
.cb-checkbox { display: flex; gap: .5rem; align-items: center; margin-top: .5rem; cursor: pointer; }

.cb-pdf-preview { margin-top: 1rem; padding: .75rem; background: #f9fafb; border-radius: 6px; font-size: .9rem; }
.cb-nf-list { list-style: none; padding: 0; margin: .25rem 0; max-height: 200px; overflow-y: auto; }
.cb-nf-list li { padding: .25rem 0; display: flex; align-items: center; }
.cb-raw { background: #0f172a; color: #e2e8f0; padding: .5rem; border-radius: 4px; font-size: .75rem; max-height: 200px; overflow: auto; white-space: pre-wrap; }

.text-center { text-align: center; }
.text-right { text-align: right; }
</style>
