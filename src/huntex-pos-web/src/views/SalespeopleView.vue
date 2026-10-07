<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { http } from '@/api/http'
import { useToast } from '@/composables/useToast'
import { commissionBasisLabel, commissionSummary, type CommissionBasis, type Salesperson } from '@/composables/useSalespeople'
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

const toast = useToast()
const rows = ref<Salesperson[]>([])
const loading = ref(true)
const err = ref<string | null>(null)
const includeInactive = ref(false)

const activeCount = computed(() => rows.value.filter(r => r.isActive).length)

async function load() {
  loading.value = true
  err.value = null
  try {
    const { data } = await http.get<Salesperson[]>('/api/salespeople', {
      params: { includeInactive: includeInactive.value }
    })
    rows.value = data
  } catch (e: unknown) {
    const ax = e as { response?: { data?: { error?: string } } }
    err.value = ax.response?.data?.error ?? 'Could not load salespeople.'
  } finally {
    loading.value = false
  }
}

// ── Upsert modal ──────────────────────────────────────────────────────────────
const showModal = ref(false)
const editing = ref<Salesperson | null>(null)
const modalBusy = ref(false)
const modalErr = ref<string | null>(null)
const form = ref<{ name: string; commissionPercent: number; commissionBasis: CommissionBasis }>({
  name: '',
  commissionPercent: 0,
  commissionBasis: 'SalesExVat'
})
const basisOptions: CommissionBasis[] = ['SalesExVat', 'GrossProfit']

function openNew() {
  editing.value = null
  form.value = { name: '', commissionPercent: 0, commissionBasis: 'SalesExVat' }
  modalErr.value = null
  showModal.value = true
}

function openEdit(s: Salesperson) {
  editing.value = s
  form.value = { name: s.name, commissionPercent: s.commissionPercent, commissionBasis: s.commissionBasis }
  modalErr.value = null
  showModal.value = true
}

async function save() {
  modalErr.value = null
  if (!form.value.name.trim()) {
    modalErr.value = 'Name is required.'
    return
  }
  const pct = Number(form.value.commissionPercent) || 0
  if (pct < 0 || pct > 100) {
    modalErr.value = 'Commission must be between 0% and 100%.'
    return
  }
  modalBusy.value = true
  try {
    const payload = {
      name: form.value.name.trim(),
      commissionPercent: pct,
      commissionBasis: form.value.commissionBasis
    }
    if (editing.value) {
      await http.put(`/api/salespeople/${editing.value.id}`, payload)
      toast.success('Salesperson updated')
    } else {
      await http.post('/api/salespeople', payload)
      toast.success('Salesperson added')
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

async function setActive(s: Salesperson, isActive: boolean) {
  if (!isActive && !confirm(`Deactivate "${s.name}"? They will be hidden from checkout, but their past sales stay in reports.`)) return
  try {
    await http.put(`/api/salespeople/${s.id}`, {
      name: s.name,
      commissionPercent: s.commissionPercent,
      commissionBasis: s.commissionBasis,
      isActive
    })
    toast.success(`${s.name} ${isActive ? 'reactivated' : 'deactivated'}`)
    if (!isActive) includeInactive.value = true
    await load()
  } catch (e: unknown) {
    const ax = e as { response?: { data?: { error?: string } } }
    toast.error(ax.response?.data?.error ?? 'Update failed')
  }
}

function commissionLabel(s: Salesperson): string {
  return commissionSummary(s.commissionPercent, s.commissionBasis)
}

onMounted(load)
</script>

<template>
  <div class="sp-page">
    <McPageHeader
      title="Salespeople"
      description="The people who can be credited with a sale at checkout. They don't need their own login — the till stays signed in and the operator picks who made each sale. Monthly totals and commission are under Reports → Salespeople."
    />

    <McAlert v-if="err" variant="error">{{ err }}</McAlert>

    <McCard>
      <div class="sp-toolbar">
        <McCheckbox
          v-model="includeInactive"
          label="Show inactive"
          :hint="`${activeCount} active`"
          @update:modelValue="load"
        />
        <McButton variant="primary" type="button" @click="openNew">
          <Plus :size="16" style="margin-right:4px" /> Add salesperson
        </McButton>
      </div>

      <div v-if="loading" class="sp-loading"><McSpinner /> Loading…</div>

      <McEmptyState
        v-else-if="rows.length === 0"
        title="No salespeople yet"
        hint="Add the people who work the till. Once at least one is added, checkout will ask who made each sale."
      />

      <div v-else class="sp-list">
        <div
          v-for="s in rows"
          :key="s.id"
          class="sp-row"
          :class="{ 'sp-row--inactive': !s.isActive }"
        >
          <div class="sp-row__main">
            <div class="sp-row__title">
              <strong>{{ s.name }}</strong>
              <McBadge v-if="!s.isActive" variant="neutral">Inactive</McBadge>
            </div>
            <p class="sp-row__meta">{{ commissionLabel(s) }}</p>
          </div>
          <div class="sp-row__actions">
            <McButton variant="ghost" dense type="button" title="Edit" @click="openEdit(s)">
              <Pencil :size="14" />
            </McButton>
            <McButton v-if="s.isActive" variant="ghost" dense type="button" title="Deactivate" @click="setActive(s, false)">
              <Archive :size="14" />
            </McButton>
            <McButton v-else variant="ghost" dense type="button" title="Reactivate" @click="setActive(s, true)">
              <RotateCcw :size="14" />
            </McButton>
          </div>
        </div>
      </div>
    </McCard>

    <McModal v-model="showModal" :title="editing ? 'Edit salesperson' : 'Add salesperson'">
      <McAlert v-if="modalErr" variant="error">{{ modalErr }}</McAlert>

      <div class="sp-form">
        <McField label="Name" for-id="sp-name" hint="Shown at checkout and as “Served by” on receipts">
          <input id="sp-name" v-model="form.name" type="text" maxlength="128" required placeholder="e.g. Johan" />
        </McField>
        <McField label="Commission (%)" for-id="sp-pct" hint="Leave at 0 if this person doesn't earn commission">
          <input id="sp-pct" v-model.number="form.commissionPercent" type="number" min="0" max="100" step="0.01" />
        </McField>
        <McField label="Commission is worked out on" for-id="sp-basis">
          <select id="sp-basis" v-model="form.commissionBasis">
            <option v-for="b in basisOptions" :key="b" :value="b">{{ commissionBasisLabel(b) }}</option>
          </select>
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
.sp-page {
  display: flex;
  flex-direction: column;
  gap: 1.25rem;
  max-width: var(--mc-container-width, 1200px);
  margin: 0 auto;
  width: 100%;
}

.sp-toolbar {
  display: flex;
  gap: 1rem;
  align-items: center;
  justify-content: space-between;
  flex-wrap: wrap;
  margin-bottom: 1rem;
}

.sp-loading {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  padding: 1.25rem;
  color: #5c5a56;
}

.sp-list {
  display: flex;
  flex-direction: column;
  gap: 0.65rem;
}

.sp-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 0.5rem;
  border: 1px solid #e7e5e0;
  border-radius: 12px;
  padding: 0.85rem 1rem;
  background: #fff;
  transition: border-color 0.15s;
}

.sp-row:hover {
  border-color: var(--mc-accent, #f47a20);
}

.sp-row--inactive {
  opacity: 0.65;
  background: #faf8f5;
}

.sp-row__title {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  flex-wrap: wrap;
}

.sp-row__meta {
  margin: 0.25rem 0 0;
  color: #8a8780;
  font-size: 0.85rem;
}

.sp-row__actions {
  display: flex;
  gap: 0.25rem;
}

.sp-form {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}
</style>
