<script setup lang="ts">
import { computed, onMounted, watch } from 'vue'
import { useRoute } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import McToastHost from '@/components/ui/McToastHost.vue'
import AppShell from '@/components/layout/AppShell.vue'
import PwaInstallBanner from '@/components/layout/PwaInstallBanner.vue'

const auth = useAuthStore()
const route = useRoute()

const isPublicLayout = computed(() => route.meta.layout === 'public')

watch(
  [isPublicLayout, () => auth.isAuthenticated],
  () => {
    const useAppChrome = !isPublicLayout.value && auth.isAuthenticated
    document.body.classList.toggle('mc-body-app', useAppChrome)
  },
  { immediate: true }
)

onMounted(() => {
  void auth.loadMe()
})
</script>

<template>
  <McToastHost />
  <PwaInstallBanner />
  <AppShell v-if="!isPublicLayout && auth.isAuthenticated">
    <RouterView />
  </AppShell>
  <div v-else class="layout-public">
    <RouterView />
  </div>
</template>
