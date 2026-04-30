import { createRouter, createWebHashHistory } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import { useBranding } from '@/composables/useBranding'

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
    { path: '/dashboard', component: () => import('@/views/ExecutiveDashboardView.vue'), meta: { layout: 'app' } },
    { path: '/price-lookup', component: () => import('@/views/PriceLookupView.vue'), meta: { layout: 'app' } },
    { path: '/stock', component: () => import('@/views/StockListView.vue'), meta: { layout: 'app' } },
    { path: '/stock/labels', component: () => import('@/views/LabelsPrintView.vue'), meta: { layout: 'app' } },
    { path: '/stocktake', component: () => import('@/views/StocktakeView.vue'), meta: { layout: 'app' } },
    { path: '/consignment', component: () => import('@/views/ConsignmentBatchView.vue'), meta: { layout: 'app' } },
    { path: '/receiving', redirect: (to) => ({ path: '/consignment', query: { type: (to.query.type as string) || 'OwnedReceive' } }) },
    { path: '/deliveries', component: () => import('@/views/DeliveriesView.vue'), meta: { layout: 'app' } },
    { path: '/wholesalers', component: () => import('@/views/WholesalersView.vue'), meta: { layout: 'app' } },
    { path: '/customers', component: () => import('@/views/CustomersListView.vue'), meta: { layout: 'app' } },
    { path: '/customers/:id', component: () => import('@/views/CustomerDetailView.vue'), meta: { layout: 'app' } },
    { path: '/import', component: () => import('@/views/ImportView.vue'), meta: { layout: 'app' } },
    { path: '/reports', component: () => import('@/views/ReportsView.vue'), meta: { layout: 'app' } },
    { path: '/financial-report', component: () => import('@/views/FinancialReportView.vue'), meta: { layout: 'app' } },
    { path: '/vendor-report', component: () => import('@/views/VendorReportView.vue'), meta: { layout: 'app' } },
    { path: '/settings', component: () => import('@/views/SettingsView.vue'), meta: { layout: 'app' } },
    { path: '/settings/business', component: () => import('@/views/BusinessSettingsView.vue'), meta: { layout: 'app' } },
    { path: '/settings/pricing-rules', redirect: '/settings' },
    { path: '/settings/email', redirect: '/setup' },
    { path: '/setup', component: () => import('@/views/SetupView.vue'), meta: { layout: 'app' } },
    { path: '/admin/team', component: () => import('@/views/AdminTeamView.vue'), meta: { layout: 'app' } },
    {
      path: '/setup-password',
      component: () => import('@/views/SetupPasswordView.vue'),
      meta: { public: true, layout: 'public' }
    },
    { path: '/quotes', component: () => import('@/views/QuotesListView.vue'), meta: { layout: 'app' } },
    { path: '/quotes/new', component: () => import('@/views/QuoteEditView.vue'), meta: { layout: 'app' } },
    { path: '/quotes/:id', component: () => import('@/views/QuoteDetailView.vue'), meta: { layout: 'app' } },
    { path: '/quotes/:id/edit', component: () => import('@/views/QuoteEditView.vue'), meta: { layout: 'app' } },
    {
      path: '/invoice/:token',
      component: () => import('@/views/InvoicePublicView.vue'),
      meta: { public: true, layout: 'public' }
    },
    {
      path: '/quote/:token',
      component: () => import('@/views/QuotePublicView.vue'),
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

  if (to.path.startsWith('/quotes')) {
    const { features } = useBranding()
    if (!features.value.quotes) return '/pos'
  }

  return true
})

export default router
