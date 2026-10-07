import { createRouter, createWebHashHistory } from 'vue-router'
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

const LEGACY_REPORT_TABS = ['financial', 'stock', 'consignment', 'sales', 'salespeople', 'shopify', 'missing-costs']

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
    { path: '/sales', redirect: '/sales/invoices' },
    { path: '/sales/invoices', component: SalesView, meta: { layout: 'app' } },
    { path: '/sales/quotes', component: SalesView, meta: { layout: 'app', feature: 'quotes' } },
    { path: '/sales/deliveries', component: SalesView, meta: { layout: 'app', roles: MANAGER_ROLES } },
    { path: '/quotes/new', component: () => import('@/views/QuoteEditView.vue'), meta: { layout: 'app', feature: 'quotes' } },
    { path: '/quotes/:id', component: () => import('@/views/QuoteDetailView.vue'), meta: { layout: 'app', feature: 'quotes' } },
    { path: '/quotes/:id/edit', component: () => import('@/views/QuoteEditView.vue'), meta: { layout: 'app', feature: 'quotes' } },

    // Stock
    { path: '/stock', component: () => import('@/views/StockListView.vue'), meta: { layout: 'app' } },
    { path: '/receiving', redirect: (to) => ({ path: '/receiving/batches', query: to.query }) },
    { path: '/receiving/batches', component: ReceivingView, meta: { layout: 'app', roles: MANAGER_ROLES } },
    { path: '/receiving/import', component: ReceivingView, meta: { layout: 'app', roles: MANAGER_ROLES } },
    { path: '/stocktake', component: () => import('@/views/StocktakeView.vue'), meta: { layout: 'app' } },

    // Manage
    { path: '/suppliers', component: () => import('@/views/WholesalersView.vue'), meta: { layout: 'app', roles: MANAGER_ROLES } },
    {
      path: '/reports',
      redirect: (to) => {
        const tab = typeof to.query.tab === 'string' && LEGACY_REPORT_TABS.includes(to.query.tab) ? to.query.tab : 'financial'
        return { path: `/reports/${tab}`, query: {} }
      }
    },
    { path: '/reports/financial', component: ReportsView, meta: { layout: 'app', roles: MANAGER_ROLES } },
    { path: '/reports/stock', component: ReportsView, meta: { layout: 'app', roles: MANAGER_ROLES } },
    { path: '/reports/consignment', component: ReportsView, meta: { layout: 'app', roles: MANAGER_ROLES } },
    { path: '/reports/sales', component: ReportsView, meta: { layout: 'app', roles: MANAGER_ROLES } },
    { path: '/reports/salespeople', component: ReportsView, meta: { layout: 'app', roles: MANAGER_ROLES } },
    { path: '/reports/shopify', component: ReportsView, meta: { layout: 'app', roles: SHOPIFY_ROLES } },
    { path: '/reports/missing-costs', component: ReportsView, meta: { layout: 'app', roles: MANAGER_ROLES } },
    { path: '/vendor-report', component: () => import('@/views/VendorReportView.vue'), meta: { layout: 'app', vendorScope: true } },

    // Settings
    { path: '/settings', redirect: '/settings/business' },
    { path: '/settings/business', component: SettingsShellView, meta: { layout: 'app', roles: MANAGER_ROLES } },
    { path: '/settings/pricing', component: SettingsShellView, meta: { layout: 'app', roles: MANAGER_ROLES } },
    { path: '/settings/team', component: SettingsShellView, meta: { layout: 'app', roles: MANAGER_ROLES } },
    { path: '/settings/integrations', component: SettingsShellView, meta: { layout: 'app', roles: MANAGER_ROLES } },

    // Old paths, kept so bookmarks, PWA caches and printed links still land somewhere sensible.
    { path: '/price-lookup', redirect: { path: '/pos', query: { check: '1' } } },
    { path: '/find-sale', redirect: (to) => ({ path: '/sales/invoices', query: to.query }) },
    { path: '/sales-history', redirect: (to) => ({ path: '/sales/invoices', query: to.query }) },
    { path: '/quotes', redirect: '/sales/quotes' },
    { path: '/deliveries', redirect: '/sales/deliveries' },
    { path: '/stock/labels', redirect: '/stock' },
    { path: '/consignment', redirect: (to) => ({ path: '/receiving/batches', query: to.query }) },
    { path: '/import', redirect: '/receiving/import' },
    { path: '/wholesalers', redirect: '/suppliers' },
    { path: '/salespeople', redirect: '/settings/team' },
    { path: '/financial-report', redirect: '/reports/financial' },
    { path: '/settings/shopify', redirect: '/settings/integrations' },
    { path: '/settings/email', redirect: '/settings/integrations' },
    { path: '/settings/pricing-rules', redirect: '/settings/pricing' },
    { path: '/setup', redirect: '/settings/integrations' },
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
