<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { http } from '@/api/http'
import { useToast } from '@/composables/useToast'
import { formatZAR } from '@/utils/format'
import McPageHeader from '@/components/ui/McPageHeader.vue'
import McCard from '@/components/ui/McCard.vue'
import McButton from '@/components/ui/McButton.vue'
import McAlert from '@/components/ui/McAlert.vue'
import McBadge from '@/components/ui/McBadge.vue'
import McSpinner from '@/components/ui/McSpinner.vue'
import McEmptyState from '@/components/ui/McEmptyState.vue'
import McModal from '@/components/ui/McModal.vue'

type Delivery = {
  id: string
  invoiceNumber: string
  customerName: string | null
  customerEmail: string | null
  grandTotal: number
  createdAt: string
  isDelivered: boolean
  deliveredAt: string | null
  deliveryNotes: string | null
  itemsSummary: string
}

const toast = useToast()
const deliveries = ref<Delivery[]>([])
const busy = ref(false)
const err = ref<string | null>(null)
const filter = ref<'pending' | 'delivered' | 'all'>('pending')

const showDeliverModal = ref(false)
const deliverTarget = ref<Delivery | null>(null)
const deliverNotes = ref('')
const deliverBusy = ref(false)

async function load() {
  busy.value = true
  err.value = null
  try {
    const { data } = await http.get<Delivery[]>('/api/invoices/pending-deliveries', {
      params: { filter: filter.value }
    })
    deliveries.value = data
  } catch {
    err.value = 'Could not load deliveries'
  } finally {
    busy.value = false
  }
}

function openDeliverModal(d: Delivery) {
  deliverTarget.value = d
  deliverNotes.value = ''
  showDeliverModal.value = true
}

async function markDelivered() {
  if (!deliverTarget.value) return
  deliverBusy.value = true
  try {
    await http.post(`/api/invoices/${deliverTarget.value.id}/mark-delivered`, {
      notes: deliverNotes.value || null
    })
    toast.success(`${deliverTarget.value.invoiceNumber} marked as delivered`)
    showDeliverModal.value = false
    await load()
  } catch {
    toast.error('Failed to mark as delivered')
  } finally {
    deliverBusy.value = false
  }
}

function openOrderConfirmation(d: Delivery) {
  window.open(`/api/invoices/${d.id}/order-confirmation-pdf`, '_blank')
}

function openInvoicePdf(d: Delivery) {
  window.open(`/api/invoices/${d.id}/pdf`, '_blank')
}

function formatDate(iso: string) {
  return new Date(iso).toLocaleDateString('en-ZA', { day: 'numeric', month: 'short', year: 'numeric' })
}

onMounted(() => void load())
</script>

<template>
  <div>
    <McPageHeader title="Deliveries">
      <template #default>
        <span>Track special orders that need to be delivered to customers.</span>
      </template>
    </McPageHeader>

    <McCard title="Filter">
      <div class="del-filter-row">
        <McButton
          v-for="f in (['pending', 'delivered', 'all'] as const)"
          :key="f"
          :variant="filter === f ? 'primary' : 'ghost'"
          dense
          type="button"
          @click="filter = f; load()"
        >
          {{ f.charAt(0).toUpperCase() + f.slice(1) }}
        </McButton>
        <McSpinner v-if="busy" />
      </div>
    </McCard>

    <McAlert v-if="err" variant="error">{{ err }}</McAlert>

    <McCard>
      <McEmptyState
        v-if="!busy && !deliveries.length"
        title="No deliveries"
        :hint="filter === 'pending' ? 'No pending special orders.' : 'No special orders match this filter.'"
      />

      <div v-if="deliveries.length" class="del-table-wrap">
        <table class="mc-table">
          <thead>
            <tr>
              <th>Invoice</th>
              <th>Date</th>
              <th>Customer</th>
              <th>Items</th>
              <th class="del-num">Total</th>
              <th>Status</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="d in deliveries" :key="d.id">
              <td class="del-mono">{{ d.invoiceNumber }}</td>
              <td>{{ formatDate(d.createdAt) }}</td>
              <td>{{ d.customerName || d.customerEmail || '—' }}</td>
              <td class="del-items">{{ d.itemsSummary }}</td>
              <td class="del-num">{{ formatZAR(d.grandTotal) }}</td>
              <td>
                <McBadge v-if="d.isDelivered" variant="success">Delivered</McBadge>
                <McBadge v-else variant="warning">Pending</McBadge>
                <div v-if="d.deliveredAt" class="del-meta">{{ formatDate(d.deliveredAt) }}</div>
                <div v-if="d.deliveryNotes" class="del-meta">{{ d.deliveryNotes }}</div>
              </td>
              <td>
                <div class="del-actions">
                  <McButton variant="ghost" dense type="button" @click="openOrderConfirmation(d)">Confirmation</McButton>
                  <McButton variant="ghost" dense type="button" @click="openInvoicePdf(d)">Invoice</McButton>
                  <McButton v-if="!d.isDelivered" variant="primary" dense type="button" @click="openDeliverModal(d)">Mark delivered</McButton>
                </div>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </McCard>

    <McModal v-model="showDeliverModal" title="Mark as delivered">
      <template v-if="deliverTarget">
        <p>Mark <strong>{{ deliverTarget.invoiceNumber }}</strong> ({{ deliverTarget.customerName || 'No name' }}) as delivered?</p>
        <div style="margin-top: 0.75rem">
          <label for="deliver-notes" style="font-weight: 600; font-size: 0.85rem">Delivery notes (optional)</label>
          <textarea id="deliver-notes" v-model="deliverNotes" rows="2" style="width: 100%; margin-top: 0.25rem" placeholder="e.g. Collected in store, courier ref #123" />
        </div>
      </template>
      <template #footer>
        <McButton variant="secondary" type="button" @click="showDeliverModal = false">Cancel</McButton>
        <McButton variant="primary" type="button" :disabled="deliverBusy" @click="markDelivered">
          <McSpinner v-if="deliverBusy" />
          <span v-else>Confirm delivered</span>
        </McButton>
      </template>
    </McModal>
  </div>
</template>

<style scoped>
.del-filter-row {
  display: flex;
  gap: 0.5rem;
  align-items: center;
}

.del-table-wrap {
  overflow-x: auto;
  -webkit-overflow-scrolling: touch;
}

.del-num {
  text-align: right;
  font-variant-numeric: tabular-nums;
}

.del-mono {
  font-family: 'SF Mono', 'Fira Code', 'Consolas', monospace;
  font-size: 0.85rem;
}

.del-items {
  max-width: 300px;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.del-meta {
  font-size: 0.78rem;
  color: var(--mc-app-text-muted, #5c5a56);
  margin-top: 0.15rem;
}

.del-actions {
  display: flex;
  gap: 0.35rem;
  flex-wrap: wrap;
}
</style>
