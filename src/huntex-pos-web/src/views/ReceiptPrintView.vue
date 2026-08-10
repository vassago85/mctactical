<script setup lang="ts">
/**
 * 80 mm thermal receipt — Track 1 of the thermal-printer rollout.
 *
 * Reads the same public invoice payload that InvoicePublicView uses, renders
 * it at exactly 80 mm width with print CSS, then fires window.print() on
 * mount. Designed for an Xprinter XP-Q200 (or any standard ESC/POS 80 mm
 * printer) set as the OS default printer — the browser dispatches the page
 * straight to it. Track 2 (network-attached, native ESC/POS over port 9100)
 * will reuse this same data shape, so this view is also the print-fallback
 * when no printer IP is configured for the station.
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
  unitPrice: number
  /** Catalog retail at time of sale. Above unitPrice when a promotion applied. */
  originalUnitPrice: number
  /** Operator concession booked against this line, in rand. */
  lineDiscount: number
  lineTotal: number
}

type Inv = {
  invoiceNumber: string
  grandTotal: number
  subTotal: number
  taxRate: number
  taxAmount: number
  createdAt: string
  customerName?: string | null
  paymentMethod: string
  lines: Line[]
  companyContact?: CompanyContact | null
  receiptFooter?: string | null
}

const inv = ref<Inv | null>(null)
const err = ref<string | null>(null)
// "auto" query param lets you open the page without auto-printing
// (useful for preview / debugging the layout).
const autoPrint = route.query.auto !== '0'
const logoLoaded = ref(false)
const printDispatched = ref(false)

// Prefer the logo embedded in the public payload (works without
// branding fetch race). Fall back to the authenticated branding cache
// if the operator opened the receipt while logged in but the public
// payload happens not to have it.
const effectiveLogoUrl = computed<string | null>(() => {
  const fromPayload = inv.value?.companyContact?.logoUrl ?? null
  if (fromPayload) return fromPayload
  return brandingLogoUrl.value ?? null
})

function maybePrint() {
  if (!autoPrint || printDispatched.value) return
  if (!inv.value) return
  // If there's a logo we expect to print, wait for it to actually load
  // so the print snapshot includes the image rather than an empty box.
  if (effectiveLogoUrl.value && !logoLoaded.value) return
  printDispatched.value = true
  // One frame to let DOM settle, then print.
  requestAnimationFrame(() => {
    setTimeout(() => window.print(), 150)
  })
}

/** `window` is not exposed to the template from `<script setup>`, so bind through these. */
function reprint() {
  window.print()
}

function closeReceipt() {
  window.close()
  // A tab the operator opened themselves can't be closed by script; fall back to history.
  setTimeout(() => {
    if (!window.closed) window.history.back()
  }, 150)
}

function onLogoLoaded() {
  logoLoaded.value = true
  maybePrint()
}

function onLogoFailed() {
  // Pretend it loaded so we don't hang the print dialog on a broken image.
  logoLoaded.value = true
  maybePrint()
}

onMounted(async () => {
  try {
    const { data } = await client.get<Inv>(`/api/public/invoices/${token}`)
    inv.value = data
    // If there's no logo to wait for, fire the print as soon as the
    // payload is in. Otherwise we'll print from the image onload handler.
    if (!effectiveLogoUrl.value) maybePrint()
  } catch {
    err.value = 'Receipt not found.'
  }
})

function subtotal(lines: Line[]): number {
  return lines.reduce((s, l) => s + l.lineTotal, 0)
}

/**
 * Rand the customer saved on a line, whether it came from a promotion or an
 * operator concession. Printing it matters for returns: the thermal slip is often
 * the only record the customer keeps of what they actually paid.
 */
function lineSaving(l: Line): number {
  const promo = l.originalUnitPrice > l.unitPrice
    ? (l.originalUnitPrice - l.unitPrice) * l.quantity
    : 0
  return Math.round((promo + (l.lineDiscount ?? 0)) * 100) / 100
}

/** Retail unit price to print above the discount. */
function lineListPrice(l: Line): number {
  return l.originalUnitPrice > 0 ? l.originalUnitPrice : l.unitPrice
}

/** Line value at retail, before the saving. Equals lineListPrice × quantity. */
function lineGross(l: Line): number {
  return Math.round((l.lineTotal + lineSaving(l)) * 100) / 100
}

/**
 * Line prices are VAT-inclusive in this system. For the receipt we want
 * the classical layout: subtotal excl VAT, VAT line, TOTAL incl VAT.
 * Ex-VAT is the inclusive grand total minus the VAT portion already
 * baked into it.
 */
function exVat(i: Inv): number {
  return Math.max(0, (i.grandTotal ?? 0) - (i.taxAmount ?? 0))
}

/**
 * TaxRate is stored as a percent (e.g. 15 for 15% VAT), not a fraction.
 * Display directly; the previous *100 caused the "1500%" bug.
 */
function vatLabel(i: Inv): string {
  if (!i.taxRate || i.taxRate <= 0) return 'VAT'
  // Drop trailing ".0" but keep one decimal for unusual rates (e.g. 12.5).
  const r = Number(i.taxRate)
  const pretty = Number.isInteger(r) ? r.toString() : r.toFixed(1)
  return `VAT (${pretty}%)`
}

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

    <article v-else-if="inv" class="rcpt__paper">
      <header class="rcpt__head">
        <img
          v-if="effectiveLogoUrl"
          class="rcpt__logo"
          :src="effectiveLogoUrl"
          :alt="inv.companyContact?.displayName || businessName"
          crossorigin="anonymous"
          @load="onLogoLoaded"
          @error="onLogoFailed"
        />
        <div class="rcpt__name">{{ inv.companyContact?.displayName || businessName }}</div>
        <div v-if="inv.companyContact?.address" class="rcpt__addr">{{ inv.companyContact.address }}</div>
        <div v-if="inv.companyContact?.phone" class="rcpt__line">Tel: {{ inv.companyContact.phone }}</div>
        <div v-if="inv.companyContact?.email" class="rcpt__line">{{ inv.companyContact.email }}</div>
        <div v-if="inv.companyContact?.vatNumber" class="rcpt__line">VAT: {{ inv.companyContact.vatNumber }}</div>
      </header>

      <hr class="rcpt__rule" />

      <div class="rcpt__meta">
        <div><span>Receipt:</span> <strong>{{ inv.invoiceNumber }}</strong></div>
        <div><span>Date:</span> <strong>{{ fmtDate(inv.createdAt) }}</strong></div>
        <div v-if="inv.customerName"><span>Customer:</span> <strong>{{ inv.customerName }}</strong></div>
      </div>

      <hr class="rcpt__rule" />

      <section class="rcpt__items">
        <div v-for="(l, idx) in inv.lines" :key="idx" class="rcpt__item">
          <div class="rcpt__item-name">{{ l.description }}</div>
          <div v-if="l.sku" class="rcpt__item-sku">SKU: {{ l.sku }}</div>
          <!-- Discounted lines print at retail, then the saving, then what was paid,
               so a customer returning without paperwork can prove the price. -->
          <template v-if="lineSaving(l) > 0">
            <div class="rcpt__item-line">
              <span>{{ l.quantity }} &times; {{ formatZAR(lineListPrice(l)) }}</span>
              <span class="rcpt__num">{{ formatZAR(lineGross(l)) }}</span>
            </div>
            <div class="rcpt__item-disc">
              <span>Discount</span>
              <span class="rcpt__num">-{{ formatZAR(lineSaving(l)) }}</span>
            </div>
            <div class="rcpt__item-paid">
              <span>Paid</span>
              <span class="rcpt__num">{{ formatZAR(l.lineTotal) }}</span>
            </div>
          </template>
          <div v-else class="rcpt__item-line">
            <span>{{ l.quantity }} &times; {{ formatZAR(l.unitPrice) }}</span>
            <span class="rcpt__num">{{ formatZAR(l.lineTotal) }}</span>
          </div>
        </div>
      </section>

      <hr class="rcpt__rule" />

      <div class="rcpt__totals">
        <!-- VAT-registered: show the ex-VAT subtotal, the extracted VAT
             portion, then the inclusive TOTAL. Line prices above are
             VAT-inclusive (system convention). -->
        <template v-if="inv.taxAmount > 0">
          <div class="rcpt__sub">
            <span>Subtotal (excl VAT)</span>
            <span class="rcpt__num">{{ formatZAR(exVat(inv)) }}</span>
          </div>
          <div class="rcpt__sub">
            <span>{{ vatLabel(inv) }}</span>
            <span class="rcpt__num">{{ formatZAR(inv.taxAmount) }}</span>
          </div>
          <div class="rcpt__total">
            <span>TOTAL (incl VAT)</span>
            <span class="rcpt__num">{{ formatZAR(inv.grandTotal) }}</span>
          </div>
        </template>
        <!-- Non-VAT (default for MC Tactical today): single subtotal +
             total, no confusing duplicate lines. -->
        <template v-else>
          <div class="rcpt__sub">
            <span>Subtotal</span>
            <span class="rcpt__num">{{ formatZAR(inv.subTotal || subtotal(inv.lines)) }}</span>
          </div>
          <div class="rcpt__total">
            <span>TOTAL</span>
            <span class="rcpt__num">{{ formatZAR(inv.grandTotal) }}</span>
          </div>
        </template>
        <div class="rcpt__pay">
          <span>Paid by</span>
          <strong>{{ inv.paymentMethod }}</strong>
        </div>
      </div>

      <hr class="rcpt__rule" />

      <footer class="rcpt__foot">
        <p v-if="inv.receiptFooter" class="rcpt__footer-text">{{ inv.receiptFooter }}</p>

        <!-- Compact returns policy summary. Wording mirrors the full policy
             at mctactical.co.za/policies/refund-policy; this block keeps
             the customer informed at point of sale and the URL points to
             the authoritative version. -->
        <section class="rcpt__policy">
          <div class="rcpt__policy-title">MC TACTICAL — RETURNS POLICY</div>
          <p class="rcpt__policy-p">
            90 days for refund or exchange, if unused, undamaged, in
            original packaging and with this receipt.
          </p>
          <p class="rcpt__policy-p">
            <strong>Not returnable:</strong> modified items, clothing
            &amp; footwear, consumables, clearance/voetstoots goods,
            gift cards and services. Other terms apply.
          </p>
          <p class="rcpt__policy-url-line">
            Full policy:<br />
            <span class="rcpt__refund-url">mctactical.co.za/policies/refund-policy</span>
          </p>
        </section>

        <p class="rcpt__thanks">Thank you!</p>
      </footer>

      <div class="rcpt__cut">&nbsp;</div>

      <!-- Screen-only controls — never printed -->
      <div class="rcpt__controls no-print">
        <button type="button" class="rcpt__btn" @click="reprint">Print again</button>
        <button type="button" class="rcpt__btn rcpt__btn--secondary" @click="closeReceipt">Close</button>
      </div>
    </article>
  </div>
</template>

<style scoped>
/* ── Page sizing for 80 mm thermal ────────────────────────────────────── */
/*
 * `size: auto` hands page sizing to the printer driver. On the XP-Q200 as
 * configured that is Letter/A4, whose printable area is ~58mm wide — hence
 * .rcpt__paper at 58mm, which prints at 100% scale with no operator setup.
 *
 * Do not write `size: 58mm auto`: a length mixed with `auto` is not a legal
 * value, so Chrome drops the whole declaration. Receipt height is therefore
 * bounded by the driver page height (~297mm) and spills onto a second page
 * past that, which is why the printed content below is kept short. For a true
 * continuous roll, set a roll page size in the Windows driver instead.
 */
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

/* ── The "paper" ─────────────────────────────────────────────────────── */
.rcpt__paper {
  width: 58mm;
  background: #fff;
  padding: 2mm 2mm 4mm;
  box-sizing: border-box;
  font-family: 'Menlo', 'Consolas', 'Courier New', monospace;
  font-size: 10.5px;
  line-height: 1.35;
  color: #000;
  /* Soft shadow on-screen only */
  box-shadow: 0 6px 24px rgba(0, 0, 0, 0.08);
}

/* ── Header block ────────────────────────────────────────────────────── */
.rcpt__head { text-align: center; }
.rcpt__logo {
  max-width: 48mm;
  max-height: 16mm;
  width: auto;
  height: auto;
  object-fit: contain;
  margin: 0 auto 2mm;
  display: block;
  /* Force the logo to print solid black on thermal: */
  filter: contrast(1.2);
}
.rcpt__name {
  font-weight: 900;
  /* Sized to span the full ~54mm of usable paper width — "MC TACTICAL"
     fills the slip header. If the configured business name is much
     longer this will wrap to two lines which is still fine. */
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

/* ── Rule (dashed line, prints sharp) ────────────────────────────────── */
.rcpt__rule {
  border: none;
  border-top: 1px dashed #000;
  margin: 2mm 0;
}

/* ── Meta (receipt #, date, customer) ────────────────────────────────── */
.rcpt__meta div {
  word-break: break-word;
  margin-bottom: 0.5mm;
}
.rcpt__meta span { color: #444; }
.rcpt__meta strong { font-weight: 700; }

/* ── Items ───────────────────────────────────────────────────────────── */
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
.rcpt__item-disc,
.rcpt__item-paid {
  display: flex;
  justify-content: space-between;
  gap: 2mm;
  font-size: 9.5px;
  padding-left: 2mm;
}
.rcpt__item-paid { font-weight: 700; }

.rcpt__num {
  font-variant-numeric: tabular-nums;
  white-space: nowrap;
}

/* ── Totals ──────────────────────────────────────────────────────────── */
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
}

/* ── Footer ──────────────────────────────────────────────────────────── */
.rcpt__foot {
  margin-top: 1mm;
  text-align: center;
}
.rcpt__footer-text {
  white-space: pre-line;
  font-size: 10px;
  margin: 0 0 2mm;
}
/* ── Returns policy block (compact summary + URL) ────────────────────── */
.rcpt__policy {
  /* Visually separated from the totals but tight on vertical space —
     this prints on every receipt so each mm counts. */
  margin: 2mm 0 2mm;
  padding: 1.5mm 0 0;
  border-top: 1px dashed #000;
  text-align: left;
}
.rcpt__policy-title {
  font-weight: 700;
  font-size: 9.5px;
  text-align: center;
  text-transform: uppercase;
  letter-spacing: 0.04em;
  margin-bottom: 1.5mm;
}
.rcpt__policy-p {
  font-size: 8.5px;
  line-height: 1.3;
  margin: 0 0 1.2mm;
  /* Justified prints a touch denser on the narrow paper without
     looking like a single-line wall of text. */
  text-align: justify;
  word-break: normal;
  overflow-wrap: anywhere;
}
.rcpt__policy-url-line {
  font-size: 9px;
  line-height: 1.3;
  margin: 1.5mm 0 0;
  text-align: center;
}
.rcpt__refund-url {
  /* Long URL — let it break wherever to fit the narrow paper. */
  font-weight: 700;
  word-break: break-all;
}
.rcpt__thanks {
  margin: 1mm 0 0;
  font-weight: 700;
  font-size: 12px;
}

/* Whitespace so the paper cutter doesn't slice the bottom line. */
.rcpt__cut { height: 10mm; }

/* ── Screen-only controls ────────────────────────────────────────────── */
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

/* ── PRINT ───────────────────────────────────────────────────────────── */
@media print {
  .rcpt {
    background: #fff;
    padding: 0;
    min-height: 0;
    display: block;
  }
  .rcpt__paper {
    /* IMPORTANT: do NOT override width here — the screen width (58mm)
       is also the print width, matching @page above. The previous
       override to 80mm was the reason 80% scale was needed at the
       print dialog. */
    box-shadow: none;
  }
  .no-print { display: none !important; }
  /* Make sure logos and dashed rules print solid */
  * { -webkit-print-color-adjust: exact; print-color-adjust: exact; }
}
</style>
