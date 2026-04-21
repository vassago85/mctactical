<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { http } from '@/api/http'
import { useToast } from '@/composables/useToast'
import McPageHeader from '@/components/ui/McPageHeader.vue'
import McCard from '@/components/ui/McCard.vue'
import McButton from '@/components/ui/McButton.vue'
import McField from '@/components/ui/McField.vue'
import McAlert from '@/components/ui/McAlert.vue'
import McModal from '@/components/ui/McModal.vue'
import McBadge from '@/components/ui/McBadge.vue'
import McCheckbox from '@/components/ui/McCheckbox.vue'
import McEmptyState from '@/components/ui/McEmptyState.vue'
import McSpinner from '@/components/ui/McSpinner.vue'
import { Plus, Pencil, Archive, RotateCcw } from 'lucide-vue-next'

type Wholesaler = {
  id: string
  name: string
  defaultCurrency: string | null
  notes: string | null
  isActive: boolean
  productCount: number
  receiptCount: number
  consignmentBatchCount: number
  pricingRuleCount: number
}

const toast = useToast()
const rows = ref<Wholesaler[]>([])
const loading = ref(true)
const err = ref<string | null>(null)
const includeInactive = ref(false)
const search = ref('')

const filtered = computed(() => {
  const q = search.value.trim().toLowerCase()
  if (!q) return rows.value
  return rows.value.filter(r =>
    r.name.toLowerCase().includes(q) ||
    (r.defaultCurrency ?? '').toLowerCase().includes(q) ||
    (r.notes ?? '').toLowerCase().includes(q)
  )
})

const activeCount = computed(() => rows.value.filter(r => r.isActive).length)
const inactiveCount = computed(() => rows.value.filter(r => !r.isActive).length)

async function load() {
  loading.value = true
  err.value = null
  try {
    const { data } = await http.get<Wholesaler[]>('/api/suppliers', {
      params: { includeInactive: includeInactive.value }
    })
    rows.value = data
  } catch (e: unknown) {
    const ax = e as { response?: { data?: { error?: string } } }
    err.value = ax.response?.data?.error ?? 'Could not load wholesalers.'
  } finally {
    loading.value = false
  }
}

// ── Upsert modal ──────────────────────────────────────────────────────────────
const showModal = ref(false)
const editing = ref<Wholesaler | null>(null)
const modalBusy = ref(false)
const modalErr = ref<string | null>(null)
const form = ref({ name: '', defaultCurrency: 'ZAR', notes: '' })

function openNew() {
  editing.value = null
  form.value = { name: '', defaultCurrency: 'ZAR', notes: '' }
  modalErr.value = null
  showModal.value = true
}

function openEdit(w: Wholesaler) {
  editing.value = w
  form.value = {
    name: w.name,
    defaultCurrency: w.defaultCurrency ?? '',
    notes: w.notes ?? ''
  }
  modalErr.value = null
  showModal.value = true
}

async function save() {
  modalErr.value = null
  if (!form.value.name.trim()) {
    modalErr.value = 'Name is required.'
    return
  }
  modalBusy.value = true
  try {
    const payload = {
      name: form.value.name.trim(),
      defaultCurrency: form.value.defaultCurrency.trim() || null,
      notes: form.value.notes.trim() || null
    }
    if (editing.value) {
      await http.put(`/api/suppliers/${editing.value.id}`, payload)
      toast.success('Wholesaler updated')
    } else {
      await http.post('/api/suppliers', payload)
      toast.success('Wholesaler added')
    }
    showModal.value = false
    await load()
  } catch (e: unknown) {
    const ax = e as { response?: { data?: { error?: string } } }
    modalErr.value = ax.response?.data?.error ?? 'Save failed.'
  } finally {
    modalBusy.value = false
  }
}

// ── Soft-delete / reactivate ─────────────────────────────────────────────────
async function deactivate(w: Wholesaler) {
  const usage = w.productCount + w.receiptCount + w.consignmentBatchCount + w.pricingRuleCount
  const msg = usage > 0
    ? `"${w.name}" is linked to ${usage} record(s). Deactivating hides it from pickers but keeps history intact. Continue?`
    : `Deactivate "${w.name}"?`
  if (!confirm(msg)) return
  try {
    await http.delete(`/api/suppliers/${w.id}`)
    toast.success(`${w.name} deactivated`)
    includeInactive.value = true
    await load()
  } catch (e: unknown) {
    const ax = e as { response?: { data?: { error?: string } } }
    toast.error(ax.response?.data?.error ?? 'Deactivate failed')
  }
}

async function reactivate(w: Wholesaler) {
  try {
    await http.post(`/api/suppliers/${w.id}/reactivate`, {})
    toast.success(`${w.name} reactivated`)
    await load()
  } catch (e: unknown) {
    const ax = e as { response?: { data?: { error?: string } } }
    toast.error(ax.response?.data?.error ?? 'Reactivate failed')
  }
}

function usageLabel(w: Wholesaler): string {
  const parts: string[] = []
  if (w.productCount) parts.push(`${w.productCount} product${w.productCount !== 1 ? 's' : ''}`)
  if (w.receiptCount) parts.push(`${w.receiptCount} receipt${w.receiptCount !== 1 ? 's' : ''}`)
  if (w.consignmentBatchCount) parts.push(`${w.consignmentBatchCount} consignment batch${w.consignmentBatchCount !== 1 ? 'es' : ''}`)
  if (w.pricingRuleCount) parts.push(`${w.pricingRuleCount} pricing rule${w.pricingRuleCount !== 1 ? 's' : ''}`)
  return parts.join(' · ') || 'No references'
}

onMounted(load)
</script>

<template>
  <div class="ws-page">
    <McPageHeader
      title="Wholesalers / Distributors"
      description="Manage the suppliers you buy from. Used by stock imports, consignment batches, stock receipts, supplier-scoped pricing rules, and reporting filters. Deactivating a wholesaler hides it from pickers for new records but keeps all historical references intact."
    />

    <McAlert v-if="err" variant="error">{{ err }}</McAlert>

    <McCard>
      <div class="ws-toolbar">
        <McField label="Search" for-id="ws-search" style="flex:1;min-width:220px">
          <input id="ws-search" v-model="search" type="search" placeholder="Name, currency, or notes…" autocomplete="off" />
        </McField>
        <McCheckbox
          v-model="includeInactive"
          label="Show inactive"
          :hint="`${activeCount} active · ${inactiveCount} inactive`"
          @update:modelValue="load"
        />
        <McButton variant="primary" type="button" @click="openNew">
          <Plus :size="16" style="margin-right:4px" /> Add wholesaler
        </McButton>
      </div>

      <div v-if="loading" class="ws-loading"><McSpinner /> Loading…</div>

      <McEmptyState
        v-else-if="rows.length === 0"
        title="No wholesalers yet"
        hint="Add your first wholesaler/distributor to link it to products, stock receipts, consignment batches, and supplier-scoped pricing rules."
      />

      <McEmptyState
        v-else-if="filtered.length === 0"
        title="No matches"
        hint="Try a different search term, or clear the filter to see all wholesalers."
      />

      <div v-else class="ws-list">
        <div
          v-for="w in filtered"
          :key="w.id"
          class="ws-row"
          :class="{ 'ws-row--inactive': !w.isActive }"
        >
          <div class="ws-row__head">
            <div class="ws-row__title">
              <strong>{{ w.name }}</strong>
              <McBadge v-if="w.defaultCurrency" variant="neutral">{{ w.defaultCurrency }}</McBadge>
              <McBadge v-if="!w.isActive" variant="neutral">Inactive</McBadge>
            </div>
            <div class="ws-row__actions">
              <McButton variant="ghost" dense type="button" title="Edit" @click="openEdit(w)">
                <Pencil :size="14" />
              </McButton>
              <McButton
                v-if="w.isActive"
                variant="ghost"
                dense
                type="button"
                title="Deactivate"
                @click="deactivate(w)"
              >
                <Archive :size="14" />
              </McButton>
              <McButton
                v-else
                variant="ghost"
                dense
                type="button"
                title="Reactivate"
                @click="reactivate(w)"
              >
                <RotateCcw :size="14" />
              </McButton>
            </div>
          </div>
          <p v-if="w.notes" class="ws-row__notes">{{ w.notes }}</p>
          <p class="ws-row__usage">{{ usageLabel(w) }}</p>
        </div>
      </div>
    </McCard>

    <McModal v-model="showModal" :title="editing ? 'Edit wholesaler' : 'Add wholesaler'">
      <McAlert v-if="modalErr" variant="error">{{ modalErr }}</McAlert>

      <div class="ws-form">
        <McField label="Name" for-id="ws-name">
          <input id="ws-name" v-model="form.name" type="text" required placeholder="e.g. ABC Distributors" />
        </McField>
        <McField label="Default currency" for-id="ws-cur" hint="Used as the default when importing costs from this wholesaler">
          <input id="ws-cur" v-model="form.defaultCurrency" type="text" placeholder="ZAR" maxlength="8" />
        </McField>
        <McField label="Notes" for-id="ws-notes">
          <textarea id="ws-notes" v-model="form.notes" rows="3" placeholder="Account number, rep contact, terms, etc." />
        </McField>
      </div>

      <template #footer>
        <McButton variant="ghost" type="button" @click="showModal = false">Cancel</McButton>
        <McButton variant="primary" type="button" :disabled="modalBusy" @click="save">
          {{ modalBusy ? 'Saving…' : (editing ? 'Save' : 'Add') }}
        </McButton>
      </template>
    </McModal>
  </div>
</template>

<style scoped>
.ws-page {
  display: flex;
  flex-direction: column;
  gap: 1.25rem;
  max-width: var(--mc-container-width, 1200px);
  margin: 0 auto;
  width: 100%;
}

.ws-toolbar {
  display: flex;
  gap: 1rem;
  align-items: flex-end;
  flex-wrap: wrap;
  margin-bottom: 1rem;
}

.ws-loading {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  padding: 1.25rem;
  color: #5c5a56;
}

.ws-list {
  display: flex;
  flex-direction: column;
  gap: 0.65rem;
}

.ws-row {
  border: 1px solid #e7e5e0;
  border-radius: 12px;
  padding: 0.85rem 1rem;
  background: #fff;
  transition: border-color 0.15s;
}

.ws-row:hover {
  border-color: var(--mc-accent, #f47a20);
}

.ws-row--inactive {
  opacity: 0.65;
  background: #faf8f5;
}

.ws-row__head {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 0.5rem;
}

.ws-row__title {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  flex-wrap: wrap;
}

.ws-row__actions {
  display: flex;
  gap: 0.25rem;
}

.ws-row__notes {
  margin: 0.35rem 0 0;
  color: #3a3733;
  font-size: 0.9rem;
  white-space: pre-wrap;
}

.ws-row__usage {
  margin: 0.35rem 0 0;
  color: #8a8780;
  font-size: 0.8rem;
  letter-spacing: 0.02em;
}

.ws-form {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.ws-form textarea {
  width: 100%;
  font: inherit;
  padding: 0.5rem 0.75rem;
  border: 1px solid var(--mc-app-border, #d6d3ce);
  border-radius: 0.35rem;
  resize: vertical;
}
</style>
