<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import McPageHeader from '@/components/ui/McPageHeader.vue'
import McTabs, { type McTab } from '@/components/ui/McTabs.vue'
import McTabPanel from '@/components/ui/McTabPanel.vue'
import BusinessSettingsView from '@/views/BusinessSettingsView.vue'
import SettingsView from '@/views/SettingsView.vue'
import AdminTeamView from '@/views/AdminTeamView.vue'
import SalespeopleView from '@/views/SalespeopleView.vue'
import ShopifySettingsView from '@/views/ShopifySettingsView.vue'
import SetupView from '@/views/SetupView.vue'

type SettingsTab = 'business' | 'pricing' | 'team' | 'integrations'
const SETTINGS_TABS: readonly SettingsTab[] = ['business', 'pricing', 'team', 'integrations']

const route = useRoute()
const auth = useAuthStore()

const activeTab = computed<SettingsTab>(() => {
  const segment = route.path.split('/')[2]
  return SETTINGS_TABS.find(t => t === segment) ?? 'business'
})

const canSeeShopify = computed(() => auth.hasRole('Owner', 'Dev'))

const tabs: McTab[] = [
  { to: '/settings', label: 'Business' },
  { to: '/settings/pricing', label: 'Pricing' },
  { to: '/settings/team', label: 'Team & salespeople' },
  { to: '/settings/integrations', label: 'Integrations' }
]
</script>

<template>
  <div class="settings-shell">
    <McPageHeader title="Settings" description="Business details, pricing, staff and connected services." />
    <McTabs :tabs="tabs" label="Settings sections" />

    <McTabPanel v-if="activeTab === 'business'">
      <BusinessSettingsView />
    </McTabPanel>

    <McTabPanel v-else-if="activeTab === 'pricing'">
      <SettingsView />
    </McTabPanel>

    <McTabPanel v-else-if="activeTab === 'team'" class="settings-shell__stack">
      <AdminTeamView />
      <SalespeopleView />
    </McTabPanel>

    <McTabPanel v-else class="settings-shell__stack">
      <ShopifySettingsView v-if="canSeeShopify" />
      <SetupView />
    </McTabPanel>
  </div>
</template>

<style scoped>
.settings-shell__stack {
  display: flex;
  flex-direction: column;
  gap: 2.5rem;
}
</style>
