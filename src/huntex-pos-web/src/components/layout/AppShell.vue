<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import { logoLight } from '@/branding'
import { useBranding } from '@/composables/useBranding'
import {
  Menu, ChevronLeft, ChevronRight,
  ShoppingCart, Receipt, Package, ClipboardList, Truck, BarChart3, Building2,
  Settings as SettingsIcon,
  Store,
  LogOut
} from 'lucide-vue-next'

const auth = useAuthStore()
const route = useRoute()
const router = useRouter()
const sidebarOpen = ref(false)
const { businessName, logoUrl } = useBranding()
const isManager = computed(() => auth.hasRole('Admin', 'Owner', 'Dev'))

/** Tabbed pages link to their first tab, so keep the menu item lit on every tab. */
function isUnder(prefix: string) {
  return route.path === prefix || route.path.startsWith(`${prefix}/`)
}
const brandLogo = computed(() => logoUrl.value ?? logoLight)

const isStandalone = ref(
  window.matchMedia('(display-mode: standalone)').matches ||
  (navigator as any).standalone === true
)

const canGoBack = computed(() => window.history.length > 1)

/** Highest role held, which is the one that decides what the operator can do. */
const roleLabel = computed(() => {
  for (const r of ['Dev', 'Owner', 'Admin', 'Sales']) {
    if (auth.roles.includes(r)) return r
  }
  return null
})

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
        <img class="mc-sidebar__logo" :src="brandLogo" :alt="businessName" width="140" height="36" />
        <p class="mc-sidebar__tag">Point of sale</p>
      </div>
      <nav class="mc-sidebar__nav" aria-label="Main">
        <div class="mc-nav-group">
          <p class="mc-nav-group__label">Sell</p>
          <RouterLink class="mc-nav-link" to="/pos" @click="sidebarOpen = false"><ShoppingCart :size="16" />POS</RouterLink>
          <RouterLink class="mc-nav-link" :class="{ 'router-link-active': isUnder('/sales') }" to="/sales" @click="sidebarOpen = false"><Receipt :size="16" />Sales</RouterLink>
        </div>
        <div class="mc-nav-group">
          <p class="mc-nav-group__label">Stock</p>
          <RouterLink class="mc-nav-link" to="/stock" @click="sidebarOpen = false"><Package :size="16" />Products</RouterLink>
          <RouterLink v-if="isManager" class="mc-nav-link" :class="{ 'router-link-active': isUnder('/receiving') }" to="/receiving" @click="sidebarOpen = false"><Truck :size="16" />Receiving</RouterLink>
          <RouterLink class="mc-nav-link" to="/stocktake" @click="sidebarOpen = false"><ClipboardList :size="16" />Stocktake</RouterLink>
        </div>
        <div v-if="auth.hasVendorScope" class="mc-nav-group">
          <p class="mc-nav-group__label">Vendor</p>
          <RouterLink class="mc-nav-link" to="/vendor" @click="sidebarOpen = false"><Store :size="16" />My vendor report</RouterLink>
        </div>
        <div v-if="isManager" class="mc-nav-group">
          <p class="mc-nav-group__label">Manage</p>
          <RouterLink class="mc-nav-link" to="/suppliers" @click="sidebarOpen = false"><Building2 :size="16" />Suppliers</RouterLink>
          <RouterLink class="mc-nav-link" :class="{ 'router-link-active': isUnder('/reports') }" to="/reports" @click="sidebarOpen = false"><BarChart3 :size="16" />Reports</RouterLink>
        </div>
        <div v-if="isManager" class="mc-nav-group">
          <p class="mc-nav-group__label">Settings</p>
          <RouterLink class="mc-nav-link" :class="{ 'router-link-active': isUnder('/settings') }" to="/settings" @click="sidebarOpen = false"><SettingsIcon :size="16" />Settings</RouterLink>
        </div>
      </nav>
      <div class="mc-sidebar__foot">
        <!-- Till tablets are shared, so who is signed in matters before you log out. -->
        <p v-if="auth.email" class="mc-sidebar__who">
          <span class="mc-sidebar__who-email">{{ auth.email }}</span>
          <span v-if="roleLabel" class="mc-sidebar__who-role">{{ roleLabel }}</span>
        </p>
        <button type="button" class="mc-sidebar__logout" @click="logout"><LogOut :size="16" />Log out</button>
      </div>
    </aside>
    <div class="app-main">
      <header class="mc-topbar">
        <button type="button" class="mc-topbar__menu" aria-label="Open menu" @click="sidebarOpen = true"><Menu :size="20" /></button>
        <button
          type="button"
          class="mc-topbar__nav-btn"
          :class="{ 'mc-topbar__nav-btn--disabled': !canGoBack }"
          :disabled="!canGoBack"
          aria-label="Go back"
          @click="goBack"
        ><ChevronLeft :size="20" /></button>
        <button
          type="button"
          class="mc-topbar__nav-btn"
          aria-label="Go forward"
          @click="goForward"
        ><ChevronRight :size="20" /></button>
        <span class="brand-wordmark" style="font-size: 0.95rem; color: #2a2a2d">{{ businessName }}</span>
        <span v-if="roleLabel" class="mc-topbar__role">{{ roleLabel }}</span>
      </header>
      <div class="app-main__inner">
        <slot />
      </div>
    </div>
  </div>
</template>
