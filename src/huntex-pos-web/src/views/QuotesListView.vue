<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { http } from '@/api/http'
import { formatZAR } from '@/utils/format'
import McPageHeader from '@/components/ui/McPageHeader.vue'
import McCard from '@/components/ui/McCard.vue'
import McButton from '@/components/ui/McButton.vue'
import McAlert from '@/components/ui/McAlert.vue'
import McBadge from '@/components/ui/McBadge.vue'
import McSpinner from '@/components/ui/McSpinner.vue'
import McEmptyState from '@/components/ui/McEmptyState.vue'
import McField from '@/components/ui/McField.vue'
import { Plus, Eye } from 'lucide-vue-next'

type QuoteListItem = {
  id: string
  quoteNumber: string
  status: string
  customerName: string | null
  customerCompany: string | null
  grandTotal: number
  createdAt: string
  validUntil: string | null
  convertedInvoiceId: string | null
}

const router = useRouter()

const items = ref<QuoteListItem[]>([])
const busy = ref(false)
const err = ref<string | null>(null)
const statusFilter = ref<string>('')
const search = ref('')

const statuses = ['Draft', 'Sent', 'Accepted', 'Rejected', 'Expired', 'Converted']

async function load() {
  busy.value = true
  err.value = null
  try {
    const { data } = await http.get<QuoteListItem[]>('/api/quotes', {
      params: { status: statusFilter.value || undefined, search: search.value || undefined, take: 200 }
    })
    items.value = data
  } catch {
    err.value = 'Could not load quotes'
  } finally {
    busy.value = false
  }
}

let searchTimer: ReturnType<typeof setTimeout> | null = null
watch(search, () => {
  if (searchTimer) clearTimeout(searchTimer)
  searchTimer = setTimeout(() => void load(), 300)
})

watch(statusFilter, () => void load())

onMounted(() => void load())

function openNew() {
  router.push('/quotes/new')
}

function open(id: string) {
  router.push(`/quotes/${id}`)
}

function badgeVariant(status: string): 'success' | 'warning' | 'danger' | 'neutral' | 'accent' {
  switch (status) {
    case 'Accepted':
    case 'Converted':
      return 'success'
    case 'Sent':
      return 'accent'
    case 'Rejected':
    case 'Expired':
      return 'danger'
    case 'Draft':
      return 'warning'
    default:
      return 'neutral'
  }
}

function formatDate(iso: string | null) {
  if (!iso) return '—'
  return new Date(iso).toLocaleDateString('en-ZA', { day: 'numeric', month: 'short', year: 'numeric' })
}

const totalGrand = computed(() => items.value.reduce((s, i) => s + i.grandTotal, 0))
</script>

<template>
  <div>
    <McPageHeader title="Quotes">
      <template #default>
        <span>Create and track customer quotes. Accepted quotes convert into invoices.</span>
      </template>
      <template #actions>
        <McButton variant="primary" @click="openNew">
          <Plus :size="16" /> New quote
        </McButton>
      </template>
    </McPageHeader>

    <McCard title="Filter">
      <div class="q-filter-row">
        <McField label="Status">
          <select v-model="statusFilter">
            <option value="">All statuses</option>
            <option v-for="s in statuses" :key="s" :value="s">{{ s }}</option>
          </select>
        </McField>
        <McField label="Search">
          <input v-model="search" type="search" placeholder="Quote number, customer, company…" />
        </McField>
        <McSpinner v-if="busy" />
      </div>
    </McCard>

    <McAlert v-if="err" variant="error">{{ err }}</McAlert>

    <McCard>
      <McEmptyState
        v-if="!busy && !items.length"
        title="No quotes yet"
        hint="Create your first quote to send to a customer."
      >
        <McButton variant="primary" @click="openNew"><Plus :size="16" />New quote</McButton>
      </McEmptyState>

      <div v-if="items.length" class="q-list-wrap">
        <table class="mc-table">
          <thead>
            <tr>
              <th>Quote #</th>
              <th>Date</th>
              <th>Customer</th>
              <th>Valid until</th>
              <th class="q-num">Total</th>
              <th>Status</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="q in items" :key="q.id" class="q-row" @click="open(q.id)">
              <td class="q-mono">{{ q.quoteNumber }}</td>
              <td>{{ formatDate(q.createdAt) }}</td>
              <td>{{ q.customerCompany || q.customerName || '—' }}</td>
              <td>{{ formatDate(q.validUntil) }}</td>
              <td class="q-num">{{ formatZAR(q.grandTotal) }}</td>
              <td><McBadge :variant="badgeVariant(q.status)">{{ q.status }}</McBadge></td>
              <td class="q-actions" @click.stop>
                <McButton variant="ghost" dense @click="open(q.id)">
                  <Eye :size="14" /> View
                </McButton>
              </td>
            </tr>
          </tbody>
          <tfoot v-if="items.length">
            <tr>
              <td colspan="4" class="q-foot-label">Total ({{ items.length }} quote{{ items.length === 1 ? '' : 's' }})</td>
              <td class="q-num q-foot">{{ formatZAR(totalGrand) }}</td>
              <td colspan="2"></td>
            </tr>
          </tfoot>
        </table>
      </div>
    </McCard>
  </div>
</template>

<style scoped>
.q-filter-row {
  display: grid;
  grid-template-columns: minmax(180px, 220px) minmax(220px, 1fr) auto;
  gap: 1rem;
  align-items: end;
}
@media (max-width: 640px) {
  .q-filter-row { grid-template-columns: 1fr; }
}
.q-list-wrap {
  overflow-x: auto;
}
.q-num {
  text-align: right;
  font-variant-numeric: tabular-nums;
  white-space: nowrap;
}
.q-mono {
  font-family: 'JetBrains Mono', ui-monospace, monospace;
  font-size: 0.88rem;
  white-space: nowrap;
}
.q-row { cursor: pointer; }
.q-row:hover { background: var(--mc-app-surface-muted, #f5f3ef); }
.q-actions { text-align: right; white-space: nowrap; }
.q-foot-label {
  text-align: right;
  font-weight: 600;
  color: var(--mc-app-text-muted, #5c5a56);
}
.q-foot {
  font-weight: 700;
  color: var(--mc-app-text, #1a1a1c);
}
</style>
