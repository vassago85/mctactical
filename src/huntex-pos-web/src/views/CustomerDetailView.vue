<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
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
import { ArrowLeft, Pencil, Receipt, FileText, CreditCard, AlertTriangle } from 'lucide-vue-next'

type AccountInvoice = {
  id: string
  invoiceNumber: string
  createdAt: string
  dueDate: string | null
  grandTotal: number
  amountPaid: number
  amountOutstanding: number
  paymentStatus: string
}

type AccountPayment = {
  id: string
  invoiceId: string | null
  invoiceNumber: string | null
  amount: number
  method: string
  reference: string | null
  paidAt: string
}

type CustomerAccount = {
  customerId: string
  name: string | null
  email: string | null
  company: string | null
  tradeAccount: boolean
  accountEnabled: boolean
  creditLimit: number
  paymentTermsDays: number
  balance: number
  creditAvailable: number
  openInvoiceCount: number
  overdueInvoiceCount: number
  openInvoices: AccountInvoice[]
  recentPayments: AccountPayment[]
}

type CustomerProfile = {
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
}

const route = useRoute()
const router = useRouter()
const auth = useAuthStore()
const toast = useToast()

const id = computed(() => String(route.params.id))
const canManageAr = computed(() => auth.hasRole('Admin', 'Owner', 'Dev'))

const account = ref<CustomerAccount | null>(null)
const profile = ref<CustomerProfile | null>(null)
const loading = ref(false)
const err = ref<string | null>(null)
const tab = ref<'profile' | 'invoices' | 'payments'>('invoices')

// ---- profile edit modal ----
const editOpen = ref(false)
const editSaving = ref(false)
const editDraft = ref<CustomerProfile | null>(null)

// ---- take-payment modal ----
const payOpen = ref(false)
const paySaving = ref(false)
const payAmount = ref<number>(0)
const payMethod = ref<'Cash' | 'Card' | 'EFT' | 'Other'>('Cash')
const payReference = ref('')
const payNotes = ref('')
const paySelectAll = ref(true)
const paySelectedInvoices = ref<Set<string>>(new Set())

onMounted(load)

async function load() {
  loading.value = true
  err.value = null
  try {
    const [{ data: acc }, { data: prof }] = await Promise.all([
      http.get<CustomerAccount>(`/api/customers/${id.value}/account`),
      http.get<CustomerProfile>(`/api/customers/${id.value}`)
    ])
    account.value = acc
    profile.value = prof
  } catch (e: any) {
    err.value = e?.response?.data?.error ?? e?.message ?? 'Could not load customer.'
  } finally {
    loading.value = false
  }
}

function fmtDate(iso: string | null | undefined): string {
  if (!iso) return '—'
  const d = new Date(iso)
  if (isNaN(d.getTime())) return '—'
  return d.toLocaleDateString('en-ZA', { year: 'numeric', month: 'short', day: '2-digit' })
}

function fmtDateTime(iso: string): string {
  const d = new Date(iso)
  if (isNaN(d.getTime())) return '—'
  return d.toLocaleString('en-ZA', {
    year: 'numeric', month: 'short', day: '2-digit', hour: '2-digit', minute: '2-digit'
  })
}

function isOverdue(inv: AccountInvoice): boolean {
  if (!inv.dueDate) return false
  return new Date(inv.dueDate).getTime() < Date.now() && inv.amountOutstanding > 0
}

const balanceVariant = computed<'success' | 'warning' | 'error'>(() => {
  if (!account.value) return 'success'
  if (account.value.overdueInvoiceCount > 0) return 'error'
  if (account.value.balance > 0) return 'warning'
  return 'success'
})

const overLimit = computed(() => {
  const a = account.value
  if (!a) return false
  return a.creditLimit > 0 && a.balance > a.creditLimit
})

// ---- profile edit ----
function openEdit() {
  if (!profile.value) return
  editDraft.value = { ...profile.value }
  editOpen.value = true
}

async function saveEdit() {
  if (!editDraft.value) return
  editSaving.value = true
  try {
    const d = editDraft.value
    const payload: Record<string, any> = {
      email: (d.email || '').trim().toLowerCase(),
      name: (d.name || '')?.trim() || null,
      phone: (d.phone || '')?.trim() || null,
      company: (d.company || '')?.trim() || null,
      address: (d.address || '')?.trim() || null,
      vatNumber: (d.vatNumber || '')?.trim() || null,
      customerType: (d.customerType || '')?.trim() || null
    }
    if (canManageAr.value) {
      payload.tradeAccount = !!d.tradeAccount
      payload.accountEnabled = !!d.accountEnabled
      payload.creditLimit = Math.max(0, Number(d.creditLimit) || 0)
      payload.paymentTermsDays = Math.max(0, Math.min(365, Number(d.paymentTermsDays) || 0))
    }
    await http.put(`/api/customers/${id.value}`, payload)
    toast.success('Customer updated.')
    editOpen.value = false
    await load()
  } catch (e: any) {
    toast.error(e?.response?.data?.error ?? e?.message ?? 'Could not save customer.')
  } finally {
    editSaving.value = false
  }
}

// ---- take payment ----
function openTakePayment() {
  if (!account.value) return
  // Default amount = current balance (one click to settle in full).
  payAmount.value = Math.max(0, Number(account.value.balance.toFixed(2)))
  payMethod.value = 'Cash'
  payReference.value = ''
  payNotes.value = ''
  paySelectAll.value = true
  paySelectedInvoices.value = new Set(account.value.openInvoices.map((i) => i.id))
  payOpen.value = true
}

function toggleInvoice(invId: string) {
  paySelectAll.value = false
  const next = new Set(paySelectedInvoices.value)
  if (next.has(invId)) next.delete(invId)
  else next.add(invId)
  paySelectedInvoices.value = next
}

function onSelectAllChange(v: boolean) {
  paySelectAll.value = v
  if (v && account.value) {
    paySelectedInvoices.value = new Set(account.value.openInvoices.map((i) => i.id))
  }
}

const payAllocSummary = computed(() => {
  if (paySelectAll.value) return 'auto-allocate oldest invoice first'
  return `${paySelectedInvoices.value.size} invoice(s) selected`
})

async function submitPayment() {
  if (!account.value) return
  const amt = Math.max(0, Number(payAmount.value) || 0)
  if (amt <= 0) {
    toast.error('Enter a payment amount greater than zero.')
    return
  }
  paySaving.value = true
  try {
    const payload: Record<string, any> = {
      amount: amt,
      method: payMethod.value,
      reference: payReference.value.trim() || null,
      notes: payNotes.value.trim() || null
    }
    if (!paySelectAll.value && paySelectedInvoices.value.size > 0) {
      // Send in invoice creation order so the user gets predictable allocation.
      const ordered = account.value.openInvoices
        .filter((i) => paySelectedInvoices.value.has(i.id))
        .map((i) => i.id)
      payload.applyToInvoiceIds = ordered
    }
    const { data } = await http.post<{ unallocatedCredit: number; newBalance: number }>(
      `/api/customers/${id.value}/payments`,
      payload
    )
    if (data.unallocatedCredit > 0) {
      toast.success(`Payment recorded. ${formatZAR(data.unallocatedCredit)} credit available.`)
    } else {
      toast.success(`Payment recorded. New balance ${formatZAR(data.newBalance)}.`)
    }
    payOpen.value = false
    await load()
    tab.value = 'payments'
  } catch (e: any) {
    toast.error(e?.response?.data?.error ?? e?.message ?? 'Could not record payment.')
  } finally {
    paySaving.value = false
  }
}
</script>

<template>
  <div class="cd">
    <McPageHeader
      :title="account?.name || account?.company || account?.email || 'Customer'"
      :description="account?.email || ''"
    >
      <template #actions>
        <McButton variant="ghost" @click="router.push('/customers')">
          <ArrowLeft :size="16" />
          Back
        </McButton>
        <McButton
          v-if="canManageAr"
          variant="secondary"
          :disabled="!profile"
          @click="openEdit"
        >
          <Pencil :size="16" />
          Edit profile
        </McButton>
        <McButton
          v-if="canManageAr"
          variant="primary"
          :disabled="!account || !account.accountEnabled || account.balance <= 0"
          @click="openTakePayment"
        >
          <CreditCard :size="16" />
          Take payment
        </McButton>
      </template>
    </McPageHeader>

    <McAlert v-if="err" variant="error">{{ err }}</McAlert>

    <div v-if="loading && !account" class="cd__loading">
      <McSpinner /> Loading customer…
    </div>

    <template v-else-if="account">
      <McCard class="cd__summary">
        <div class="cd__summary-row">
          <div class="cd__badges">
            <McBadge v-if="account.accountEnabled" variant="info">Account</McBadge>
            <McBadge v-if="account.tradeAccount" variant="success">Trade</McBadge>
            <McBadge v-if="account.overdueInvoiceCount > 0" variant="error">
              <AlertTriangle :size="12" />
              {{ account.overdueInvoiceCount }} overdue
            </McBadge>
            <McBadge v-if="overLimit" variant="warning">Over limit</McBadge>
          </div>

          <div class="cd__pills">
            <div class="cd__pill" :class="`cd__pill--${balanceVariant}`">
              <span class="cd__pill-label">Balance</span>
              <span class="cd__pill-value">{{ formatZAR(account.balance) }}</span>
            </div>
            <div class="cd__pill cd__pill--neutral">
              <span class="cd__pill-label">Credit limit</span>
              <span class="cd__pill-value">{{ account.creditLimit > 0 ? formatZAR(account.creditLimit) : '—' }}</span>
            </div>
            <div class="cd__pill cd__pill--neutral">
              <span class="cd__pill-label">Available</span>
              <span class="cd__pill-value">{{ account.creditLimit > 0 ? formatZAR(account.creditAvailable) : '—' }}</span>
            </div>
            <div class="cd__pill cd__pill--neutral">
              <span class="cd__pill-label">Terms</span>
              <span class="cd__pill-value">{{ account.accountEnabled ? `Net ${account.paymentTermsDays}` : '—' }}</span>
            </div>
          </div>
        </div>
      </McCard>

      <div class="cd__tabs" role="tablist">
        <button
          type="button"
          class="cd__tab"
          :class="{ 'cd__tab--active': tab === 'invoices' }"
          @click="tab = 'invoices'"
        >
          <FileText :size="14" />
          Open invoices
          <span v-if="account.openInvoiceCount > 0" class="cd__tab-count">{{ account.openInvoiceCount }}</span>
        </button>
        <button
          type="button"
          class="cd__tab"
          :class="{ 'cd__tab--active': tab === 'payments' }"
          @click="tab = 'payments'"
        >
          <Receipt :size="14" />
          Recent payments
          <span v-if="account.recentPayments.length > 0" class="cd__tab-count">{{ account.recentPayments.length }}</span>
        </button>
        <button
          type="button"
          class="cd__tab"
          :class="{ 'cd__tab--active': tab === 'profile' }"
          @click="tab = 'profile'"
        >
          Profile
        </button>
      </div>

      <McCard :padded="false">
        <!-- INVOICES -->
        <div v-if="tab === 'invoices'">
          <McEmptyState
            v-if="account.openInvoices.length === 0"
            title="No open invoices"
            hint="All account sales for this customer are settled."
          />
          <div v-else class="cd__table-wrap">
            <table class="cd__table">
              <thead>
                <tr>
                  <th>Invoice</th>
                  <th>Date</th>
                  <th>Due</th>
                  <th>Status</th>
                  <th class="cd__col-num">Total</th>
                  <th class="cd__col-num">Paid</th>
                  <th class="cd__col-num">Outstanding</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="inv in account.openInvoices" :key="inv.id">
                  <td><strong>{{ inv.invoiceNumber }}</strong></td>
                  <td>{{ fmtDate(inv.createdAt) }}</td>
                  <td :class="{ 'cd__overdue': isOverdue(inv) }">
                    {{ fmtDate(inv.dueDate) }}
                    <McBadge v-if="isOverdue(inv)" variant="error">Overdue</McBadge>
                  </td>
                  <td>
                    <McBadge
                      :variant="inv.paymentStatus === 'Partial' ? 'warning'
                        : inv.paymentStatus === 'Overdue' ? 'error' : 'info'"
                    >
                      {{ inv.paymentStatus }}
                    </McBadge>
                  </td>
                  <td class="cd__col-num">{{ formatZAR(inv.grandTotal) }}</td>
                  <td class="cd__col-num">{{ formatZAR(inv.amountPaid) }}</td>
                  <td class="cd__col-num cd__col-strong">{{ formatZAR(inv.amountOutstanding) }}</td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>

        <!-- PAYMENTS -->
        <div v-else-if="tab === 'payments'">
          <McEmptyState
            v-if="account.recentPayments.length === 0"
            title="No payments yet"
            hint="Payments recorded against this customer's account will appear here."
          />
          <div v-else class="cd__table-wrap">
            <table class="cd__table">
              <thead>
                <tr>
                  <th>When</th>
                  <th>Method</th>
                  <th>Reference</th>
                  <th>Applied to</th>
                  <th class="cd__col-num">Amount</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="p in account.recentPayments" :key="p.id">
                  <td>{{ fmtDateTime(p.paidAt) }}</td>
                  <td>{{ p.method }}</td>
                  <td>{{ p.reference || '—' }}</td>
                  <td>
                    <span v-if="p.invoiceNumber"><strong>{{ p.invoiceNumber }}</strong></span>
                    <McBadge v-else variant="info">Unallocated credit</McBadge>
                  </td>
                  <td class="cd__col-num cd__col-strong">{{ formatZAR(p.amount) }}</td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>

        <!-- PROFILE -->
        <div v-else-if="tab === 'profile' && profile" class="cd__profile">
          <dl class="cd__profile-grid">
            <div><dt>Name</dt><dd>{{ profile.name || '—' }}</dd></div>
            <div><dt>Email</dt><dd>{{ profile.email }}</dd></div>
            <div><dt>Phone</dt><dd>{{ profile.phone || '—' }}</dd></div>
            <div><dt>Company</dt><dd>{{ profile.company || '—' }}</dd></div>
            <div><dt>VAT number</dt><dd>{{ profile.vatNumber || '—' }}</dd></div>
            <div><dt>Customer type</dt><dd>{{ profile.customerType || '—' }}</dd></div>
            <div class="cd__profile-grid--full"><dt>Address</dt><dd>{{ profile.address || '—' }}</dd></div>
            <div><dt>Account enabled</dt><dd>{{ profile.accountEnabled ? 'Yes' : 'No' }}</dd></div>
            <div><dt>Trade customer</dt><dd>{{ profile.tradeAccount ? 'Yes' : 'No' }}</dd></div>
            <div>
              <dt>Credit limit</dt>
              <dd>{{ profile.accountEnabled && profile.creditLimit > 0 ? formatZAR(profile.creditLimit) : '—' }}</dd>
            </div>
            <div>
              <dt>Payment terms</dt>
              <dd>{{ profile.accountEnabled ? `Net ${profile.paymentTermsDays}` : '—' }}</dd>
            </div>
          </dl>
        </div>
      </McCard>
    </template>

    <!-- Edit profile modal -->
    <McModal
      v-model="editOpen"
      title="Edit customer"
      :close-on-backdrop="!editSaving"
    >
      <div v-if="editDraft" class="cd__form">
        <div class="cd__form-grid">
          <McField label="Name">
            <input v-model="editDraft.name" type="text" class="mc-input" />
          </McField>
          <McField label="Email *">
            <input v-model="editDraft.email" type="email" class="mc-input" required />
          </McField>
          <McField label="Phone">
            <input v-model="editDraft.phone" type="tel" class="mc-input" />
          </McField>
          <McField label="Company">
            <input v-model="editDraft.company" type="text" class="mc-input" />
          </McField>
          <McField label="VAT number">
            <input v-model="editDraft.vatNumber" type="text" class="mc-input" />
          </McField>
          <McField label="Customer type">
            <input v-model="editDraft.customerType" type="text" class="mc-input" />
          </McField>
          <McField label="Address" class="cd__form-grid--full">
            <textarea v-model="editDraft.address" rows="2" class="mc-input mc-input--textarea" />
          </McField>
        </div>

        <div v-if="canManageAr" class="cd__ar">
          <h4>Account &amp; credit</h4>
          <div class="cd__ar-grid">
            <McCheckbox v-model="editDraft.tradeAccount" label="Trade customer" />
            <McCheckbox v-model="editDraft.accountEnabled" label="Allow account sales (AR)" />
            <McField label="Credit limit (R)">
              <input
                v-model.number="editDraft.creditLimit"
                type="number"
                min="0"
                step="100"
                class="mc-input"
                :disabled="!editDraft.accountEnabled"
              />
            </McField>
            <McField label="Payment terms (days)">
              <input
                v-model.number="editDraft.paymentTermsDays"
                type="number"
                min="0"
                max="365"
                step="1"
                class="mc-input"
                :disabled="!editDraft.accountEnabled"
              />
            </McField>
          </div>
        </div>
      </div>
      <template #footer>
        <McButton variant="secondary" :disabled="editSaving" @click="editOpen = false">Cancel</McButton>
        <McButton variant="primary" :disabled="editSaving" @click="saveEdit">
          {{ editSaving ? 'Saving…' : 'Save changes' }}
        </McButton>
      </template>
    </McModal>

    <!-- Take payment modal -->
    <McModal
      v-model="payOpen"
      title="Take payment"
      :close-on-backdrop="!paySaving"
    >
      <div v-if="account" class="cd__pay">
        <div class="cd__pay-summary">
          <div>Outstanding balance: <strong>{{ formatZAR(account.balance) }}</strong></div>
          <div v-if="account.overdueInvoiceCount > 0" class="cd__pay-warn">
            <AlertTriangle :size="14" />
            {{ account.overdueInvoiceCount }} overdue invoice(s)
          </div>
        </div>

        <div class="cd__pay-grid">
          <McField label="Amount (R) *">
            <input
              v-model.number="payAmount"
              type="number"
              min="0"
              step="0.01"
              class="mc-input"
              autofocus
            />
          </McField>
          <McField label="Method *">
            <select v-model="payMethod" class="mc-input">
              <option value="Cash">Cash</option>
              <option value="Card">Card</option>
              <option value="EFT">EFT</option>
              <option value="Other">Other</option>
            </select>
          </McField>
          <McField label="Reference">
            <input v-model="payReference" type="text" class="mc-input" placeholder="e.g. EFT 24 Apr" />
          </McField>
          <McField label="Notes">
            <input v-model="payNotes" type="text" class="mc-input" />
          </McField>
        </div>

        <div v-if="account.openInvoices.length > 0" class="cd__pay-alloc">
          <div class="cd__pay-alloc-head">
            <h5>Apply to invoices</h5>
            <span class="cd__pay-alloc-summary">{{ payAllocSummary }}</span>
          </div>
          <McCheckbox
            :model-value="paySelectAll"
            label="Auto-allocate (oldest first)"
            @update:model-value="onSelectAllChange"
          />
          <div v-if="!paySelectAll" class="cd__pay-list">
            <label
              v-for="inv in account.openInvoices"
              :key="inv.id"
              class="cd__pay-row"
              :class="{ 'cd__pay-row--checked': paySelectedInvoices.has(inv.id) }"
            >
              <input
                type="checkbox"
                :checked="paySelectedInvoices.has(inv.id)"
                @change="toggleInvoice(inv.id)"
              />
              <span class="cd__pay-row-label">
                <strong>{{ inv.invoiceNumber }}</strong>
                <span class="cd__pay-row-meta">{{ fmtDate(inv.createdAt) }}</span>
              </span>
              <span class="cd__pay-row-amount">{{ formatZAR(inv.amountOutstanding) }}</span>
            </label>
          </div>
        </div>
      </div>
      <template #footer>
        <McButton variant="secondary" :disabled="paySaving" @click="payOpen = false">Cancel</McButton>
        <McButton variant="primary" :disabled="paySaving" @click="submitPayment">
          {{ paySaving ? 'Recording…' : `Record ${formatZAR(payAmount || 0)}` }}
        </McButton>
      </template>
    </McModal>
  </div>
</template>

<style scoped>
.cd { display: flex; flex-direction: column; gap: 16px; }

.cd__loading {
  display: flex; align-items: center; gap: 12px;
  padding: 32px; justify-content: center;
  color: var(--mc-text-muted, #888);
}

.cd__summary { padding: 16px 20px; }
.cd__summary-row {
  display: flex; flex-wrap: wrap; gap: 16px;
  align-items: center; justify-content: space-between;
}

.cd__badges { display: flex; gap: 8px; flex-wrap: wrap; }

.cd__pills { display: flex; flex-wrap: wrap; gap: 8px; }

.cd__pill {
  display: flex; flex-direction: column;
  padding: 8px 14px; border-radius: 10px;
  min-width: 110px;
  border: 1px solid transparent;
  background: var(--mc-surface-alt, #fafafa);
}
.cd__pill-label {
  font-size: 11px; text-transform: uppercase; letter-spacing: 0.05em;
  color: var(--mc-text-muted, #888);
}
.cd__pill-value {
  font-weight: 600; font-variant-numeric: tabular-nums;
  font-size: 16px; margin-top: 2px;
}
.cd__pill--success { background: #ecfdf5; border-color: #a7f3d0; }
.cd__pill--success .cd__pill-value { color: #047857; }
.cd__pill--warning { background: #fffbeb; border-color: #fde68a; }
.cd__pill--warning .cd__pill-value { color: #b45309; }
.cd__pill--error { background: #fef2f2; border-color: #fecaca; }
.cd__pill--error .cd__pill-value { color: #b91c1c; }
.cd__pill--neutral { background: var(--mc-surface-alt, #fafafa); border-color: var(--mc-border, #e5e7eb); }

.cd__tabs {
  display: flex; gap: 4px; border-bottom: 1px solid var(--mc-border, #e5e7eb);
  padding: 0 4px;
}
.cd__tab {
  display: inline-flex; align-items: center; gap: 6px;
  padding: 10px 16px; border: none; background: transparent;
  font-size: 14px; font-weight: 500; color: var(--mc-text-muted, #666);
  cursor: pointer; border-bottom: 2px solid transparent;
  margin-bottom: -1px; border-radius: 6px 6px 0 0;
}
.cd__tab:hover { background: var(--mc-surface-alt, #fafafa); color: var(--mc-text, #111); }
.cd__tab--active {
  color: var(--mc-text, #111); border-bottom-color: var(--mc-primary, #2563eb);
  background: var(--mc-surface, #fff);
}
.cd__tab-count {
  background: var(--mc-border, #e5e7eb); color: var(--mc-text-muted, #555);
  padding: 2px 7px; border-radius: 999px; font-size: 11px; font-weight: 600;
}
.cd__tab--active .cd__tab-count {
  background: var(--mc-primary, #2563eb); color: #fff;
}

.cd__table-wrap { overflow-x: auto; }
.cd__table { width: 100%; border-collapse: collapse; font-size: 14px; }
.cd__table thead th {
  text-align: left; padding: 12px 16px;
  font-weight: 600; color: var(--mc-text-muted, #666);
  border-bottom: 1px solid var(--mc-border, #e5e7eb);
  background: var(--mc-surface-alt, #fafafa);
  font-size: 12px; text-transform: uppercase; letter-spacing: 0.04em;
}
.cd__table tbody td {
  padding: 12px 16px;
  border-bottom: 1px solid var(--mc-border-subtle, #f0f0f0);
  vertical-align: middle;
}
.cd__col-num { text-align: right; font-variant-numeric: tabular-nums; white-space: nowrap; }
.cd__col-strong { font-weight: 600; }
.cd__overdue { color: #b91c1c; }

.cd__profile { padding: 20px 24px; }
.cd__profile-grid {
  display: grid; grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 14px 24px; margin: 0;
}
.cd__profile-grid--full { grid-column: 1 / -1; }
.cd__profile-grid > div { display: flex; flex-direction: column; gap: 4px; }
.cd__profile-grid dt { font-size: 12px; color: var(--mc-text-muted, #888); text-transform: uppercase; letter-spacing: 0.04em; }
.cd__profile-grid dd { margin: 0; font-size: 14px; }

.cd__form { display: flex; flex-direction: column; gap: 18px; }
.cd__form-grid { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 12px 16px; }
.cd__form-grid--full { grid-column: 1 / -1; }
.cd__ar { border-top: 1px solid var(--mc-border, #e5e7eb); padding-top: 16px; }
.cd__ar h4 { margin: 0 0 12px; font-size: 14px; font-weight: 600; }
.cd__ar-grid { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 12px 16px; align-items: end; }

.cd__pay { display: flex; flex-direction: column; gap: 16px; }
.cd__pay-summary {
  background: var(--mc-surface-alt, #fafafa);
  border-radius: 8px; padding: 10px 14px; font-size: 14px;
  display: flex; gap: 16px; align-items: center;
}
.cd__pay-warn { display: inline-flex; gap: 4px; color: #b45309; font-weight: 500; }
.cd__pay-grid { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 12px 16px; }
.cd__pay-alloc { border-top: 1px solid var(--mc-border, #e5e7eb); padding-top: 14px; }
.cd__pay-alloc-head { display: flex; justify-content: space-between; align-items: baseline; margin-bottom: 8px; }
.cd__pay-alloc-head h5 { margin: 0; font-size: 13px; font-weight: 600; }
.cd__pay-alloc-summary { font-size: 12px; color: var(--mc-text-muted, #888); }
.cd__pay-list { display: flex; flex-direction: column; gap: 4px; max-height: 200px; overflow-y: auto; }
.cd__pay-row {
  display: flex; align-items: center; gap: 10px;
  padding: 8px 12px; border-radius: 6px;
  border: 1px solid var(--mc-border-subtle, #f0f0f0);
  cursor: pointer;
}
.cd__pay-row:hover { background: var(--mc-surface-alt, #fafafa); }
.cd__pay-row--checked { background: #eff6ff; border-color: #bfdbfe; }
.cd__pay-row-label { flex: 1; display: flex; flex-direction: column; gap: 2px; }
.cd__pay-row-meta { font-size: 12px; color: var(--mc-text-muted, #888); }
.cd__pay-row-amount { font-variant-numeric: tabular-nums; font-weight: 600; }

@media (max-width: 700px) {
  .cd__pay-grid, .cd__profile-grid, .cd__form-grid, .cd__ar-grid {
    grid-template-columns: 1fr;
  }
}
</style>
