import { createRouter, createWebHashHistory } from 'vue-router'
import { useAuthStore } from '@/stores/auth'

const router = createRouter({
  history: createWebHashHistory(),
  routes: [
    { path: '/', redirect: '/pos' },
    { path: '/login', component: () => import('@/views/LoginView.vue'), meta: { public: true } },
    { path: '/pos', component: () => import('@/views/PosView.vue') },
    { path: '/stock', component: () => import('@/views/StockListView.vue') },
    { path: '/stocktake', component: () => import('@/views/StocktakeView.vue') },
    { path: '/import', component: () => import('@/views/ImportView.vue') },
    { path: '/reports', component: () => import('@/views/ReportsView.vue') },
    { path: '/settings', component: () => import('@/views/SettingsView.vue') },
    { path: '/setup', component: () => import('@/views/SetupView.vue') },
    { path: '/admin/team', component: () => import('@/views/AdminTeamView.vue') },
    { path: '/invoice/:token', component: () => import('@/views/InvoicePublicView.vue'), meta: { public: true } }
  ]
})

router.beforeEach(async (to) => {
  const auth = useAuthStore()
  if (auth.isAuthenticated && to.path === '/login') return '/pos'
  if (to.meta.public) return true
  if (!auth.isAuthenticated) {
    return { path: '/login', query: { redirect: to.fullPath } }
  }
  if (!auth.roles.length) await auth.loadMe()
  return true
})

export default router
