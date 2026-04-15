<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { http } from '@/api/http'
import { useToast } from '@/composables/useToast'
import McPageHeader from '@/components/ui/McPageHeader.vue'
import McCard from '@/components/ui/McCard.vue'
import McButton from '@/components/ui/McButton.vue'
import McField from '@/components/ui/McField.vue'
import McAlert from '@/components/ui/McAlert.vue'
import McBadge from '@/components/ui/McBadge.vue'
import McModal from '@/components/ui/McModal.vue'

type Supplier = { id: string; name: string }
type Preset = { id: string; supplierId: string; name: string; mapping: Record<string, string | undefined> }
type PreviewRow = {
  rowIndex: number
  sku: string
  name: string
  manufacturer?: string | null
  itemType?: string | null
  cost: number
  sellPrice: number
  qtyOnHand: number
  error?: string | null
  warning?: string | null
}

const toast = useToast()
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

const showSupplierModal = ref(false)
const newSupplierName = ref('')
const showPresetModal = ref(false)
const newPresetName = ref('')

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
  toast.success(`Applied preset “${p.name}”`)
}

function openPresetModal() {
  newPresetName.value = ''
  showPresetModal.value = true
}

async function confirmSavePreset() {
  const name = newPresetName.value.trim()
  if (!name || !supplierId.value) return
  let mapping: Record<string, string | undefined>
  try {
    mapping = JSON.parse(mappingJson.value) as Record<string, string | undefined>
  } catch {
    err.value = 'Mapping JSON is invalid'
    toast.error('Mapping JSON is invalid')
    return
  }
  try {
    await http.post('/api/imports/presets', { supplierId: supplierId.value, name, mapping })
    await loadPresets()
    showPresetModal.value = false
    toast.success('Preset saved')
  } catch {
    toast.error('Could not save preset')
  }
}

function openSupplierModal() {
  newSupplierName.value = ''
  showSupplierModal.value = true
}

async function confirmAddSupplier() {
  const name = newSupplierName.value.trim()
  if (!name) return
  try {
    const { data } = await http.post<Supplier>('/api/suppliers', { name })
    suppliers.value.push(data)
    supplierId.value = data.id
    showSupplierModal.value = false
    await loadPresets()
    toast.success(`Supplier “${name}” added`)
  } catch {
    toast.error('Could not add supplier')
  }
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
    toast.success('Preview ready — review rows, then commit.')
  } catch {
    err.value = 'Huntex preview failed'
    toast.error('Huntex preview failed')
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
    const { data } = await http.post<{ imported: number }>('/api/imports/huntex', fd)
    toast.success(`Imported ${data.imported} rows`)
    huntexPreview.value = []
  } catch {
    err.value = 'Huntex import failed'
    toast.error('Huntex import failed')
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
    toast.success('Wholesaler preview ready')
  } catch {
    err.value = 'Wholesaler preview failed (check mapping JSON matches CSV headers or use column letters A,B,…)'
    toast.error('Wholesaler preview failed')
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
    const { data } = await http.post<{ imported: number }>('/api/imports/wholesaler', fd)
    toast.success(`Imported ${data.imported} rows`)
    wholesalerPreview.value = []
  } catch {
    err.value = 'Wholesaler import failed'
    toast.error('Wholesaler import failed')
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <div class="imp-page">
    <McPageHeader title="Stock import">
      <template #default>
        After importing, open <RouterLink to="/stock">Stock list</RouterLink> to verify. Use <strong>Preview</strong> before
        <strong>Commit</strong> so you can catch mapping issues.
      </template>
    </McPageHeader>

    <McAlert v-if="err" variant="error">{{ err }}</McAlert>

    <div class="imp-steps" aria-hidden="true">
      <McBadge variant="accent">1</McBadge>
      <span class="imp-steps__txt">Supplier &amp; presets</span>
      <span class="imp-steps__sep">→</span>
      <McBadge variant="neutral">2</McBadge>
      <span class="imp-steps__txt">File + preview</span>
      <span class="imp-steps__sep">→</span>
      <McBadge variant="neutral">3</McBadge>
      <span class="imp-steps__txt">Commit</span>
    </div>

    <McCard title="Supplier & mapping presets">
      <div class="imp-row">
        <div class="imp-grow">
          <McField label="Supplier" for-id="imp-supplier">
            <select id="imp-supplier" v-model="supplierId" @change="loadPresets">
              <option v-for="s in suppliers" :key="s.id" :value="s.id">{{ s.name }}</option>
            </select>
          </McField>
        </div>
        <McButton variant="secondary" type="button" @click="openSupplierModal">New supplier</McButton>
      </div>
      <div v-if="presets.length" class="imp-presets">
        <p class="imp-presets__label">Presets</p>
        <div class="imp-presets__btns">
          <McButton v-for="p in presets" :key="p.id" variant="secondary" type="button" @click="applyPreset(p)">
            {{ p.name }}
          </McButton>
          <McButton variant="secondary" type="button" @click="openPresetModal">Save mapping as preset…</McButton>
        </div>
      </div>
    </McCard>

    <McCard title="Huntex workbook or CSV">
      <p class="imp-lead">
        Upload <strong>.xlsx</strong> / <strong>.xlsm</strong> (sheet defaults to <code>huntex 2026</code>) or
        <strong>.csv</strong> with headers in row 1.
        <a class="imp-link" href="/api/imports/example-csv" download>Download example CSV</a>
      </p>
      <McField label="Sheet name (Excel only)" for-id="imp-sheet">
        <input id="imp-sheet" v-model="sheetName" />
      </McField>
      <input
        type="file"
        accept=".xlsx,.xlsm,.csv"
        class="imp-file"
        @change="huntexFile = ($event.target as HTMLInputElement).files?.[0] ?? null"
      />
      <div class="imp-actions">
        <McButton variant="secondary" type="button" :disabled="busy" @click="previewHuntex">Preview</McButton>
        <McButton variant="primary" type="button" :disabled="busy" @click="commitHuntex">Commit import</McButton>
      </div>
      <McAlert v-for="w in huntexWarnings" :key="w" variant="warning">{{ w }}</McAlert>
      <div v-if="huntexPreview.length" class="imp-table-wrap">
        <table class="mc-table">
          <thead>
            <tr>
              <th>#</th>
              <th>SKU</th>
              <th>Name</th>
              <th>Mfr</th>
              <th>Type</th>
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
              <td>{{ r.manufacturer }}</td>
              <td>{{ r.itemType }}</td>
              <td>{{ r.cost }}</td>
              <td :class="{ 'imp-warn': !!r.warning }">
                {{ r.sellPrice }}
                <span v-if="r.warning" :title="r.warning ?? ''" class="imp-warn-ic">⚠</span>
              </td>
              <td>{{ r.qtyOnHand }}</td>
              <td>{{ r.error }}</td>
            </tr>
          </tbody>
        </table>
      </div>
    </McCard>

    <McCard title="Wholesaler CSV / Excel">
      <p class="imp-lead">
        Column mapping JSON: CSV header names or Excel columns (<code>A</code>, <code>B</code>, …). Adjust to match your
        file.
      </p>
      <textarea v-model="mappingJson" class="imp-json" rows="8" spellcheck="false" />
      <input
        type="file"
        accept=".csv,.xlsx,.xlsm"
        class="imp-file"
        @change="wholesalerFile = ($event.target as HTMLInputElement).files?.[0] ?? null"
      />
      <div class="imp-actions">
        <McButton variant="secondary" type="button" :disabled="busy" @click="previewWholesaler">Preview</McButton>
        <McButton variant="primary" type="button" :disabled="busy" @click="commitWholesaler">Commit import</McButton>
      </div>
      <div v-if="wholesalerPreview.length" class="imp-table-wrap">
        <table class="mc-table">
          <thead>
            <tr>
              <th>#</th>
              <th>SKU</th>
              <th>Name</th>
              <th>Mfr</th>
              <th>Type</th>
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
              <td>{{ r.manufacturer }}</td>
              <td>{{ r.itemType }}</td>
              <td>{{ r.cost }}</td>
              <td :class="{ 'imp-warn': !!r.warning }">
                {{ r.sellPrice }}
                <span v-if="r.warning" :title="r.warning ?? ''" class="imp-warn-ic">⚠</span>
              </td>
              <td>{{ r.qtyOnHand }}</td>
              <td>{{ r.error }}</td>
            </tr>
          </tbody>
        </table>
      </div>
    </McCard>

    <McModal v-model="showSupplierModal" title="New supplier">
      <McField label="Supplier name" for-id="imp-new-sup">
        <input id="imp-new-sup" v-model="newSupplierName" type="text" autocomplete="off" @keyup.enter="confirmAddSupplier" />
      </McField>
      <template #footer>
        <McButton variant="secondary" type="button" @click="showSupplierModal = false">Cancel</McButton>
        <McButton variant="primary" type="button" :disabled="!newSupplierName.trim()" @click="confirmAddSupplier">
          Add
        </McButton>
      </template>
    </McModal>

    <McModal v-model="showPresetModal" title="Save mapping preset">
      <McField label="Preset name" for-id="imp-preset-name">
        <input id="imp-preset-name" v-model="newPresetName" type="text" autocomplete="off" @keyup.enter="confirmSavePreset" />
      </McField>
      <template #footer>
        <McButton variant="secondary" type="button" @click="showPresetModal = false">Cancel</McButton>
        <McButton variant="primary" type="button" :disabled="!newPresetName.trim()" @click="confirmSavePreset">
          Save
        </McButton>
      </template>
    </McModal>
  </div>
</template>

<style scoped>
.imp-page {
  min-height: 100%;
  max-width: 100%;
  overflow-x: clip;
}

.imp-steps {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.35rem 0.65rem;
  margin-bottom: 1.25rem;
  font-size: 0.85rem;
  color: var(--mc-app-text-muted, #5c5a56);
}

.imp-steps__sep {
  color: #d4d2cd;
}

.imp-row {
  display: flex;
  flex-wrap: wrap;
  gap: 0.75rem;
  align-items: flex-end;
}

.imp-grow {
  flex: 1 1 220px;
}

.imp-grow :deep(.mc-field) {
  margin-bottom: 0;
}

.imp-presets {
  margin-top: 1.25rem;
  padding-top: 1.25rem;
  border-top: 1px solid var(--mc-app-border-faint, #eceae5);
}

.imp-presets__label {
  margin: 0 0 0.5rem;
  font-size: 0.8rem;
  font-weight: 700;
  color: var(--mc-app-text-muted, #5c5a56);
  text-transform: uppercase;
  letter-spacing: 0.06em;
}

.imp-presets__btns {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
}

.imp-lead {
  margin: 0 0 1rem;
  color: var(--mc-app-text-muted, #5c5a56);
  line-height: 1.5;
}

.imp-link {
  font-weight: 600;
}

.imp-file {
  display: block;
  margin: 0.75rem 0;
  font-size: 0.9rem;
  max-width: 100%;
  overflow: hidden;
}

.imp-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
  margin-top: 0.75rem;
}

.imp-json {
  width: 100%;
  max-width: 100%;
  min-width: 0;
  font-family: ui-monospace, monospace;
  font-size: 0.85rem;
  padding: 0.85rem;
  border-radius: 10px;
  border: 1.5px solid var(--mc-app-border-subtle, #c8c5bd);
  box-sizing: border-box;
  background: var(--mc-app-surface, #fff);
  color: var(--mc-app-text, #1a1a1c);
  transition: border-color 0.15s ease, box-shadow 0.15s ease;
}

.imp-json:focus {
  outline: none;
  border-color: var(--mc-accent, #f47a20);
  box-shadow: inset 0 0 0 1px var(--mc-accent, #f47a20);
}

.imp-table-wrap {
  overflow-x: auto;
  margin-top: 1rem;
  -webkit-overflow-scrolling: touch;
}

.imp-warn {
  color: #b71c1c;
  font-weight: 600;
}

.imp-warn-ic {
  cursor: help;
  margin-left: 0.2rem;
}
</style>
