<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import axios from 'axios'
import { logoLight } from '@/branding'

const route = useRoute()
const token = route.params.token as string
const base = import.meta.env.VITE_API_BASE?.replace(/\/$/, '') || ''
const client = axios.create({ baseURL: base || undefined })

type Inv = {
  invoiceNumber: string
  grandTotal: number
  createdAt: string
  customerName?: string | null
  paymentMethod: string
  lines: Array<{ description: string; quantity: number; unitPrice: number; lineTotal: number }>
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
  <div class="card" style="max-width: 640px; margin: 2rem auto">
    <img class="invoice-public-logo" :src="logoLight" alt="MC Tactical" />
    <h1 class="brand-wordmark" style="font-size: 1.2rem; margin: 0 0 0.5rem">Invoice</h1>
    <p class="err" v-if="err">{{ err }}</p>
    <template v-if="inv">
      <p>
        <strong>{{ inv.invoiceNumber }}</strong> — {{ new Date(inv.createdAt).toLocaleString() }}
      </p>
      <p v-if="inv.customerName">Customer: {{ inv.customerName }}</p>
      <p>Payment: {{ inv.paymentMethod }}</p>
      <table>
        <thead>
          <tr>
            <th>Item</th>
            <th>Qty</th>
            <th>Price</th>
            <th>Total</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="(l, i) in inv.lines" :key="i">
            <td>{{ l.description }}</td>
            <td>{{ l.quantity }}</td>
            <td>{{ l.unitPrice.toFixed(2) }}</td>
            <td>{{ l.lineTotal.toFixed(2) }}</td>
          </tr>
        </tbody>
      </table>
      <p><strong>Total: {{ inv.grandTotal.toFixed(2) }}</strong></p>
      <p><a :href="pdfLink()" target="_blank" rel="noreferrer">Download PDF</a></p>
    </template>
  </div>
</template>
