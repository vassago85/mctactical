import { test, expect, type Page } from '@playwright/test'
import { Api, signIn, uniq, type Product, type Salesperson } from './helpers'

let owner: Api
let admin: Api
let till: Api
let product: Product
let seller: Salesperson

test.beforeAll(async () => {
  owner = await Api.login()
  admin = await owner.createUser('Admin')
  till = await owner.createSalesUser()
  product = await owner.createProduct(230, 100)
  seller = await owner.createSalesperson(uniq('Nav'))
})

const navLinks = (page: Page) => page.locator('.mc-sidebar__nav .mc-nav-link')

/** Fails the test on uncaught page errors (a broken embedded view shows up here first). */
function trackPageErrors(page: Page): string[] {
  const errors: string[] = []
  page.on('pageerror', (e) => errors.push(e.message))
  return errors
}

/** Clicks every tab on the current page and checks the URL and active state follow. */
async function clickThroughTabs(page: Page, expected: string[]) {
  const tabs = page.locator('.rep-tab')
  await expect(tabs).toHaveText(expected)
  for (const label of expected) {
    const tab = tabs.filter({ hasText: new RegExp(`^${label}$`) })
    const href = await tab.getAttribute('href')
    await tab.click()
    await expect(page).toHaveURL(new RegExp(`${href!.replace(/[/#]/g, (c) => `\\${c}`)}$`))
    await expect(page.locator('.rep-tab--active')).toHaveText(label)
    await expect(page.getByText('You do not have access to that page.')).toHaveCount(0)
  }
}

test.describe('sidebar', () => {
  test('Owner sees the 8 consolidated items under Sell, Stock, Manage and Settings', async ({ page }) => {
    await signIn(page, owner.token)
    await page.goto('/#/pos')
    await expect(page.locator('.mc-sidebar__nav .mc-nav-group__label')).toHaveText(['Sell', 'Stock', 'Manage', 'Settings'])
    await expect(navLinks(page)).toHaveText(['POS', 'Sales', 'Products', 'Receiving', 'Stocktake', 'Suppliers', 'Reports', 'Settings'])
  })

  test('Sales sees POS, Sales, Products and Stocktake only', async ({ page }) => {
    await signIn(page, till.token)
    await page.goto('/#/pos')
    await expect(page.locator('.mc-sidebar__nav .mc-nav-group__label')).toHaveText(['Sell', 'Stock'])
    await expect(navLinks(page)).toHaveText(['POS', 'Sales', 'Products', 'Stocktake'])
  })

  test('menu item stays highlighted on every tab of its page', async ({ page }) => {
    await signIn(page, owner.token)
    await page.goto('/#/settings/integrations')
    await expect(page.locator('.mc-nav-link.router-link-active')).toHaveText('Settings')
    await page.goto('/#/reports/stock')
    await expect(page.locator('.mc-nav-link.router-link-active')).toHaveText('Reports')
  })
})

test.describe('Owner click-through', () => {
  test('every menu item and tab opens without errors', async ({ page }) => {
    const errors = trackPageErrors(page)
    await signIn(page, owner.token)
    await page.goto('/#/pos')

    await navLinks(page).filter({ hasText: 'Sales' }).click()
    await expect(page).toHaveURL(/#\/sales\/invoices$/)
    await clickThroughTabs(page, ['Invoices', 'Quotes', 'Deliveries'])

    await navLinks(page).filter({ hasText: 'Products' }).click()
    await expect(page.getByRole('heading', { name: 'Products', level: 1 })).toBeVisible()

    await navLinks(page).filter({ hasText: 'Receiving' }).click()
    await expect(page).toHaveURL(/#\/receiving\/batches$/)
    await clickThroughTabs(page, ['Batches', 'Import'])
    await expect(page.getByText('Huntex workbook or CSV', { exact: true })).toBeVisible()

    await navLinks(page).filter({ hasText: 'Stocktake' }).click()
    await expect(page).toHaveURL(/#\/stocktake$/)

    await navLinks(page).filter({ hasText: 'Suppliers' }).click()
    await expect(page).toHaveURL(/#\/suppliers$/)
    await expect(page.getByRole('heading', { name: 'Suppliers', level: 1 })).toBeVisible()

    await navLinks(page).filter({ hasText: 'Reports' }).click()
    await expect(page).toHaveURL(/#\/reports\/financial$/)
    await expect(page.getByText('Financial Overview', { exact: true })).toBeVisible()
    await clickThroughTabs(page, ['Financial overview', 'Stock', 'Consignment', 'Sales', 'Salespeople', 'Shopify sales', 'Missing costs'])
    await page.locator('.rep-tab', { hasText: /^Shopify sales$/ }).click()
    await expect(page.getByText('Shopify revenue (incl VAT)')).toBeVisible()

    await navLinks(page).filter({ hasText: 'Settings' }).click()
    await expect(page).toHaveURL(/#\/settings\/business$/)
    await clickThroughTabs(page, ['Business', 'Pricing', 'Team & salespeople', 'Integrations'])
    await expect(page.getByRole('heading', { name: 'Shopify', level: 2, exact: true })).toBeVisible()
    await expect(page.getByRole('heading', { name: 'Email (Mailgun)', level: 2, exact: true })).toBeVisible()
    await page.locator('.rep-tab', { hasText: /^Team & salespeople$/ }).click()
    await expect(page.getByRole('heading', { name: 'Team & sales logins', level: 2, exact: true })).toBeVisible()
    await expect(page.getByRole('heading', { name: 'Salespeople', level: 2, exact: true })).toBeVisible()

    expect(errors).toEqual([])
  })

  test('Find sale lists recent invoices and no longer has its own Shopify sync button', async ({ page }) => {
    const sale = await owner.sale(product, 1, seller.id)
    await signIn(page, owner.token)
    await page.goto('/#/sales/invoices')
    await expect(page.locator('.hist-recent__row', { hasText: sale.invoiceNumber })).toBeVisible()
    await expect(page.getByRole('button', { name: 'Sync Shopify sales' })).toHaveCount(0)
  })
})

test.describe('Admin', () => {
  test('sees every page but not the Owner-only Shopify parts', async ({ page }) => {
    await signIn(page, admin.token)
    await page.goto('/#/reports/financial')
    await expect(page.locator('.rep-tab')).toHaveText(['Financial overview', 'Stock', 'Consignment', 'Sales', 'Salespeople', 'Missing costs'])
    await page.goto('/#/settings/integrations')
    await expect(page.getByRole('heading', { name: 'Email (Mailgun)', level: 2, exact: true })).toBeVisible()
    await expect(page.getByRole('heading', { name: 'Shopify', level: 2, exact: true })).toHaveCount(0)
    await page.goto('/#/reports/shopify')
    await expect(page).toHaveURL(/#\/pos$/)
  })
})

test.describe('Sales role', () => {
  test('pages and tabs match what Sales could reach before', async ({ page }) => {
    const errors = trackPageErrors(page)
    await signIn(page, till.token)
    await page.goto('/#/sales/invoices')
    await clickThroughTabs(page, ['Invoices', 'Quotes'])

    await page.goto('/#/stock')
    await expect(page.getByRole('heading', { name: 'Products', level: 1 })).toBeVisible()
    const row = page.locator('.stock-table tbody tr', { hasText: product.sku })
    await page.getByRole('searchbox', { name: 'Search inventory' }).fill(product.sku)
    await expect(row).toBeVisible()
    // Cost hidden (default setting), no label printing or row selection for Sales
    await expect(row.locator('td').nth(2)).toHaveText('—')
    await expect(row.getByRole('checkbox')).toHaveCount(0)
    await expect(row.getByRole('button', { name: 'Label' })).toHaveCount(0)

    await page.goto('/#/stocktake')
    await expect(page).toHaveURL(/#\/stocktake$/)
    expect(errors).toEqual([])
  })

  for (const path of [
    '/sales/deliveries', '/receiving/batches', '/receiving/import', '/suppliers',
    '/reports/financial', '/reports/salespeople', '/settings/business', '/settings/team', '/settings/integrations',
    '/consignment', '/import', '/wholesalers', '/admin/team', '/setup', '/financial-report'
  ]) {
    test(`cannot open ${path}`, async ({ page }) => {
      await signIn(page, till.token)
      await page.goto(`/#${path}`)
      await expect(page).toHaveURL(/#\/pos$/)
    })
  }
})

test.describe('old routes redirect', () => {
  const redirects: [string, RegExp][] = [
    ['/price-lookup', /#\/pos\?check=1$/],
    ['/find-sale', /#\/sales\/invoices$/],
    ['/find-sale?q=ABC', /#\/sales\/invoices\?q=ABC$/],
    ['/sales-history', /#\/sales\/invoices$/],
    ['/sales', /#\/sales\/invoices$/],
    ['/quotes', /#\/sales\/quotes$/],
    ['/deliveries', /#\/sales\/deliveries$/],
    ['/stock/labels', /#\/stock$/],
    ['/consignment', /#\/receiving\/batches$/],
    ['/receiving', /#\/receiving\/batches$/],
    ['/import', /#\/receiving\/import$/],
    ['/wholesalers', /#\/suppliers$/],
    ['/salespeople', /#\/settings\/team$/],
    ['/financial-report', /#\/reports\/financial$/],
    ['/reports', /#\/reports\/financial$/],
    ['/reports?tab=missing-costs', /#\/reports\/missing-costs$/],
    ['/settings', /#\/settings\/business$/],
    ['/settings/business', /#\/settings\/business$/],
    ['/settings/shopify', /#\/settings\/integrations$/],
    ['/settings/email', /#\/settings\/integrations$/],
    ['/settings/pricing-rules', /#\/settings\/pricing$/],
    ['/setup', /#\/settings\/integrations$/],
    ['/admin/team', /#\/settings\/team$/]
  ]
  for (const [from, to] of redirects) {
    test(`${from}`, async ({ page }) => {
      await signIn(page, owner.token)
      await page.goto(`/#${from}`)
      await expect(page).toHaveURL(to)
    })
  }
})

test.describe('POS price check', () => {
  test('shows price and stock without adding to the sale', async ({ page }) => {
    await signIn(page, owner.token)
    await page.goto('/#/pos')
    await page.getByRole('button', { name: 'Check price' }).click()
    const pop = page.getByRole('dialog', { name: 'Check price' })
    await pop.locator('#price-check-search').fill(product.sku)
    const item = pop.locator('.pc-item', { hasText: product.sku })
    await expect(item).toContainText('R230.00')
    await expect(item).toContainText('In stock')
    await expect(item).toContainText('Cost R100.00')
    await item.click()
    await expect(page.locator('.pos-cart-table tbody tr', { hasText: product.sku })).toHaveCount(0)
    await page.keyboard.press('Escape')
    await expect(pop).toHaveCount(0)
  })

  test('old Price lookup link opens it; Sales users do not see cost', async ({ page }) => {
    await signIn(page, till.token)
    await page.goto('/#/price-lookup')
    const pop = page.getByRole('dialog', { name: 'Check price' })
    await expect(pop).toBeVisible()
    await pop.locator('#price-check-search').fill(product.sku)
    const item = pop.locator('.pc-item', { hasText: product.sku })
    await expect(item).toContainText('R230.00')
    await expect(item).not.toContainText('Cost')
  })
})

test.describe('Products bulk labels', () => {
  test('select rows and print labels from the selection bar', async ({ page }) => {
    const second = await owner.createProduct(115, 50)
    await signIn(page, owner.token)
    await page.goto('/#/stock')
    await page.getByRole('searchbox', { name: 'Search inventory' }).fill('Test item E2E-')
    await page.getByRole('checkbox', { name: `Select Test item ${product.sku}` }).check()
    await page.getByRole('checkbox', { name: `Select Test item ${second.sku}` }).check()
    const bar = page.locator('.stock-select-bar')
    await expect(bar).toContainText('2 selected')

    await bar.getByRole('button', { name: 'Print labels' }).click()
    const dialog = page.getByRole('dialog')
    await expect(dialog).toContainText('2 products selected')
    await expect(dialog).toContainText('Brother QL-800')
    await dialog.getByText('Fixed number per product').click()
    await dialog.getByRole('spinbutton', { name: 'Labels per product' }).fill('3')
    await expect(dialog).toContainText('about 6 labels')

    const labels = page.waitForResponse((r) => r.url().endsWith('/api/products/labels') && r.request().method() === 'POST')
    page.context().on('page', (p) => void p.close())
    await dialog.getByRole('button', { name: 'Print labels' }).click()
    const res = await labels
    expect(res.status()).toBe(200)
    expect(res.headers()['content-type']).toContain('application/pdf')
    expect(res.headers()['x-label-count']).toBe('6')
  })
})

test.describe('Purge removed', () => {
  test('API endpoint is gone and the Reports page has no purge button', async ({ page }) => {
    const res = await owner.ctx.post('/api/reports/purge')
    expect([404, 405]).toContain(res.status())

    await signIn(page, owner.token)
    await page.goto('/#/reports/stock')
    await expect(page.getByRole('button', { name: /purge/i })).toHaveCount(0)
  })
})
