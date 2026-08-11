<script setup lang="ts">
/**
 * Shopify dashboard (Owner/Dev): KPI cards, quick actions, the "match unlinked sales" tool,
 * category/tag sync, and a browsable list of all Shopify variants with link status.
 */
import { computed, onMounted, ref } from 'vue'
import { http } from '@/api/http'
import { useToast } from '@/composables/useToast'
import { formatZAR, formatNumber } from '@/utils/format'
import McPageHeader from '@/components/ui/McPageHeader.vue'
import McCard from '@/components/ui/McCard.vue'
import McButton from '@/components/ui/McButton.vue'
import McField from '@/components/ui/McField.vue'
import McAlert from '@/components/ui/McAlert.vue'
import McSpinner from '@/components/ui/McSpinner.vue'
import McEmptyState from '@/components/ui/McEmptyState.vue'
import McModal from '@/components/ui/McModal.vue'
import McBadge from '@/components/ui/McBadge.vue'

const toast = useToast()

function handleErr(e: unknown, fallback: string): string {
  const ax = e as { response?: { data?: { error?: string } }; message?: string }
  return ax.response?.data?.error ?? ax.message ?? fallback
}

function toDateStr(d: Date) { return d.toISOString().slice(0, 10) }

// ── KPI dashboard ───────────────────────────────────────────────────────────
type TopUnlinked = { title: string; revenue: number }
type Dashboard = {
  revenue: number
  orders: number
  units: number
  avgOrderValue: number
  linkedProducts: number
  unlinkedItems: number
  unlinkedRevenue: number
  topUnlinked: TopUnlinked | null
}

const from = ref(toDateStr(new Date(Date.now() - 30 * 864e5)))
const to = ref(toDateStr(new Date()))
const dash = ref<Dashboard | null>(null)
const dashBusy = ref(false)
const dashErr = ref<string | null>(null)

async function loadDashboard() {
  dashBusy.value = true
  dashErr.value = null
  try {
    const params: Record<string, string> = {}
    if (from.value) params.from = new Date(from.value).toISOString()
    if (to.value) { const e = new Date(to.value); e.setHours(23, 59, 59, 999); params.to = e.toISOString() }
    const { data } = await http.get<Dashboard>('/api/shopify/dashboard', { params })
    dash.value = data
  } catch (e) {
    dashErr.value = handleErr(e, 'Could not load dashboard')
  } finally {
    dashBusy.value = false
  }
}

// ── Quick actions ─────────────────────────────────────────────────────────────
const syncBusy = ref(false)
const reconcileBusy = ref(false)

async function syncSales() {
  if (syncBusy.value) return
  syncBusy.value = true
  try {
    const { data } = await http.post('/api/shopify/orders/sync?apply=true')
    const imported = data.importedCount ?? 0
    const repaired = data.repairedCount ?? 0
    toast.success(`Sync done: ${imported} new, ${repaired} updated.`)
    await Promise.all([loadDashboard(), loadUnlinked()])
  } catch (e) {
    toast.error(handleErr(e, 'Sync failed'))
  } finally {
    syncBusy.value = false
  }
}

async function autoLink() {
  if (reconcileBusy.value) return
  reconcileBusy.value = true
  try {
    const { data } = await http.post('/api/shopify/reconcile?apply=true')
    const n = data.linkedCount ?? 0
    toast.success(`Auto-linked ${n} product${n === 1 ? '' : 's'}.`)
    await Promise.all([loadDashboard(), loadUnlinked()])
  } catch (e) {
    toast.error(handleErr(e, 'Auto-link failed'))
  } finally {
    reconcileBusy.value = false
  }
}

const importBusy = ref(false)

async function importAll() {
  if (importBusy.value) return
  importBusy.value = true
  try {
    const { data: preview } = await http.post('/api/shopify/import-products')
    const n = preview.creatableCount ?? 0
    if (n === 0) {
      toast.info('No new Shopify items to import — everything is already linked.')
      return
    }
    const skipped = preview.skuCollisionCount ?? 0
    const ok = confirm(
      `Create ${n} new POS product${n === 1 ? '' : 's'} from Shopify?` +
        (skipped ? ` (${skipped} skipped — SKU already in POS)` : '') +
        '\nStock starts at 0. Set prices afterwards, then push.'
    )
    if (!ok) return
    const { data } = await http.post('/api/shopify/import-products?apply=true')
    toast.success(
      `Created ${data.createdCount} product${data.createdCount === 1 ? '' : 's'}. ` +
        `${data.reclassifiedLineCount} past sale line${data.reclassifiedLineCount === 1 ? '' : 's'} reclassified.`
    )
    await Promise.all([loadDashboard(), loadUnlinked()])
    if (variantsLoaded.value) await loadVariants()
  } catch (e) {
    toast.error(handleErr(e, 'Import failed'))
  } finally {
    importBusy.value = false
  }
}

const pushPricesBusy = ref(false)

async function pushPrices() {
  if (pushPricesBusy.value) return
  pushPricesBusy.value = true
  try {
    const { data: preview } = await http.post('/api/shopify/push-prices')
    const n = preview.linkedProductCount ?? 0
    if (n === 0) {
      toast.info('No linked products to push prices for.')
      return
    }
    if (!confirm(`Push POS prices to ${n} linked Shopify product${n === 1 ? '' : 's'}? Stock is not changed.`)) return
    const { data } = await http.post('/api/shopify/push-prices?apply=true')
    const failed = data.failedCount ?? 0
    if (failed) toast.error(`Pushed ${data.updatedCount}, ${failed} failed.`)
    else toast.success(`Pushed prices to ${data.updatedCount} product${data.updatedCount === 1 ? '' : 's'}.`)
  } catch (e) {
    toast.error(handleErr(e, 'Push failed'))
  } finally {
    pushPricesBusy.value = false
  }
}

// ── Price review (POS vs Shopify) ────────────────────────────────────────────
type PriceRow = {
  productId: string
  sku: string
  name: string
  posPrice: number
  specialPrice: number | null
  specialLabel: string | null
  effectivePrice: number
  shopifyPrice: number | null
  shopifyCompareAt: number | null
  shopifyOnSpecial: boolean
  priceLocked: boolean
  differs: boolean
}
const priceRows = ref<PriceRow[]>([])
const priceLoaded = ref(false)
const priceBusy = ref(false)
const priceErr = ref<string | null>(null)
const onlyChanged = ref(true)
const pushingId = ref<string | null>(null)
const pullingId = ref<string | null>(null)
const pushAllBusy = ref(false)
const pullAllBusy = ref(false)

const round2 = (n: number) => Math.round(n * 100) / 100

function recomputeDiffers(row: PriceRow) {
  row.effectivePrice = row.specialPrice ?? row.posPrice
  row.differs = row.shopifyPrice !== null && round2(row.effectivePrice) !== round2(row.shopifyPrice)
}

const visiblePriceRows = computed(() => (onlyChanged.value ? priceRows.value.filter(r => r.differs) : priceRows.value))
const changedCount = computed(() => priceRows.value.filter(r => r.differs).length)
const onShopifySpecialCount = computed(() => priceRows.value.filter(r => r.shopifyOnSpecial).length)

async function loadPriceReview() {
  priceBusy.value = true
  priceErr.value = null
  try {
    const { data } = await http.get<{ items: PriceRow[] }>('/api/shopify/price-review')
    priceRows.value = data.items
    priceLoaded.value = true
  } catch (e) {
    priceErr.value = handleErr(e, 'Could not load price comparison')
  } finally {
    priceBusy.value = false
  }
}

async function pushOnePrice(row: PriceRow) {
  if (pushingId.value || pullingId.value) return
  pushingId.value = row.productId
  try {
    await http.post(`/api/shopify/push-price/${row.productId}`)
    row.shopifyPrice = row.effectivePrice
    row.differs = false
    toast.success(`Pushed ${row.sku} price to Shopify.`)
    await loadDashboard()
  } catch (e) {
    toast.error(handleErr(e, 'Push failed'))
  } finally {
    pushingId.value = null
  }
}

async function pushAllChanged() {
  const changed = priceRows.value.filter(r => r.differs)
  if (!changed.length) {
    toast.info('No changed prices to push.')
    return
  }
  if (!confirm(`Push ${changed.length} changed price${changed.length === 1 ? '' : 's'} to Shopify? Stock is not changed.`)) return
  pushAllBusy.value = true
  let ok = 0
  let fail = 0
  for (const row of changed) {
    try {
      await http.post(`/api/shopify/push-price/${row.productId}`)
      row.shopifyPrice = row.effectivePrice
      row.differs = false
      ok++
    } catch {
      fail++
    }
  }
  pushAllBusy.value = false
  if (fail) toast.error(`Pushed ${ok}, ${fail} failed.`)
  else toast.success(`Pushed ${ok} price${ok === 1 ? '' : 's'}.`)
  await loadDashboard()
}

async function pullOnePrice(row: PriceRow) {
  if (pushingId.value || pullingId.value || row.shopifyPrice === null || row.priceLocked) return
  pullingId.value = row.productId
  try {
    const { data } = await http.post<{ skipped: boolean }>(`/api/shopify/pull-price/${row.productId}`, {
      price: row.shopifyPrice
    })
    if (data.skipped) {
      toast.info(`${row.sku} is price-locked — not changed.`)
      return
    }
    row.posPrice = row.shopifyPrice
    recomputeDiffers(row)
    toast.success(`Pulled Shopify price into ${row.sku}.`)
    await loadDashboard()
  } catch (e) {
    toast.error(handleErr(e, 'Pull failed'))
  } finally {
    pullingId.value = null
  }
}

async function pullAllChanged() {
  const changed = priceRows.value.filter(r => r.differs && !r.priceLocked && r.shopifyPrice !== null)
  if (!changed.length) {
    toast.info('No changed prices to pull.')
    return
  }
  if (!confirm(`Pull ${changed.length} Shopify price${changed.length === 1 ? '' : 's'} into the POS? Price-locked items are skipped.`)) return
  pullAllBusy.value = true
  let ok = 0
  let fail = 0
  for (const row of changed) {
    try {
      await http.post(`/api/shopify/pull-price/${row.productId}`, { price: row.shopifyPrice })
      row.posPrice = row.shopifyPrice as number
      recomputeDiffers(row)
      ok++
    } catch {
      fail++
    }
  }
  pullAllBusy.value = false
  if (fail) toast.error(`Pulled ${ok}, ${fail} failed.`)
  else toast.success(`Pulled ${ok} price${ok === 1 ? '' : 's'}.`)
  await loadDashboard()
}

// ── Stock sync (POS → Shopify) ───────────────────────────────────────────────
type StockRow = {
  productId: string
  sku: string
  name: string
  posQtyOnHand: number
  shopifyAvailable: number | null
  differs: boolean
}
const stockRows = ref<StockRow[]>([])
const stockLoaded = ref(false)
const stockBusy = ref(false)
const stockErr = ref<string | null>(null)
const stockOnlyChanged = ref(true)
const stockPushingId = ref<string | null>(null)
const stockPushAllBusy = ref(false)
const syncStockBusy = ref(false)

const visibleStockRows = computed(() =>
  stockOnlyChanged.value ? stockRows.value.filter(r => r.differs) : stockRows.value)
const stockChangedCount = computed(() => stockRows.value.filter(r => r.differs).length)

async function loadStockReview() {
  stockBusy.value = true
  stockErr.value = null
  try {
    const { data } = await http.get<{ items: StockRow[] }>('/api/shopify/stock-review')
    stockRows.value = data.items
    stockLoaded.value = true
  } catch (e) {
    stockErr.value = handleErr(e, 'Could not load stock comparison')
  } finally {
    stockBusy.value = false
  }
}

async function pushOneStock(row: StockRow) {
  if (stockPushingId.value) return
  stockPushingId.value = row.productId
  try {
    await http.post(`/api/shopify/push-stock/${row.productId}`)
    row.shopifyAvailable = row.posQtyOnHand
    row.differs = false
    toast.success(`Pushed ${row.sku} stock to Shopify.`)
  } catch (e) {
    toast.error(handleErr(e, 'Push failed'))
  } finally {
    stockPushingId.value = null
  }
}

async function pushAllStock() {
  const changed = stockRows.value.filter(r => r.differs)
  if (!changed.length) {
    toast.info('No changed stock to push.')
    return
  }
  if (!confirm(`Push ${changed.length} changed stock level${changed.length === 1 ? '' : 's'} to Shopify?`)) return
  stockPushAllBusy.value = true
  let ok = 0
  let fail = 0
  for (const row of changed) {
    try {
      await http.post(`/api/shopify/push-stock/${row.productId}`)
      row.shopifyAvailable = row.posQtyOnHand
      row.differs = false
      ok++
    } catch {
      fail++
    }
  }
  stockPushAllBusy.value = false
  if (fail) toast.error(`Pushed ${ok}, ${fail} failed.`)
  else toast.success(`Pushed ${ok} stock level${ok === 1 ? '' : 's'}.`)
}

async function syncStock() {
  if (syncStockBusy.value) return
  syncStockBusy.value = true
  try {
    const { data: preview } = await http.post('/api/shopify/push-stock')
    const n = preview.linkedProductCount ?? 0
    if (n === 0) {
      toast.info('No linked products with a Shopify inventory item to push.')
      return
    }
    if (!confirm(`Push POS on-hand quantities to ${n} linked Shopify product${n === 1 ? '' : 's'}?`)) return
    const { data } = await http.post('/api/shopify/push-stock?apply=true')
    const failed = data.failedCount ?? 0
    if (failed) toast.error(`Pushed ${data.updatedCount}, ${failed} failed.`)
    else toast.success(`Pushed stock for ${data.updatedCount} product${data.updatedCount === 1 ? '' : 's'}.`)
    if (stockLoaded.value) await loadStockReview()
  } catch (e) {
    toast.error(handleErr(e, 'Stock push failed'))
  } finally {
    syncStockBusy.value = false
  }
}

const creatingVariantId = ref<number | null>(null)

async function createInPos(v: Variant) {
  if (creatingVariantId.value !== null) return
  creatingVariantId.value = v.shopifyVariantId
  try {
    const { data } = await http.post<{ sku: string }>('/api/shopify/import-product', {
      shopifyVariantId: v.shopifyVariantId
    })
    v.linked = true
    v.posSku = data.sku
    toast.success(`Created ${data.sku} in the POS.`)
    await loadDashboard()
  } catch (e) {
    toast.error(handleErr(e, 'Create failed'))
  } finally {
    creatingVariantId.value = null
  }
}

// ── Shared link modal (used by the Match tool and the variants table) ─────────
type LinkTarget = { kind: 'match' | 'variant'; shopifyVariantId: number | null; shopifySku: string | null; title: string }
type ProductHit = { id: string; sku: string; name: string; sellPrice: number }

const linkTarget = ref<LinkTarget | null>(null)
const productQuery = ref('')
const productResults = ref<ProductHit[]>([])
const searchingProducts = ref(false)
const linkBusy = ref(false)
let productSearchTimer: ReturnType<typeof setTimeout> | null = null

const showLinkModal = computed({
  get: () => linkTarget.value !== null,
  set: (v: boolean) => { if (!v) closeLink() }
})

function openLink(t: LinkTarget) {
  linkTarget.value = t
  productQuery.value = ''
  productResults.value = []
}
function closeLink() {
  linkTarget.value = null
  productQuery.value = ''
  productResults.value = []
}

function searchProducts() {
  if (productSearchTimer) clearTimeout(productSearchTimer)
  productSearchTimer = setTimeout(async () => {
    const q = productQuery.value.trim()
    if (!q) { productResults.value = []; return }
    searchingProducts.value = true
    try {
      const { data } = await http.get<ProductHit[]>('/api/products', { params: { q, take: 10 } })
      productResults.value = data
    } catch {
      productResults.value = []
    } finally {
      searchingProducts.value = false
    }
  }, 250)
}

async function confirmLink(product: ProductHit) {
  const t = linkTarget.value
  if (!t || linkBusy.value) return
  linkBusy.value = true
  try {
    const { data } = await http.post<{ reclassifiedLineCount: number }>('/api/shopify/link-variant', {
      shopifyVariantId: t.shopifyVariantId,
      shopifySku: t.shopifySku,
      posProductId: product.id
    })
    const n = data.reclassifiedLineCount ?? 0
    toast.success(`Linked to ${product.sku}. ${n} past sale line${n === 1 ? '' : 's'} reclassified.`)

    if (t.kind === 'match') {
      unlinked.value = unlinked.value.filter(x => unlinkedKey(x) !== matchKey(t))
    } else {
      const row = variants.value.find(v => v.shopifyVariantId === t.shopifyVariantId)
      if (row) { row.linked = true; row.posSku = product.sku }
    }
    closeLink()
    await loadDashboard()
  } catch (e) {
    toast.error(handleErr(e, 'Link failed'))
  } finally {
    linkBusy.value = false
  }
}

function matchKey(t: LinkTarget): string {
  return t.shopifyVariantId != null ? `v:${t.shopifyVariantId}` : `s:${(t.shopifySku ?? '').toLowerCase()}`
}

// ── Match Shopify sales (unlinked, by revenue) ───────────────────────────────
type UnlinkedSale = {
  shopifyVariantId: number | null
  shopifySku: string | null
  title: string
  qtySold: number
  revenue: number
  orderCount: number
}
const unlinked = ref<UnlinkedSale[]>([])
const unlinkedBusy = ref(false)
const unlinkedErr = ref<string | null>(null)

function unlinkedKey(r: UnlinkedSale): string {
  return r.shopifyVariantId != null ? `v:${r.shopifyVariantId}` : `s:${(r.shopifySku ?? '').toLowerCase()}`
}

async function loadUnlinked() {
  unlinkedBusy.value = true
  unlinkedErr.value = null
  try {
    const { data } = await http.get<UnlinkedSale[]>('/api/shopify/unlinked-sales')
    unlinked.value = data
  } catch (e) {
    unlinkedErr.value = handleErr(e, 'Could not load unlinked sales')
  } finally {
    unlinkedBusy.value = false
  }
}

// ── Categories & tags sync ────────────────────────────────────────────────────
type TagPreview = {
  applied: boolean
  linkedProductCount: number
  sample?: { sku: string; tags: string[] }[]
  updatedCount?: number
  failedCount?: number
  failures?: { sku: string; status: number; detail: string }[]
}
const tagBusy = ref(false)
const tagErr = ref<string | null>(null)
const tagPreview = ref<TagPreview | null>(null)

async function previewTags() {
  tagBusy.value = true
  tagErr.value = null
  try {
    const { data } = await http.post<TagPreview>('/api/shopify/sync-tags')
    tagPreview.value = data
  } catch (e) {
    tagErr.value = handleErr(e, 'Preview failed')
    toast.error(tagErr.value)
  } finally {
    tagBusy.value = false
  }
}

async function syncTags() {
  const count = tagPreview.value?.linkedProductCount
  const msg = count
    ? `Push categories/tags to ${count} linked Shopify product${count === 1 ? '' : 's'}?`
    : 'Push categories/tags to all linked Shopify products?'
  if (!confirm(msg)) return
  tagBusy.value = true
  tagErr.value = null
  try {
    const { data } = await http.post<TagPreview>('/api/shopify/sync-tags?apply=true')
    tagPreview.value = data
    const updated = data.updatedCount ?? 0
    const failed = data.failedCount ?? 0
    if (failed > 0) toast.error(`Synced ${updated}, ${failed} failed.`)
    else toast.success(`Synced tags to ${updated} product${updated === 1 ? '' : 's'}.`)
  } catch (e) {
    tagErr.value = handleErr(e, 'Sync failed')
    toast.error(tagErr.value)
  } finally {
    tagBusy.value = false
  }
}

// ── All Shopify variants (loaded on demand) ──────────────────────────────────
type Variant = { shopifyVariantId: number; sku: string | null; title: string; linked: boolean; posSku: string | null }
const variants = ref<Variant[]>([])
const variantsLoaded = ref(false)
const variantsBusy = ref(false)
const variantsErr = ref<string | null>(null)
const variantSearch = ref('')
const variantFilter = ref<'all' | 'unlinked' | 'linked'>('all')
const variantPage = ref(1)
const pageSize = 50

async function loadVariants() {
  variantsBusy.value = true
  variantsErr.value = null
  try {
    const { data } = await http.get<{ items: Variant[] }>('/api/shopify/variants')
    variants.value = data.items
    variantsLoaded.value = true
  } catch (e) {
    variantsErr.value = handleErr(e, 'Could not load Shopify variants')
  } finally {
    variantsBusy.value = false
  }
}

const filteredVariants = computed(() => {
  const q = variantSearch.value.trim().toLowerCase()
  return variants.value.filter(v => {
    if (variantFilter.value === 'linked' && !v.linked) return false
    if (variantFilter.value === 'unlinked' && v.linked) return false
    if (!q) return true
    return (v.sku ?? '').toLowerCase().includes(q) || v.title.toLowerCase().includes(q)
  })
})
const pageCount = computed(() => Math.max(1, Math.ceil(filteredVariants.value.length / pageSize)))
const pagedVariants = computed(() => {
  const start = (variantPage.value - 1) * pageSize
  return filteredVariants.value.slice(start, start + pageSize)
})
function resetPage() { variantPage.value = 1 }

onMounted(() => {
  void loadDashboard()
  void loadUnlinked()
})
</script>

<template>
  <div class="shopify-page">
    <McPageHeader title="Shopify">
      <template #default>
        Overview of your online channel. The POS is the source of truth.
      </template>
    </McPageHeader>

    <!-- Period + KPIs -->
    <McCard title="Overview">
      <div class="shp-period">
        <McField label="From" for-id="shp-from"><input id="shp-from" v-model="from" type="date" /></McField>
        <McField label="To" for-id="shp-to"><input id="shp-to" v-model="to" type="date" /></McField>
        <McButton variant="primary" type="button" :disabled="dashBusy" @click="loadDashboard">
          <McSpinner v-if="dashBusy" />
          <span v-else>Refresh</span>
        </McButton>
      </div>

      <McAlert v-if="dashErr" variant="error">{{ dashErr }}</McAlert>

      <div v-if="dash" class="shp-kpis">
        <div class="kpi kpi--accent">
          <span class="kpi__label">Shopify revenue (incl VAT)</span>
          <strong class="kpi__value">{{ formatZAR(dash.revenue) }}</strong>
          <span class="kpi__sub">selected period</span>
        </div>
        <div class="kpi">
          <span class="kpi__label">Orders</span>
          <strong class="kpi__value">{{ formatNumber(dash.orders) }}</strong>
          <span class="kpi__sub">selected period</span>
        </div>
        <div class="kpi">
          <span class="kpi__label">Items sold</span>
          <strong class="kpi__value">{{ formatNumber(dash.units) }}</strong>
          <span class="kpi__sub">units, selected period</span>
        </div>
        <div class="kpi">
          <span class="kpi__label">Avg order value</span>
          <strong class="kpi__value">{{ formatZAR(dash.avgOrderValue) }}</strong>
          <span class="kpi__sub">selected period</span>
        </div>
        <div class="kpi">
          <span class="kpi__label">Linked products</span>
          <strong class="kpi__value">{{ formatNumber(dash.linkedProducts) }}</strong>
          <span class="kpi__sub">all time</span>
        </div>
        <div class="kpi kpi--warn">
          <span class="kpi__label">Unlinked items</span>
          <strong class="kpi__value">{{ formatNumber(dash.unlinkedItems) }}</strong>
          <span class="kpi__sub">still to match</span>
        </div>
        <div class="kpi kpi--warn">
          <span class="kpi__label">Unlinked revenue</span>
          <strong class="kpi__value">{{ formatZAR(dash.unlinkedRevenue) }}</strong>
          <span class="kpi__sub">GP not yet accurate</span>
        </div>
        <div class="kpi">
          <span class="kpi__label">Top unlinked seller</span>
          <strong class="kpi__value kpi__value--sm">{{ dash.topUnlinked ? dash.topUnlinked.title : '—' }}</strong>
          <span v-if="dash.topUnlinked" class="kpi__sub">{{ formatZAR(dash.topUnlinked.revenue) }}</span>
        </div>
      </div>

      <div class="shp-quick">
        <McButton variant="secondary" type="button" :disabled="syncBusy" @click="syncSales">
          <McSpinner v-if="syncBusy" />
          <span v-else>Sync Shopify sales</span>
        </McButton>
        <McButton variant="secondary" type="button" :disabled="reconcileBusy" @click="autoLink">
          <McSpinner v-if="reconcileBusy" />
          <span v-else>Auto-link products</span>
        </McButton>
        <McButton variant="secondary" type="button" :disabled="importBusy" @click="importAll">
          <McSpinner v-if="importBusy" />
          <span v-else>Import all missing to POS</span>
        </McButton>
        <McButton variant="primary" type="button" :disabled="pushPricesBusy" @click="pushPrices">
          <McSpinner v-if="pushPricesBusy" />
          <span v-else>Push prices to Shopify</span>
        </McButton>
        <McButton variant="primary" type="button" :disabled="syncStockBusy" @click="syncStock">
          <McSpinner v-if="syncStockBusy" />
          <span v-else>Sync stock to Shopify</span>
        </McButton>
      </div>
    </McCard>

    <!-- Match Shopify sales -->
    <McCard title="Match Shopify sales">
      <p class="shp-hint">
        Online items not yet linked to a POS product, highest revenue first. Link each to fix its past
        and future sales (and tighten the Shopify gross-profit figure).
      </p>
      <McAlert v-if="unlinkedErr" variant="error">{{ unlinkedErr }}</McAlert>
      <div class="shp-actions">
        <McButton variant="secondary" type="button" :disabled="unlinkedBusy" @click="loadUnlinked">
          <McSpinner v-if="unlinkedBusy" /><span v-else>Refresh</span>
        </McButton>
      </div>

      <div v-if="unlinkedBusy && !unlinked.length" class="shp-loading"><McSpinner /> Loading…</div>
      <McEmptyState
        v-else-if="!unlinked.length"
        title="Nothing to link"
        hint="Every Shopify item that has sold is linked to a POS product."
      />
      <table v-else class="shp-table">
        <thead>
          <tr><th>Item</th><th class="shp-r">Qty</th><th class="shp-r">Revenue</th><th class="shp-r">Orders</th><th></th></tr>
        </thead>
        <tbody>
          <tr v-for="r in unlinked" :key="unlinkedKey(r)">
            <td>
              <div class="shp-item-title">{{ r.title }}</div>
              <div v-if="r.shopifySku" class="shp-item-sku">{{ r.shopifySku }}</div>
            </td>
            <td class="shp-r">{{ formatNumber(r.qtySold) }}</td>
            <td class="shp-r">{{ formatZAR(r.revenue) }}</td>
            <td class="shp-r">{{ formatNumber(r.orderCount) }}</td>
            <td class="shp-r">
              <McButton variant="ghost" dense type="button"
                @click="openLink({ kind: 'match', shopifyVariantId: r.shopifyVariantId, shopifySku: r.shopifySku, title: r.title })">
                Link
              </McButton>
            </td>
          </tr>
        </tbody>
      </table>
    </McCard>

    <!-- All Shopify variants -->
    <McCard title="All Shopify variants">
      <p class="shp-hint">
        Every product variant in your Shopify store and whether it's linked to a POS product.
      </p>
      <McAlert v-if="variantsErr" variant="error">{{ variantsErr }}</McAlert>

      <div v-if="!variantsLoaded" class="shp-actions">
        <McButton variant="secondary" type="button" :disabled="variantsBusy" @click="loadVariants">
          <McSpinner v-if="variantsBusy" />
          <span v-else>Load Shopify variants</span>
        </McButton>
      </div>

      <template v-else>
        <div class="shp-var-controls">
          <input v-model="variantSearch" class="shp-search-input" placeholder="Search SKU or title…" @input="resetPage" />
          <div class="shp-seg" role="group">
            <button type="button" class="shp-seg__btn" :class="{ 'shp-seg__btn--active': variantFilter === 'all' }" @click="variantFilter = 'all'; resetPage()">All</button>
            <button type="button" class="shp-seg__btn" :class="{ 'shp-seg__btn--active': variantFilter === 'unlinked' }" @click="variantFilter = 'unlinked'; resetPage()">Unlinked</button>
            <button type="button" class="shp-seg__btn" :class="{ 'shp-seg__btn--active': variantFilter === 'linked' }" @click="variantFilter = 'linked'; resetPage()">Linked</button>
          </div>
          <McButton variant="ghost" dense type="button" :disabled="variantsBusy" @click="loadVariants">Reload</McButton>
        </div>

        <p class="shp-hint">{{ formatNumber(filteredVariants.length) }} variant(s).</p>

        <table class="shp-table">
          <thead>
            <tr><th>Title</th><th>SKU</th><th>Status</th><th></th></tr>
          </thead>
          <tbody>
            <tr v-for="v in pagedVariants" :key="v.shopifyVariantId">
              <td>{{ v.title }}</td>
              <td class="shp-item-sku">{{ v.sku || '—' }}</td>
              <td>
                <McBadge v-if="v.linked" variant="success">Linked → {{ v.posSku }}</McBadge>
                <McBadge v-else variant="neutral">Unlinked</McBadge>
              </td>
              <td class="shp-r">
                <div v-if="!v.linked" class="shp-row-actions">
                  <McButton variant="ghost" dense type="button"
                    @click="openLink({ kind: 'variant', shopifyVariantId: v.shopifyVariantId, shopifySku: v.sku, title: v.title })">
                    Link
                  </McButton>
                  <McButton variant="secondary" dense type="button"
                    :disabled="creatingVariantId === v.shopifyVariantId"
                    @click="createInPos(v)">
                    <McSpinner v-if="creatingVariantId === v.shopifyVariantId" />
                    <span v-else>Create in POS</span>
                  </McButton>
                </div>
              </td>
            </tr>
          </tbody>
        </table>

        <div v-if="pageCount > 1" class="shp-pager">
          <McButton variant="ghost" dense type="button" :disabled="variantPage <= 1" @click="variantPage--">Prev</McButton>
          <span>Page {{ variantPage }} / {{ pageCount }}</span>
          <McButton variant="ghost" dense type="button" :disabled="variantPage >= pageCount" @click="variantPage++">Next</McButton>
        </div>
      </template>
    </McCard>

    <!-- Price sync (POS -> Shopify) -->
    <McCard title="Price sync (POS &rarr; Shopify)">
      <p class="shp-hint">
        Compare each linked product's POS price with its current Shopify price and push changes — one at
        a time or all at once. Price-only: stock and availability are never changed.
      </p>
      <McAlert v-if="priceErr" variant="error">{{ priceErr }}</McAlert>

      <div v-if="!priceLoaded" class="shp-actions">
        <McButton variant="secondary" type="button" :disabled="priceBusy" @click="loadPriceReview">
          <McSpinner v-if="priceBusy" />
          <span v-else>Load price comparison</span>
        </McButton>
      </div>

      <template v-else>
        <div class="shp-var-controls">
          <label class="shp-check"><input type="checkbox" v-model="onlyChanged" /> Only changed</label>
          <span class="shp-hint shp-hint--inline">
            {{ formatNumber(changedCount) }} changed of {{ formatNumber(priceRows.length) }}
            <template v-if="onShopifySpecialCount"> · {{ formatNumber(onShopifySpecialCount) }} on Shopify special</template>
          </span>
          <McButton variant="primary" dense type="button" :disabled="pushAllBusy || changedCount === 0" @click="pushAllChanged">
            <McSpinner v-if="pushAllBusy" />
            <span v-else>Push all changed</span>
          </McButton>
          <McButton variant="secondary" dense type="button" :disabled="pullAllBusy || changedCount === 0" @click="pullAllChanged">
            <McSpinner v-if="pullAllBusy" />
            <span v-else>Pull all changed</span>
          </McButton>
          <McButton variant="ghost" dense type="button" :disabled="priceBusy" @click="loadPriceReview">Reload</McButton>
        </div>

        <McEmptyState
          v-if="!visiblePriceRows.length"
          title="Nothing to show"
          hint="No products match the current filter."
        />
        <table v-else class="shp-table">
          <thead>
            <tr>
              <th>Item</th>
              <th class="shp-r">POS price</th>
              <th class="shp-r">Special</th>
              <th class="shp-r">Shopify price</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="row in visiblePriceRows" :key="row.productId">
              <td>
                <div class="shp-item-title">{{ row.name }}</div>
                <div class="shp-item-sku">{{ row.sku }}</div>
              </td>
              <td class="shp-r">{{ formatZAR(row.posPrice) }}</td>
              <td class="shp-r">
                <template v-if="row.specialPrice !== null">
                  <div class="shp-special">{{ formatZAR(row.specialPrice) }}</div>
                  <div class="shp-item-sku">{{ row.specialLabel }}</div>
                </template>
                <template v-else>—</template>
              </td>
              <td class="shp-r" :class="{ 'shp-diff': row.differs }">
                <template v-if="row.shopifyPrice === null">—</template>
                <template v-else>
                  <div>{{ formatZAR(row.shopifyPrice) }}</div>
                  <div v-if="row.shopifyOnSpecial" class="shp-onsale">
                    <span class="shp-was">{{ formatZAR(row.shopifyCompareAt as number) }}</span>
                    <McBadge variant="warning">On sale</McBadge>
                  </div>
                </template>
              </td>
              <td class="shp-r">
                <div class="shp-row-actions">
                  <template v-if="row.differs">
                    <McButton variant="secondary" dense type="button"
                      :disabled="pushingId === row.productId || pullingId === row.productId" @click="pushOnePrice(row)">
                      <McSpinner v-if="pushingId === row.productId" />
                      <span v-else>Push</span>
                    </McButton>
                    <McButton v-if="!row.priceLocked && row.shopifyPrice !== null" variant="ghost" dense type="button"
                      :disabled="pushingId === row.productId || pullingId === row.productId" @click="pullOnePrice(row)">
                      <McSpinner v-if="pullingId === row.productId" />
                      <span v-else>Pull</span>
                    </McButton>
                    <span v-else-if="row.priceLocked" class="shp-lock">Locked</span>
                  </template>
                  <McBadge v-else variant="success">Synced</McBadge>
                </div>
              </td>
            </tr>
          </tbody>
        </table>
      </template>
    </McCard>

    <!-- Stock sync (POS -> Shopify) -->
    <McCard title="Stock sync (POS &rarr; Shopify)">
      <p class="shp-hint">
        Compare each linked product's POS on-hand quantity with its current Shopify available stock and
        push changes — one at a time or all at once. Only sends the "available" level at your configured
        location; prices are never changed.
      </p>
      <McAlert v-if="stockErr" variant="error">{{ stockErr }}</McAlert>

      <div v-if="!stockLoaded" class="shp-actions">
        <McButton variant="secondary" type="button" :disabled="stockBusy" @click="loadStockReview">
          <McSpinner v-if="stockBusy" />
          <span v-else>Load stock comparison</span>
        </McButton>
      </div>

      <template v-else>
        <div class="shp-var-controls">
          <label class="shp-check"><input type="checkbox" v-model="stockOnlyChanged" /> Only changed</label>
          <span class="shp-hint shp-hint--inline">
            {{ formatNumber(stockChangedCount) }} changed of {{ formatNumber(stockRows.length) }}
          </span>
          <McButton variant="primary" dense type="button" :disabled="stockPushAllBusy || stockChangedCount === 0" @click="pushAllStock">
            <McSpinner v-if="stockPushAllBusy" />
            <span v-else>Push all changed</span>
          </McButton>
          <McButton variant="ghost" dense type="button" :disabled="stockBusy" @click="loadStockReview">Reload</McButton>
        </div>

        <McEmptyState
          v-if="!visibleStockRows.length"
          title="Nothing to show"
          hint="No products match the current filter."
        />
        <table v-else class="shp-table">
          <thead>
            <tr>
              <th>Item</th>
              <th class="shp-r">POS on hand</th>
              <th class="shp-r">Shopify available</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="row in visibleStockRows" :key="row.productId">
              <td>
                <div class="shp-item-title">{{ row.name }}</div>
                <div class="shp-item-sku">{{ row.sku }}</div>
              </td>
              <td class="shp-r">{{ formatNumber(row.posQtyOnHand) }}</td>
              <td class="shp-r" :class="{ 'shp-diff': row.differs }">
                {{ row.shopifyAvailable === null ? '—' : formatNumber(row.shopifyAvailable) }}
              </td>
              <td class="shp-r">
                <div class="shp-row-actions">
                  <McButton v-if="row.differs" variant="secondary" dense type="button"
                    :disabled="stockPushingId === row.productId" @click="pushOneStock(row)">
                    <McSpinner v-if="stockPushingId === row.productId" />
                    <span v-else>Push</span>
                  </McButton>
                  <McBadge v-else variant="success">Synced</McBadge>
                </div>
              </td>
            </tr>
          </tbody>
        </table>
      </template>
    </McCard>

    <!-- Categories & tags -->
    <McCard title="Categories &amp; tags">
      <p class="shp-hint">
        Push each linked product's <strong>Category</strong>, <strong>Manufacturer</strong> and
        <strong>Item type</strong> to Shopify as tags (and keep product type and vendor in sync).
        One-way — no other product fields change.
      </p>
      <McAlert v-if="tagErr" variant="error">{{ tagErr }}</McAlert>
      <div class="shp-actions">
        <McButton variant="secondary" type="button" :disabled="tagBusy" @click="previewTags">
          <McSpinner v-if="tagBusy" /><span v-else>Preview</span>
        </McButton>
        <McButton variant="primary" type="button" :disabled="tagBusy || !tagPreview" @click="syncTags">Sync now</McButton>
      </div>
      <div v-if="tagPreview" class="shp-result">
        <p v-if="!tagPreview.applied" class="shp-hint">
          {{ tagPreview.linkedProductCount }} linked product{{ tagPreview.linkedProductCount === 1 ? '' : 's' }} will be updated.
        </p>
        <p v-else class="shp-hint">
          Updated {{ tagPreview.updatedCount }} of {{ tagPreview.linkedProductCount }}.
          <span v-if="tagPreview.failedCount"> {{ tagPreview.failedCount }} failed.</span>
        </p>
      </div>
    </McCard>

    <!-- Shared link modal -->
    <McModal v-model="showLinkModal" :title="linkTarget ? `Link: ${linkTarget.title}` : 'Link'">
      <McField label="Search POS product" for-id="shp-link-search">
        <input id="shp-link-search" v-model="productQuery" autocomplete="off" placeholder="SKU or name…" @input="searchProducts" />
      </McField>
      <div v-if="searchingProducts" class="shp-hint"><McSpinner /> Searching…</div>
      <ul v-else-if="productResults.length" class="shp-search-results">
        <li v-for="p in productResults" :key="p.id" :class="{ 'shp-disabled': linkBusy }" @click="confirmLink(p)">
          <strong>{{ p.sku }}</strong> — {{ p.name }} ({{ formatZAR(p.sellPrice) }})
        </li>
      </ul>
      <p v-else-if="productQuery.trim()" class="shp-hint">No products match “{{ productQuery }}”.</p>
      <template #footer>
        <McButton variant="ghost" type="button" @click="closeLink">Close</McButton>
      </template>
    </McModal>
  </div>
</template>

<style scoped>
.shopify-page {
  min-height: 100%;
  display: flex;
  flex-direction: column;
  gap: 1.25rem;
  max-width: var(--mc-container-width, 1200px);
  margin: 0 auto;
  width: 100%;
}
.shp-hint { margin: 0 0 0.75rem; font-size: 0.9rem; color: var(--mc-app-text-muted, #5c5a56); line-height: 1.55; }
.shp-period { display: flex; flex-wrap: wrap; align-items: flex-end; gap: 0.75rem; margin-bottom: 1rem; }
.shp-period :deep(.mc-field) { margin-bottom: 0; }
.shp-actions { display: flex; gap: 0.6rem; margin: 0.5rem 0; }
.shp-quick { display: flex; flex-wrap: wrap; gap: 0.6rem; margin-top: 1rem; padding-top: 1rem; border-top: 1px solid var(--mc-app-border-faint, #eceae5); }

/* KPI cards */
.shp-kpis { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: 0.75rem; margin: 0.5rem 0; }
.kpi { border: 1px solid var(--mc-app-border-soft, #e0ddd8); border-radius: 12px; padding: 0.85rem 1rem; background: var(--mc-app-surface, #fff); display: flex; flex-direction: column; gap: 0.15rem; }
.kpi--accent { border-color: var(--mc-accent, #f47a20); box-shadow: inset 3px 0 0 var(--mc-accent, #f47a20); }
.kpi--warn { border-color: #e6a23c; box-shadow: inset 3px 0 0 #e6a23c; }
.kpi__label { font-size: 0.72rem; letter-spacing: 0.05em; text-transform: uppercase; color: var(--mc-app-text-muted, #8a8780); }
.kpi__value { font-size: 1.4rem; font-weight: 700; color: var(--mc-app-text, #1a1a1c); font-variant-numeric: tabular-nums; }
.kpi__value--sm { font-size: 0.95rem; line-height: 1.25; }
.kpi__sub { font-size: 0.74rem; color: var(--mc-app-text-muted, #8a8780); }

/* Tables */
.shp-loading { display: flex; align-items: center; gap: 0.5rem; padding: 1.25rem 0; color: var(--mc-app-text-muted, #5c5a56); }
.shp-table { width: 100%; border-collapse: collapse; margin-top: 0.75rem; font-size: 0.86rem; }
.shp-table th { text-align: left; font-size: 0.7rem; font-weight: 700; text-transform: uppercase; letter-spacing: 0.04em; color: var(--mc-app-text-muted, #666); padding: 0.4rem 0.5rem; border-bottom: 1.5px solid var(--mc-app-border-subtle, #c8c5bd); }
.shp-table td { padding: 0.5rem 0.5rem; border-bottom: 1px solid var(--mc-app-border-faint, #eceae5); vertical-align: top; }
.shp-r { text-align: right; font-variant-numeric: tabular-nums; }
.shp-row-actions { display: flex; gap: 0.4rem; justify-content: flex-end; }
.shp-hint--inline { margin: 0; }
.shp-check { display: inline-flex; align-items: center; gap: 0.4rem; font-size: 0.85rem; font-weight: 600; color: var(--mc-app-text-secondary, #333); cursor: pointer; }
.shp-diff { color: #b45309; font-weight: 700; }
.shp-special { color: #065f46; font-weight: 600; }
.shp-onsale { display: flex; align-items: center; gap: 0.4rem; justify-content: flex-end; margin-top: 0.15rem; }
.shp-was { font-size: 0.78rem; color: var(--mc-app-text-muted, #8a8780); text-decoration: line-through; }
.shp-lock { font-size: 0.72rem; color: var(--mc-app-text-muted, #8a8780); text-transform: uppercase; letter-spacing: 0.04em; }
.shp-item-title { font-weight: 600; color: var(--mc-app-text, #1a1a1c); }
.shp-item-sku { font-size: 0.78rem; color: var(--mc-app-text-muted, #8a8780); font-variant-numeric: tabular-nums; }

/* Variants controls */
.shp-var-controls { display: flex; flex-wrap: wrap; align-items: center; gap: 0.6rem; margin-top: 0.5rem; }
.shp-search-input { flex: 1 1 220px; padding: 0.5rem 0.7rem; border: 1px solid var(--mc-app-border-subtle, #c8c5bd); border-radius: 8px; font-size: 0.88rem; }
.shp-seg { display: inline-flex; padding: 0.2rem; gap: 0.2rem; background: var(--mc-app-bg-subtle, #f0ede8); border: 1px solid var(--mc-app-border-subtle, #c8c5bd); border-radius: 10px; }
.shp-seg__btn { border: 0; background: transparent; padding: 0.4rem 0.75rem; border-radius: 8px; font-size: 0.82rem; font-weight: 600; color: var(--mc-app-text-secondary, #555); cursor: pointer; }
.shp-seg__btn--active { background: var(--mc-app-surface, #fff); color: var(--mc-accent, #f47a20); box-shadow: 0 1px 2px rgba(0,0,0,0.08); }
.shp-pager { display: flex; align-items: center; gap: 0.75rem; margin-top: 0.75rem; font-size: 0.85rem; color: var(--mc-app-text-muted, #5c5a56); }

/* Link modal search */
.shp-search-results { list-style: none; margin: 0.25rem 0 0; padding: 0; border: 1px solid var(--mc-app-border-soft, #ddd9d3); border-radius: 0.35rem; max-height: 240px; overflow-y: auto; background: var(--mc-app-surface, #fff); }
.shp-search-results li { padding: 0.5rem 0.75rem; cursor: pointer; font-size: 0.84rem; }
.shp-search-results li:hover { background: var(--mc-app-hover, #f0ede8); }
.shp-search-results li.shp-disabled { pointer-events: none; opacity: 0.6; }

.shp-result { margin-top: 0.75rem; }

@media (max-width: 1000px) { .shp-kpis { grid-template-columns: repeat(2, minmax(0, 1fr)); } }
@media (max-width: 560px) { .shp-kpis { grid-template-columns: 1fr; } }
</style>
