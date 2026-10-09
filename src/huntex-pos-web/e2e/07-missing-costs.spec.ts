import { test, expect } from '@playwright/test'
import { Api, signIn, sql, sqlId, uniq, type Invoice, type MissingCostRow, type Product, type Salesperson } from './helpers'

let api: Api
let sp: Salesperson
let noCost: Product          // POS product with no cost, sold twice (qty 1 + 2)
let costed: Product          // has a cost → never listed
let partly: Product          // no cost, but one past line already carries a cost
let partlyCostedLine: string
let placeholder: Product     // stands in for the Shopify import's SHOPIFY-UNLINKED product
const torchKey = 'u:v:9001'
const knifeKey = 'u:v:9002'
const lineCosts = (productId: string) =>
  sql(`SELECT printf('%.2f', CostAtSale) FROM InvoiceLines WHERE ProductId = ${sqlId(productId)} ORDER BY printf('%.2f', CostAtSale);`)
    .split('\n').filter(Boolean)

const keyOf = (p: Product) => `p:${p.id}`
const find = (rows: MissingCostRow[], key: string) => rows.find((r) => r.key === key)

test.beforeAll(async () => {
  api = await Api.login()
  sp = await api.createSalesperson(uniq('Mc'))

  noCost = await api.createProduct(230, 0)
  await api.sale(noCost, 1, sp.id)
  await api.sale(noCost, 2, sp.id)

  costed = await api.createProduct(230, 100)
  await api.sale(costed, 1, sp.id)

  partly = await api.createProduct(230, 0)
  const first = await api.sale(partly, 1, sp.id)
  await api.sale(partly, 1, sp.id)
  partlyCostedLine = first.lines[0].id
  sql(`UPDATE InvoiceLines SET CostAtSale = '55.0' WHERE Id = ${sqlId(partlyCostedLine)};`)

  // Recreate what the Shopify import produces: lines on the placeholder product carrying the
  // online item's identity, plus a courier line. The API can't create these, so seed via SQL.
  placeholder = await api.ok<Product>(
    await api.ctx.post('/api/products', { data: { sku: 'SHOPIFY-UNLINKED', name: 'Shopify online item (not in POS)', cost: 0, sellPrice: 100, qtyOnHand: 1000 } }),
    'placeholder'
  )
  const torch1 = await api.sale(placeholder, 1, sp.id)
  const torch2 = await api.sale(placeholder, 1, sp.id)
  const knife = await api.sale(placeholder, 1, sp.id)
  const shipping = await api.sale(placeholder, 1, sp.id)
  const ids = (invs: Invoice[]) => invs.map((i) => sqlId(i.lines[0].id)).join(',')
  const invIds = [torch1, torch2, knife, shipping].map((i) => sqlId(i.id)).join(',')
  sql(`
    UPDATE InvoiceLines SET ShopifyVariantId = 9001, SkuAtSale = 'SHOP-TORCH', Description = 'Online torch' WHERE Id IN (${ids([torch1, torch2])});
    UPDATE InvoiceLines SET ShopifyVariantId = 9002, SkuAtSale = 'SHOP-KNIFE', Description = 'Online knife' WHERE Id IN (${ids([knife])});
    UPDATE InvoiceLines SET ShopifyVariantId = NULL, SkuAtSale = NULL, Description = 'Shipping – Courier Guy' WHERE Id IN (${ids([shipping])});
    UPDATE Invoices SET Source = 'Shopify' WHERE Id IN (${invIds});
    UPDATE Products SET Active = 0 WHERE Id = ${sqlId(placeholder.id)};
  `)
})

test.describe('missing costs — list', () => {
  test('lists only items sold at R0 cost, grouped like the Financial Report, biggest first', async () => {
    const rows = await api.missingCosts()

    expect(find(rows, keyOf(noCost))).toMatchObject({ isUnlinkedShopify: false, productId: noCost.id, sku: noCost.sku, qtySold: 3, saleCount: 2, revenue: 690 })
    expect(find(rows, keyOf(partly))).toMatchObject({ qtySold: 1, saleCount: 1, revenue: 230 })
    expect(find(rows, torchKey)).toMatchObject({ isUnlinkedShopify: true, productId: null, shopifyVariantId: 9001, sku: 'SHOP-TORCH', name: 'Online torch', qtySold: 2 })
    expect(find(rows, knifeKey)).toMatchObject({ isUnlinkedShopify: true, name: 'Online knife', qtySold: 1 })

    expect(find(rows, keyOf(costed))).toBeUndefined()
    expect(find(rows, keyOf(placeholder))).toBeUndefined()
    expect(rows.some((r) => /shipping/i.test(r.name))).toBe(false)

    for (let i = 1; i < rows.length; i++) expect(rows[i - 1].revenue).toBeGreaterThanOrEqual(rows[i].revenue)
  })

  test('period filter', async () => {
    const future = await api.missingCosts(new Date(Date.now() + 86_400_000), new Date(Date.now() + 2 * 86_400_000))
    expect(future).toEqual([])
    const today = await api.missingCosts(new Date(Date.now() - 3_600_000), new Date(Date.now() + 3_600_000))
    expect(find(today, keyOf(noCost))).toBeTruthy()
  })
})

test.describe('missing costs — fixing them', () => {
  test('UI: enter costs for a POS product and an online item; past sales are back-filled', async ({ page }) => {
    await signIn(page, api.token)
    await page.goto('/#/reports/costs')
    await expect(page.locator('.rep-tab--active')).toHaveText('Missing costs')

    const torchRow = page.locator('.mcost-row', { hasText: 'Online torch' })
    await expect(torchRow.getByText('Online item, not linked')).toBeVisible()
    await expect(torchRow.getByRole('link', { name: 'Link to product' })).toHaveAttribute('href', '#/settings/integrations')
    await expect(page.locator('.mcost-row', { hasText: noCost.sku }).getByText('Online item, not linked')).toHaveCount(0)

    const save = page.locator('.mcost-actions button')
    await expect(save).toBeDisabled()
    await expect(save).toHaveText('Enter a cost to save')

    await page.getByLabel(`Cost excluding VAT for ${noCost.name}`).fill('0')
    await expect(page.getByText('Costs must be more than R0.')).toBeVisible()
    await expect(save).toBeDisabled()

    await page.getByLabel(`Cost excluding VAT for ${noCost.name}`).fill('100')
    await page.getByLabel('Cost excluding VAT for Online torch').fill('150.5')
    await expect(save).toHaveText('Save 2 costs')
    await save.click()

    await expect(page.getByText('Saved 2 costs · 4 past sale lines updated')).toBeVisible()
    await expect(page.locator('.mcost-row', { hasText: noCost.sku })).toHaveCount(0)
    await expect(page.locator('.mcost-row', { hasText: 'Online torch' })).toHaveCount(0)
    await expect(page.locator('.mcost-row', { hasText: 'Online knife' })).toBeVisible()

    // Product: cost saved for future sales, selling price untouched
    const product = await api.ok<Product & { cost: number }>(await api.ctx.get(`/api/products/${noCost.id}`), 'product')
    expect(product.cost).toBe(100)
    expect(product.sellPrice).toBe(230)
    expect(lineCosts(noCost.id)).toEqual(['100.00', '100.00'])

    // Online item: only its own lines; the other online item and the courier line stay at R0
    expect(sql(`SELECT Description, printf('%.2f', CostAtSale) FROM InvoiceLines WHERE ProductId = ${sqlId(placeholder.id)} ORDER BY Description;`).split('\n'))
      .toEqual(['Online knife|0.00', 'Online torch|150.50', 'Online torch|150.50', 'Shipping – Courier Guy|0.00'])

    // Financial Report now uses the real cost (3 × R100 excl. → R345 incl. VAT)
    const stock = await api.ok<{ soldInPeriod: { sku: string; costExVat: number; costInclVat: number }[] }>(
      await api.ctx.get('/api/reports/stock'), 'stock report')
    expect(stock.soldInPeriod.find((r) => r.sku === noCost.sku)).toMatchObject({ costExVat: 300, costInclVat: 345 })

    // Future sales record the new cost
    await api.sale(noCost, 1, sp.id)
    expect(lineCosts(noCost.id)).toEqual(['100.00', '100.00', '100.00'])
  })

  test('lines that already had a cost are never overwritten', async () => {
    const res = await api.saveMissingCostsRaw([{ key: keyOf(partly), costExVat: 80 }])
    expect(res.status()).toBe(200)
    expect(await res.json()).toEqual({ productsUpdated: 1, linesUpdated: 1 })
    expect(lineCosts(partly.id)).toEqual(['55.00', '80.00'])
  })

  test('supplier discount is applied to back-filled lines the same way the till does', async () => {
    const p = await api.createProduct(230, 0)
    await api.ok(await api.ctx.put(`/api/products/${p.id}`, { data: { supplierDiscountPercent: 10, sellPrice: 230 } }), 'discount')
    await api.sale(p, 1, sp.id)
    await api.ok(await api.saveMissingCostsRaw([{ key: keyOf(p), costExVat: 200 }]), 'save')
    expect(lineCosts(p.id)).toEqual(['180.00'])
    const after = await api.ok<{ cost: number }>(await api.ctx.get(`/api/products/${p.id}`), 'product')
    expect(after.cost).toBe(200)
  })

  test('rejects bad input without changing anything', async () => {
    const before = lineCosts(placeholder.id)
    for (const items of [
      [],
      [{ key: knifeKey, costExVat: 0 }],
      [{ key: knifeKey, costExVat: -5 }],
      [{ key: 'nonsense', costExVat: 10 }],
      [{ key: 'p:00000000-0000-0000-0000-000000000042', costExVat: 10 }],
      [{ key: keyOf(placeholder), costExVat: 10 }],
      [{ key: knifeKey, costExVat: 10 }, { key: knifeKey, costExVat: 12 }]
    ]) {
      const res = await api.saveMissingCostsRaw(items)
      expect(res.status(), JSON.stringify(items)).toBe(400)
    }
    expect(lineCosts(placeholder.id)).toEqual(before)
  })

  test('Sales role cannot see or change costs', async () => {
    const till = await api.createSalesUser()
    expect((await till.ctx.get('/api/missing-costs')).status()).toBe(403)
    expect((await till.saveMissingCostsRaw([{ key: knifeKey, costExVat: 1 }])).status()).toBe(403)
  })
})

test.describe('missing costs — elsewhere in reports', () => {
  test('Financial Report warns and links to the fixer', async ({ page }) => {
    await signIn(page, api.token)
    await page.goto('/#/reports')
    const banner = page.locator('.fr-missing-costs')
    await expect(banner).toBeVisible()
    await expect(banner).toContainText('sold in this period')
    await expect(banner).toContainText('counted as 100% profit')
    await banner.getByRole('link', { name: 'Fix costs' }).click()
    await expect(page).toHaveURL(/#\/reports\/costs$/)
    await expect(page.locator('.rep-tab--active')).toHaveText('Missing costs')
    await expect(page.locator('.mcost-row', { hasText: 'Online knife' })).toBeVisible()
  })

  test('Daily gross profit falls back to the product cost like the other reports', async () => {
    const p = await api.createProduct(230, 0)
    await api.sale(p, 1, sp.id)
    const range = { from: new Date(Date.now() - 86_400_000).toISOString(), to: new Date(Date.now() + 86_400_000).toISOString() }
    const gp = async () => {
      const rows = await api.ok<{ grossProfit: number }[]>(await api.ctx.get('/api/reports/daily', { params: range }), 'daily')
      return rows.reduce((s, r) => s + r.grossProfit, 0)
    }
    const before = await gp()
    await api.ok(await api.ctx.put(`/api/products/${p.id}`, { data: { cost: 100, sellPrice: 230 } }), 'set cost')
    expect(Math.round((before - (await gp())) * 100) / 100).toBe(115)
  })
})
