<script setup lang="ts">
import { onMounted, ref, watch } from 'vue'
import { http } from '@/api/http'
import { formatZAR } from '@/utils/format'
import BarcodeScanner from '@/components/BarcodeScanner.vue'
import McPageHeader from '@/components/ui/McPageHeader.vue'
import McCard from '@/components/ui/McCard.vue'
import McButton from '@/components/ui/McButton.vue'
import McField from '@/components/ui/McField.vue'
import McEmptyState from '@/components/ui/McEmptyState.vue'
import McSkeleton from '@/components/ui/McSkeleton.vue'
import McBadge from '@/components/ui/McBadge.vue'

type Product = {
  id: string
  sku: string
  barcode?: string | null
  name: string
  sellPrice: number
  qtyOnHand: number
}

type ActiveSpecial = { productId: string; specialPrice?: number | null; discountPercent?: number | null }
type ActivePromotion = {
  promotionId?: string | null
  promotionName?: string | null
  siteDiscountPercent: number
  specials: ActiveSpecial[]
}

const q = ref('')
const results = ref<Product[]>([])
const scanOpen = ref(false)
const searchLoading = ref(false)
const activePromo = ref<ActivePromotion | null>(null)

function roundUpR10(v: number): number {
  return Math.ceil(v / 10) * 10
}

function getEffectivePrice(p: Product): { price: number; hasDiscount: boolean } {
  if (!activePromo.value) return { price: p.sellPrice, hasDiscount: false }
  const special = activePromo.value.specials.find(s => s.productId === p.id)
  if (special) {
    if (special.specialPrice != null) return { price: special.specialPrice, hasDiscount: special.specialPrice !== p.sellPrice }
    if (special.discountPercent != null) {
      const price = roundUpR10(p.sellPrice * (1 - special.discountPercent / 100))
      return { price, hasDiscount: special.discountPercent > 0 }
    }
  }
  if (activePromo.value.siteDiscountPercent > 0) {
    const price = roundUpR10(p.sellPrice * (1 - activePromo.value.siteDiscountPercent / 100))
    return { price, hasDiscount: true }
  }
  return { price: p.sellPrice, hasDiscount: false }
}

onMounted(async () => {
  try {
    const { data } = await http.get<ActivePromotion>('/api/promotions/active')
    if (data.promotionId || data.specials.length) activePromo.value = data
  } catch { /* no active promotion */ }
})

let searchTimer: ReturnType<typeof setTimeout> | null = null
watch(q, () => {
  if (searchTimer) clearTimeout(searchTimer)
  searchTimer = setTimeout(() => void runSearch(), 250)
})

async function runSearch() {
  const s = q.value.trim()
  if (!s) {
    results.value = []
    searchLoading.value = false
    return
  }
  searchLoading.value = true
  try {
    const { data } = await http.get<Product[]>('/api/products', {
      params: { q: s, take: 40 }
    })
    results.value = data
  } catch {
    results.value = []
  } finally {
    searchLoading.value = false
  }
}

function onScan(code: string) {
  q.value = code.trim()
  scanOpen.value = false
  void runSearch()
}
</script>

<template>
  <div class="lookup-page">
    <McPageHeader title="Price lookup" description="Scan a barcode or search by SKU / name to check prices and stock." />

    <div v-if="activePromo?.promotionName" class="lookup-promo-banner">
      <McBadge variant="accent">{{ activePromo.promotionName }}</McBadge>
      <span v-if="activePromo.siteDiscountPercent > 0">{{ activePromo.siteDiscountPercent }}% off all items</span>
      <span v-if="activePromo.specials.length"> · {{ activePromo.specials.length }} product special{{ activePromo.specials.length !== 1 ? 's' : '' }}</span>
    </div>

    <McCard title="Find product">
      <div class="lookup-scan-row">
        <McButton variant="secondary" type="button" @click="scanOpen = !scanOpen">
          {{ scanOpen ? 'Hide scanner' : 'Scan barcode' }}
        </McButton>
      </div>
      <div v-if="scanOpen" class="lookup-scanner-wrap">
        <BarcodeScanner :active="scanOpen" @decode="onScan" />
      </div>
      <McField label="Search" for-id="lookup-search">
        <input
          id="lookup-search"
          v-model="q"
          type="search"
          autocomplete="off"
          placeholder="SKU, barcode, or name…"
          class="lookup-search-input"
        />
      </McField>

      <McSkeleton v-if="searchLoading" :lines="4" />

      <McEmptyState
        v-else-if="!q.trim()"
        title="Search or scan"
        hint="Type a SKU, barcode, or product name. Or use the scanner to look up a product."
      />

      <McEmptyState
        v-else-if="!searchLoading && q.trim() && !results.length"
        title="No matches"
        hint="Try other words — search matches text anywhere in the name."
      />

      <ul v-else class="lookup-results">
        <li v-for="p in results" :key="p.id" class="lookup-result">
          <div class="lookup-result__main">
            <p class="lookup-result__name">{{ p.name }}</p>
            <p class="lookup-result__meta">
              <span>{{ p.sku }}</span>
              <span v-if="p.barcode"> · {{ p.barcode }}</span>
            </p>
          </div>
          <div class="lookup-result__side">
            <template v-if="getEffectivePrice(p).hasDiscount">
              <span class="lookup-result__price lookup-result__price--sale">{{ formatZAR(getEffectivePrice(p).price) }}</span>
              <span class="lookup-result__price--was">{{ formatZAR(p.sellPrice) }}</span>
            </template>
            <span v-else class="lookup-result__price">{{ formatZAR(p.sellPrice) }}</span>
            <McBadge :variant="p.qtyOnHand > 0 ? 'success' : 'danger'">
              {{ p.qtyOnHand > 0 ? `In stock (${p.qtyOnHand})` : 'Out of stock' }}
            </McBadge>
          </div>
        </li>
      </ul>
    </McCard>
  </div>
</template>

<style scoped>
.lookup-page {
  max-width: 700px;
}
.lookup-promo-banner {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  padding: 0.5rem 0.75rem;
  margin-bottom: 1rem;
  background: var(--mc-app-surface-alt, #faf9f6);
  border-radius: 8px;
  font-size: 0.85rem;
}
.lookup-scan-row {
  margin-bottom: 0.75rem;
}
.lookup-scanner-wrap {
  margin-bottom: 0.75rem;
}
.lookup-search-input {
  width: 100%;
}
.lookup-results {
  list-style: none;
  padding: 0;
  margin: 1rem 0 0;
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}
.lookup-result {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 1rem;
  padding: 0.75rem;
  background: var(--mc-app-surface-alt, #faf9f6);
  border-radius: 8px;
  border: 1px solid var(--mc-app-border-faint, #eceae5);
}
.lookup-result__main {
  flex: 1;
  min-width: 0;
}
.lookup-result__name {
  margin: 0;
  font-weight: 600;
  font-size: 0.95rem;
  line-height: 1.3;
}
.lookup-result__meta {
  margin: 0.15rem 0 0;
  font-size: 0.8rem;
  color: var(--mc-app-text-muted, #777);
}
.lookup-result__side {
  display: flex;
  flex-direction: column;
  align-items: flex-end;
  gap: 0.25rem;
  flex-shrink: 0;
}
.lookup-result__price {
  font-weight: 700;
  font-size: 1.1rem;
}
.lookup-result__price--sale {
  font-weight: 700;
  font-size: 1.1rem;
  color: #cc0000;
}
.lookup-result__price--was {
  font-size: 0.8rem;
  color: #999;
  text-decoration: line-through;
}
</style>
