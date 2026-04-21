<script setup lang="ts">
import { onMounted, ref, computed } from 'vue'
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
import McSpinner from '@/components/ui/McSpinner.vue'
import McEmptyState from '@/components/ui/McEmptyState.vue'
import { Plus, Pencil, Trash2 } from 'lucide-vue-next'

type Scope = 'Global' | 'Category' | 'Manufacturer' | 'Supplier'

interface PricingRule {
  id: string
  scope: Scope
  scopeKey?: string | null
  supplierId?: string | null
  supplierName?: string | null
  defaultMarkupPercent?: number | null
  maxDiscountPercent?: number | null
  roundToNearest?: number | null
  minMarginPercent?: number | null
  isActive: boolean
  updatedAt: string
}

interface SupplierOpt { id: string; name: string }

const toast = useToast()
const rules = ref<PricingRule[]>([])
const suppliers = ref<SupplierOpt[]>([])
const loading = ref(true)
const err = ref<string | null>(null)

const tabs: Scope[] = ['Global', 'Category', 'Manufacturer', 'Supplier']
const activeTab = ref<Scope>('Global')

const rulesByScope = computed(() => ({
  Global: rules.value.filter(r => r.scope === 'Global'),
  Category: rules.value.filter(r => r.scope === 'Category'),
  Manufacturer: rules.value.filter(r => r.scope === 'Manufacturer'),
  Supplier: rules.value.filter(r => r.scope === 'Supplier')
}))

const showModal = ref(false)
const editing = ref<PricingRule | null>(null)
const modalBusy = ref(false)
const modalErr = ref<string | null>(null)
const form = ref(blankForm())

function blankForm() {
  return {
    scope: 'Category' as Scope,
    scopeKey: '',
    supplierId: null as string | null,
    defaultMarkupPercent: null as number | null,
    maxDiscountPercent: null as number | null,
    roundToNearest: null as number | null,
    minMarginPercent: null as number | null,
    isActive: true
  }
}

async function load() {
  loading.value = true
  err.value = null
  try {
    const [{ data: r }, { data: s }] = await Promise.all([
      http.get<PricingRule[]>('/api/pricing-rules'),
      http.get<SupplierOpt[]>('/api/suppliers').catch(() => ({ data: [] as SupplierOpt[] }))
    ])
    rules.value = r
    suppliers.value = s
  } catch (e: unknown) {
    const ax = e as { response?: { data?: { error?: string } } }
    err.value = ax.response?.data?.error ?? 'Could not load pricing rules.'
  } finally {
    loading.value = false
  }
}

function openNew(scope: Scope) {
  editing.value = null
  form.value = { ...blankForm(), scope }
  modalErr.value = null
  showModal.value = true
}

function openEdit(rule: PricingRule) {
  editing.value = rule
  form.value = {
    scope: rule.scope,
    scopeKey: rule.scopeKey ?? '',
    supplierId: rule.supplierId ?? null,
    defaultMarkupPercent: rule.defaultMarkupPercent ?? null,
    maxDiscountPercent: rule.maxDiscountPercent ?? null,
    roundToNearest: rule.roundToNearest ?? null,
    minMarginPercent: rule.minMarginPercent ?? null,
    isActive: rule.isActive
  }
  modalErr.value = null
  showModal.value = true
}

async function saveRule() {
  modalErr.value = null
  modalBusy.value = true
  try {
    const payload = {
      scope: form.value.scope,
      scopeKey: form.value.scope === 'Category' || form.value.scope === 'Manufacturer' ? form.value.scopeKey : null,
      supplierId: form.value.scope === 'Supplier' ? form.value.supplierId : null,
      defaultMarkupPercent: form.value.defaultMarkupPercent,
      maxDiscountPercent: form.value.maxDiscountPercent,
      roundToNearest: form.value.roundToNearest,
      minMarginPercent: form.value.minMarginPercent,
      isActive: form.value.isActive
    }
    if (editing.value) {
      await http.put(`/api/pricing-rules/${editing.value.id}`, payload)
      toast.success('Rule updated')
    } else {
      await http.post('/api/pricing-rules', payload)
      toast.success('Rule created')
    }
    showModal.value = false
    await load()
  } catch (e: unknown) {
    const ax = e as { response?: { data?: { error?: string } } }
    modalErr.value = ax.response?.data?.error ?? 'Could not save rule.'
  } finally {
    modalBusy.value = false
  }
}

async function removeRule(rule: PricingRule) {
  if (rule.scope === 'Global') {
    toast.error('Global rule cannot be deleted — edit its values instead.')
    return
  }
  if (!confirm('Delete this pricing rule?')) return
  try {
    await http.delete(`/api/pricing-rules/${rule.id}`)
    toast.success('Rule deleted')
    await load()
  } catch (e: unknown) {
    const ax = e as { response?: { data?: { error?: string } } }
    toast.error(ax.response?.data?.error ?? 'Delete failed')
  }
}

function ruleLabel(r: PricingRule): string {
  if (r.scope === 'Global') return 'Global default'
  if (r.scope === 'Supplier') return r.supplierName ?? 'Supplier'
  return r.scopeKey ?? r.scope
}

function fmtPct(v: number | null | undefined): string {
  return v == null ? '—' : `${v}%`
}
function fmtMoney(v: number | null | undefined): string {
  return v == null ? '—' : `R${Number(v).toFixed(2)}`
}

onMounted(load)
</script>

<template>
  <div class="pr-page">
    <McPageHeader
      title="Pricing rules"
      description="Configure default markups, max discounts, rounding, and margin floors at different levels. More specific rules override less specific ones; product overrides always win."
    />

    <McAlert v-if="err" variant="error">{{ err }}</McAlert>

    <div v-if="loading" class="pr-loading"><McSpinner /> Loading…</div>

    <template v-else>
      <div class="pr-tabs" role="tablist">
        <button
          v-for="t in tabs"
          :key="t"
          type="button"
          role="tab"
          class="pr-tab"
          :class="{ 'pr-tab--active': activeTab === t }"
          :aria-selected="activeTab === t"
          @click="activeTab = t"
        >
          {{ t }}
          <span class="pr-tab__count">{{ rulesByScope[t].length }}</span>
        </button>
      </div>

      <McCard :title="`${activeTab} rules`">
        <p class="pr-hint">
          <template v-if="activeTab === 'Global'">
            The fallback applied when no more specific rule matches. One global rule exists per deployment.
          </template>
          <template v-else-if="activeTab === 'Category'">
            Applied when a product has a matching category. Category rules override the global default.
          </template>
          <template v-else-if="activeTab === 'Manufacturer'">
            Applied when a product has a matching manufacturer. Manufacturer rules override category rules.
          </template>
          <template v-else>
            Applied when a product is linked to this wholesaler/supplier. Supplier rules are the most specific scope.
          </template>
        </p>

        <div v-if="activeTab !== 'Global'" class="pr-toolbar">
          <McButton variant="primary" type="button" @click="openNew(activeTab)">
            <Plus :size="16" style="margin-right:4px" /> New {{ activeTab.toLowerCase() }} rule
          </McButton>
        </div>

        <div v-if="rulesByScope[activeTab].length === 0" class="pr-empty">
          <McEmptyState
            :title="`No ${activeTab.toLowerCase()} rules yet`"
            :description="activeTab === 'Global'
              ? 'The global rule is seeded automatically on first run.'
              : `Add a ${activeTab.toLowerCase()}-specific rule to override the defaults for matching products.`"
          />
        </div>

        <div v-else class="pr-list">
          <div v-for="r in rulesByScope[activeTab]" :key="r.id" class="pr-row">
            <div class="pr-row__head">
              <div class="pr-row__title">
                <strong>{{ ruleLabel(r) }}</strong>
                <McBadge v-if="!r.isActive" variant="neutral">Inactive</McBadge>
              </div>
              <div class="pr-row__actions">
                <McButton variant="ghost" dense type="button" @click="openEdit(r)">
                  <Pencil :size="14" />
                </McButton>
                <McButton v-if="r.scope !== 'Global'" variant="ghost" dense type="button" @click="removeRule(r)">
                  <Trash2 :size="14" />
                </McButton>
              </div>
            </div>
            <dl class="pr-row__stats">
              <div>
                <dt>Markup</dt>
                <dd>{{ fmtPct(r.defaultMarkupPercent) }}</dd>
              </div>
              <div>
                <dt>Max discount</dt>
                <dd>{{ fmtPct(r.maxDiscountPercent) }}</dd>
              </div>
              <div>
                <dt>Round to</dt>
                <dd>{{ fmtMoney(r.roundToNearest) }}</dd>
              </div>
              <div>
                <dt>Min margin</dt>
                <dd>{{ fmtPct(r.minMarginPercent) }}</dd>
              </div>
            </dl>
          </div>
        </div>
      </McCard>
    </template>

    <McModal v-model="showModal" :title="editing ? 'Edit pricing rule' : 'New pricing rule'">
      <McAlert v-if="modalErr" variant="error">{{ modalErr }}</McAlert>

      <div class="pr-form">
        <McField label="Scope" for-id="pr-scope">
          <select id="pr-scope" v-model="form.scope" :disabled="!!editing">
            <option v-for="t in tabs" :key="t" :value="t">{{ t }}</option>
          </select>
        </McField>

        <McField
          v-if="form.scope === 'Category' || form.scope === 'Manufacturer'"
          :label="form.scope === 'Category' ? 'Category name' : 'Manufacturer name'"
          for-id="pr-key"
        >
          <input id="pr-key" v-model="form.scopeKey" type="text" :disabled="!!editing" />
        </McField>

        <McField v-if="form.scope === 'Supplier'" label="Wholesaler" for-id="pr-sup">
          <select id="pr-sup" v-model="form.supplierId" :disabled="!!editing">
            <option :value="null">— Select —</option>
            <option v-for="s in suppliers" :key="s.id" :value="s.id">{{ s.name }}</option>
          </select>
        </McField>

        <McField label="Default markup % (cost × 1 + markup/100)" for-id="pr-markup">
          <input id="pr-markup" v-model.number="form.defaultMarkupPercent" type="number" step="0.01" placeholder="Inherit" />
        </McField>

        <McField label="Max discount %" for-id="pr-maxd">
          <input id="pr-maxd" v-model.number="form.maxDiscountPercent" type="number" step="0.01" placeholder="Inherit" />
        </McField>

        <McField label="Round sell up to nearest (R)" for-id="pr-round">
          <input id="pr-round" v-model.number="form.roundToNearest" type="number" step="0.01" placeholder="Inherit" />
        </McField>

        <McField label="Minimum margin %" for-id="pr-minm">
          <input id="pr-minm" v-model.number="form.minMarginPercent" type="number" step="0.01" placeholder="Inherit" />
        </McField>

        <McCheckbox v-model="form.isActive" label="Active" />
      </div>

      <template #footer>
        <McButton variant="ghost" type="button" @click="showModal = false">Cancel</McButton>
        <McButton variant="primary" type="button" :disabled="modalBusy" @click="saveRule">
          {{ modalBusy ? 'Saving…' : 'Save rule' }}
        </McButton>
      </template>
    </McModal>
  </div>
</template>

<style scoped>
.pr-page { display: flex; flex-direction: column; gap: 1.25rem; max-width: var(--mc-container-width, 1200px); margin: 0 auto; width: 100%; }
.pr-loading { display: flex; align-items: center; gap: 0.5rem; padding: 1.25rem; color: #5c5a56; }
.pr-tabs { display: flex; flex-wrap: wrap; gap: 0.5rem; padding: 0.25rem; background: #f5f5f4; border-radius: 12px; border: 1px solid #e7e5e0; }
.pr-tab { display: inline-flex; align-items: center; gap: 0.4rem; border: 0; background: transparent; padding: 0.55rem 0.95rem; border-radius: 9px; font-weight: 600; font-size: 0.9rem; cursor: pointer; color: #3a3733; }
.pr-tab:hover { background: #ebeae6; }
.pr-tab--active { background: #fff; color: #1a1a1c; box-shadow: 0 1px 2px rgba(0,0,0,0.08); }
.pr-tab__count { font-size: 0.75rem; background: #e7e5e0; padding: 0.1rem 0.45rem; border-radius: 999px; color: #5c5a56; }
.pr-tab--active .pr-tab__count { background: var(--mc-accent, #f47a20); color: #fff; }
.pr-hint { margin: 0 0 0.75rem; color: #5c5a56; font-size: 0.9rem; }
.pr-toolbar { margin-bottom: 0.75rem; }
.pr-empty { padding: 0.5rem 0; }
.pr-list { display: flex; flex-direction: column; gap: 0.65rem; }
.pr-row { border: 1px solid #e7e5e0; border-radius: 12px; padding: 0.85rem 1rem; background: #fff; }
.pr-row__head { display: flex; justify-content: space-between; align-items: center; gap: 0.5rem; margin-bottom: 0.5rem; }
.pr-row__title { display: flex; align-items: center; gap: 0.5rem; }
.pr-row__actions { display: flex; gap: 0.25rem; }
.pr-row__stats { display: grid; grid-template-columns: repeat(auto-fit, minmax(120px, 1fr)); gap: 0.5rem 1rem; margin: 0; }
.pr-row__stats dt { font-size: 0.72rem; letter-spacing: 0.08em; text-transform: uppercase; color: #8a8780; margin-bottom: 0.1rem; }
.pr-row__stats dd { margin: 0; font-weight: 600; color: #1a1a1c; }
.pr-form { display: flex; flex-direction: column; gap: 0.75rem; }
</style>
