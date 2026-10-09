<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import { useBranding } from '@/composables/useBranding'
import McPageHeader from '@/components/ui/McPageHeader.vue'
import McTabs, { type McTab } from '@/components/ui/McTabs.vue'
import McTabPanel from '@/components/ui/McTabPanel.vue'
import SalesHistoryView from '@/views/SalesHistoryView.vue'
import QuotesListView from '@/views/QuotesListView.vue'
import DeliveriesView from '@/views/DeliveriesView.vue'

const route = useRoute()
const auth = useAuthStore()
const { features, terminology } = useBranding()

const tabs = computed<McTab[]>(() => {
  const list: McTab[] = [{ to: '/sales', label: 'Invoices' }]
  if (features.value.quotes) list.push({ to: '/sales/quotes', label: `${terminology.value.quote}s` })
  if (auth.hasRole('Admin', 'Owner', 'Dev')) list.push({ to: '/sales/deliveries', label: 'Deliveries' })
  return list
})
</script>

<template>
  <div class="sales-page">
    <McPageHeader title="Sales" description="Find past sales, manage quotes and track deliveries." />
    <McTabs :tabs="tabs" label="Sales sections" />
    <McTabPanel>
      <QuotesListView v-if="route.path === '/sales/quotes'" />
      <DeliveriesView v-else-if="route.path === '/sales/deliveries'" />
      <SalesHistoryView v-else />
    </McTabPanel>
  </div>
</template>
