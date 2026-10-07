import { test, expect } from '@playwright/test'
import { Api, addToCart, checkoutButton, salespersonButtons, signIn, uniq, type Product, type Salesperson } from './helpers'

let owner: Api
let till: Api
let product: Product
let sp: Salesperson

test.beforeAll(async () => {
  owner = await Api.login()
  till = await owner.createSalesUser()
  product = await owner.createProduct()
  sp = await owner.createSalesperson(uniq('Rol'), 5)
})

test.describe('Sales-role login (till staff)', () => {
  test('API: can list active salespeople but cannot manage them or see the report', async () => {
    const list = await till.ctx.get('/api/salespeople')
    expect(list.status()).toBe(200)
    const visible = (await list.json()) as Salesperson[]
    expect(visible.some((s) => s.id === sp.id)).toBe(true)
    // Till staff see names only — not each other's commission rates, and not people who left
    expect(visible.every((s) => s.commissionPercent === 0 && s.isActive)).toBe(true)
    const withInactive = (await (await till.ctx.get('/api/salespeople?includeInactive=true')).json()) as Salesperson[]
    expect(withInactive.every((s) => s.isActive)).toBe(true)
    expect((await owner.listSalespeople(true)).some((s) => !s.isActive)).toBe(true)

    expect((await till.ctx.post('/api/salespeople', { data: { name: uniq('Hack'), commissionPercent: 50 } })).status()).toBe(403)
    expect((await till.ctx.put(`/api/salespeople/${sp.id}`, { data: { name: sp.name, commissionPercent: 99 } })).status()).toBe(403)
    expect((await till.ctx.get('/api/reports/salespeople')).status()).toBe(403)

    // Commission was not touched
    const fresh = (await owner.listSalespeople()).find((s) => s.id === sp.id)
    expect(fresh?.commissionPercent).toBe(5)
  })

  test('API: can load the active promotion for the till, but cannot manage promotions', async () => {
    expect((await till.ctx.get('/api/promotions/active')).status()).toBe(200)
    expect((await till.ctx.get('/api/promotions')).status()).toBe(403)
    expect((await till.ctx.post('/api/promotions', { data: { name: 'Hack', discountPercent: 90 } })).status()).toBe(403)
    expect((await till.ctx.post('/api/promotions/specials', { data: {} })).status()).toBe(403)
    expect((await owner.ctx.get('/api/promotions')).status()).toBe(200)
  })

  test('UI: no Salespeople nav link, page redirects to the till, report tab unreachable', async ({ page }) => {
    await signIn(page, till.token)
    await page.goto('/#/pos')
    await expect(page.getByRole('searchbox', { name: /Scan barcode, or search products/ })).toBeVisible()
    await expect(page.getByRole('link', { name: 'Salespeople' })).toHaveCount(0)

    await page.goto('/#/salespeople')
    await expect(page).toHaveURL(/#\/pos$/)

    await page.goto('/#/reports')
    await expect(page).toHaveURL(/#\/pos$/)
  })

  test('UI: Sales user picks a salesperson and completes a sale credited to them', async ({ page }) => {
    await signIn(page, till.token)
    await page.goto('/#/pos')
    await addToCart(page, product.sku)
    await expect(checkoutButton(page)).toBeDisabled()
    await salespersonButtons(page).filter({ hasText: sp.name }).click()
    await checkoutButton(page).click()
    await expect(page.locator('.sale-summary__served')).toHaveText(`Served by ${sp.name}`)

    const latest = await owner.latestInvoice()
    expect((await owner.invoice(latest.id)).salespersonId).toBe(sp.id)
  })
})
