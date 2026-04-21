<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { http } from '@/api/http'
import { useToast } from '@/composables/useToast'
import { formatZAR } from '@/utils/format'
import McPageHeader from '@/components/ui/McPageHeader.vue'
import McCard from '@/components/ui/McCard.vue'
import McButton from '@/components/ui/McButton.vue'
import McField from '@/components/ui/McField.vue'
import McAlert from '@/components/ui/McAlert.vue'
import McEmptyState from '@/components/ui/McEmptyState.vue'
import McSpinner from '@/components/ui/McSpinner.vue'
import { Search, Plus, Trash2, Save, ArrowLeft } from 'lucide-vue-next'

type Product = {
  id: string
  sku: string
  name: string
  sellPrice: number
  cost?: number | null
  qtyOnHand: number
}

type Line = {
  productId: string | null
  sku: string | null
  itemName: string
  description: string | null
  quantity: number
  unitCost: number | null
  unitPrice: number
  discountPercent: number | null
  discountAmount: number | null
  sortOrder: number
}

type QuoteDto = {
  id: string
  quoteNumber: string
  status: string
  customerId?: string | null
  customerName?: string | null
  customerEmail?: string | null
  customerPhone?: string | null
  customerCompany?: string | null
  customerAddress?: string | null
  customerVatNumber?: string | null
  publicNotes?: string | null
  internalNotes?: string | null
  validUntil?: string | null
  discountTotal: number
  taxRate: number
  lines: Array<Line & { id: string; lineTotal: number; taxRate: number }>
}

const route = useRoute()
const router = useRouter()
const toast = useToast()

const editingId = computed(() => (route.params.id as string | undefined) || null)
const isEdit = computed(() => !!editingId.value)
const title = computed(() => (isEdit.value ? 'Edit quote' : 'New quote'))

const customerId = ref<string | null>(null)
const customerName = ref('')
const customerEmail = ref('')
const customerPhone = ref('')
const customerCompany = ref('')
const customerAddress = ref('')
const customerVatNumber = ref('')
const publicNotes = ref('')
const internalNotes = ref('')
const validUntil = ref<string>('')
const discountTotal = ref(0)
const taxRate = ref(15)

const lines = ref<Line[]>([])

const q = ref('')
const searchLoading = ref(false)
const results = ref<Product[]>([])

const busy = ref(false)
const loading = ref(false)
const err = ref<string | null>(null)

let searchTimer: ReturnType<typeof setTimeout> | null = null
watch(q, () => {
  if (searchTimer) clearTimeout(searchTimer)
  searchTimer = setTimeout(() => void runSearch(), 250)
})

async function runSearch() {
  const s = q.value.trim()
  if (!s) { results.value = []; return }
  searchLoading.value = true
  try {
    const { data } = await http.get<Product[]>('/api/products', { params: { q: s, take: 30 } })
    results.value = data
  } catch { results.value = [] } finally { searchLoading.value = false }
}

function addProduct(p: Product) {
  const existing = lines.value.find(l => l.productId === p.id)
  if (existing) { existing.quantity += 1; return }
  lines.value.push({
    productId: p.id,
    sku: p.sku,
    itemName: p.name,
    description: null,
    quantity: 1,
    unitCost: p.cost ?? null,
    unitPrice: p.sellPrice,
    discountPercent: null,
    discountAmount: null,
    sortOrder: lines.value.length
  })
  q.value = ''
  results.value = []
}

function addCustomLine() {
  lines.value.push({
    productId: null,
    sku: null,
    itemName: '',
    description: null,
    quantity: 1,
    unitCost: null,
    unitPrice: 0,
    discountPercent: null,
    discountAmount: null,
    sortOrder: lines.value.length
  })
}

function removeLine(idx: number) {
  lines.value.splice(idx, 1)
}

function lineTotal(l: Line): number {
  const gross = (l.unitPrice || 0) * (l.quantity || 0)
  const disc = l.discountAmount != null
    ? l.discountAmount
    : l.discountPercent != null
      ? Math.round(gross * (l.discountPercent / 100) * 100) / 100
      : 0
  return Math.max(0, Math.round((gross - disc) * 100) / 100)
}

const subTotal = computed(() => lines.value.reduce((s, l) => s + lineTotal(l), 0))
const afterDiscount = computed(() => Math.max(0, subTotal.value - (discountTotal.value || 0)))
const taxAmount = computed(() => {
  const r = taxRate.value || 0
  if (r <= 0) return 0
  return Math.round((afterDiscount.value - afterDiscount.value / (1 + r / 100)) * 100) / 100
})
const grandTotal = computed(() => afterDiscount.value)

async function lookupCustomer() {
  const email = customerEmail.value.trim()
  if (!email || email.length < 3) return
  try {
    const { data } = await http.get('/api/customers/by-email', { params: { email } })
    if (data) {
      customerName.value = data.name || customerName.value
      customerCompany.value = data.company || ''
      customerAddress.value = data.address || ''
      customerVatNumber.value = data.vatNumber || ''
      customerId.value = data.id || null
    }
  } catch { /* ignore */ }
}

async function loadExisting(id: string) {
  loading.value = true
  try {
    const { data } = await http.get<QuoteDto>(`/api/quotes/${id}`)
    customerId.value = data.customerId ?? null
    customerName.value = data.customerName ?? ''
    customerEmail.value = data.customerEmail ?? ''
    customerPhone.value = data.customerPhone ?? ''
    customerCompany.value = data.customerCompany ?? ''
    customerAddress.value = data.customerAddress ?? ''
    customerVatNumber.value = data.customerVatNumber ?? ''
    publicNotes.value = data.publicNotes ?? ''
    internalNotes.value = data.internalNotes ?? ''
    validUntil.value = data.validUntil ? data.validUntil.substring(0, 10) : ''
    discountTotal.value = data.discountTotal
    taxRate.value = data.taxRate
    lines.value = data.lines.map(l => ({
      productId: l.productId,
      sku: l.sku,
      itemName: l.itemName,
      description: l.description,
      quantity: l.quantity,
      unitCost: l.unitCost,
      unitPrice: l.unitPrice,
      discountPercent: l.discountPercent,
      discountAmount: l.discountAmount,
      sortOrder: l.sortOrder
    }))
  } catch {
    err.value = 'Could not load quote'
  } finally {
    loading.value = false
  }
}

function buildPayload() {
  return {
    customerId: customerId.value,
    customerName: customerName.value.trim() || null,
    customerEmail: customerEmail.value.trim() || null,
    customerPhone: customerPhone.value.trim() || null,
    customerCompany: customerCompany.value.trim() || null,
    customerAddress: customerAddress.value.trim() || null,
    customerVatNumber: customerVatNumber.value.trim() || null,
    publicNotes: publicNotes.value.trim() || null,
    internalNotes: internalNotes.value.trim() || null,
    validUntil: validUntil.value ? new Date(validUntil.value).toISOString() : null,
    discountTotal: discountTotal.value || 0,
    taxRate: taxRate.value,
    lines: lines.value.map((l, i) => ({
      productId: l.productId,
      sku: l.sku,
      itemName: l.itemName,
      description: l.description,
      quantity: l.quantity,
      unitCost: l.unitCost,
      unitPrice: l.unitPrice,
      discountPercent: l.discountPercent,
      discountAmount: l.discountAmount,
      sortOrder: i
    }))
  }
}

async function save() {
  if (!lines.value.length) {
    err.value = 'Add at least one line before saving.'
    return
  }
  if (lines.value.some(l => !l.itemName.trim())) {
    err.value = 'Every line needs a name.'
    return
  }
  err.value = null
  busy.value = true
  try {
    const payload = buildPayload()
    const { data } = isEdit.value
      ? await http.put<QuoteDto>(`/api/quotes/${editingId.value}`, payload)
      : await http.post<QuoteDto>('/api/quotes', payload)
    toast.success(isEdit.value ? 'Quote updated' : `Quote ${data.quoteNumber} created`)
    router.push(`/quotes/${data.id}`)
  } catch (e: unknown) {
    const msg = (e as { response?: { data?: { error?: string } } })?.response?.data?.error
    err.value = msg || 'Could not save quote'
  } finally {
    busy.value = false
  }
}

onMounted(() => {
  if (editingId.value) void loadExisting(editingId.value)
})
</script>

<template>
  <div>
    <McPageHeader :title="title">
      <template #default>
        <span>Build a quote by searching for stock items or adding custom lines.</span>
      </template>
      <template #actions>
        <McButton variant="ghost" @click="router.push('/quotes')">
          <ArrowLeft :size="16" /> Back
        </McButton>
        <McButton variant="primary" :disabled="busy || loading" @click="save">
          <Save :size="16" /> {{ isEdit ? 'Save changes' : 'Create quote' }}
        </McButton>
      </template>
    </McPageHeader>

    <McAlert v-if="err" variant="error">{{ err }}</McAlert>

    <div class="q-edit-grid">
      <div class="q-edit-main">
        <McCard title="Line items">
          <div class="q-search-row">
            <div class="q-search-input">
              <Search :size="16" class="q-search-icon" />
              <input v-model="q" type="search" placeholder="Search stock by name, SKU or barcode…" />
              <McSpinner v-if="searchLoading" />
            </div>
            <McButton variant="secondary" dense @click="addCustomLine">
              <Plus :size="14" /> Custom line
            </McButton>
          </div>

          <div v-if="results.length" class="q-search-results">
            <button
              v-for="p in results"
              :key="p.id"
              type="button"
              class="q-search-result"
              @click="addProduct(p)"
            >
              <span class="q-search-result__name">{{ p.name }}</span>
              <span class="q-search-result__meta">
                <span>SKU: {{ p.sku }}</span>
                <span>Stock: {{ p.qtyOnHand }}</span>
                <span class="q-search-result__price">{{ formatZAR(p.sellPrice) }}</span>
              </span>
            </button>
          </div>

          <McEmptyState
            v-if="!lines.length"
            title="No lines yet"
            hint="Search for stock above, or add a custom item."
          />

          <div v-if="lines.length" class="q-lines">
            <div v-for="(l, i) in lines" :key="i" class="q-line">
              <div class="q-line__main">
                <McField label="Item">
                  <input v-model="l.itemName" type="text" placeholder="Item name" />
                </McField>
                <McField label="Description (optional)">
                  <textarea v-model="l.description" rows="1" placeholder="Extra detail shown on the quote" />
                </McField>
              </div>
              <div class="q-line__grid">
                <McField label="Qty">
                  <input v-model.number="l.quantity" type="number" min="1" />
                </McField>
                <McField label="Unit price">
                  <input v-model.number="l.unitPrice" type="number" min="0" step="0.01" />
                </McField>
                <McField label="Disc %">
                  <input v-model.number="l.discountPercent" type="number" min="0" step="0.01" placeholder="—" />
                </McField>
                <McField label="Disc (R)">
                  <input v-model.number="l.discountAmount" type="number" min="0" step="0.01" placeholder="—" />
                </McField>
                <McField label="Total">
                  <input :value="formatZAR(lineTotal(l))" type="text" readonly />
                </McField>
                <div class="q-line__remove">
                  <McButton variant="ghost" dense @click="removeLine(i)"><Trash2 :size="14" /></McButton>
                </div>
              </div>
            </div>
          </div>
        </McCard>

        <McCard title="Notes">
          <McField label="Public notes" hint="Shown on the PDF and public quote view.">
            <textarea v-model="publicNotes" rows="3" placeholder="Payment terms, turnaround times, exclusions…" />
          </McField>
          <McField label="Internal notes" hint="Only visible to your team.">
            <textarea v-model="internalNotes" rows="2" placeholder="Reminders, supplier info, etc." />
          </McField>
        </McCard>
      </div>

      <aside class="q-edit-aside">
        <McCard title="Customer">
          <McField label="Email">
            <input
              v-model="customerEmail"
              type="email"
              placeholder="customer@example.com"
              @blur="lookupCustomer"
            />
          </McField>
          <McField label="Name">
            <input v-model="customerName" type="text" placeholder="Contact name" />
          </McField>
          <McField label="Company">
            <input v-model="customerCompany" type="text" placeholder="Company name (optional)" />
          </McField>
          <McField label="Phone">
            <input v-model="customerPhone" type="tel" placeholder="Phone" />
          </McField>
          <McField label="Address">
            <textarea v-model="customerAddress" rows="2" placeholder="Street address" />
          </McField>
          <McField label="VAT number">
            <input v-model="customerVatNumber" type="text" placeholder="VAT number (optional)" />
          </McField>
        </McCard>

        <McCard title="Quote details">
          <McField label="Valid until">
            <input v-model="validUntil" type="date" />
          </McField>
          <McField label="Tax rate (%)">
            <input v-model.number="taxRate" type="number" min="0" step="0.01" />
          </McField>
          <McField label="Order discount (R)">
            <input v-model.number="discountTotal" type="number" min="0" step="0.01" />
          </McField>
        </McCard>

        <McCard title="Totals">
          <dl class="q-totals">
            <div><dt>Subtotal</dt><dd>{{ formatZAR(subTotal) }}</dd></div>
            <div v-if="discountTotal > 0"><dt>Discount</dt><dd>-{{ formatZAR(discountTotal) }}</dd></div>
            <div v-if="taxAmount > 0"><dt>VAT ({{ taxRate }}%)</dt><dd>{{ formatZAR(taxAmount) }}</dd></div>
            <div class="q-totals__grand">
              <dt>Grand total</dt><dd>{{ formatZAR(grandTotal) }}</dd>
            </div>
          </dl>
        </McCard>
      </aside>
    </div>
  </div>
</template>

<style scoped>
.q-edit-grid {
  display: grid;
  grid-template-columns: 1fr minmax(300px, 360px);
  gap: 1.25rem;
}
@media (max-width: 1000px) {
  .q-edit-grid { grid-template-columns: 1fr; }
}
.q-edit-main, .q-edit-aside {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}
.q-search-row {
  display: flex;
  gap: 0.5rem;
  align-items: center;
  margin-bottom: 0.75rem;
}
.q-search-input {
  flex: 1;
  position: relative;
  display: flex;
  align-items: center;
  gap: 0.5rem;
}
.q-search-icon {
  position: absolute;
  left: 0.75rem;
  top: 50%;
  transform: translateY(-50%);
  pointer-events: none;
  color: var(--mc-app-text-muted, #5c5a56);
}
.q-search-input input {
  flex: 1;
  padding-left: 2.25rem !important;
}
.q-search-results {
  border: 1px solid var(--mc-app-border-faint, #eceae5);
  border-radius: 10px;
  overflow: hidden;
  margin-bottom: 1rem;
  max-height: 280px;
  overflow-y: auto;
}
.q-search-result {
  display: flex;
  flex-direction: column;
  gap: 0.2rem;
  width: 100%;
  padding: 0.6rem 0.85rem;
  text-align: left;
  background: #fff;
  border: 0;
  border-bottom: 1px solid var(--mc-app-border-faint, #eceae5);
  cursor: pointer;
  font: inherit;
}
.q-search-result:last-child { border-bottom: 0; }
.q-search-result:hover { background: var(--mc-app-surface-muted, #f5f3ef); }
.q-search-result__name { font-weight: 600; color: var(--mc-app-text, #1a1a1c); }
.q-search-result__meta {
  display: flex;
  gap: 0.85rem;
  font-size: 0.8rem;
  color: var(--mc-app-text-muted, #5c5a56);
}
.q-search-result__price { margin-left: auto; font-weight: 600; }

.q-lines { display: flex; flex-direction: column; gap: 0.75rem; }
.q-line {
  border: 1px solid var(--mc-app-border-faint, #eceae5);
  border-radius: 10px;
  padding: 0.85rem;
  background: var(--mc-app-surface, #fff);
}
.q-line__main { display: grid; grid-template-columns: 1fr 1fr; gap: 0.75rem; }
@media (max-width: 780px) {
  .q-line__main { grid-template-columns: 1fr; }
}
.q-line__grid {
  display: grid;
  grid-template-columns: repeat(5, 1fr) auto;
  gap: 0.6rem;
  align-items: end;
  margin-top: 0.5rem;
}
@media (max-width: 780px) {
  .q-line__grid {
    grid-template-columns: repeat(2, 1fr) auto;
  }
}
.q-line__remove { padding-bottom: 0.15rem; }

.q-totals { margin: 0; display: grid; gap: 0.5rem; }
.q-totals > div {
  display: flex;
  justify-content: space-between;
  font-size: 0.95rem;
}
.q-totals dt { margin: 0; color: var(--mc-app-text-muted, #5c5a56); }
.q-totals dd { margin: 0; font-variant-numeric: tabular-nums; }
.q-totals__grand {
  padding-top: 0.5rem;
  border-top: 1px solid var(--mc-app-border-faint, #eceae5);
  font-weight: 700;
  font-size: 1.05rem;
  color: var(--mc-app-text, #1a1a1c);
}
.q-totals__grand dd { font-size: 1.2rem; }
</style>
