<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { http } from '@/api/http'
import { useToast } from '@/composables/useToast'
import { formatZAR } from '@/utils/format'
import McPageHeader from '@/components/ui/McPageHeader.vue'
import McCard from '@/components/ui/McCard.vue'
import McButton from '@/components/ui/McButton.vue'
import McAlert from '@/components/ui/McAlert.vue'
import McBadge from '@/components/ui/McBadge.vue'
import McSpinner from '@/components/ui/McSpinner.vue'
import McModal from '@/components/ui/McModal.vue'
import McField from '@/components/ui/McField.vue'
import { ArrowLeft, Edit, FileText, Send, Check, X, CheckCircle2, Copy, RefreshCw, Trash2 } from 'lucide-vue-next'

type QuoteLine = {
  id: string
  productId: string | null
  sku: string | null
  itemName: string
  description: string | null
  quantity: number
  unitPrice: number
  discountAmount: number | null
  lineTotal: number
}

type Quote = {
  id: string
  quoteNumber: string
  status: string
  customerName: string | null
  customerEmail: string | null
  customerPhone: string | null
  customerCompany: string | null
  customerAddress: string | null
  customerVatNumber: string | null
  subTotal: number
  discountTotal: number
  taxRate: number
  taxAmount: number
  grandTotal: number
  publicNotes: string | null
  internalNotes: string | null
  validUntil: string | null
  publicToken: string
  publicUrl: string
  pdfUrl: string | null
  convertedInvoiceId: string | null
  convertedAt: string | null
  createdAt: string
  lines: QuoteLine[]
}

const route = useRoute()
const router = useRouter()
const toast = useToast()

const id = computed(() => route.params.id as string)
const quote = ref<Quote | null>(null)
const err = ref<string | null>(null)
const busy = ref(false)

const showConvert = ref(false)
const convertPayment = ref('Cash')
const convertBusy = ref(false)

async function load() {
  busy.value = true
  err.value = null
  try {
    const { data } = await http.get<Quote>(`/api/quotes/${id.value}`)
    quote.value = data
  } catch {
    err.value = 'Could not load quote'
  } finally {
    busy.value = false
  }
}

async function setStatus(status: string) {
  if (!quote.value) return
  try {
    const { data } = await http.post<Quote>(`/api/quotes/${quote.value.id}/status`, { status })
    quote.value = data
    toast.success(`Marked as ${status}`)
  } catch (e: unknown) {
    const msg = (e as { response?: { data?: { error?: string } } })?.response?.data?.error
    toast.error(msg || 'Could not update status')
  }
}

async function convert() {
  if (!quote.value) return
  convertBusy.value = true
  try {
    const { data } = await http.post(`/api/quotes/${quote.value.id}/convert`, {
      paymentMethod: convertPayment.value
    })
    showConvert.value = false
    toast.success(`Quote converted to invoice ${data.invoice.invoiceNumber}`)
    if (data.warning) toast.info(data.warning)
    await load()
  } catch (e: unknown) {
    const msg = (e as { response?: { data?: { error?: string } } })?.response?.data?.error
    toast.error(msg || 'Could not convert quote')
  } finally {
    convertBusy.value = false
  }
}

async function remove() {
  if (!quote.value) return
  if (!confirm(`Delete quote ${quote.value.quoteNumber}? This cannot be undone.`)) return
  try {
    await http.delete(`/api/quotes/${quote.value.id}`)
    toast.success('Quote deleted')
    router.push('/quotes')
  } catch (e: unknown) {
    const msg = (e as { response?: { data?: { error?: string } } })?.response?.data?.error
    toast.error(msg || 'Could not delete')
  }
}

function openPdf() {
  if (!quote.value) return
  window.open(`/api/quotes/${quote.value.id}/pdf`, '_blank')
}

async function copyPublicLink() {
  if (!quote.value) return
  try {
    await navigator.clipboard.writeText(quote.value.publicUrl)
    toast.success('Public link copied')
  } catch {
    toast.error('Could not copy link')
  }
}

function badgeVariant(status: string): 'success' | 'accent' | 'danger' | 'neutral' | 'warning' {
  switch (status) {
    case 'Accepted':
    case 'Converted': return 'success'
    case 'Sent': return 'accent'
    case 'Rejected':
    case 'Expired': return 'danger'
    case 'Draft': return 'warning'
    default: return 'neutral'
  }
}

function formatDate(iso: string | null) {
  if (!iso) return '—'
  return new Date(iso).toLocaleDateString('en-ZA', { day: 'numeric', month: 'short', year: 'numeric' })
}

const canEdit = computed(() => quote.value && quote.value.status !== 'Converted')
const canConvert = computed(() => quote.value && quote.value.status !== 'Converted')

onMounted(() => void load())
</script>

<template>
  <div>
    <McPageHeader :title="quote ? `Quote ${quote.quoteNumber}` : 'Quote'">
      <template #default>
        <span v-if="quote">Created {{ formatDate(quote.createdAt) }}</span>
      </template>
      <template #actions>
        <McButton variant="ghost" @click="router.push('/quotes')">
          <ArrowLeft :size="16" /> Back
        </McButton>
        <McButton v-if="canEdit" variant="secondary" @click="router.push(`/quotes/${id}/edit`)">
          <Edit :size="16" /> Edit
        </McButton>
        <McButton variant="secondary" @click="openPdf">
          <FileText :size="16" /> PDF
        </McButton>
        <McButton v-if="canConvert" variant="primary" @click="showConvert = true">
          <CheckCircle2 :size="16" /> Convert to invoice
        </McButton>
      </template>
    </McPageHeader>

    <McAlert v-if="err" variant="error">{{ err }}</McAlert>
    <McSpinner v-if="busy && !quote" />

    <template v-if="quote">
      <McCard>
        <div class="q-detail-top">
          <div class="q-detail-top__status">
            <McBadge :variant="badgeVariant(quote.status)">{{ quote.status }}</McBadge>
            <span v-if="quote.validUntil" class="q-detail-top__valid">
              Valid until {{ formatDate(quote.validUntil) }}
            </span>
          </div>
          <div class="q-detail-top__actions">
            <McButton v-if="quote.status === 'Draft'" variant="secondary" dense @click="setStatus('Sent')">
              <Send :size="14" /> Mark as sent
            </McButton>
            <McButton v-if="quote.status === 'Sent'" variant="secondary" dense @click="setStatus('Accepted')">
              <Check :size="14" /> Mark accepted
            </McButton>
            <McButton v-if="quote.status === 'Sent'" variant="ghost" dense @click="setStatus('Rejected')">
              <X :size="14" /> Mark rejected
            </McButton>
            <McButton v-if="quote.status !== 'Draft' && quote.status !== 'Converted'" variant="ghost" dense @click="setStatus('Draft')">
              <RefreshCw :size="14" /> Back to draft
            </McButton>
          </div>
        </div>
      </McCard>

      <div class="q-detail-grid">
        <div class="q-detail-main">
          <McCard title="Items">
            <table class="mc-table">
              <thead>
                <tr>
                  <th>Item</th>
                  <th class="q-num">Qty</th>
                  <th class="q-num">Unit</th>
                  <th class="q-num">Total</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="l in quote.lines" :key="l.id">
                  <td>
                    <div class="q-line-name">{{ l.itemName }}</div>
                    <div v-if="l.sku" class="q-line-sub">SKU: {{ l.sku }}</div>
                    <div v-if="l.description" class="q-line-sub">{{ l.description }}</div>
                    <div v-if="l.discountAmount && l.discountAmount > 0" class="q-line-disc">
                      Discount: -{{ formatZAR(l.discountAmount) }}
                    </div>
                  </td>
                  <td class="q-num">{{ l.quantity }}</td>
                  <td class="q-num">{{ formatZAR(l.unitPrice) }}</td>
                  <td class="q-num">{{ formatZAR(l.lineTotal) }}</td>
                </tr>
              </tbody>
            </table>
          </McCard>

          <McCard v-if="quote.publicNotes || quote.internalNotes" title="Notes">
            <div v-if="quote.publicNotes" class="q-notes">
              <h3>Customer-facing notes</h3>
              <p>{{ quote.publicNotes }}</p>
            </div>
            <div v-if="quote.internalNotes" class="q-notes">
              <h3>Internal notes</h3>
              <p>{{ quote.internalNotes }}</p>
            </div>
          </McCard>
        </div>

        <aside class="q-detail-aside">
          <McCard title="Customer">
            <dl class="q-kv">
              <div v-if="quote.customerCompany"><dt>Company</dt><dd>{{ quote.customerCompany }}</dd></div>
              <div v-if="quote.customerName"><dt>Contact</dt><dd>{{ quote.customerName }}</dd></div>
              <div v-if="quote.customerEmail"><dt>Email</dt><dd>{{ quote.customerEmail }}</dd></div>
              <div v-if="quote.customerPhone"><dt>Phone</dt><dd>{{ quote.customerPhone }}</dd></div>
              <div v-if="quote.customerAddress"><dt>Address</dt><dd class="q-multiline">{{ quote.customerAddress }}</dd></div>
              <div v-if="quote.customerVatNumber"><dt>VAT</dt><dd>{{ quote.customerVatNumber }}</dd></div>
            </dl>
            <McAlert v-if="!quote.customerName && !quote.customerCompany" variant="info">
              No customer details captured.
            </McAlert>
          </McCard>

          <McCard title="Totals">
            <dl class="q-totals">
              <div><dt>Subtotal</dt><dd>{{ formatZAR(quote.subTotal) }}</dd></div>
              <div v-if="quote.discountTotal > 0"><dt>Discount</dt><dd>-{{ formatZAR(quote.discountTotal) }}</dd></div>
              <div v-if="quote.taxAmount > 0"><dt>VAT ({{ quote.taxRate }}%)</dt><dd>{{ formatZAR(quote.taxAmount) }}</dd></div>
              <div class="q-totals__grand">
                <dt>Grand total</dt><dd>{{ formatZAR(quote.grandTotal) }}</dd>
              </div>
            </dl>
          </McCard>

          <McCard title="Share">
            <p class="q-share-hint">Anyone with this link can view the quote and download the PDF.</p>
            <McField label="Public link">
              <input :value="quote.publicUrl" readonly type="text" />
            </McField>
            <McButton variant="secondary" dense block @click="copyPublicLink">
              <Copy :size="14" /> Copy link
            </McButton>
          </McCard>

          <McCard v-if="quote.convertedInvoiceId" title="Converted">
            <p>This quote was converted to an invoice on {{ formatDate(quote.convertedAt) }}.</p>
          </McCard>

          <McCard v-else title="Danger zone">
            <McButton variant="danger" dense block @click="remove">
              <Trash2 :size="14" /> Delete quote
            </McButton>
          </McCard>
        </aside>
      </div>
    </template>

    <McModal v-model="showConvert" title="Convert quote to invoice">
      <p class="q-convert-info">
        This will create a new invoice from this quote. Stock levels will be adjusted as
        with any normal sale.
      </p>
      <McField label="Payment method">
        <select v-model="convertPayment">
          <option>Cash</option>
          <option>Card</option>
          <option>EFT</option>
          <option>Account</option>
        </select>
      </McField>
      <template #footer>
        <McButton variant="ghost" @click="showConvert = false">Cancel</McButton>
        <McButton variant="primary" :disabled="convertBusy" @click="convert">
          <CheckCircle2 :size="14" /> Convert
        </McButton>
      </template>
    </McModal>
  </div>
</template>

<style scoped>
.q-detail-top {
  display: flex;
  flex-wrap: wrap;
  gap: 0.75rem;
  align-items: center;
  justify-content: space-between;
}
.q-detail-top__status {
  display: flex;
  gap: 0.75rem;
  align-items: center;
}
.q-detail-top__valid {
  font-size: 0.85rem;
  color: var(--mc-app-text-muted, #5c5a56);
}
.q-detail-top__actions {
  display: flex;
  gap: 0.4rem;
  flex-wrap: wrap;
}

.q-detail-grid {
  display: grid;
  grid-template-columns: 1fr minmax(300px, 360px);
  gap: 1.25rem;
  margin-top: 1rem;
}
@media (max-width: 1000px) {
  .q-detail-grid { grid-template-columns: 1fr; }
}
.q-detail-main, .q-detail-aside {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.q-num { text-align: right; font-variant-numeric: tabular-nums; white-space: nowrap; }
.q-line-name { font-weight: 600; }
.q-line-sub { font-size: 0.8rem; color: var(--mc-app-text-muted, #5c5a56); }
.q-line-disc { font-size: 0.8rem; color: #c0392b; }

.q-notes { margin-bottom: 0.75rem; }
.q-notes h3 {
  margin: 0 0 0.25rem;
  font-size: 0.78rem;
  text-transform: uppercase;
  letter-spacing: 0.06em;
  color: var(--mc-app-text-muted, #7a7874);
}
.q-notes p { margin: 0; white-space: pre-line; }

.q-kv { margin: 0; display: grid; gap: 0.4rem; }
.q-kv > div { display: grid; grid-template-columns: 80px 1fr; gap: 0.5rem; font-size: 0.9rem; }
.q-kv dt { margin: 0; color: var(--mc-app-text-muted, #5c5a56); }
.q-kv dd { margin: 0; }
.q-multiline { white-space: pre-line; }

.q-totals { margin: 0; display: grid; gap: 0.5rem; }
.q-totals > div { display: flex; justify-content: space-between; font-size: 0.95rem; }
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

.q-share-hint { margin: 0 0 0.75rem; font-size: 0.85rem; color: var(--mc-app-text-muted, #5c5a56); }
.q-convert-info { margin: 0 0 0.75rem; font-size: 0.9rem; color: var(--mc-app-text-muted, #5c5a56); }
</style>
