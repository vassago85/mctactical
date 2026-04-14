<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { http } from '@/api/http'

type Row = {
  id: string
  invoiceNumber: string
  status: string
  grandTotal: number
  createdAt: string
  customerName?: string | null
}
type Daily = { date: string; invoiceCount: number; grandTotal: number }

const invoices = ref<Row[]>([])
const daily = ref<Daily[]>([])
const err = ref<string | null>(null)

async function openPdf(id: string) {
  const res = await http.get(`/api/invoices/${id}/pdf`, { responseType: 'blob' })
  const url = URL.createObjectURL(res.data)
  window.open(url, '_blank', 'noopener')
}

async function load() {
  err.value = null
  try {
    const [inv, d] = await Promise.all([
      http.get<Row[]>('/api/reports/invoices'),
      http.get<Daily[]>('/api/reports/daily?days=21')
    ])
    invoices.value = inv.data
    daily.value = d.data
  } catch {
    err.value = 'Failed to load reports'
  }
}

onMounted(() => void load())

async function exportCsv() {
  const res = await http.get('/api/reports/invoices/export', { responseType: 'blob' })
  const url = URL.createObjectURL(res.data)
  const a = document.createElement('a')
  a.href = url
  a.download = 'invoices.csv'
  a.click()
  URL.revokeObjectURL(url)
}
</script>

<template>
  <h1>Reports</h1>
  <p class="err" v-if="err">{{ err }}</p>
  <div class="row" style="margin-bottom: 1rem">
    <button type="button" class="btn secondary" @click="load">Refresh</button>
    <button type="button" class="btn" @click="exportCsv">Export invoices CSV</button>
  </div>

  <h2>Daily totals (final invoices)</h2>
  <div class="card">
    <table>
      <thead>
        <tr>
          <th>Date</th>
          <th>Count</th>
          <th>Total</th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="d in daily" :key="d.date">
          <td>{{ d.date }}</td>
          <td>{{ d.invoiceCount }}</td>
          <td>{{ d.grandTotal.toFixed(2) }}</td>
        </tr>
      </tbody>
    </table>
  </div>

  <h2>Recent invoices</h2>
  <div class="card">
    <table>
      <thead>
        <tr>
          <th>#</th>
          <th>When</th>
          <th>Customer</th>
          <th>Total</th>
          <th>Status</th>
          <th>PDF</th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="r in invoices" :key="r.id">
          <td>{{ r.invoiceNumber }}</td>
          <td>{{ new Date(r.createdAt).toLocaleString() }}</td>
          <td>{{ r.customerName }}</td>
          <td>{{ r.grandTotal.toFixed(2) }}</td>
          <td>{{ r.status }}</td>
          <td><button type="button" class="btn secondary" @click="openPdf(r.id)">PDF</button></td>
        </tr>
      </tbody>
    </table>
  </div>
</template>
