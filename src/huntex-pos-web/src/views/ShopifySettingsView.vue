<script setup lang="ts">
/**
 * Shopify integration tools (Owner/Dev). Currently hosts category/tag sync; the
 * "Match Shopify sales" tool is added here in a later phase.
 */
import { onMounted, ref } from 'vue'
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

const toast = useToast()

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

function handleErr(e: unknown, fallback: string): string {
  const ax = e as { response?: { data?: { error?: string } }; message?: string }
  return ax.response?.data?.error ?? ax.message ?? fallback
}

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

// ── Match Shopify sales (link unlinked online items to POS products) ────────
type UnlinkedSale = {
  shopifyVariantId: number | null
  shopifySku: string | null
  title: string
  qtySold: number
  revenue: number
  orderCount: number
}
type ProductHit = { id: string; sku: string; name: string; sellPrice: number }

const unlinked = ref<UnlinkedSale[]>([])
const unlinkedBusy = ref(false)
const unlinkedErr = ref<string | null>(null)
const activeKey = ref<string | null>(null)
const productQuery = ref('')
const productResults = ref<ProductHit[]>([])
const searchingProducts = ref(false)
const linkBusy = ref(false)
let productSearchTimer: ReturnType<typeof setTimeout> | null = null

function rowKey(r: UnlinkedSale): string {
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

function openLink(r: UnlinkedSale) {
  const key = rowKey(r)
  activeKey.value = activeKey.value === key ? null : key
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

async function linkTo(r: UnlinkedSale, product: ProductHit) {
  if (linkBusy.value) return
  if (!confirm(`Link "${r.title}" to ${product.sku} — ${product.name}? This also reclassifies its past Shopify sales.`)) return
  linkBusy.value = true
  try {
    const { data } = await http.post<{ reclassifiedLineCount: number }>('/api/shopify/link-variant', {
      shopifyVariantId: r.shopifyVariantId,
      shopifySku: r.shopifySku,
      posProductId: product.id
    })
    const n = data.reclassifiedLineCount ?? 0
    toast.success(`Linked. ${n} past sale line${n === 1 ? '' : 's'} reclassified.`)
    unlinked.value = unlinked.value.filter(x => rowKey(x) !== rowKey(r))
    activeKey.value = null
    productQuery.value = ''
    productResults.value = []
  } catch (e) {
    toast.error(handleErr(e, 'Link failed'))
  } finally {
    linkBusy.value = false
  }
}

onMounted(loadUnlinked)

async function syncTags() {
  const count = tagPreview.value?.linkedProductCount
  const confirmMsg = count
    ? `Push categories/tags to ${count} linked Shopify product${count === 1 ? '' : 's'}? This updates tags, product type and vendor on Shopify only.`
    : 'Push categories/tags to all linked Shopify products?'
  if (!confirm(confirmMsg)) return

  tagBusy.value = true
  tagErr.value = null
  try {
    const { data } = await http.post<TagPreview>('/api/shopify/sync-tags?apply=true')
    tagPreview.value = data
    const updated = data.updatedCount ?? 0
    const failed = data.failedCount ?? 0
    if (failed > 0) toast.error(`Synced ${updated}, ${failed} failed. See details below.`)
    else toast.success(`Synced tags to ${updated} Shopify product${updated === 1 ? '' : 's'}.`)
  } catch (e) {
    tagErr.value = handleErr(e, 'Sync failed')
    toast.error(tagErr.value)
  } finally {
    tagBusy.value = false
  }
}
</script>

<template>
  <div class="shopify-page">
    <McPageHeader title="Shopify">
      <template #default>
        Tools for keeping the POS and your Shopify store aligned. The POS is the source of truth.
      </template>
    </McPageHeader>

    <McCard title="Match Shopify sales">
      <p class="shp-hint">
        Shopify items that sold online but aren't linked to a POS product, highest revenue first.
        Link each to its POS product to attribute past and future sales correctly (this also fixes the
        Shopify gross-profit figure as items are matched).
      </p>

      <McAlert v-if="unlinkedErr" variant="error">{{ unlinkedErr }}</McAlert>

      <div class="shp-actions">
        <McButton variant="secondary" type="button" :disabled="unlinkedBusy" @click="loadUnlinked">
          <McSpinner v-if="unlinkedBusy" />
          <span v-else>Refresh</span>
        </McButton>
      </div>

      <div v-if="unlinkedBusy && !unlinked.length" class="shp-loading"><McSpinner /> Loading…</div>

      <McEmptyState
        v-else-if="!unlinked.length"
        title="Nothing to link"
        description="Every Shopify item that has sold is linked to a POS product."
      />

      <table v-else class="shp-table">
        <thead>
          <tr>
            <th>Item</th>
            <th class="shp-r">Qty</th>
            <th class="shp-r">Revenue</th>
            <th class="shp-r">Orders</th>
            <th></th>
          </tr>
        </thead>
        <tbody>
          <template v-for="r in unlinked" :key="rowKey(r)">
            <tr>
              <td>
                <div class="shp-item-title">{{ r.title }}</div>
                <div v-if="r.shopifySku" class="shp-item-sku">{{ r.shopifySku }}</div>
              </td>
              <td class="shp-r">{{ formatNumber(r.qtySold) }}</td>
              <td class="shp-r">{{ formatZAR(r.revenue) }}</td>
              <td class="shp-r">{{ formatNumber(r.orderCount) }}</td>
              <td class="shp-r">
                <McButton variant="ghost" dense type="button" @click="openLink(r)">
                  {{ activeKey === rowKey(r) ? 'Cancel' : 'Link to POS product' }}
                </McButton>
              </td>
            </tr>
            <tr v-if="activeKey === rowKey(r)" class="shp-link-row">
              <td colspan="5">
                <McField label="Search POS product" :for-id="`lk-${rowKey(r)}`">
                  <input
                    :id="`lk-${rowKey(r)}`"
                    v-model="productQuery"
                    autocomplete="off"
                    placeholder="SKU or name…"
                    @input="searchProducts"
                  />
                </McField>
                <div v-if="searchingProducts" class="shp-hint"><McSpinner /> Searching…</div>
                <ul v-else-if="productResults.length" class="shp-search-results">
                  <li v-for="p in productResults" :key="p.id" :class="{ 'shp-disabled': linkBusy }" @click="linkTo(r, p)">
                    <strong>{{ p.sku }}</strong> — {{ p.name }} ({{ formatZAR(p.sellPrice) }})
                  </li>
                </ul>
                <p v-else-if="productQuery.trim()" class="shp-hint">No products match “{{ productQuery }}”.</p>
              </td>
            </tr>
          </template>
        </tbody>
      </table>
    </McCard>

    <McCard title="Categories &amp; tags">
      <p class="shp-hint">
        Push each linked product's <strong>Category</strong>, <strong>Manufacturer</strong> and
        <strong>Item type</strong> to Shopify as tags (and keep product type and vendor in sync), so
        products filter into the same groups in both systems. One-way — no other product fields change.
      </p>

      <McAlert v-if="tagErr" variant="error">{{ tagErr }}</McAlert>

      <div class="shp-actions">
        <McButton variant="secondary" type="button" :disabled="tagBusy" @click="previewTags">
          <McSpinner v-if="tagBusy" />
          <span v-else>Preview</span>
        </McButton>
        <McButton variant="primary" type="button" :disabled="tagBusy || !tagPreview" @click="syncTags">
          Sync now
        </McButton>
      </div>

      <div v-if="tagPreview" class="shp-result">
        <p v-if="!tagPreview.applied" class="shp-hint">
          {{ tagPreview.linkedProductCount }} linked product{{ tagPreview.linkedProductCount === 1 ? '' : 's' }}
          will be updated. Sample of tags to be sent:
        </p>
        <p v-else class="shp-hint">
          Updated {{ tagPreview.updatedCount }} of {{ tagPreview.linkedProductCount }} products.
          <span v-if="tagPreview.failedCount"> {{ tagPreview.failedCount }} failed.</span>
        </p>

        <ul v-if="!tagPreview.applied && tagPreview.sample?.length" class="shp-samples">
          <li v-for="s in tagPreview.sample" :key="s.sku">
            <strong>{{ s.sku }}</strong>
            <span class="shp-tags">
              <span v-for="t in s.tags" :key="t" class="shp-tag">{{ t }}</span>
              <span v-if="!s.tags.length" class="shp-hint">no category fields set</span>
            </span>
          </li>
        </ul>

        <ul v-if="tagPreview.failures?.length" class="shp-failures">
          <li v-for="f in tagPreview.failures" :key="f.sku">
            <strong>{{ f.sku }}</strong> — {{ f.detail }}
          </li>
        </ul>
      </div>
    </McCard>
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
.shp-hint {
  margin: 0 0 0.75rem;
  font-size: 0.9rem;
  color: var(--mc-app-text-muted, #5c5a56);
  line-height: 1.55;
}
.shp-actions {
  display: flex;
  gap: 0.6rem;
  margin-top: 0.5rem;
}
.shp-result {
  margin-top: 1rem;
  padding-top: 1rem;
  border-top: 1px solid var(--mc-app-border-soft, #ddd9d3);
}
.shp-samples {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}
.shp-samples li {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.5rem 0.75rem;
  font-size: 0.85rem;
}
.shp-tags {
  display: inline-flex;
  flex-wrap: wrap;
  gap: 0.35rem;
}
.shp-tag {
  background: var(--mc-app-surface-muted, #f0eeea);
  border: 1px solid var(--mc-app-border-soft, #ddd9d3);
  border-radius: 999px;
  padding: 0.1rem 0.55rem;
  font-size: 0.75rem;
  color: var(--mc-app-text, #1a1a1c);
}
.shp-failures {
  margin: 0.75rem 0 0;
  padding-left: 1.1rem;
  font-size: 0.82rem;
  color: var(--mc-app-danger, #9a1818);
}

.shp-loading {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  padding: 1.25rem 0;
  color: var(--mc-app-text-muted, #5c5a56);
}

.shp-table {
  width: 100%;
  border-collapse: collapse;
  margin-top: 0.75rem;
  font-size: 0.86rem;
}
.shp-table th {
  text-align: left;
  font-size: 0.7rem;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.04em;
  color: var(--mc-app-text-muted, #666);
  padding: 0.4rem 0.5rem;
  border-bottom: 1.5px solid var(--mc-app-border-subtle, #c8c5bd);
}
.shp-table td {
  padding: 0.5rem 0.5rem;
  border-bottom: 1px solid var(--mc-app-border-faint, #eceae5);
  vertical-align: top;
}
.shp-r { text-align: right; font-variant-numeric: tabular-nums; }
.shp-item-title { font-weight: 600; color: var(--mc-app-text, #1a1a1c); }
.shp-item-sku { font-size: 0.76rem; color: var(--mc-app-text-muted, #8a8780); font-variant-numeric: tabular-nums; }
.shp-link-row td { background: var(--mc-app-bg-subtle, #f6f4f1); }
.shp-search-results {
  list-style: none;
  margin: 0.25rem 0 0;
  padding: 0;
  border: 1px solid var(--mc-app-border-soft, #ddd9d3);
  border-radius: 0.35rem;
  max-height: 220px;
  overflow-y: auto;
  background: var(--mc-app-surface, #fff);
}
.shp-search-results li {
  padding: 0.5rem 0.75rem;
  cursor: pointer;
  font-size: 0.84rem;
}
.shp-search-results li:hover { background: var(--mc-app-hover, #f0ede8); }
.shp-search-results li.shp-disabled { pointer-events: none; opacity: 0.6; }
</style>
