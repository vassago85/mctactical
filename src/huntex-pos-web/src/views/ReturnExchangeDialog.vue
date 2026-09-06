<script setup lang="ts">
/**
 * Till-side exchange / return dialog. Opened from Find sale: staff pick lines to
 * bring back, optionally scan a replacement, and the till settles only the net
 * difference. Original invoice stays Final; the server tracks partial returns
 * per line via <c>InvoiceLine.ReturnedQuantity</c> and links any replacement
 * sale via a SaleReturn record so reports and audit trails match up.
 *
 * Deliberately narrow scope: no line-level discount override, no promo tweak.
 * Anything more nuanced can be rung up as a fresh sale after the exchange.
 */
import { computed, nextTick, onMounted, ref, watch } from 'vue'
import { http } from '@/api/http'
import { useToast } from '@/composables/useToast'
import { formatZAR } from '@/utils/format'
import McCard from '@/components/ui/McCard.vue'
import McButton from '@/components/ui/McButton.vue'
import McField from '@/components/ui/McField.vue'
import McAlert from '@/components/ui/McAlert.vue'
import McSpinner from '@/components/ui/McSpinner.vue'
import McBadge from '@/components/ui/McBadge.vue'
import { Minus, Plus, Trash2, Search } from 'lucide-vue-next'

type ExchangeResult = {
  saleReturnId: string
  saleReturnPublicToken: string
  originalInvoiceId: string
  originalInvoiceNumber: string
  creditTotal: number
  netSettlement: number
  exchangeInvoice: {
    id: string
    invoiceNumber: string
    publicToken: string
    grandTotal: number
    amountPaid: number
  } | null
}

const props = defineProps<{
  invoiceId: string
  invoiceNumber?: string
}>()

const emit = defineEmits<{
  close: []
  done: [result: ExchangeResult]
}>()

type InvoiceLine = {
  id: string
  productId: string
  description: string
  sku?: string | null
  quantity: number
  unitPrice: number
  originalUnitPrice: number
  lineDiscount: number
  lineTotal: number
  returnedQuantity: number
}

type InvoiceDto = {
  id: string
  invoiceNumber: string
  customerName?: string | null
  customerEmail?: string | null
  paymentMethod: string
  grandTotal: number
  lines: InvoiceLine[]
}

type Product = {
  id: string
  sku: string
  barcode?: string | null
  name: string
  sellPrice: number
  qtyOnHand: number
}

type NewLine = {
  productId: string
  sku: string
  name: string
  sellPrice: number
  qty: number
  qtyOnHand: number
}

const toast = useToast()

const loading = ref(true)
const busy = ref(false)
const err = ref<string | null>(null)

const invoice = ref<InvoiceDto | null>(null)

/** invoiceLineId -> qty coming back. Zero (or missing) means "not returning this line". */
const returnQty = ref<Record<string, number>>({})

const productQuery = ref('')
const productResults = ref<Product[]>([])
const searchLoading = ref(false)
const productSearchInput = ref<HTMLInputElement | null>(null)

const newLines = ref<NewLine[]>([])

const reason = ref('')
const paymentMethod = ref<'Card' | 'Cash' | 'EFT'>('Card')

async function loadInvoice() {
  loading.value = true
  err.value = null
  try {
    const { data } = await http.get<InvoiceDto>(`/api/invoices/${props.invoiceId}`)
    invoice.value = data
    // Only allow tender the customer already used, plus the standard three so
    // staff can refund to a different method if needed.
    const seen = (data.paymentMethod || 'Card').trim()
    if (seen === 'Cash' || seen === 'EFT' || seen === 'Card') paymentMethod.value = seen
  } catch (e: unknown) {
    const ax = e as { response?: { data?: { error?: string } }; message?: string }
    err.value = ax.response?.data?.error ?? ax.message ?? 'Could not load invoice'
  } finally {
    loading.value = false
  }
}

onMounted(loadInvoice)

function effectiveUnitPrice(l: InvoiceLine): number {
  return l.quantity > 0 ? Math.round((l.lineTotal / l.quantity) * 100) / 100 : l.unitPrice
}

function remaining(l: InvoiceLine): number {
  return Math.max(0, l.quantity - l.returnedQuantity)
}

function currentReturnQty(l: InvoiceLine): number {
  return returnQty.value[l.id] ?? 0
}

function bumpReturn(l: InvoiceLine, delta: number) {
  const next = Math.max(0, Math.min(remaining(l), currentReturnQty(l) + delta))
  returnQty.value = { ...returnQty.value, [l.id]: next }
}

function returnAll(l: InvoiceLine) {
  returnQty.value = { ...returnQty.value, [l.id]: remaining(l) }
}

const creditTotal = computed(() => {
  if (!invoice.value) return 0
  const total = invoice.value.lines.reduce((sum, l) => {
    const q = currentReturnQty(l)
    if (q <= 0) return sum
    return sum + effectiveUnitPrice(l) * q
  }, 0)
  return Math.round(total * 100) / 100
})

const anyReturned = computed(() => creditTotal.value > 0)

const newSaleTotal = computed(() => {
  const total = newLines.value.reduce((sum, l) => sum + l.sellPrice * l.qty, 0)
  return Math.round(total * 100) / 100
})

/** Positive: customer owes the difference. Negative: they get a refund. Zero: even swap. */
const netSettlement = computed(() =>
  Math.round((newSaleTotal.value - creditTotal.value) * 100) / 100,
)

const netLabel = computed(() => {
  if (netSettlement.value > 0) return `Collect from customer`
  if (netSettlement.value < 0) return `Refund to customer`
  return `Even swap`
})

const netVariant = computed<'ok' | 'due' | 'refund'>(() => {
  if (netSettlement.value > 0) return 'due'
  if (netSettlement.value < 0) return 'refund'
  return 'ok'
})

const canConfirm = computed(() =>
  anyReturned.value && reason.value.trim().length >= 3 && !busy.value,
)

let searchTimer: ReturnType<typeof setTimeout> | null = null
watch(productQuery, () => {
  if (searchTimer) clearTimeout(searchTimer)
  const term = productQuery.value.trim()
  if (term.length < 2) {
    productResults.value = []
    return
  }
  searchTimer = setTimeout(async () => {
    searchLoading.value = true
    try {
      const { data } = await http.get<Product[]>('/api/products', {
        params: { q: term, take: 12 },
      })
      productResults.value = data
    } catch {
      productResults.value = []
    } finally {
      searchLoading.value = false
    }
  }, 200)
})

function addProduct(p: Product) {
  const existing = newLines.value.find((l) => l.productId === p.id)
  if (existing) {
    existing.qty += 1
  } else {
    newLines.value.push({
      productId: p.id,
      sku: p.sku,
      name: p.name,
      sellPrice: p.sellPrice,
      qty: 1,
      qtyOnHand: p.qtyOnHand,
    })
  }
  productQuery.value = ''
  productResults.value = []
  void nextTick(() => productSearchInput.value?.focus())
}

function bumpNew(l: NewLine, delta: number) {
  const next = l.qty + delta
  if (next < 1) return
  l.qty = next
}

function removeNew(idx: number) {
  newLines.value.splice(idx, 1)
}

async function submit() {
  if (!canConfirm.value || !invoice.value) return
  busy.value = true
  err.value = null
  try {
    const returnLinesPayload = invoice.value.lines
      .map((l) => ({ invoiceLineId: l.id, quantity: currentReturnQty(l) }))
      .filter((x) => x.quantity > 0)

    const newLinesPayload = newLines.value.map((l) => ({
      productId: l.productId,
      quantity: l.qty,
      originalUnitPrice: l.sellPrice,
      lineDiscount: 0,
    }))

    const body = {
      reason: reason.value.trim(),
      returnLines: returnLinesPayload,
      newLines: newLinesPayload,
      discountTotal: 0,
      paymentMethod: paymentMethod.value,
      sendEmail: false,
    }

    const { data } = await http.post<ExchangeResult>(
      `/api/invoices/${props.invoiceId}/exchange`,
      body,
    )
    toast.success(
      data.netSettlement > 0
        ? `Collected ${formatZAR(data.netSettlement)}`
        : data.netSettlement < 0
          ? `Refunded ${formatZAR(-data.netSettlement)}`
          : 'Exchange complete',
    )
    emit('done', data)
  } catch (e: unknown) {
    const ax = e as { response?: { data?: { error?: string } }; message?: string }
    err.value = ax.response?.data?.error ?? ax.message ?? 'Exchange failed'
    toast.error(err.value)
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <div class="rx-overlay" @click.self="emit('close')">
    <McCard class="rx-dialog" :title="`Return / exchange — ${invoice?.invoiceNumber ?? invoiceNumber ?? ''}`">
      <McAlert v-if="err" variant="error">{{ err }}</McAlert>

      <div v-if="loading" class="rx-center"><McSpinner /></div>

      <template v-else-if="invoice">
        <section class="rx-section">
          <header class="rx-section__head">
            <h3>Bring back</h3>
            <p class="rx-section__hint">
              Pick the qty coming back per line. Credit is what the customer actually paid,
              including any discount at the time.
            </p>
          </header>

          <ul class="rx-lines">
            <li v-for="l in invoice.lines" :key="l.id" class="rx-line" :class="{ 'rx-line--done': remaining(l) === 0 }">
              <div class="rx-line__main">
                <div class="rx-line__title">
                  <strong>{{ l.description }}</strong>
                  <span v-if="l.sku" class="rx-line__sku">{{ l.sku }}</span>
                </div>
                <div class="rx-line__paid">
                  <span class="rx-line__paid-label">Paid each</span>
                  <strong>{{ formatZAR(effectiveUnitPrice(l)) }}</strong>
                </div>
              </div>
              <div class="rx-line__ctrl">
                <div class="rx-line__meta">
                  <McBadge v-if="l.returnedQuantity > 0" variant="warning">
                    {{ l.returnedQuantity }} already returned
                  </McBadge>
                  <span v-if="remaining(l) > 0">
                    {{ remaining(l) }} of {{ l.quantity }} still returnable
                  </span>
                  <span v-else>Fully returned</span>
                </div>
                <div class="rx-line__stepper">
                  <McButton
                    variant="secondary"
                    dense
                    type="button"
                    :disabled="currentReturnQty(l) === 0"
                    :aria-label="`Decrease return qty for ${l.description}`"
                    @click="bumpReturn(l, -1)"
                  ><Minus :size="14" /></McButton>
                  <span class="rx-line__qty">{{ currentReturnQty(l) }}</span>
                  <McButton
                    variant="secondary"
                    dense
                    type="button"
                    :disabled="currentReturnQty(l) >= remaining(l)"
                    :aria-label="`Increase return qty for ${l.description}`"
                    @click="bumpReturn(l, 1)"
                  ><Plus :size="14" /></McButton>
                  <McButton
                    v-if="remaining(l) > 1 && currentReturnQty(l) < remaining(l)"
                    variant="ghost"
                    dense
                    type="button"
                    @click="returnAll(l)"
                  >All {{ remaining(l) }}</McButton>
                </div>
              </div>
            </li>
          </ul>
        </section>

        <section class="rx-section">
          <header class="rx-section__head">
            <h3>Replace with</h3>
            <p class="rx-section__hint">
              Optional. Scan or search a product to swap in. Leave empty for a refund only.
            </p>
          </header>

          <div class="rx-search">
            <div class="rx-search__input">
              <Search :size="16" class="rx-search__icon" />
              <input
                ref="productSearchInput"
                v-model="productQuery"
                type="search"
                placeholder="Scan barcode or type SKU / item name…"
                autocomplete="off"
              />
              <McSpinner v-if="searchLoading" />
            </div>
            <ul v-if="productResults.length" class="rx-search__results">
              <li v-for="p in productResults" :key="p.id">
                <button type="button" class="rx-search__hit" @click="addProduct(p)">
                  <span class="rx-search__hit-name">{{ p.name }}</span>
                  <span class="rx-search__hit-sku">{{ p.sku }}</span>
                  <span class="rx-search__hit-price">{{ formatZAR(p.sellPrice) }}</span>
                  <span
                    class="rx-search__hit-stock"
                    :class="{ 'rx-search__hit-stock--low': p.qtyOnHand <= 0 }"
                  >{{ p.qtyOnHand > 0 ? `${p.qtyOnHand} in stock` : 'Out of stock' }}</span>
                </button>
              </li>
            </ul>
          </div>

          <ul v-if="newLines.length" class="rx-newlines">
            <li v-for="(l, idx) in newLines" :key="l.productId" class="rx-newline">
              <div class="rx-newline__main">
                <strong>{{ l.name }}</strong>
                <span class="rx-newline__sku">{{ l.sku }} · {{ formatZAR(l.sellPrice) }} each</span>
              </div>
              <div class="rx-newline__ctrl">
                <McButton
                  variant="secondary"
                  dense
                  type="button"
                  :disabled="l.qty <= 1"
                  :aria-label="`Decrease qty for ${l.name}`"
                  @click="bumpNew(l, -1)"
                ><Minus :size="14" /></McButton>
                <span class="rx-line__qty">{{ l.qty }}</span>
                <McButton
                  variant="secondary"
                  dense
                  type="button"
                  :aria-label="`Increase qty for ${l.name}`"
                  @click="bumpNew(l, 1)"
                ><Plus :size="14" /></McButton>
                <McButton
                  variant="ghost"
                  dense
                  type="button"
                  :aria-label="`Remove ${l.name}`"
                  @click="removeNew(idx)"
                ><Trash2 :size="14" /></McButton>
              </div>
            </li>
          </ul>
        </section>

        <section class="rx-section">
          <div class="rx-form-row">
            <McField label="Reason" for-id="rx-reason">
              <textarea
                id="rx-reason"
                v-model="reason"
                rows="2"
                maxlength="500"
                placeholder="e.g. Didn't fit, wrong colour, warranty swap…"
              ></textarea>
            </McField>
            <McField label="Tender for the difference" for-id="rx-tender">
              <select id="rx-tender" v-model="paymentMethod">
                <option value="Card">Card</option>
                <option value="Cash">Cash</option>
                <option value="EFT">EFT</option>
              </select>
            </McField>
          </div>
        </section>

        <section class="rx-summary" :data-variant="netVariant">
          <div class="rx-summary__row">
            <span>Return credit</span>
            <strong>{{ formatZAR(creditTotal) }}</strong>
          </div>
          <div class="rx-summary__row">
            <span>New sale</span>
            <strong>{{ formatZAR(newSaleTotal) }}</strong>
          </div>
          <div class="rx-summary__row rx-summary__row--net">
            <span>{{ netLabel }}</span>
            <strong>{{ formatZAR(Math.abs(netSettlement)) }}</strong>
          </div>
        </section>

        <div class="rx-actions">
          <McButton variant="secondary" type="button" :disabled="busy" @click="emit('close')">Cancel</McButton>
          <McButton
            variant="primary"
            type="button"
            :disabled="!canConfirm"
            @click="submit"
          >
            <McSpinner v-if="busy" />
            <span v-else>Confirm exchange</span>
          </McButton>
        </div>
      </template>
    </McCard>
  </div>
</template>

<style scoped>
.rx-overlay {
  position: fixed;
  inset: 0;
  z-index: 999;
  background: rgba(0, 0, 0, 0.5);
  display: flex;
  align-items: flex-start;
  justify-content: center;
  padding: 2rem 1rem;
  overflow-y: auto;
}

.rx-dialog {
  max-width: 720px;
  width: 100%;
}

.rx-center {
  display: flex;
  justify-content: center;
  padding: 2rem;
}

.rx-section {
  padding: 1rem 0;
  border-top: 1px solid var(--mc-app-border-faint, #eceae5);
}

.rx-section:first-of-type {
  border-top: none;
}

.rx-section__head {
  margin-bottom: 0.75rem;
}

.rx-section__head h3 {
  margin: 0 0 0.25rem;
  font-size: 0.95rem;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.04em;
  color: var(--mc-app-heading, #0a0a0c);
}

.rx-section__hint {
  margin: 0;
  font-size: 0.82rem;
  color: var(--mc-app-text-muted, #5c5a56);
}

.rx-lines {
  list-style: none;
  margin: 0;
  padding: 0;
  border: 1px solid var(--mc-app-border-soft, #ddd9d3);
  border-radius: 12px;
  overflow: hidden;
  background: var(--mc-app-surface, #fff);
}

.rx-line {
  padding: 0.75rem 0.9rem;
  border-bottom: 1px solid var(--mc-app-border-faint, #eceae5);
  display: flex;
  flex-direction: column;
  gap: 0.55rem;
}

.rx-line:last-child {
  border-bottom: none;
}

.rx-line--done {
  opacity: 0.55;
}

.rx-line__main {
  display: flex;
  gap: 1rem;
  align-items: flex-start;
  justify-content: space-between;
}

.rx-line__title {
  display: flex;
  flex-direction: column;
  gap: 0.15rem;
}

.rx-line__sku {
  font-size: 0.78rem;
  color: var(--mc-app-text-muted, #5c5a56);
  font-family: var(--mc-app-mono, ui-monospace, SFMono-Regular, Menlo, monospace);
}

.rx-line__paid {
  text-align: right;
}

.rx-line__paid-label {
  display: block;
  font-size: 0.7rem;
  letter-spacing: 0.04em;
  text-transform: uppercase;
  color: var(--mc-app-text-muted, #5c5a56);
}

.rx-line__ctrl {
  display: flex;
  flex-wrap: wrap;
  gap: 0.65rem;
  align-items: center;
  justify-content: space-between;
}

.rx-line__meta {
  display: flex;
  gap: 0.5rem;
  align-items: center;
  font-size: 0.8rem;
  color: var(--mc-app-text-muted, #5c5a56);
}

.rx-line__stepper {
  display: inline-flex;
  align-items: center;
  gap: 0.4rem;
}

.rx-line__qty {
  min-width: 2rem;
  text-align: center;
  font-weight: 700;
  font-variant-numeric: tabular-nums;
}

.rx-search {
  position: relative;
}

.rx-search__input {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  padding: 0 0.75rem;
  border: 1px solid var(--mc-app-border-soft, #ddd9d3);
  border-radius: 10px;
  background: var(--mc-app-surface, #fff);
}

.rx-search__input input {
  flex: 1 1 auto;
  border: none;
  outline: none;
  padding: 0.7rem 0;
  background: transparent;
  font-size: 1rem;
  color: inherit;
}

.rx-search__icon {
  color: var(--mc-app-text-muted, #5c5a56);
}

.rx-search__results {
  list-style: none;
  margin: 0.35rem 0 0;
  padding: 0;
  border: 1px solid var(--mc-app-border-soft, #ddd9d3);
  border-radius: 10px;
  overflow: hidden;
  background: var(--mc-app-surface, #fff);
}

.rx-search__hit {
  display: grid;
  grid-template-columns: 1fr auto auto auto;
  gap: 0.75rem;
  align-items: center;
  width: 100%;
  padding: 0.6rem 0.75rem;
  border: none;
  background: none;
  cursor: pointer;
  text-align: left;
  font: inherit;
  color: inherit;
}

.rx-search__hit:hover {
  background: var(--mc-app-bg, #f6f4f0);
}

.rx-search__hit + .rx-search__hit,
.rx-search__results li + li .rx-search__hit {
  border-top: 1px solid var(--mc-app-border-faint, #eceae5);
}

.rx-search__hit-sku {
  font-family: var(--mc-app-mono, ui-monospace, SFMono-Regular, Menlo, monospace);
  font-size: 0.8rem;
  color: var(--mc-app-text-muted, #5c5a56);
}

.rx-search__hit-price {
  font-weight: 700;
  font-variant-numeric: tabular-nums;
}

.rx-search__hit-stock {
  font-size: 0.75rem;
  color: var(--mc-app-text-muted, #5c5a56);
}

.rx-search__hit-stock--low {
  color: #b42318;
}

.rx-newlines {
  list-style: none;
  margin: 0.75rem 0 0;
  padding: 0;
  border: 1px solid var(--mc-app-border-soft, #ddd9d3);
  border-radius: 12px;
  overflow: hidden;
  background: var(--mc-app-surface, #fff);
}

.rx-newline {
  padding: 0.65rem 0.9rem;
  border-bottom: 1px solid var(--mc-app-border-faint, #eceae5);
  display: flex;
  gap: 0.75rem;
  align-items: center;
  justify-content: space-between;
}

.rx-newline:last-child {
  border-bottom: none;
}

.rx-newline__main {
  display: flex;
  flex-direction: column;
  gap: 0.15rem;
}

.rx-newline__sku {
  font-size: 0.78rem;
  color: var(--mc-app-text-muted, #5c5a56);
}

.rx-newline__ctrl {
  display: inline-flex;
  align-items: center;
  gap: 0.4rem;
}

.rx-form-row {
  display: grid;
  grid-template-columns: 1fr 12rem;
  gap: 1rem;
}

@media (max-width: 560px) {
  .rx-form-row {
    grid-template-columns: 1fr;
  }
}

.rx-summary {
  padding: 0.85rem 1rem;
  border-radius: 12px;
  background: var(--mc-app-bg, #f6f4f0);
  border: 1px solid var(--mc-app-border-soft, #ddd9d3);
  display: flex;
  flex-direction: column;
  gap: 0.35rem;
}

.rx-summary__row {
  display: flex;
  justify-content: space-between;
  align-items: baseline;
  gap: 1rem;
  font-variant-numeric: tabular-nums;
}

.rx-summary__row--net {
  border-top: 1px solid var(--mc-app-border-faint, #eceae5);
  padding-top: 0.5rem;
  margin-top: 0.15rem;
  font-size: 1.15rem;
}

.rx-summary[data-variant='due'] .rx-summary__row--net strong {
  color: #1565c0;
}

.rx-summary[data-variant='refund'] .rx-summary__row--net strong {
  color: #b42318;
}

.rx-actions {
  display: flex;
  justify-content: flex-end;
  gap: 0.75rem;
  margin-top: 1rem;
}
</style>
