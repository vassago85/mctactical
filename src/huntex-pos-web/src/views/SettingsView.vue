<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { http } from '@/api/http'
import { useToast } from '@/composables/useToast'
import { useBranding } from '@/composables/useBranding'
import { formatZAR } from '@/utils/format'
import McPageHeader from '@/components/ui/McPageHeader.vue'
import McCard from '@/components/ui/McCard.vue'
import McButton from '@/components/ui/McButton.vue'
import McField from '@/components/ui/McField.vue'
import McAlert from '@/components/ui/McAlert.vue'
import McModal from '@/components/ui/McModal.vue'
import McBadge from '@/components/ui/McBadge.vue'
import McCheckbox from '@/components/ui/McCheckbox.vue'
import McActionBar from '@/components/ui/McActionBar.vue'
import McEmptyState from '@/components/ui/McEmptyState.vue'
import McSpinner from '@/components/ui/McSpinner.vue'
import { Plus, Pencil, Trash2, X } from 'lucide-vue-next'

const toast = useToast()
const { businessName } = useBranding()

// ── Legacy PricingSettings (kept under the hood for import fallbacks) ────────
// The UI no longer exposes the old "default markup %" card — the Global
// pricing rule is the single source of truth. We still round-trip the other
// fields via the existing PUT so imports and POS display settings keep working.
type PricingSettingsDto = {
  defaultMarginPercent: number
  defaultFixedMarkup: number
  useMarginPercent: boolean
  pricingMode: string
  hideCostForSalesRole: boolean
}

const dto = ref<PricingSettingsDto>({
  defaultMarginPercent: 50,
  defaultFixedMarkup: 0,
  useMarginPercent: true,
  pricingMode: 'normal',
  hideCostForSalesRole: true
})
const err = ref<string | null>(null)
const ok = ref<string | null>(null)

async function loadSettings() {
  const { data } = await http.get<PricingSettingsDto>('/api/settings/pricing')
  dto.value = { ...dto.value, ...data }
}

async function savePosDisplay() {
  err.value = null
  ok.value = null
  try {
    await http.put('/api/settings/pricing', dto.value)
    ok.value = 'Saved'
    toast.success('POS display settings saved')
  } catch {
    err.value = 'Save failed'
    toast.error('Save failed')
  }
}

// ── Pricing rules (moved in from PricingRulesView) ───────────────────────────
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

const rules = ref<PricingRule[]>([])
const suppliers = ref<SupplierOpt[]>([])
const rulesLoading = ref(true)
const rulesErr = ref<string | null>(null)

const scopeTabs: Scope[] = ['Global', 'Category', 'Manufacturer', 'Supplier']
const activeScope = ref<Scope>('Global')

const rulesByScope = computed(() => ({
  Global: rules.value.filter(r => r.scope === 'Global'),
  Category: rules.value.filter(r => r.scope === 'Category'),
  Manufacturer: rules.value.filter(r => r.scope === 'Manufacturer'),
  Supplier: rules.value.filter(r => r.scope === 'Supplier')
}))

const showRuleModal = ref(false)
const editingRule = ref<PricingRule | null>(null)
const ruleBusy = ref(false)
const ruleErr = ref<string | null>(null)
const ruleForm = ref(blankRuleForm())

function blankRuleForm() {
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

async function loadRules() {
  rulesLoading.value = true
  rulesErr.value = null
  try {
    const [{ data: r }, { data: s }] = await Promise.all([
      http.get<PricingRule[]>('/api/pricing-rules'),
      http.get<SupplierOpt[]>('/api/suppliers').catch(() => ({ data: [] as SupplierOpt[] }))
    ])
    rules.value = r
    suppliers.value = s
  } catch (e: unknown) {
    const ax = e as { response?: { data?: { error?: string } } }
    rulesErr.value = ax.response?.data?.error ?? 'Could not load pricing rules.'
  } finally {
    rulesLoading.value = false
  }
}

function openNewRule(scope: Scope) {
  editingRule.value = null
  ruleForm.value = { ...blankRuleForm(), scope }
  ruleErr.value = null
  showRuleModal.value = true
}

function openEditRule(rule: PricingRule) {
  editingRule.value = rule
  ruleForm.value = {
    scope: rule.scope,
    scopeKey: rule.scopeKey ?? '',
    supplierId: rule.supplierId ?? null,
    defaultMarkupPercent: rule.defaultMarkupPercent ?? null,
    maxDiscountPercent: rule.maxDiscountPercent ?? null,
    roundToNearest: rule.roundToNearest ?? null,
    minMarginPercent: rule.minMarginPercent ?? null,
    isActive: rule.isActive
  }
  ruleErr.value = null
  showRuleModal.value = true
}

async function saveRule() {
  ruleErr.value = null
  ruleBusy.value = true
  try {
    const payload = {
      scope: ruleForm.value.scope,
      scopeKey: ruleForm.value.scope === 'Category' || ruleForm.value.scope === 'Manufacturer' ? ruleForm.value.scopeKey : null,
      supplierId: ruleForm.value.scope === 'Supplier' ? ruleForm.value.supplierId : null,
      defaultMarkupPercent: ruleForm.value.defaultMarkupPercent,
      maxDiscountPercent: ruleForm.value.maxDiscountPercent,
      roundToNearest: ruleForm.value.roundToNearest,
      minMarginPercent: ruleForm.value.minMarginPercent,
      isActive: ruleForm.value.isActive
    }
    if (editingRule.value) {
      await http.put(`/api/pricing-rules/${editingRule.value.id}`, payload)
      toast.success('Rule updated')
    } else {
      await http.post('/api/pricing-rules', payload)
      toast.success('Rule created')
    }
    showRuleModal.value = false
    await loadRules()
  } catch (e: unknown) {
    const ax = e as { response?: { data?: { error?: string } } }
    ruleErr.value = ax.response?.data?.error ?? 'Could not save rule.'
  } finally {
    ruleBusy.value = false
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
    await loadRules()
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

// ── Promotions & specials ────────────────────────────────────────────────────
type Promotion = {
  id: string; name: string; discountPercent: number
  isActive: boolean; startsAt?: string | null; endsAt?: string | null
  specialsCount: number
}
type ProductSpecial = {
  id: string; productId: string; productSku: string; productName: string
  baseSellPrice: number; promotionId?: string | null; promotionName?: string | null
  specialPrice?: number | null; discountPercent?: number | null
  effectivePrice: number; isActive: boolean
}

const promotions = ref<Promotion[]>([])
const showPromoForm = ref(false)
const promoEditId = ref<string | null>(null)
const promoForm = ref({ name: '', discountPercent: 0, isActive: false, startsAt: '', endsAt: '' })
const promoFormBusy = ref(false)
const promoFormErr = ref<string | null>(null)

const showSpecialsDrawer = ref(false)
const specialsPromo = ref<Promotion | null>(null)
const specials = ref<ProductSpecial[]>([])
const specialsBusy = ref(false)

const showAddSpecial = ref(false)
const addSpecialForm = ref({ productSearch: '', productId: '', specialPrice: 0, discountPercent: 0, usePrice: true })
const addSpecialBusy = ref(false)
const addSpecialErr = ref<string | null>(null)
const productSearchResults = ref<{ id: string; sku: string; name: string; sellPrice: number }[]>([])
let searchTimer: ReturnType<typeof setTimeout> | null = null

async function loadPromotions() {
  try {
    const { data } = await http.get<Promotion[]>('/api/promotions')
    promotions.value = data
  } catch (e) {
    console.error('Failed to load promotions', e)
    toast.error('Failed to load promotions')
  }
}

function openAddPromo() {
  promoEditId.value = null
  promoForm.value = { name: '', discountPercent: 0, isActive: false, startsAt: '', endsAt: '' }
  promoFormErr.value = null
  showPromoForm.value = true
}

function openEditPromo(p: Promotion) {
  promoEditId.value = p.id
  promoForm.value = {
    name: p.name,
    discountPercent: p.discountPercent,
    isActive: p.isActive,
    startsAt: p.startsAt ? p.startsAt.slice(0, 10) : '',
    endsAt: p.endsAt ? p.endsAt.slice(0, 10) : ''
  }
  promoFormErr.value = null
  showPromoForm.value = true
}

async function savePromo() {
  promoFormErr.value = null
  promoFormBusy.value = true
  try {
    const payload = {
      name: promoForm.value.name,
      discountPercent: promoForm.value.discountPercent,
      isActive: promoForm.value.isActive,
      startsAt: promoForm.value.startsAt ? new Date(promoForm.value.startsAt).toISOString() : null,
      endsAt: promoForm.value.endsAt ? new Date(promoForm.value.endsAt + 'T23:59:59').toISOString() : null
    }
    if (promoEditId.value) {
      await http.put(`/api/promotions/${promoEditId.value}`, payload)
      toast.success('Promotion updated')
    } else {
      await http.post('/api/promotions', payload)
      toast.success('Promotion created')
    }
    showPromoForm.value = false
    await loadPromotions()
  } catch (e: unknown) {
    const ax = e as { response?: { data?: { error?: string } } }
    promoFormErr.value = ax.response?.data?.error ?? 'Save failed'
  } finally {
    promoFormBusy.value = false
  }
}

const toggleBusy = ref<string | null>(null)

async function togglePromoActive(p: Promotion) {
  toggleBusy.value = p.id
  try {
    const activating = !p.isActive
    await http.put(`/api/promotions/${p.id}`, { isActive: activating })
    toast.success(activating ? 'Promotion activated' : 'Promotion deactivated')
    await loadPromotions()
  } catch {
    toast.error('Update failed')
  } finally {
    toggleBusy.value = null
  }
}

async function deletePromo(p: Promotion) {
  if (!confirm(`Delete "${p.name}" and all its specials?`)) return
  try {
    await http.delete(`/api/promotions/${p.id}`)
    toast.success('Promotion deleted')
    await loadPromotions()
  } catch {
    toast.error('Delete failed')
  }
}

async function openSpecials(p: Promotion) {
  specialsPromo.value = p
  specials.value = []
  specialsBusy.value = true
  showSpecialsDrawer.value = true
  try {
    const { data } = await http.get<ProductSpecial[]>(`/api/promotions/${p.id}/specials`)
    specials.value = data
  } catch {
    toast.error('Could not load specials')
  } finally {
    specialsBusy.value = false
  }
}

function openAddSpecialModal() {
  addSpecialForm.value = { productSearch: '', productId: '', specialPrice: 0, discountPercent: 0, usePrice: true }
  addSpecialErr.value = null
  productSearchResults.value = []
  showAddSpecial.value = true
}

function searchProducts() {
  if (searchTimer) clearTimeout(searchTimer)
  searchTimer = setTimeout(async () => {
    const s = addSpecialForm.value.productSearch.trim()
    if (!s) { productSearchResults.value = []; return }
    try {
      const { data } = await http.get<{ id: string; sku: string; name: string; sellPrice: number }[]>('/api/products', { params: { q: s, take: 10 } })
      productSearchResults.value = data
    } catch { productSearchResults.value = [] }
  }, 250)
}

function pickProduct(p: { id: string; sku: string; name: string; sellPrice: number }) {
  addSpecialForm.value.productId = p.id
  addSpecialForm.value.productSearch = `${p.sku} — ${p.name}`
  addSpecialForm.value.specialPrice = p.sellPrice
  productSearchResults.value = []
}

async function saveSpecial() {
  addSpecialErr.value = null
  if (!addSpecialForm.value.productId) { addSpecialErr.value = 'Select a product'; return }
  addSpecialBusy.value = true
  try {
    await http.post('/api/promotions/specials', {
      productId: addSpecialForm.value.productId,
      promotionId: specialsPromo.value?.id ?? null,
      specialPrice: addSpecialForm.value.usePrice ? addSpecialForm.value.specialPrice : null,
      discountPercent: !addSpecialForm.value.usePrice ? addSpecialForm.value.discountPercent : null,
      isActive: true
    })
    toast.success('Special added')
    showAddSpecial.value = false
    if (specialsPromo.value) await openSpecials(specialsPromo.value)
    await loadPromotions()
  } catch (e: unknown) {
    const ax = e as { response?: { data?: { error?: string } } }
    addSpecialErr.value = ax.response?.data?.error ?? 'Save failed'
  } finally {
    addSpecialBusy.value = false
  }
}

async function deleteSpecial(s: ProductSpecial) {
  try {
    await http.delete(`/api/promotions/specials/${s.id}`)
    toast.success('Special removed')
    if (specialsPromo.value) await openSpecials(specialsPromo.value)
    await loadPromotions()
  } catch { toast.error('Delete failed') }
}

async function toggleSpecialActive(s: ProductSpecial) {
  try {
    await http.put(`/api/promotions/specials/${s.id}`, { isActive: !s.isActive })
    if (specialsPromo.value) await openSpecials(specialsPromo.value)
  } catch { toast.error('Update failed') }
}

onMounted(() => {
  void loadSettings().catch(() => (err.value = 'Load failed'))
  void loadRules()
  void loadPromotions()
})
</script>

<template>
  <div class="set-page">
    <McPageHeader
      title="Pricing"
      description="One place for markup rules, POS display, and promotions. Pricing rules set default markups and discount limits at different scopes (more specific rules win). Promotions apply discounts at point of sale without changing base prices."
    />

    <McAlert v-if="err" variant="error">{{ err }}</McAlert>
    <McAlert v-if="ok" variant="success">{{ ok }}</McAlert>

    <!-- Pricing rules (scoped markup / discount / rounding / min-margin) -->
    <McCard title="Pricing rules">
      <p class="set-hint" style="margin-top:0">
        Configure default markups, max discounts, rounding, and margin floors. More specific rules override less specific ones; product overrides always win.
      </p>

      <McAlert v-if="rulesErr" variant="error">{{ rulesErr }}</McAlert>

      <div v-if="rulesLoading" class="pr-loading"><McSpinner /> Loading…</div>

      <template v-else>
        <div class="pr-tabs" role="tablist">
          <button
            v-for="t in scopeTabs"
            :key="t"
            type="button"
            role="tab"
            class="pr-tab"
            :class="{ 'pr-tab--active': activeScope === t }"
            :aria-selected="activeScope === t"
            @click="activeScope = t"
          >
            {{ t }}
            <span class="pr-tab__count">{{ rulesByScope[t].length }}</span>
          </button>
        </div>

        <p class="pr-hint">
          <template v-if="activeScope === 'Global'">
            The fallback applied when no more specific rule matches. One global rule exists per deployment.
          </template>
          <template v-else-if="activeScope === 'Category'">
            Applied when a product has a matching category. Category rules override the global default.
          </template>
          <template v-else-if="activeScope === 'Manufacturer'">
            Applied when a product has a matching manufacturer. Manufacturer rules override category rules.
          </template>
          <template v-else>
            Applied when a product is linked to this wholesaler/supplier. Supplier rules are the most specific scope.
          </template>
        </p>

        <div v-if="activeScope !== 'Global'" class="pr-toolbar">
          <McButton variant="primary" type="button" @click="openNewRule(activeScope)">
            <Plus :size="16" style="margin-right:4px" /> New {{ activeScope.toLowerCase() }} rule
          </McButton>
        </div>

        <div v-if="rulesByScope[activeScope].length === 0" class="pr-empty">
          <McEmptyState
            :title="`No ${activeScope.toLowerCase()} rules yet`"
            :description="activeScope === 'Global'
              ? 'The global rule is seeded automatically on first run.'
              : `Add a ${activeScope.toLowerCase()}-specific rule to override the defaults for matching products.`"
          />
        </div>

        <div v-else class="pr-list">
          <div v-for="r in rulesByScope[activeScope]" :key="r.id" class="pr-row">
            <div class="pr-row__head">
              <div class="pr-row__title">
                <strong>{{ ruleLabel(r) }}</strong>
                <McBadge v-if="!r.isActive" variant="neutral">Inactive</McBadge>
              </div>
              <div class="pr-row__actions">
                <McButton variant="ghost" dense type="button" @click="openEditRule(r)">
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
      </template>
    </McCard>

    <!-- POS display -->
    <McCard title="POS display">
      <p class="set-hint" style="margin-top: 0">{{ businessName }} is not VAT registered — customer invoices do not add VAT.</p>
      <McCheckbox v-model="dto.hideCostForSalesRole" label="Hide product cost for Sales role" hint="Sales staff won't see cost or margin values" />
      <McActionBar>
        <McButton variant="primary" type="button" @click="savePosDisplay">Save POS display</McButton>
      </McActionBar>
    </McCard>

    <!-- Promotions & specials -->
    <McCard title="Promotions &amp; specials">
      <p class="set-hint" style="margin-top:0">
        Create named promotions (e.g. "Huntex Show 2026") with a site-wide discount %. Add per-product specials for individual pricing. Only one promotion can be active at a time.
      </p>
      <McButton variant="primary" type="button" style="margin-top:0.75rem" @click="openAddPromo">New promotion</McButton>

      <div v-if="promotions.length" class="promo-list">
        <div v-for="p in promotions" :key="p.id" class="promo-card" :class="{ 'promo-card--active': p.isActive }">
          <div class="promo-card__head">
            <div>
              <strong class="promo-card__name">{{ p.name }}</strong>
              <McBadge v-if="p.isActive" variant="success">Active</McBadge>
              <McBadge v-else variant="neutral">Inactive</McBadge>
            </div>
            <span class="promo-card__discount">{{ p.discountPercent }}% off</span>
          </div>
          <div class="promo-card__meta">
            <span v-if="p.startsAt || p.endsAt">
              {{ p.startsAt ? new Date(p.startsAt).toLocaleDateString() : '—' }}
              to
              {{ p.endsAt ? new Date(p.endsAt).toLocaleDateString() : '—' }}
            </span>
            <span v-else>No date range</span>
            <span>{{ p.specialsCount }} product special{{ p.specialsCount !== 1 ? 's' : '' }}</span>
          </div>
          <div class="promo-card__actions">
            <McButton
              :variant="p.isActive ? 'danger' : 'primary'"
              dense
              type="button"
              :disabled="toggleBusy === p.id"
              @click="togglePromoActive(p)"
            >
              <McSpinner v-if="toggleBusy === p.id" />
              <span v-else>{{ p.isActive ? 'Deactivate' : 'Activate' }}</span>
            </McButton>
            <McButton variant="secondary" dense type="button" @click="openEditPromo(p)">Edit</McButton>
            <McButton variant="secondary" dense type="button" @click="openSpecials(p)">Specials</McButton>
            <McButton variant="ghost" dense type="button" @click="deletePromo(p)">Delete</McButton>
          </div>
        </div>
      </div>
      <McEmptyState v-else title="No promotions" hint="Create your first promotion to offer site-wide discounts or per-product specials." />
    </McCard>

    <!-- Pricing rule modal -->
    <McModal v-model="showRuleModal" :title="editingRule ? 'Edit pricing rule' : 'New pricing rule'">
      <McAlert v-if="ruleErr" variant="error">{{ ruleErr }}</McAlert>

      <div class="pr-form">
        <McField label="Scope" for-id="pr-scope">
          <select id="pr-scope" v-model="ruleForm.scope" :disabled="!!editingRule">
            <option v-for="t in scopeTabs" :key="t" :value="t">{{ t }}</option>
          </select>
        </McField>

        <McField
          v-if="ruleForm.scope === 'Category' || ruleForm.scope === 'Manufacturer'"
          :label="ruleForm.scope === 'Category' ? 'Category name' : 'Manufacturer name'"
          for-id="pr-key"
        >
          <input id="pr-key" v-model="ruleForm.scopeKey" type="text" :disabled="!!editingRule" />
        </McField>

        <McField v-if="ruleForm.scope === 'Supplier'" label="Wholesaler" for-id="pr-sup">
          <select id="pr-sup" v-model="ruleForm.supplierId" :disabled="!!editingRule">
            <option :value="null">— Select —</option>
            <option v-for="s in suppliers" :key="s.id" :value="s.id">{{ s.name }}</option>
          </select>
        </McField>

        <McField label="Default markup % (cost × 1 + markup/100)" for-id="pr-markup">
          <input id="pr-markup" v-model.number="ruleForm.defaultMarkupPercent" type="number" step="0.01" placeholder="Inherit" />
        </McField>

        <McField label="Max discount %" for-id="pr-maxd">
          <input id="pr-maxd" v-model.number="ruleForm.maxDiscountPercent" type="number" step="0.01" placeholder="Inherit" />
        </McField>

        <McField label="Round sell up to nearest (R)" for-id="pr-round">
          <input id="pr-round" v-model.number="ruleForm.roundToNearest" type="number" step="0.01" placeholder="Inherit" />
        </McField>

        <McField label="Minimum margin %" for-id="pr-minm">
          <input id="pr-minm" v-model.number="ruleForm.minMarginPercent" type="number" step="0.01" placeholder="Inherit" />
        </McField>

        <McCheckbox v-model="ruleForm.isActive" label="Active" />
      </div>

      <template #footer>
        <McButton variant="ghost" type="button" @click="showRuleModal = false">Cancel</McButton>
        <McButton variant="primary" type="button" :disabled="ruleBusy" @click="saveRule">
          {{ ruleBusy ? 'Saving…' : 'Save rule' }}
        </McButton>
      </template>
    </McModal>

    <!-- Promotion form modal -->
    <McModal v-model="showPromoForm" :title="promoEditId ? 'Edit promotion' : 'New promotion'">
      <McAlert v-if="promoFormErr" variant="error">{{ promoFormErr }}</McAlert>
      <McField label="Name" for-id="pf-name">
        <input id="pf-name" v-model="promoForm.name" required placeholder="e.g. Huntex Show 2026" />
      </McField>
      <McField label="Site-wide discount %" for-id="pf-disc" hint="Applied to all products at POS (0 for no site-wide discount)">
        <input id="pf-disc" v-model.number="promoForm.discountPercent" type="number" min="0" max="100" step="0.01" />
      </McField>
      <div class="set-grid-2">
        <McField label="Starts (optional)" for-id="pf-start">
          <input id="pf-start" v-model="promoForm.startsAt" type="date" />
        </McField>
        <McField label="Ends (optional)" for-id="pf-end">
          <input id="pf-end" v-model="promoForm.endsAt" type="date" />
        </McField>
      </div>
      <McCheckbox v-model="promoForm.isActive" label="Active now" hint="Only one promotion can be active at a time" />
      <template #footer>
        <McButton variant="secondary" type="button" @click="showPromoForm = false">Cancel</McButton>
        <McButton variant="primary" type="button" :disabled="promoFormBusy" @click="savePromo">
          {{ promoEditId ? 'Save' : 'Create' }}
        </McButton>
      </template>
    </McModal>

    <!-- Specials drawer -->
    <Teleport to="body">
      <Transition name="promo-drawer-fade">
        <div v-if="showSpecialsDrawer" class="promo-overlay" @click.self="showSpecialsDrawer = false" />
      </Transition>
      <Transition name="promo-drawer-slide">
        <aside v-if="showSpecialsDrawer" class="promo-drawer" role="dialog">
          <header class="promo-drawer__head">
            <h2 class="promo-drawer__title">Specials — {{ specialsPromo?.name }}</h2>
            <button type="button" class="promo-drawer__close" aria-label="Close" @click="showSpecialsDrawer = false"><X :size="20" /></button>
          </header>
          <div class="promo-drawer__body">
            <McButton variant="primary" type="button" @click="openAddSpecialModal">Add product special</McButton>
            <div v-if="specialsBusy" style="padding:2rem;text-align:center"><McSpinner /></div>
            <McEmptyState v-else-if="!specials.length" title="No specials" hint="Add individual product specials for this promotion." />
            <table v-else class="mc-table" style="margin-top:1rem;font-size:0.85rem">
              <thead>
                <tr><th>Product</th><th>Base</th><th>Special</th><th>Active</th><th></th></tr>
              </thead>
              <tbody>
                <tr v-for="s in specials" :key="s.id">
                  <td><strong>{{ s.productSku }}</strong> — {{ s.productName }}</td>
                  <td>{{ formatZAR(s.baseSellPrice) }}</td>
                  <td>
                    <strong>{{ formatZAR(s.effectivePrice) }}</strong>
                    <span v-if="s.discountPercent" class="set-hint" style="margin:0"> ({{ s.discountPercent }}% off)</span>
                  </td>
                  <td>
                    <McBadge :variant="s.isActive ? 'success' : 'neutral'" style="cursor:pointer" @click="toggleSpecialActive(s)">
                      {{ s.isActive ? 'Yes' : 'No' }}
                    </McBadge>
                  </td>
                  <td><McButton variant="ghost" dense type="button" @click="deleteSpecial(s)">Remove</McButton></td>
                </tr>
              </tbody>
            </table>
          </div>
        </aside>
      </Transition>
    </Teleport>

    <!-- Add special modal -->
    <McModal v-model="showAddSpecial" title="Add product special">
      <McAlert v-if="addSpecialErr" variant="error">{{ addSpecialErr }}</McAlert>
      <McField label="Search product" for-id="as-search">
        <input id="as-search" v-model="addSpecialForm.productSearch" autocomplete="off" placeholder="SKU or name…" @input="searchProducts" />
      </McField>
      <ul v-if="productSearchResults.length" class="promo-search-results">
        <li v-for="p in productSearchResults" :key="p.id" @click="pickProduct(p)">
          <strong>{{ p.sku }}</strong> — {{ p.name }} ({{ formatZAR(p.sellPrice) }})
        </li>
      </ul>
      <div class="set-grid-2" style="margin-top:0.75rem">
        <label class="promo-type-toggle">
          <input v-model="addSpecialForm.usePrice" type="radio" :value="true" /> Special price (R)
        </label>
        <label class="promo-type-toggle">
          <input v-model="addSpecialForm.usePrice" type="radio" :value="false" /> Discount %
        </label>
      </div>
      <McField v-if="addSpecialForm.usePrice" label="Special price (R)" for-id="as-price">
        <input id="as-price" v-model.number="addSpecialForm.specialPrice" type="number" step="0.01" min="0" />
      </McField>
      <McField v-else label="Discount %" for-id="as-disc">
        <input id="as-disc" v-model.number="addSpecialForm.discountPercent" type="number" step="0.01" min="0" max="100" />
      </McField>
      <template #footer>
        <McButton variant="secondary" type="button" @click="showAddSpecial = false">Cancel</McButton>
        <McButton variant="primary" type="button" :disabled="addSpecialBusy" @click="saveSpecial">Add special</McButton>
      </template>
    </McModal>
  </div>
</template>

<style scoped>
.set-page { min-height: 100%; display: flex; flex-direction: column; gap: 1.25rem; max-width: var(--mc-container-width, 1200px); margin: 0 auto; width: 100%; }
.set-hint { margin: 0.75rem 0 0; font-size: 0.88rem; color: var(--mc-app-text-muted, #5c5a56); line-height: 1.5; }
.set-grid-2 { display: grid; grid-template-columns: 1fr 1fr; gap: 0.75rem; }

/* Pricing rules */
.pr-loading { display: flex; align-items: center; gap: 0.5rem; padding: 1.25rem; color: #5c5a56; }
.pr-tabs { display: flex; flex-wrap: wrap; gap: 0.5rem; padding: 0.25rem; background: #f5f5f4; border-radius: 12px; border: 1px solid #e7e5e0; margin-top: 0.75rem; }
.pr-tab { display: inline-flex; align-items: center; gap: 0.4rem; border: 0; background: transparent; padding: 0.55rem 0.95rem; border-radius: 9px; font-weight: 600; font-size: 0.9rem; cursor: pointer; color: #3a3733; }
.pr-tab:hover { background: #ebeae6; }
.pr-tab--active { background: #fff; color: #1a1a1c; box-shadow: 0 1px 2px rgba(0,0,0,0.08); }
.pr-tab__count { font-size: 0.75rem; background: #e7e5e0; padding: 0.1rem 0.45rem; border-radius: 999px; color: #5c5a56; }
.pr-tab--active .pr-tab__count { background: var(--mc-accent, #f47a20); color: #fff; }
.pr-hint { margin: 0.75rem 0; color: #5c5a56; font-size: 0.9rem; }
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

/* Promotion cards */
.promo-list { margin-top: 1rem; display: flex; flex-direction: column; gap: 0.75rem; }
.promo-card { border: 1px solid var(--mc-app-border, #d6d3ce); border-radius: 0.5rem; padding: 0.75rem 1rem; background: var(--mc-app-surface, #fff); }
.promo-card--active { border-color: var(--mc-primary, #38a169); box-shadow: inset 3px 0 0 var(--mc-primary, #38a169); }
.promo-card__head { display: flex; justify-content: space-between; align-items: center; gap: 0.5rem; }
.promo-card__name { font-size: 0.95rem; margin-right: 0.5rem; }
.promo-card__discount { font-size: 1.1rem; font-weight: 600; color: var(--mc-primary, #38a169); white-space: nowrap; }
.promo-card__meta { display: flex; gap: 1.5rem; margin-top: 0.25rem; font-size: 0.82rem; color: var(--mc-app-text-muted, #5c5a56); }
.promo-card__actions { margin-top: 0.5rem; display: flex; gap: 0.5rem; flex-wrap: wrap; }

/* Specials drawer */
.promo-overlay { position: fixed; inset: 0; background: rgba(0,0,0,0.35); z-index: 998; }
.promo-drawer { position: fixed; top: 0; right: 0; bottom: 0; width: min(480px, 90vw); background: var(--mc-app-bg, #faf8f5); box-shadow: -4px 0 24px rgba(0,0,0,0.15); z-index: 999; display: flex; flex-direction: column; overflow: hidden; }
.promo-drawer__head { display: flex; justify-content: space-between; align-items: center; padding: 1rem 1.25rem; border-bottom: 1px solid var(--mc-app-border, #d6d3ce); }
.promo-drawer__title { font-size: 1.1rem; margin: 0; }
.promo-drawer__close { display: inline-flex; align-items: center; justify-content: center; background: none; border: none; font-size: 1.5rem; cursor: pointer; color: var(--mc-app-text-muted); }
.promo-drawer__body { flex: 1; overflow-y: auto; padding: 1rem 1.25rem; }

/* Search results dropdown */
.promo-search-results { list-style: none; margin: 0.25rem 0 0; padding: 0; border: 1px solid var(--mc-app-border, #d6d3ce); border-radius: 0.35rem; max-height: 200px; overflow-y: auto; background: var(--mc-app-surface, #fff); }
.promo-search-results li { padding: 0.5rem 0.75rem; cursor: pointer; font-size: 0.85rem; }
.promo-search-results li:hover { background: var(--mc-app-hover, #f0ede8); }

.promo-type-toggle { display: flex; align-items: center; gap: 0.35rem; font-size: 0.88rem; cursor: pointer; }

/* Transitions */
.promo-drawer-fade-enter-active, .promo-drawer-fade-leave-active { transition: opacity 0.25s ease; }
.promo-drawer-fade-enter-from, .promo-drawer-fade-leave-to { opacity: 0; }
.promo-drawer-slide-enter-active, .promo-drawer-slide-leave-active { transition: transform 0.3s ease; }
.promo-drawer-slide-enter-from, .promo-drawer-slide-leave-to { transform: translateX(100%); }
</style>
