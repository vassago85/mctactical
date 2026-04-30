<script setup lang="ts">
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { LayoutDashboard, FileText, Package, Truck } from 'lucide-vue-next'

/**
 * Shared tab strip used on /dashboard and /reports so the consolidated analytics
 * surface feels like one page even though it spans two routes (Overview lives
 * on /dashboard, the report sections live on /reports?tab=).
 *
 * Active tab is derived from the current route + query, so deep-links into a
 * specific report still highlight the correct tab.
 */

const route = useRoute()
const router = useRouter()

type TabKey = 'overview' | 'sales' | 'stock' | 'consignment'

const tabs: { key: TabKey; label: string; icon: typeof LayoutDashboard; to: () => any }[] = [
  { key: 'overview',    label: 'Overview',    icon: LayoutDashboard, to: () => ({ path: '/dashboard' }) },
  { key: 'sales',       label: 'Invoices',    icon: FileText,        to: () => ({ path: '/reports', query: { tab: 'sales' } }) },
  { key: 'stock',       label: 'Stock',       icon: Package,         to: () => ({ path: '/reports', query: { tab: 'stock' } }) },
  { key: 'consignment', label: 'Consignment', icon: Truck,           to: () => ({ path: '/reports', query: { tab: 'consignment' } }) }
]

const activeKey = computed<TabKey>(() => {
  if (route.path === '/dashboard') return 'overview'
  if (route.path === '/reports') {
    const t = String(route.query.tab ?? '')
    if (t === 'sales' || t === 'stock' || t === 'consignment') return t
    return 'sales'
  }
  return 'overview'
})

function go(t: TabKey) {
  const target = tabs.find((x) => x.key === t)
  if (!target) return
  router.push(target.to())
}
</script>

<template>
  <nav class="dt" role="tablist" aria-label="Dashboard sections">
    <button
      v-for="t in tabs"
      :key="t.key"
      type="button"
      class="dt__tab"
      :class="{ 'dt__tab--active': activeKey === t.key }"
      :aria-selected="activeKey === t.key"
      role="tab"
      @click="go(t.key)"
    >
      <component :is="t.icon" :size="14" />
      <span>{{ t.label }}</span>
    </button>
  </nav>
</template>

<style scoped>
.dt {
  display: flex;
  flex-wrap: wrap;
  gap: 4px;
  border-bottom: 1px solid var(--mc-app-border-soft, #ddd9d3);
  padding: 0 4px;
  background: transparent;
}

.dt__tab {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 10px 16px;
  border: none;
  background: transparent;
  font-size: 14px;
  font-weight: 500;
  color: var(--mc-app-text-secondary, #5c5a56);
  cursor: pointer;
  border-bottom: 2px solid transparent;
  margin-bottom: -1px;
  border-radius: 6px 6px 0 0;
  transition: background 120ms ease, color 120ms ease, border-color 120ms ease;
}

.dt__tab:hover {
  background: var(--mc-app-surface-2, #faf9f6);
  color: var(--mc-app-heading, #0a0a0c);
}

.dt__tab--active {
  color: var(--mc-app-heading, #0a0a0c);
  background: var(--mc-app-surface, #fff);
  border-bottom-color: var(--mc-app-accent, #f47a20);
}

@media print {
  .dt { display: none !important; }
}
</style>
