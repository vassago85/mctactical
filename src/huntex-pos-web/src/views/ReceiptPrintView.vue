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
import { onMounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import axios from 'axios'
import { useBranding } from '@/composables/useBranding'
import { formatZAR } from '@/utils/format'

const { businessName, logoUrl } = useBranding()
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
}

type Line = {
  description: string
  sku?: string | null
  quantity: number
  unitPrice: number
  lineTotal: number
}

type Inv = {
  invoiceNumber: string
  grandTotal: number
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

onMounted(async () => {
  try {
    const { data } = await client.get<Inv>(`/api/public/invoices/${token}`)
    inv.value = data
    if (autoPrint) {
      // Wait one frame so the DOM (logo + content) is laid out before the
      // browser snapshots the page for the print dialog.
      requestAnimationFrame(() => {
        setTimeout(() => window.print(), 150)
      })
    }
  } catch {
    err.value = 'Receipt not found.'
  }
})

function subtotal(lines: Line[]): number {
  return lines.reduce((s, l) => s + l.lineTotal, 0)
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
        <img v-if="logoUrl" class="rcpt__logo" :src="logoUrl" :alt="businessName" />
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
          <div class="rcpt__item-line">
            <span>{{ l.quantity }} &times; {{ formatZAR(l.unitPrice) }}</span>
            <span class="rcpt__num">{{ formatZAR(l.lineTotal) }}</span>
          </div>
        </div>
      </section>

      <hr class="rcpt__rule" />

      <div class="rcpt__totals">
        <div class="rcpt__sub">
          <span>Subtotal</span>
          <span class="rcpt__num">{{ formatZAR(subtotal(inv.lines)) }}</span>
        </div>
        <div class="rcpt__total">
          <span>TOTAL</span>
          <span class="rcpt__num">{{ formatZAR(inv.grandTotal) }}</span>
        </div>
        <div class="rcpt__pay">
          <span>Paid by</span>
          <strong>{{ inv.paymentMethod }}</strong>
        </div>
      </div>

      <hr class="rcpt__rule" />

      <footer class="rcpt__foot">
        <p v-if="inv.receiptFooter" class="rcpt__footer-text">{{ inv.receiptFooter }}</p>
        <p class="rcpt__thanks">Thank you!</p>
      </footer>

      <div class="rcpt__cut">&nbsp;</div>

      <!-- Screen-only controls — never printed -->
      <div class="rcpt__controls no-print">
        <button type="button" class="rcpt__btn" @click="window.print()">Print again</button>
        <button type="button" class="rcpt__btn rcpt__btn--secondary" @click="window.close()">Close</button>
      </div>
    </article>
  </div>
</template>

<style scoped>
/* ── Page sizing for 80 mm thermal ────────────────────────────────────── */
/*
 * Tuned for XP-Q200 in real-world use: at "Letter / A4" defaults the
 * browser fits content into the printer's actual printable area, which
 * on this device turned out to be ~58mm (printing at 100% with content
 * any wider clips on the right; 80% scale of 72mm content also worked
 * which is roughly 58mm — same target). 58mm gives a safe margin and
 * still produces a tidy receipt.
 *
 * If the operator later sets a "Receipt 80(72)x297" page size in the
 * Windows driver, we can widen this without code changes — but for now
 * 58mm prints cleanly at 100% scale without any operator intervention.
 */
@page { size: 58mm auto; margin: 0; }

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
  font-size: 9.5px;
  line-height: 1.35;
  color: #000;
  /* Soft shadow on-screen only */
  box-shadow: 0 6px 24px rgba(0, 0, 0, 0.08);
}

/* ── Header block ────────────────────────────────────────────────────── */
.rcpt__head { text-align: center; }
.rcpt__logo {
  max-width: 60mm;
  max-height: 18mm;
  width: auto;
  height: auto;
  object-fit: contain;
  margin: 0 auto 2mm;
  display: block;
  /* Force the logo to print solid black on thermal: */
  filter: contrast(1.2);
}
.rcpt__name {
  font-weight: 700;
  font-size: 11px;
  letter-spacing: 0.02em;
  text-transform: uppercase;
  margin-bottom: 1mm;
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
  font-size: 8.5px;
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
  font-size: 12px;
  margin-top: 1mm;
}

/* ── Footer ──────────────────────────────────────────────────────────── */
.rcpt__foot {
  margin-top: 1mm;
  text-align: center;
}
.rcpt__footer-text {
  white-space: pre-line;
  font-size: 9px;
  margin: 0 0 2mm;
}
.rcpt__thanks {
  margin: 1mm 0 0;
  font-weight: 700;
  font-size: 11px;
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
