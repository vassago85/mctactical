<script setup lang="ts">
/**
 * 58 mm thermal return slip. Twin of ReceiptPrintView, but shows what the customer
 * brought back, the total credit, and how the difference was settled — so a walk-in
 * refund always has a printed record without voiding the original invoice.
 */
import { computed, onMounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import axios from 'axios'
import { useBranding } from '@/composables/useBranding'
import { formatZAR } from '@/utils/format'

const { businessName, logoUrl: brandingLogoUrl } = useBranding()
const route = useRoute()
const token = route.params.token as string
const base = import.meta.env.VITE_API_BASE?.replace(/\/$/, '') || ''
const client = axios.create({ baseURL: base || undefined })

type CompanyContact = {
  displayName: string
  phone?: string | null
  email?: string | null
  address?: string | null
  vatNumber?: string | null
  logoUrl?: string | null
}

type Line = {
  description: string
  sku?: string | null
  quantity: number
  unitCredit: number
  lineCredit: number
}

type Slip = {
  id: string
  publicToken: string
  originalInvoiceNumber: string
  exchangeInvoiceNumber?: string | null
  reason: string
  customerName?: string | null
  creditTotal: number
  netSettlement: number
  settlementMethod?: string | null
  createdAt: string
  cashierName?: string | null
  lines: Line[]
  companyContact?: CompanyContact | null
  receiptFooter?: string | null
}

const slip = ref<Slip | null>(null)
const err = ref<string | null>(null)
const autoPrint = route.query.auto !== '0'
const logoLoaded = ref(false)
const printDispatched = ref(false)

const effectiveLogoUrl = computed<string | null>(() => {
  const fromPayload = slip.value?.companyContact?.logoUrl ?? null
  if (fromPayload) return fromPayload
  return brandingLogoUrl.value ?? null
})

function maybePrint() {
  if (!autoPrint || printDispatched.value) return
  if (!slip.value) return
  if (effectiveLogoUrl.value && !logoLoaded.value) return
  printDispatched.value = true
  requestAnimationFrame(() => {
    setTimeout(() => window.print(), 150)
  })
}

function reprint() {
  window.print()
}

function closeSlip() {
  window.close()
  setTimeout(() => {
    if (!window.closed) window.history.back()
  }, 150)
}

function onLogoLoaded() {
  logoLoaded.value = true
  maybePrint()
}

function onLogoFailed() {
  logoLoaded.value = true
  maybePrint()
}

onMounted(async () => {
  try {
    const { data } = await client.get<Slip>(`/api/public/returns/${token}`)
    slip.value = data
    if (!effectiveLogoUrl.value) maybePrint()
  } catch {
    err.value = 'Return slip not found.'
  }
})

/** Positive = customer paid the difference. Negative = customer was refunded. */
const settlementLabel = computed(() => {
  const s = slip.value
  if (!s) return ''
  if (s.netSettlement > 0) return 'Balance paid by customer'
  if (s.netSettlement < 0) return 'Refunded to customer'
  return 'Even swap'
})

const settlementAmount = computed(() => {
  const s = slip.value
  if (!s) return 0
  return Math.abs(s.netSettlement)
})

function fmtDate(iso: string): string {
  const d = new Date(iso)
  return isNaN(d.getTime())
    ? '—'
    : d.toLocaleString('en-ZA', {
        year: 'numeric', month: '2-digit', day: '2-digit',
        hour: '2-digit', minute: '2-digit'
      })
}
</script>

<template>
  <div class="rcpt">
    <div v-if="err" class="rcpt__err">{{ err }}</div>

    <article v-else-if="slip" class="rcpt__paper">
      <header class="rcpt__head">
        <img
          v-if="effectiveLogoUrl"
          class="rcpt__logo"
          :src="effectiveLogoUrl"
          :alt="slip.companyContact?.displayName || businessName"
          crossorigin="anonymous"
          @load="onLogoLoaded"
          @error="onLogoFailed"
        />
        <div class="rcpt__name">{{ slip.companyContact?.displayName || businessName }}</div>
        <div v-if="slip.companyContact?.address" class="rcpt__addr">{{ slip.companyContact.address }}</div>
        <div v-if="slip.companyContact?.phone" class="rcpt__line">Tel: {{ slip.companyContact.phone }}</div>
        <div v-if="slip.companyContact?.email" class="rcpt__line">{{ slip.companyContact.email }}</div>
        <div v-if="slip.companyContact?.vatNumber" class="rcpt__line">VAT: {{ slip.companyContact.vatNumber }}</div>
      </header>

      <hr class="rcpt__rule" />

      <div class="rcpt__title">RETURN / REFUND</div>

      <div class="rcpt__meta">
        <div><span>Ref:</span> <strong>{{ slip.originalInvoiceNumber }}</strong></div>
        <div><span>Date:</span> <strong>{{ fmtDate(slip.createdAt) }}</strong></div>
        <div v-if="slip.customerName"><span>Customer:</span> <strong>{{ slip.customerName }}</strong></div>
        <div v-if="slip.exchangeInvoiceNumber">
          <span>Replaced with:</span> <strong>{{ slip.exchangeInvoiceNumber }}</strong>
        </div>
        <div v-if="slip.cashierName"><span>Cashier:</span> <strong>{{ slip.cashierName }}</strong></div>
      </div>

      <hr class="rcpt__rule" />

      <section class="rcpt__items">
        <div v-for="(l, idx) in slip.lines" :key="idx" class="rcpt__item">
          <div class="rcpt__item-name">{{ l.description }}</div>
          <div v-if="l.sku" class="rcpt__item-sku">SKU: {{ l.sku }}</div>
          <div class="rcpt__item-line">
            <span>{{ l.quantity }} &times; {{ formatZAR(l.unitCredit) }}</span>
            <span class="rcpt__num">{{ formatZAR(l.lineCredit) }}</span>
          </div>
        </div>
      </section>

      <hr class="rcpt__rule" />

      <div class="rcpt__totals">
        <div class="rcpt__sub">
          <span>Return credit</span>
          <span class="rcpt__num">{{ formatZAR(slip.creditTotal) }}</span>
        </div>
        <div class="rcpt__total">
          <span>{{ settlementLabel.toUpperCase() }}</span>
          <span class="rcpt__num">{{ formatZAR(settlementAmount) }}</span>
        </div>
        <div v-if="slip.settlementMethod && slip.netSettlement !== 0" class="rcpt__pay">
          <span>Method</span>
          <strong>{{ slip.settlementMethod }}</strong>
        </div>
      </div>

      <hr class="rcpt__rule" />

      <div v-if="slip.reason" class="rcpt__reason">
        <span class="rcpt__reason-label">REASON</span>
        <p class="rcpt__reason-text">{{ slip.reason }}</p>
      </div>

      <hr v-if="slip.reason" class="rcpt__rule" />

      <div class="rcpt__signrow">
        <span>Signature:</span>
        <span class="rcpt__sigline"></span>
      </div>

      <footer class="rcpt__foot">
        <p v-if="slip.receiptFooter" class="rcpt__footer-text">{{ slip.receiptFooter }}</p>
        <p class="rcpt__thanks">Keep this slip with your original receipt.</p>
      </footer>

      <div class="rcpt__cut">&nbsp;</div>

      <div class="rcpt__controls no-print">
        <button type="button" class="rcpt__btn" @click="reprint">Print again</button>
        <button type="button" class="rcpt__btn rcpt__btn--secondary" @click="closeSlip">Close</button>
      </div>
    </article>
  </div>
</template>

<style scoped>
@page { size: auto; margin: 0; }

.rcpt {
  background: #e8e6e1;
  min-height: 100dvh;
  padding: 1rem;
  display: flex;
  justify-content: center;
}

.rcpt__err {
  background: #fff;
  padding: 1rem 1.25rem;
  border-radius: 8px;
  color: #7f1d1d;
  font-weight: 600;
}

.rcpt__paper {
  width: 58mm;
  background: #fff;
  padding: 2mm 2mm 4mm;
  box-sizing: border-box;
  font-family: 'Menlo', 'Consolas', 'Courier New', monospace;
  font-size: 10.5px;
  line-height: 1.35;
  color: #000;
  box-shadow: 0 6px 24px rgba(0, 0, 0, 0.08);
}

.rcpt__head { text-align: center; }
.rcpt__logo {
  max-width: 48mm;
  max-height: 16mm;
  width: auto;
  height: auto;
  object-fit: contain;
  margin: 0 auto 2mm;
  display: block;
  filter: contrast(1.2);
}
.rcpt__name {
  font-weight: 900;
  font-size: 22px;
  letter-spacing: 0.06em;
  text-transform: uppercase;
  margin-bottom: 1.5mm;
  line-height: 1.1;
}
.rcpt__addr {
  white-space: pre-line;
  word-break: break-word;
  margin-bottom: 1mm;
}
.rcpt__line {
  word-break: break-word;
  margin-bottom: 0.5mm;
}

.rcpt__rule {
  border: none;
  border-top: 1px dashed #000;
  margin: 2mm 0;
}

.rcpt__title {
  text-align: center;
  font-weight: 900;
  font-size: 13px;
  letter-spacing: 0.06em;
  margin: 1mm 0 1.5mm;
}

.rcpt__meta div {
  word-break: break-word;
  margin-bottom: 0.5mm;
}
.rcpt__meta span { color: #444; }
.rcpt__meta strong { font-weight: 700; }

.rcpt__items { margin: 0; }
.rcpt__item { margin-bottom: 1.5mm; }
.rcpt__item-name {
  font-weight: 700;
  word-break: break-word;
}
.rcpt__item-sku {
  font-size: 9.5px;
  color: #333;
  letter-spacing: 0.02em;
  margin: 0.2mm 0 0.4mm;
  word-break: break-all;
}
.rcpt__item-line {
  display: flex;
  justify-content: space-between;
  gap: 2mm;
}

.rcpt__num {
  font-variant-numeric: tabular-nums;
  white-space: nowrap;
}

.rcpt__totals { display: flex; flex-direction: column; gap: 1mm; }
.rcpt__sub,
.rcpt__pay {
  display: flex;
  justify-content: space-between;
  gap: 2mm;
}
.rcpt__total {
  display: flex;
  justify-content: space-between;
  gap: 2mm;
  font-weight: 700;
  font-size: 13px;
  margin-top: 1mm;
  border-top: 1px dashed #000;
  padding-top: 1mm;
}

.rcpt__reason {
  margin-top: 1mm;
}
.rcpt__reason-label {
  font-size: 8px;
  font-weight: 700;
  letter-spacing: 0.06em;
  color: #444;
}
.rcpt__reason-text {
  margin: 0.5mm 0 0;
  white-space: pre-wrap;
  font-size: 10px;
}

.rcpt__signrow {
  display: flex;
  align-items: end;
  gap: 2mm;
  margin: 4mm 0 1mm;
  font-size: 9px;
}
.rcpt__sigline {
  flex: 1 1 auto;
  border-bottom: 1px solid #000;
  height: 6mm;
}

.rcpt__foot {
  margin-top: 1mm;
  text-align: center;
}
.rcpt__footer-text {
  white-space: pre-line;
  font-size: 10px;
  margin: 0 0 2mm;
}
.rcpt__thanks {
  margin: 1mm 0 0;
  font-weight: 700;
  font-size: 11px;
}

.rcpt__cut { height: 10mm; }

.rcpt__controls {
  display: flex;
  gap: 0.5rem;
  margin-top: 1rem;
  justify-content: center;
}
.rcpt__btn {
  padding: 0.5rem 1rem;
  border-radius: 6px;
  border: none;
  background: #1a1a1c;
  color: #fff;
  font-family: inherit;
  font-weight: 600;
  font-size: 12px;
  cursor: pointer;
}
.rcpt__btn--secondary {
  background: transparent;
  color: #333;
  border: 1px solid #c5c2bb;
}

@media print {
  .rcpt {
    background: #fff;
    padding: 0;
    min-height: 0;
    display: block;
  }
  .rcpt__paper {
    box-shadow: none;
  }
  .no-print { display: none !important; }
  * { -webkit-print-color-adjust: exact; print-color-adjust: exact; }
}
</style>
