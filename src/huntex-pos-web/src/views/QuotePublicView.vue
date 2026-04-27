<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import axios from 'axios'
import { logoDark } from '@/branding'
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
  website?: string | null
  websiteLabel?: string | null
}

type Quote = {
  quoteNumber: string
  status: string
  customerName?: string | null
  customerCompany?: string | null
  subTotal: number
  discountTotal: number
  taxRate: number
  taxAmount: number
  grandTotal: number
  publicNotes?: string | null
  validUntil?: string | null
  createdAt: string
  lines: Array<{ itemName: string; description?: string | null; quantity: number; unitPrice: number; lineTotal: number }>
  companyContact?: CompanyContact | null
}

const q = ref<Quote | null>(null)
const err = ref<string | null>(null)

onMounted(async () => {
  try {
    const { data } = await client.get<Quote>(`/api/public/quotes/${token}`)
    q.value = data
  } catch {
    err.value = 'Quote not found'
  }
})

function pdfLink() {
  return `${base || ''}/api/public/quotes/${token}/pdf`
}

function formatDate(iso?: string | null) {
  if (!iso) return '—'
  return new Date(iso).toLocaleDateString('en-ZA', { day: 'numeric', month: 'short', year: 'numeric' })
}
</script>

<template>
  <div class="qp">
    <div class="qp__sheet">
      <header class="qp__head">
        <img class="qp__logo" :src="logoUrl ?? logoDark" :alt="businessName" width="160" height="48" />
        <div class="qp__head-text">
          <h1 class="qp__h1">Quote</h1>
          <p v-if="q" class="qp__num">{{ q.quoteNumber }}</p>
        </div>
      </header>

      <div v-if="err" class="qp__err" role="alert">{{ err }}</div>

      <template v-if="q">
        <dl class="qp__meta">
          <div><dt>Date</dt><dd>{{ formatDate(q.createdAt) }}</dd></div>
          <div><dt>Valid until</dt><dd>{{ formatDate(q.validUntil) }}</dd></div>
          <div v-if="q.customerName || q.customerCompany">
            <dt>Quote for</dt>
            <dd>{{ q.customerCompany || q.customerName }}</dd>
          </div>
          <div><dt>Status</dt><dd>{{ q.status }}</dd></div>
        </dl>

        <div class="qp__table-wrap">
          <table class="qp__table">
            <thead>
              <tr>
                <th>Item</th>
                <th class="qp-num">Qty</th>
                <th class="qp-num">Price</th>
                <th class="qp-num">Total</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="(l, i) in q.lines" :key="i">
                <td>
                  <div>{{ l.itemName }}</div>
                  <div v-if="l.description" class="qp-sub">{{ l.description }}</div>
                </td>
                <td class="qp-num">{{ l.quantity }}</td>
                <td class="qp-num">{{ formatZAR(l.unitPrice) }}</td>
                <td class="qp-num">{{ formatZAR(l.lineTotal) }}</td>
              </tr>
            </tbody>
          </table>
        </div>

        <div class="qp__total">
          <span>Quote total</span>
          <strong>{{ formatZAR(q.grandTotal) }}</strong>
        </div>

        <section v-if="q.publicNotes" class="qp__notes">
          <h2>Notes</h2>
          <p>{{ q.publicNotes }}</p>
        </section>

        <a class="qp__pdf" :href="pdfLink()" target="_blank" rel="noreferrer">Download PDF</a>

        <footer v-if="q.companyContact" class="qp__contact">
          <p class="qp__contact-name">{{ q.companyContact.displayName }}</p>
          <p v-if="q.companyContact.phone">Tel: {{ q.companyContact.phone }}</p>
          <p v-if="q.companyContact.email">
            Email: <a :href="'mailto:' + q.companyContact.email">{{ q.companyContact.email }}</a>
          </p>
          <p v-if="q.companyContact.address" class="qp__address">{{ q.companyContact.address }}</p>
          <p v-if="q.companyContact.website">
            <a :href="q.companyContact.website" target="_blank" rel="noreferrer">
              {{ q.companyContact.websiteLabel || q.companyContact.website }}
            </a>
          </p>
        </footer>
      </template>
    </div>
  </div>
</template>

<style scoped>
.qp { min-height: 100dvh; padding: 1.25rem; background: #e8e6e1; }
.qp__sheet {
  max-width: 640px;
  margin: 0 auto;
  background: #fff;
  border-radius: 16px;
  border: 1px solid #e2e0db;
  box-shadow: 0 8px 40px rgba(26, 26, 28, 0.08);
  padding: 1.75rem 1.5rem 2rem;
}
.qp__head {
  display: flex;
  align-items: center;
  gap: 1.25rem;
  padding-bottom: 1.25rem;
  border-bottom: 2px solid #f47a20;
  margin-bottom: 1.25rem;
}
.qp__logo { width: auto; height: 48px; object-fit: contain; }
.qp__h1 {
  margin: 0;
  font-family: 'Barlow Condensed', sans-serif;
  font-size: 1.35rem;
  font-weight: 700;
  letter-spacing: 0.06em;
  text-transform: uppercase;
  color: #1a1a1c;
}
.qp__num { margin: 0.25rem 0 0; font-size: 0.95rem; font-weight: 600; color: #5c5a56; }
.qp__err {
  padding: 0.85rem 1rem;
  border-radius: 10px;
  background: #ffebee;
  color: #7f1d1d;
  font-weight: 500;
  margin-bottom: 1rem;
}
.qp__meta {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(140px, 1fr));
  gap: 1rem;
  margin: 0 0 1.25rem;
}
.qp__meta dt { margin: 0; font-size: 0.7rem; font-weight: 700; letter-spacing: 0.08em; text-transform: uppercase; color: #7a7874; }
.qp__meta dd { margin: 0.2rem 0 0; font-size: 0.95rem; color: #1a1a1c; }
.qp__table-wrap { overflow-x: auto; margin-bottom: 1rem; }
.qp__table { width: 100%; border-collapse: collapse; font-size: 0.9rem; color: #1a1a1c; }
.qp__table th, .qp__table td {
  padding: 0.7rem 0.6rem;
  border-bottom: 1px solid #d9d6d0;
  text-align: left;
}
.qp__table td {
  font-weight: 500;
}
.qp__table th {
  font-family: 'Barlow Condensed', sans-serif;
  font-size: 0.75rem;
  font-weight: 700;
  letter-spacing: 0.06em;
  text-transform: uppercase;
  color: #4a4843;
  background: #f3f2ef;
  border-bottom-color: #c5c2bb;
}
.qp-num { text-align: right; font-variant-numeric: tabular-nums; white-space: nowrap; }
.qp-sub { font-size: 0.8rem; color: #4a4843; margin-top: 0.15rem; }
.qp__total {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-top: 1rem;
  padding: 1rem 1.1rem;
  background: linear-gradient(90deg, rgba(244, 122, 32, 0.12) 0%, rgba(244, 122, 32, 0.03) 100%);
  border-radius: 10px;
  border: 1px solid rgba(244, 122, 32, 0.3);
  font-size: 1.05rem;
  font-weight: 600;
  color: #1a1a1c;
}
.qp__total strong { font-size: 1.35rem; color: #1a1a1c; }
.qp__notes {
  margin-top: 1.25rem;
  padding: 1rem 1.1rem;
  background: #fafaf8;
  border: 1px solid #eceae6;
  border-radius: 10px;
}
.qp__notes h2 {
  margin: 0 0 0.4rem;
  font-size: 0.75rem;
  text-transform: uppercase;
  letter-spacing: 0.06em;
  color: #7a7874;
}
.qp__notes p { margin: 0; white-space: pre-line; color: #333336; }
.qp__pdf {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-height: 44px;
  margin-top: 1.25rem;
  padding: 0 1.25rem;
  border-radius: 8px;
  background: #1a1a1c;
  color: #fff !important;
  text-decoration: none;
  font-weight: 600;
  font-size: 0.875rem;
  letter-spacing: 0.04em;
  text-transform: uppercase;
}
.qp__pdf:hover { background: #f47a20; color: #0a0a0b !important; }
.qp__contact {
  margin-top: 2rem;
  padding-top: 1.25rem;
  border-top: 1px solid #d9d6d0;
  font-size: 0.9rem;
  color: #3a3835;
  line-height: 1.55;
}
.qp__contact-name { margin: 0 0 0.5rem; font-size: 1rem; font-weight: 700; color: #1a1a1c; }
.qp__address { white-space: pre-line; }
.qp__contact a { color: #c2410c; }

@media print {
  .qp { padding: 0; background: #fff; }
  .qp__sheet { box-shadow: none; border: none; border-radius: 0; max-width: none; padding: 0; }
  .qp__pdf { display: none !important; }
}
</style>
