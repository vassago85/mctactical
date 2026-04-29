<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { http } from '@/api/http'
import { useAuthStore } from '@/stores/auth'
import { useToast } from '@/composables/useToast'
import { formatZAR } from '@/utils/format'
import McPageHeader from '@/components/ui/McPageHeader.vue'
import McCard from '@/components/ui/McCard.vue'
import McButton from '@/components/ui/McButton.vue'
import McField from '@/components/ui/McField.vue'
import McAlert from '@/components/ui/McAlert.vue'
import McBadge from '@/components/ui/McBadge.vue'
import McSpinner from '@/components/ui/McSpinner.vue'
import McEmptyState from '@/components/ui/McEmptyState.vue'
import McModal from '@/components/ui/McModal.vue'
import McCheckbox from '@/components/ui/McCheckbox.vue'
import { Plus, UserPlus, Trash2, Search } from 'lucide-vue-next'

type Customer = {
  id: string
  email: string
  name?: string | null
  phone?: string | null
  company?: string | null
  address?: string | null
  vatNumber?: string | null
  customerType?: string | null
  tradeAccount: boolean
  accountEnabled: boolean
  creditLimit: number
  paymentTermsDays: number
  createdAt: string
  updatedAt: string
}

type EditableCustomer = {
  name: string
  email: string
  phone: string
  company: string
  address: string
  vatNumber: string
  customerType: string
  tradeAccount: boolean
  accountEnabled: boolean
  creditLimit: number
  paymentTermsDays: number
}

const auth = useAuthStore()
const toast = useToast()

const canManageAr = computed(() => auth.hasRole('Admin', 'Owner', 'Dev'))

const q = ref('')
const items = ref<Customer[]>([])
const busy = ref(false)
const err = ref<string | null>(null)

const editorOpen = ref(false)
const editingId = ref<string | null>(null)
const saving = ref(false)
const draft = ref<EditableCustomer>(blankDraft())

let debounce: ReturnType<typeof setTimeout> | null = null
watch(q, () => {
  if (debounce) clearTimeout(debounce)
  debounce = setTimeout(() => void load(), 250)
})

onMounted(load)

function blankDraft(): EditableCustomer {
  return {
    name: '',
    email: '',
    phone: '',
    company: '',
    address: '',
    vatNumber: '',
    customerType: '',
    tradeAccount: false,
    accountEnabled: false,
    creditLimit: 0,
    paymentTermsDays: 30
  }
}

async function load() {
  busy.value = true
  err.value = null
  try {
    const params = new URLSearchParams()
    if (q.value.trim()) params.set('q', q.value.trim())
    params.set('take', '500')
    const { data } = await http.get<Customer[]>(`/api/customers?${params.toString()}`)
    items.value = data
  } catch (e: any) {
    err.value = e?.response?.data?.error ?? e?.message ?? 'Could not load customers.'
  } finally {
    busy.value = false
  }
}

function openCreate() {
  editingId.value = null
  draft.value = blankDraft()
  editorOpen.value = true
}

function openEdit(c: Customer) {
  editingId.value = c.id
  draft.value = {
    name: c.name ?? '',
    email: c.email ?? '',
    phone: c.phone ?? '',
    company: c.company ?? '',
    address: c.address ?? '',
    vatNumber: c.vatNumber ?? '',
    customerType: c.customerType ?? '',
    tradeAccount: !!c.tradeAccount,
    accountEnabled: !!c.accountEnabled,
    creditLimit: Number(c.creditLimit) || 0,
    paymentTermsDays: Number(c.paymentTermsDays) || 30
  }
  editorOpen.value = true
}

function closeEditor() {
  if (saving.value) return
  editorOpen.value = false
  editingId.value = null
}

async function save() {
  if (!draft.value.email.trim()) {
    toast.error('Email is required.')
    return
  }
  saving.value = true
  try {
    const payload: Record<string, any> = {
      email: draft.value.email.trim().toLowerCase(),
      name: draft.value.name.trim() || null,
      phone: draft.value.phone.trim() || null,
      company: draft.value.company.trim() || null,
      address: draft.value.address.trim() || null,
      vatNumber: draft.value.vatNumber.trim() || null,
      customerType: draft.value.customerType.trim() || null
    }
    if (canManageAr.value) {
      payload.tradeAccount = !!draft.value.tradeAccount
      payload.accountEnabled = !!draft.value.accountEnabled
      payload.creditLimit = Math.max(0, Number(draft.value.creditLimit) || 0)
      payload.paymentTermsDays = Math.max(0, Math.min(365, Number(draft.value.paymentTermsDays) || 0))
    }

    if (editingId.value) {
      await http.put(`/api/customers/${editingId.value}`, payload)
      toast.success('Customer updated.')
    } else {
      await http.post('/api/customers', payload)
      toast.success('Customer created.')
    }
    editorOpen.value = false
    editingId.value = null
    await load()
  } catch (e: any) {
    const msg = e?.response?.data?.error ?? e?.message ?? 'Could not save customer.'
    toast.error(msg)
  } finally {
    saving.value = false
  }
}

async function remove(c: Customer) {
  if (!canManageAr.value) return
  const label = c.name || c.company || c.email
  if (!confirm(`Delete customer "${label}"? This cannot be undone.`)) return
  try {
    await http.delete(`/api/customers/${c.id}`)
    toast.success('Customer deleted.')
    items.value = items.value.filter((x) => x.id !== c.id)
  } catch (e: any) {
    const msg = e?.response?.data?.error ?? e?.message ?? 'Could not delete customer.'
    toast.error(msg)
  }
}
</script>

<template>
  <div class="customers-page">
    <McPageHeader
      title="Customers"
      :description="`${items.length} customer${items.length === 1 ? '' : 's'}${q.trim() ? ' matching search' : ''}`"
    >
      <template #actions>
        <McButton variant="primary" @click="openCreate">
          <UserPlus :size="16" />
          New customer
        </McButton>
      </template>
    </McPageHeader>

    <McCard class="customers-page__filters">
      <McField label="Search">
        <div class="customers-page__search">
          <Search :size="16" class="customers-page__search-icon" />
          <input
            v-model="q"
            type="search"
            class="mc-input"
            placeholder="Name, company, email or phone"
          />
        </div>
      </McField>
    </McCard>

    <McAlert v-if="err" variant="error" class="customers-page__alert">{{ err }}</McAlert>

    <McCard :padded="false">
      <div v-if="busy && items.length === 0" class="customers-page__loading">
        <McSpinner /> Loading customers…
      </div>
      <McEmptyState
        v-else-if="!busy && items.length === 0"
        title="No customers yet"
        :hint="q.trim() ? 'Try a different search term, or create a new customer.' : 'Create your first customer to get started.'"
      >
        <McButton variant="primary" @click="openCreate">
          <Plus :size="16" />
          New customer
        </McButton>
      </McEmptyState>
      <div v-else class="customers-page__table-wrap">
        <table class="customers-page__table">
          <thead>
            <tr>
              <th>Name</th>
              <th>Company</th>
              <th>Email</th>
              <th>Phone</th>
              <th>Type</th>
              <th class="customers-page__col-num">Credit limit</th>
              <th class="customers-page__col-num">Terms</th>
              <th class="customers-page__col-actions"></th>
            </tr>
          </thead>
          <tbody>
            <tr
              v-for="c in items"
              :key="c.id"
              class="customers-page__row"
              @click="openEdit(c)"
            >
              <td>{{ c.name || '—' }}</td>
              <td>{{ c.company || '—' }}</td>
              <td class="customers-page__email">{{ c.email }}</td>
              <td>{{ c.phone || '—' }}</td>
              <td>
                <div class="customers-page__badges">
                  <McBadge v-if="c.accountEnabled" variant="info">Account</McBadge>
                  <McBadge v-if="c.tradeAccount" variant="success">Trade</McBadge>
                  <span v-if="!c.accountEnabled && !c.tradeAccount" class="customers-page__muted">—</span>
                </div>
              </td>
              <td class="customers-page__col-num">
                {{ c.accountEnabled && c.creditLimit > 0 ? formatZAR(c.creditLimit) : '—' }}
              </td>
              <td class="customers-page__col-num">
                {{ c.accountEnabled ? `Net ${c.paymentTermsDays}` : '—' }}
              </td>
              <td class="customers-page__col-actions" @click.stop>
                <McButton
                  v-if="canManageAr"
                  variant="ghost"
                  dense
                  aria-label="Delete customer"
                  @click="remove(c)"
                >
                  <Trash2 :size="14" />
                </McButton>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </McCard>

    <McModal
      v-model="editorOpen"
      :title="editingId ? 'Edit customer' : 'New customer'"
      :close-on-backdrop="!saving"
    >
      <div class="customers-page__form">
        <div class="customers-page__form-grid">
          <McField label="Name">
            <input v-model="draft.name" type="text" class="mc-input" placeholder="Full name" />
          </McField>
          <McField label="Email *">
            <input
              v-model="draft.email"
              type="email"
              class="mc-input"
              required
              placeholder="customer@example.com"
              autocomplete="email"
            />
          </McField>
          <McField label="Phone">
            <input v-model="draft.phone" type="tel" class="mc-input" placeholder="+27…" />
          </McField>
          <McField label="Company">
            <input v-model="draft.company" type="text" class="mc-input" placeholder="Optional" />
          </McField>
          <McField label="VAT number">
            <input v-model="draft.vatNumber" type="text" class="mc-input" placeholder="Optional" />
          </McField>
          <McField label="Customer type">
            <input v-model="draft.customerType" type="text" class="mc-input" placeholder="e.g. Retail" />
          </McField>
          <McField label="Address" class="customers-page__form-grid--full">
            <textarea v-model="draft.address" rows="2" class="mc-input mc-input--textarea" placeholder="Optional"></textarea>
          </McField>
        </div>

        <div v-if="canManageAr" class="customers-page__ar">
          <div class="customers-page__ar-header">
            <h4>Account &amp; credit</h4>
            <p>Used by Accounts Receivable. Leave both off for a normal walk-in customer.</p>
          </div>
          <div class="customers-page__ar-grid">
            <McCheckbox v-model="draft.tradeAccount" label="Trade customer" />
            <McCheckbox v-model="draft.accountEnabled" label="Allow account sales (AR)" />
            <McField label="Credit limit (R)">
              <input
                v-model.number="draft.creditLimit"
                type="number"
                min="0"
                step="100"
                class="mc-input"
                :disabled="!draft.accountEnabled"
              />
            </McField>
            <McField label="Payment terms (days)">
              <input
                v-model.number="draft.paymentTermsDays"
                type="number"
                min="0"
                max="365"
                step="1"
                class="mc-input"
                :disabled="!draft.accountEnabled"
              />
            </McField>
          </div>
        </div>
      </div>
      <template #footer>
        <McButton variant="secondary" :disabled="saving" @click="closeEditor">Cancel</McButton>
        <McButton variant="primary" :disabled="saving" @click="save">
          {{ saving ? 'Saving…' : (editingId ? 'Save changes' : 'Create customer') }}
        </McButton>
      </template>
    </McModal>
  </div>
</template>

<style scoped>
.customers-page {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.customers-page__filters {
  margin-bottom: 0;
}

.customers-page__search {
  display: flex;
  align-items: center;
  gap: 8px;
}

.customers-page__search-icon {
  color: var(--mc-text-muted, #888);
  flex-shrink: 0;
}

.customers-page__search input {
  flex: 1;
}

.customers-page__alert {
  margin: 0;
}

.customers-page__loading {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 32px;
  color: var(--mc-text-muted, #888);
  justify-content: center;
}

.customers-page__table-wrap {
  overflow-x: auto;
}

.customers-page__table {
  width: 100%;
  border-collapse: collapse;
  font-size: 14px;
}

.customers-page__table thead th {
  text-align: left;
  padding: 12px 16px;
  font-weight: 600;
  color: var(--mc-text-muted, #666);
  border-bottom: 1px solid var(--mc-border, #e5e7eb);
  background: var(--mc-surface-alt, #fafafa);
  font-size: 12px;
  text-transform: uppercase;
  letter-spacing: 0.04em;
}

.customers-page__table tbody td {
  padding: 14px 16px;
  border-bottom: 1px solid var(--mc-border-subtle, #f0f0f0);
  vertical-align: middle;
}

.customers-page__row {
  cursor: pointer;
  transition: background 120ms ease;
}

.customers-page__row:hover {
  background: var(--mc-surface-alt, #fafafa);
}

.customers-page__col-num {
  text-align: right;
  font-variant-numeric: tabular-nums;
  white-space: nowrap;
}

.customers-page__col-actions {
  width: 1%;
  text-align: right;
}

.customers-page__email {
  font-family: ui-monospace, SFMono-Regular, Menlo, monospace;
  font-size: 13px;
  color: var(--mc-text-muted, #555);
}

.customers-page__badges {
  display: flex;
  gap: 6px;
  flex-wrap: wrap;
}

.customers-page__muted {
  color: var(--mc-text-muted, #999);
}

.customers-page__form {
  display: flex;
  flex-direction: column;
  gap: 20px;
}

.customers-page__form-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 12px 16px;
}

.customers-page__form-grid--full {
  grid-column: 1 / -1;
}

.customers-page__ar {
  border-top: 1px solid var(--mc-border, #e5e7eb);
  padding-top: 16px;
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.customers-page__ar-header h4 {
  margin: 0 0 4px;
  font-size: 14px;
  font-weight: 600;
}

.customers-page__ar-header p {
  margin: 0;
  font-size: 12px;
  color: var(--mc-text-muted, #888);
}

.customers-page__ar-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 12px 16px;
  align-items: end;
}

@media (max-width: 700px) {
  .customers-page__form-grid,
  .customers-page__ar-grid {
    grid-template-columns: 1fr;
  }
}
</style>
