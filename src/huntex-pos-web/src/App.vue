<script setup lang="ts">
import { onMounted } from 'vue'
import { useRoute } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import { logoLight } from '@/branding'

const auth = useAuthStore()
const route = useRoute()

onMounted(() => {
  void auth.loadMe()
})
</script>

<template>
  <div class="page">
    <div class="brand-strip">
      <div class="brand-strip__left">
        <img class="brand-logo" :src="logoLight" alt="MC Tactical" />
        <span class="tagline">Point of sale</span>
      </div>
      <span class="tagline">mctactical.co.za</span>
    </div>
    <header v-if="auth.isAuthenticated && !route.path.startsWith('/invoice')" class="nav">
      <template v-if="auth.isAuthenticated">
        <RouterLink to="/pos">POS</RouterLink>
        <RouterLink to="/stock">Stock list</RouterLink>
        <RouterLink to="/stocktake">Stocktake</RouterLink>
        <RouterLink v-if="auth.hasRole('Admin', 'Owner', 'Dev')" to="/admin/team">Team</RouterLink>
        <RouterLink v-if="auth.hasRole('Admin', 'Owner', 'Dev')" to="/import">Import</RouterLink>
        <RouterLink v-if="auth.hasRole('Admin', 'Owner', 'Dev')" to="/reports">Reports</RouterLink>
        <RouterLink v-if="auth.hasRole('Admin', 'Owner', 'Dev')" to="/settings">Pricing</RouterLink>
        <RouterLink v-if="auth.hasRole('Admin', 'Owner', 'Dev')" to="/setup">Email setup</RouterLink>
        <button type="button" class="btn secondary" style="margin-left: auto" @click="auth.clear()">
          Log out
        </button>
      </template>
    </header>
    <RouterView />
  </div>
</template>
