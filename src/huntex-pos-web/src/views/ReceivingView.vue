<script setup lang="ts">
import { useRoute } from 'vue-router'
import McPageHeader from '@/components/ui/McPageHeader.vue'
import McTabs, { type McTab } from '@/components/ui/McTabs.vue'
import McTabPanel from '@/components/ui/McTabPanel.vue'
import ConsignmentBatchView from '@/views/ConsignmentBatchView.vue'
import ImportView from '@/views/ImportView.vue'

const route = useRoute()

const tabs: McTab[] = [
  { to: '/receiving', label: 'Batches' },
  { to: '/receiving/import', label: 'Import' }
]
</script>

<template>
  <div class="receiving-page">
    <McPageHeader title="Receiving" description="Receive and return stock in batches, or import products from a workbook or wholesaler file." />
    <McTabs :tabs="tabs" label="Receiving sections" />
    <McTabPanel>
      <ImportView v-if="route.path === '/receiving/import'" />
      <ConsignmentBatchView v-else />
    </McTabPanel>
  </div>
</template>
