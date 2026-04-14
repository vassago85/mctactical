<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { http } from '@/api/http'
import { useToast } from '@/composables/useToast'
import { formatZAR } from '@/utils/format'
import McPageHeader from '@/components/ui/McPageHeader.vue'
import McCard from '@/components/ui/McCard.vue'
import McButton from '@/components/ui/McButton.vue'
import McAlert from '@/components/ui/McAlert.vue'

type Row = {
  id: string
  invoiceNumber: string
  status: string
  grandTotal: number
  createdAt: string
  customerName?: string | null
}
type Daily = { date: string; invoiceCount: number; grandTotal: number }

const toast = useToast()
const invoices = ref<Row[]>([])
const daily = ref<Daily[]>([])
const err = ref<string | null>(null)

async function openPdf(id: string) {
  try {
    const res = await http.get(`/api/invoices/${id}/pdf`, { responseType: 'blob' })
    const url = URL.createObjectURL(res.data)
    window.open(url, '_blank', 'noopener')
  } catch {
    toast.error('Could not open PDF')
  }
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
  } catch (e: unknown) {
    const ax = e as { response?: { status?: number; data?: { error?: string } } }
    const status = ax.response?.status
    const detail = ax.response?.data?.error
    if (status === 401 || status === 403) {
      err.value = 'You do not have permission to view reports. Only Admin / Owner roles can access this page.'
    } else {
      err.value = detail ? `Failed to load reports: ${detail}` : 'Failed to load reports — check that the API is running.'
    }
  }
}

onMounted(() => void load())

async function exportCsv() {
  try {
    const res = await http.get('/api/reports/invoices/export', { responseType: 'blob' })
    const url = URL.createObjectURL(res.data)
    const a = document.createElement('a')
    a.href = url
    a.download = 'invoices.csv'
    a.click()
    URL.revokeObjectURL(url)
    toast.success('Invoices exported')
  } catch {
    toast.error('Export failed')
  }
}
</script>

<template>
  <div class="rep-page">
    <McPageHeader title="Reports">
      <template #actions>
        <McButton variant="secondary" type="button" @click="load">Refresh</McButton>
        <McButton variant="primary" type="button" @click="exportCsv">Export invoices CSV</McButton>
      </template>
    </McPageHeader>

    <McAlert v-if="err" variant="error">{{ err }}</McAlert>

    <McCard title="Daily totals (final invoices)">
      <div class="rep-table-wrap">
        <table class="mc-table">
          <thead>
            <tr>
              <th>Date</th>
              <th class="rep-num">Count</th>
              <th class="rep-num">Total</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="d in daily" :key="d.date">
              <td>{{ d.date }}</td>
              <td class="rep-num">{{ d.invoiceCount }}</td>
              <td class="rep-num">{{ formatZAR(d.grandTotal) }}</td>
            </tr>
          </tbody>
        </table>
      </div>
    </McCard>

    <McCard title="Recent invoices">
      <div class="rep-table-wrap">
        <table class="mc-table">
          <thead>
            <tr>
              <th>Invoice</th>
              <th>When</th>
              <th>Customer</th>
              <th class="rep-num">Total</th>
              <th>Status</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="r in invoices" :key="r.id">
              <td class="rep-mono">{{ r.invoiceNumber }}</td>
              <td>{{ new Date(r.createdAt).toLocaleString() }}</td>
              <td>{{ r.customerName ?? '—' }}</td>
              <td class="rep-num">{{ formatZAR(r.grandTotal) }}</td>
              <td>{{ r.status }}</td>
              <td>
                <McButton variant="secondary" type="button" @click="openPdf(r.id)">PDF</McButton>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </McCard>
  </div>
</template>

<style scoped>
.rep-page {
  min-height: 100%;
}

.rep-table-wrap {
  overflow-x: auto;
  -webkit-overflow-scrolling: touch;
}

.rep-num {
  text-align: right;
  font-variant-numeric: tabular-nums;
}

.rep-mono {
  font-variant-numeric: tabular-nums;
  font-weight: 600;
}
</style>
