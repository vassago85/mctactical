<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import { logoLight } from '@/branding'

const auth = useAuthStore()
const route = useRoute()
const router = useRouter()
const sidebarOpen = ref(false)

const isStandalone = ref(
  window.matchMedia('(display-mode: standalone)').matches ||
  (navigator as any).standalone === true
)

const canGoBack = computed(() => window.history.length > 1)

watch(
  () => route.fullPath,
  () => {
    sidebarOpen.value = false
  }
)

function goBack() {
  router.back()
}

function goForward() {
  router.forward()
}

function logout() {
  auth.clear()
  router.push('/login')
}
</script>

<template>
  <div class="app-shell" :class="{ 'app-shell--standalone': isStandalone }">
    <div
      class="mc-sidebar-overlay"
      :class="{ 'mc-sidebar-overlay--visible': sidebarOpen }"
      aria-hidden="true"
      @click="sidebarOpen = false"
    />
    <aside class="mc-sidebar" :class="{ 'mc-sidebar--open': sidebarOpen }">
      <div class="mc-sidebar__brand">
        <img class="mc-sidebar__logo" :src="logoLight" alt="MC Tactical" />
        <p class="mc-sidebar__tag">Point of sale</p>
      </div>
      <nav class="mc-sidebar__nav" aria-label="Main">
        <div class="mc-nav-group">
          <p class="mc-nav-group__label">Sell</p>
          <RouterLink class="mc-nav-link" to="/pos" @click="sidebarOpen = false">POS</RouterLink>
          <RouterLink class="mc-nav-link" to="/price-lookup" @click="sidebarOpen = false">Price lookup</RouterLink>
        </div>
        <div class="mc-nav-group">
          <p class="mc-nav-group__label">Inventory</p>
          <RouterLink class="mc-nav-link" to="/stock" @click="sidebarOpen = false">Stock list</RouterLink>
          <RouterLink class="mc-nav-link" to="/stocktake" @click="sidebarOpen = false">Stocktake</RouterLink>
          <RouterLink class="mc-nav-link" to="/consignment" @click="sidebarOpen = false">Consignment</RouterLink>
        </div>
        <div v-if="auth.hasRole('Admin', 'Owner', 'Dev')" class="mc-nav-group">
          <p class="mc-nav-group__label">Office</p>
          <RouterLink class="mc-nav-link" to="/deliveries" @click="sidebarOpen = false">Deliveries</RouterLink>
          <RouterLink class="mc-nav-link" to="/import" @click="sidebarOpen = false">Import</RouterLink>
          <RouterLink class="mc-nav-link" to="/reports" @click="sidebarOpen = false">Reports</RouterLink>
          <RouterLink class="mc-nav-link" to="/settings" @click="sidebarOpen = false">Pricing</RouterLink>
          <RouterLink class="mc-nav-link" to="/setup" @click="sidebarOpen = false">Email setup</RouterLink>
          <RouterLink class="mc-nav-link" to="/admin/team" @click="sidebarOpen = false">Team</RouterLink>
        </div>
      </nav>
      <div class="mc-sidebar__foot">
        <button type="button" class="mc-sidebar__logout" @click="logout">Log out</button>
      </div>
    </aside>
    <div class="app-main">
      <header class="mc-topbar">
        <button type="button" class="mc-topbar__menu" aria-label="Open menu" @click="sidebarOpen = true">☰</button>
        <button
          type="button"
          class="mc-topbar__nav-btn"
          :class="{ 'mc-topbar__nav-btn--disabled': !canGoBack }"
          :disabled="!canGoBack"
          aria-label="Go back"
          @click="goBack"
        >◀</button>
        <button
          type="button"
          class="mc-topbar__nav-btn"
          aria-label="Go forward"
          @click="goForward"
        >▶</button>
        <span class="brand-wordmark" style="font-size: 0.95rem; color: #2a2a2d">MC Tactical</span>
      </header>
      <div class="app-main__inner">
        <slot />
      </div>
    </div>
  </div>
</template>
