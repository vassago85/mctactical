<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import axios from 'axios'
import { logoDark } from '@/branding'
import { formatZAR } from '@/utils/format'

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

type Inv = {
  invoiceNumber: string
  grandTotal: number
  createdAt: string
  customerName?: string | null
  paymentMethod: string
  lines: Array<{ description: string; quantity: number; unitPrice: number; lineTotal: number }>
  companyContact?: CompanyContact | null
}

const inv = ref<Inv | null>(null)
const err = ref<string | null>(null)

onMounted(async () => {
  try {
    const { data } = await client.get<Inv>(`/api/public/invoices/${token}`)
    inv.value = data
  } catch {
    err.value = 'Invoice not found'
  }
})

function pdfLink() {
  return `${base || ''}/api/public/invoices/${token}/pdf`
}
</script>

<template>
  <div class="inv-public">
    <div class="inv-public__sheet">
      <header class="inv-public__head">
        <img class="inv-public__logo" :src="logoDark" alt="MC Tactical" />
        <div class="inv-public__head-text">
          <h1 class="inv-public__h1">Invoice</h1>
          <p v-if="inv" class="inv-public__num">{{ inv.invoiceNumber }}</p>
        </div>
      </header>

      <div v-if="err" class="inv-public__err" role="alert">{{ err }}</div>

      <template v-if="inv">
        <dl class="inv-public__meta">
          <div>
            <dt>Date</dt>
            <dd>{{ new Date(inv.createdAt).toLocaleString() }}</dd>
          </div>
          <div v-if="inv.customerName">
            <dt>Customer</dt>
            <dd>{{ inv.customerName }}</dd>
          </div>
          <div>
            <dt>Payment</dt>
            <dd>{{ inv.paymentMethod }}</dd>
          </div>
        </dl>

        <div class="inv-public__table-wrap">
          <table class="inv-public__table">
            <thead>
              <tr>
                <th>Item</th>
                <th class="inv-num">Qty</th>
                <th class="inv-num">Price</th>
                <th class="inv-num">Total</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="(l, i) in inv.lines" :key="i">
                <td>{{ l.description }}</td>
                <td class="inv-num">{{ l.quantity }}</td>
                <td class="inv-num">{{ formatZAR(l.unitPrice) }}</td>
                <td class="inv-num">{{ formatZAR(l.lineTotal) }}</td>
              </tr>
            </tbody>
          </table>
        </div>

        <div class="inv-public__total">
          <span>Total due</span>
          <strong>{{ formatZAR(inv.grandTotal) }}</strong>
        </div>

        <a class="inv-public__pdf" :href="pdfLink()" target="_blank" rel="noreferrer">Download PDF</a>

        <footer v-if="inv.companyContact" class="inv-public__contact">
          <h2 class="sr-only">Contact</h2>
          <p class="inv-public__contact-name">{{ inv.companyContact.displayName }}</p>
          <p v-if="inv.companyContact.phone">Tel: {{ inv.companyContact.phone }}</p>
          <p v-if="inv.companyContact.email">
            Email:
            <a :href="'mailto:' + inv.companyContact.email">{{ inv.companyContact.email }}</a>
          </p>
          <p v-if="inv.companyContact.address" class="inv-public__address">
            {{ inv.companyContact.address }}
          </p>
          <p v-if="inv.companyContact.website">
            <a :href="inv.companyContact.website" target="_blank" rel="noreferrer">{{
              inv.companyContact.websiteLabel || inv.companyContact.website
            }}</a>
          </p>
        </footer>
      </template>
    </div>
  </div>
</template>

<style scoped>
.inv-public {
  min-height: 100dvh;
  padding: 1.25rem;
  background: #e8e6e1;
}

.inv-public__sheet {
  max-width: 640px;
  margin: 0 auto;
  background: #fff;
  border-radius: 16px;
  border: 1px solid #e2e0db;
  box-shadow: 0 8px 40px rgba(26, 26, 28, 0.08);
  padding: 1.75rem 1.5rem 2rem;
}

.inv-public__head {
  display: flex;
  align-items: center;
  gap: 1.25rem;
  padding-bottom: 1.25rem;
  border-bottom: 2px solid #f47a20;
  margin-bottom: 1.25rem;
}

.inv-public__logo {
  width: auto;
  height: 48px;
  object-fit: contain;
}

.inv-public__h1 {
  margin: 0;
  font-family: 'Barlow Condensed', sans-serif;
  font-size: 1.35rem;
  font-weight: 700;
  letter-spacing: 0.06em;
  text-transform: uppercase;
  color: #1a1a1c;
}

.inv-public__num {
  margin: 0.25rem 0 0;
  font-size: 0.95rem;
  font-weight: 600;
  color: #5c5a56;
}

.inv-public__err {
  padding: 0.85rem 1rem;
  border-radius: 10px;
  background: #ffebee;
  color: #7f1d1d;
  font-weight: 500;
  margin-bottom: 1rem;
}

.inv-public__meta {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(140px, 1fr));
  gap: 1rem;
  margin: 0 0 1.25rem;
}

.inv-public__meta dt {
  margin: 0;
  font-size: 0.7rem;
  font-weight: 700;
  letter-spacing: 0.08em;
  text-transform: uppercase;
  color: #7a7874;
}

.inv-public__meta dd {
  margin: 0.2rem 0 0;
  font-size: 0.95rem;
  color: #1a1a1c;
}

.inv-public__table-wrap {
  overflow-x: auto;
  margin-bottom: 1rem;
}

.inv-public__table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.9rem;
}

.inv-public__table th,
.inv-public__table td {
  padding: 0.65rem 0.5rem;
  border-bottom: 1px solid #eceae6;
  text-align: left;
}

.inv-public__table th {
  font-family: 'Barlow Condensed', sans-serif;
  font-size: 0.72rem;
  letter-spacing: 0.06em;
  text-transform: uppercase;
  color: #7a7874;
  background: #fafaf8;
}

.inv-num {
  text-align: right;
  font-variant-numeric: tabular-nums;
  white-space: nowrap;
}

.inv-public__total {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-top: 1rem;
  padding: 1rem 1.1rem;
  background: linear-gradient(90deg, rgba(244, 122, 32, 0.1) 0%, transparent 100%);
  border-radius: 10px;
  border: 1px solid rgba(244, 122, 32, 0.25);
  font-size: 1.05rem;
}

.inv-public__total strong {
  font-size: 1.35rem;
  color: #1a1a1c;
}

.inv-public__pdf {
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

.inv-public__pdf:hover {
  background: #f47a20;
  color: #0a0a0b !important;
}

.inv-public__contact {
  margin-top: 2rem;
  padding-top: 1.25rem;
  border-top: 1px solid #eceae6;
  font-size: 0.9rem;
  color: #5c5a56;
  line-height: 1.55;
}

.inv-public__contact-name {
  margin: 0 0 0.5rem;
  font-size: 1rem;
  font-weight: 700;
  color: #1a1a1c;
}

.inv-public__address {
  white-space: pre-line;
}

.inv-public__contact a {
  color: #c2410c;
}

@media print {
  .inv-public {
    padding: 0;
    background: #fff;
  }

  .inv-public__sheet {
    box-shadow: none;
    border: none;
    border-radius: 0;
    max-width: none;
    padding: 0;
  }

  .inv-public__pdf {
    display: none !important;
  }
}
</style>
