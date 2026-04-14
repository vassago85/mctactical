import { createRouter, createWebHashHistory } from 'vue-router'
import { useAuthStore } from '@/stores/auth'

const router = createRouter({
  history: createWebHashHistory(),
  routes: [
    { path: '/', redirect: '/pos' },
    {
      path: '/login',
      component: () => import('@/views/LoginView.vue'),
      meta: { public: true, layout: 'public' }
    },
    { path: '/pos', component: () => import('@/views/PosView.vue'), meta: { layout: 'app' } },
    { path: '/stock', component: () => import('@/views/StockListView.vue'), meta: { layout: 'app' } },
    { path: '/stocktake', component: () => import('@/views/StocktakeView.vue'), meta: { layout: 'app' } },
    { path: '/import', component: () => import('@/views/ImportView.vue'), meta: { layout: 'app' } },
    { path: '/reports', component: () => import('@/views/ReportsView.vue'), meta: { layout: 'app' } },
    { path: '/settings', component: () => import('@/views/SettingsView.vue'), meta: { layout: 'app' } },
    { path: '/setup', component: () => import('@/views/SetupView.vue'), meta: { layout: 'app' } },
    { path: '/admin/team', component: () => import('@/views/AdminTeamView.vue'), meta: { layout: 'app' } },
    {
      path: '/setup-password',
      component: () => import('@/views/SetupPasswordView.vue'),
      meta: { public: true, layout: 'public' }
    },
    {
      path: '/invoice/:token',
      component: () => import('@/views/InvoicePublicView.vue'),
      meta: { public: true, layout: 'public' }
    }
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
