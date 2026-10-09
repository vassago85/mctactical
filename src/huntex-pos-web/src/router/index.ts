import { createRouter, createWebHashHistory, type RouteLocation } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import { useBranding } from '@/composables/useBranding'
import { useToast } from '@/composables/useToast'

/** Manage / Settings areas. The API enforces these too — this keeps the UI honest. */
const MANAGER_ROLES = ['Admin', 'Owner', 'Dev']
const SHOPIFY_ROLES = ['Owner', 'Dev']

const SalesView = () => import('@/views/SalesView.vue')
const ReceivingView = () => import('@/views/ReceivingView.vue')
const ReportsView = () => import('@/views/ReportsView.vue')
const SettingsShellView = () => import('@/views/SettingsShellView.vue')

/** Old /reports?tab=… links, mapped to the path each tab lives at now. */
const LEGACY_REPORT_TABS: Record<string, string> = {
  financial: '/reports',
  stock: '/reports/stock',
  consignment: '/reports/consignment',
  sales: '/reports/sales',
  salespeople: '/reports/salespeople',
  shopify: '/reports/shopify',
  'missing-costs': '/reports/costs'
}

const keepQuery = (path: string) => (to: RouteLocation) => ({ path, query: to.query })

const router = createRouter({
  history: createWebHashHistory(),
  routes: [
    { path: '/', redirect: '/pos' },
    {
      path: '/login',
      component: () => import('@/views/LoginView.vue'),
      meta: { public: true, layout: 'public' }
    },

    // Sell
    { path: '/pos', component: () => import('@/views/PosView.vue'), meta: { layout: 'app' } },
    { path: '/sales', component: SalesView, meta: { layout: 'app' } },
    { path: '/sales/quotes', component: SalesView, meta: { layout: 'app', feature: 'quotes' } },
    { path: '/sales/deliveries', component: SalesView, meta: { layout: 'app', roles: MANAGER_ROLES } },
    { path: '/quotes/new', component: () => import('@/views/QuoteEditView.vue'), meta: { layout: 'app', feature: 'quotes' } },
    { path: '/quotes/:id', component: () => import('@/views/QuoteDetailView.vue'), meta: { layout: 'app', feature: 'quotes' } },
    { path: '/quotes/:id/edit', component: () => import('@/views/QuoteEditView.vue'), meta: { layout: 'app', feature: 'quotes' } },

    // Stock
    { path: '/stock', component: () => import('@/views/StockListView.vue'), meta: { layout: 'app' } },
    { path: '/receiving/:tab(import)?', component: ReceivingView, meta: { layout: 'app', roles: MANAGER_ROLES } },
    { path: '/stocktake', component: () => import('@/views/StocktakeView.vue'), meta: { layout: 'app' } },

    // Manage
    { path: '/suppliers', component: () => import('@/views/WholesalersView.vue'), meta: { layout: 'app', roles: MANAGER_ROLES } },
    {
      path: '/reports/:tab(stock|consignment|sales|salespeople|costs)?',
      component: ReportsView,
      meta: { layout: 'app', roles: MANAGER_ROLES },
      beforeEnter: (to) => {
        const legacy = typeof to.query.tab === 'string' ? LEGACY_REPORT_TABS[to.query.tab] : undefined
        return legacy ? { path: legacy } : true
      }
    },
    { path: '/reports/shopify', component: ReportsView, meta: { layout: 'app', roles: SHOPIFY_ROLES } },
    { path: '/vendor', component: () => import('@/views/VendorReportView.vue'), meta: { layout: 'app', vendorScope: true } },

    // Settings
    { path: '/settings/:tab(pricing|team|integrations)?', component: SettingsShellView, meta: { layout: 'app', roles: MANAGER_ROLES } },

    // Old paths, kept so bookmarks, PWA caches and printed links still land somewhere sensible.
    { path: '/price-lookup', redirect: { path: '/pos', query: { check: '1' } } },
    { path: '/sales/invoices', redirect: keepQuery('/sales') },
    { path: '/find-sale', redirect: keepQuery('/sales') },
    { path: '/sales-history', redirect: keepQuery('/sales') },
    { path: '/quotes', redirect: '/sales/quotes' },
    { path: '/deliveries', redirect: '/sales/deliveries' },
    { path: '/stock/labels', redirect: '/stock' },
    { path: '/receiving/batches', redirect: keepQuery('/receiving') },
    { path: '/consignment', redirect: keepQuery('/receiving') },
    { path: '/import', redirect: '/receiving/import' },
    { path: '/wholesalers', redirect: '/suppliers' },
    { path: '/reports/financial', redirect: '/reports' },
    { path: '/financial-report', redirect: '/reports' },
    { path: '/reports/missing-costs', redirect: '/reports/costs' },
    { path: '/vendor-report', redirect: '/vendor' },
    { path: '/settings/business', redirect: '/settings' },
    { path: '/settings/shopify', redirect: '/settings/integrations' },
    { path: '/settings/email', redirect: '/settings/integrations' },
    { path: '/settings/pricing-rules', redirect: '/settings/pricing' },
    { path: '/setup', redirect: '/settings/integrations' },
    { path: '/salespeople', redirect: '/settings/team' },
    { path: '/admin/team', redirect: '/settings/team' },

    {
      path: '/setup-password',
      component: () => import('@/views/SetupPasswordView.vue'),
      meta: { public: true, layout: 'public' }
    },
    {
      path: '/invoice/:token',
      component: () => import('@/views/InvoicePublicView.vue'),
      meta: { public: true, layout: 'public' }
    },
    {
      path: '/receipt/:token',
      component: () => import('@/views/ReceiptPrintView.vue'),
      meta: { public: true, layout: 'public' }
    },
    {
      path: '/return/:token',
      component: () => import('@/views/ReturnSlipPrintView.vue'),
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

  if (to.meta.feature === 'quotes') {
    const { features } = useBranding()
    if (!features.value.quotes) return '/pos'
  }

  // Without this a Sales bookmark to an admin page loads the view and then fails
  // on every API call, which reads as a broken app rather than "no access".
  const needed = to.meta.roles as string[] | undefined
  if (needed?.length && !auth.hasRole(...needed)) {
    useToast().error('You do not have access to that page.')
    return '/pos'
  }
  if (to.meta.vendorScope && !auth.hasVendorScope) {
    useToast().error('That page is only for vendor accounts.')
    return '/pos'
  }

  return true
})

export default router
