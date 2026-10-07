import { test, expect } from '@playwright/test'
import { Api, addToCart, checkoutButton, salespersonButtons, signIn, uniq, type Product, type Salesperson } from './helpers'

let api: Api
let product: Product
let first: Salesperson
let second: Salesperson

test.beforeAll(async () => {
  api = await Api.login()
  product = await api.createProduct()
  first = await api.createSalesperson(uniq('Ann'), 5)
  second = await api.createSalesperson(uniq('Ben'), 0)
})

test.describe('checkout salesperson picker', () => {
  test('blocks the sale until a salesperson is picked, then credits them everywhere', async ({ page }) => {
    await signIn(page, api.token)
    await page.goto('/#/pos')
    await addToCart(page, product.sku)

    await expect(salespersonButtons(page).filter({ hasText: first.name })).toBeVisible()
    await expect(salespersonButtons(page).filter({ hasText: second.name })).toBeVisible()
    await expect(page.locator('.pos-sp-required')).toBeVisible()
    await expect(checkoutButton(page)).toBeDisabled()
    await expect(checkoutButton(page)).toHaveText('Choose salesperson')

    await salespersonButtons(page).filter({ hasText: first.name }).click()
    await expect(salespersonButtons(page).filter({ hasText: first.name })).toHaveAttribute('aria-pressed', 'true')
    await expect(page.locator('.pos-sp-required')).toHaveCount(0)
    await expect(checkoutButton(page)).toBeEnabled()
    await expect(checkoutButton(page)).toHaveText(`Complete sale · ${first.name}`)

    await checkoutButton(page).click()
    await expect(page.locator('.sale-summary__served')).toHaveText(`Served by ${first.name}`)

    const latest = await api.latestInvoice()
    const inv = await api.invoice(latest.id)
    expect(inv.salespersonId).toBe(first.id)
    expect(inv.salespersonName).toBe(first.name)

    // Thermal receipt
    await page.goto(`/#/receipt/${latest.publicToken}`)
    await expect(page.locator('.rcpt__pay', { hasText: 'Served by' })).toContainText(first.name)

    // Customer-facing invoice page
    await page.goto(`/#/invoice/${latest.publicToken}`)
    const served = page.locator('dt', { hasText: 'Served by' })
    await expect(served).toBeVisible()
    await expect(served.locator('xpath=following-sibling::dd[1]')).toHaveText(first.name)

    // PDF still renders with the extra meta cell
    const pdf = await api.ctx.get(`/api/invoices/${latest.id}/pdf`)
    expect(pdf.status()).toBe(200)
    expect((await pdf.body()).subarray(0, 4).toString()).toBe('%PDF')
  })

  test('remembers the pick across sales and page reloads, and can be switched', async ({ page }) => {
    await signIn(page, api.token)
    await page.goto('/#/pos')
    await addToCart(page, product.sku)
    await salespersonButtons(page).filter({ hasText: second.name }).click()
    await checkoutButton(page).click()
    await expect(page.locator('.sale-summary__served')).toHaveText(`Served by ${second.name}`)
    await page.getByRole('button', { name: 'Done' }).click()

    // Next customer: still selected without touching anything
    await addToCart(page, product.sku)
    await expect(salespersonButtons(page).filter({ hasText: second.name })).toHaveAttribute('aria-pressed', 'true')
    await expect(checkoutButton(page)).toHaveText(`Complete sale · ${second.name}`)

    // Survives a reload (till refreshed / PWA reopened)
    await page.reload()
    await addToCart(page, product.sku)
    await expect(salespersonButtons(page).filter({ hasText: second.name })).toHaveAttribute('aria-pressed', 'true')

    // Shift change
    await salespersonButtons(page).filter({ hasText: first.name }).click()
    await expect(salespersonButtons(page).filter({ hasText: second.name })).toHaveAttribute('aria-pressed', 'false')
    await checkoutButton(page).click()
    await expect(page.locator('.sale-summary__served')).toHaveText(`Served by ${first.name}`)
  })

  test('a remembered salesperson who was deactivated is cleared and the sale is blocked again', async ({ page }) => {
    const leaver = await api.createSalesperson(uniq('Lea'))
    await signIn(page, api.token)
    await page.goto('/#/pos')
    await addToCart(page, product.sku)
    await salespersonButtons(page).filter({ hasText: leaver.name }).click()
    await expect(checkoutButton(page)).toBeEnabled()

    await api.setActive(leaver, false)
    await page.reload()
    await addToCart(page, product.sku)

    await expect(salespersonButtons(page).filter({ hasText: leaver.name })).toHaveCount(0)
    await expect(checkoutButton(page)).toBeDisabled()
    await expect(checkoutButton(page)).toHaveText('Choose salesperson')
    expect(await page.evaluate(() => localStorage.getItem('pos.salespersonId'))).toBeNull()
  })

  test('a salesperson deactivated mid-shift (page not reloaded) gets a clear error, not a silent sale', async ({ page }) => {
    const leaver = await api.createSalesperson(uniq('Mid'))
    await signIn(page, api.token)
    await page.goto('/#/pos')
    await addToCart(page, product.sku)
    await salespersonButtons(page).filter({ hasText: leaver.name }).click()

    await api.setActive(leaver, false)
    await checkoutButton(page).click()

    await expect(page.getByText('The selected salesperson is no longer available. Pick another.').first()).toBeVisible()
    await expect(page.locator('.sale-summary')).toHaveCount(0)
    await expect(page.locator('.pos-cart-table tbody tr')).toHaveCount(1)
  })

  test('mobile layout: bottom tender bar is also blocked until a salesperson is picked', async ({ page }) => {
    await page.setViewportSize({ width: 390, height: 844 })
    await signIn(page, api.token)
    await page.goto('/#/pos')
    await addToCart(page, product.sku)

    const bar = page.locator('.pos-tender-bar__btn')
    await expect(bar).toBeVisible()
    await expect(bar).toBeDisabled()
    await expect(bar).toHaveText('Choose salesperson')

    await salespersonButtons(page).filter({ hasText: first.name }).click()
    await expect(bar).toBeEnabled()
    await expect(bar).toHaveText('Card · Complete sale')
    await bar.click()
    await expect(page.locator('.sale-summary__served')).toHaveText(`Served by ${first.name}`)
  })
})

test.describe('checkout API rules', () => {
  test('rejects a missing, inactive, or unknown salesperson once salespeople exist', async () => {
    const missing = await api.saleRaw(product, 1, null)
    expect(missing.status()).toBe(400)
    expect((await missing.json()).error).toBe('Choose the salesperson who made this sale.')

    const gone = await api.createSalesperson(uniq('Gon'))
    await api.setActive(gone, false)
    const inactive = await api.saleRaw(product, 1, gone.id)
    expect(inactive.status()).toBe(400)
    expect((await inactive.json()).error).toContain('no longer available')

    const unknown = await api.saleRaw(product, 1, '00000000-0000-0000-0000-000000000123')
    expect(unknown.status()).toBe(400)
  })

  test('a rejected sale does not take stock', async () => {
    const p = await api.createProduct()
    const before = await (await api.ctx.get(`/api/products/${p.id}`)).json()
    const res = await api.saleRaw(p, 3, null)
    expect(res.status()).toBe(400)
    const after = await (await api.ctx.get(`/api/products/${p.id}`)).json()
    expect(after.qtyOnHand).toBe(before.qtyOnHand)
  })

  test('renaming a salesperson does not change past receipts', async () => {
    const sp = await api.createSalesperson(uniq('Old'))
    const inv = await api.sale(product, 1, sp.id)
    const renamed = uniq('New')
    const res = await api.ctx.put(`/api/salespeople/${sp.id}`, { data: { name: renamed, commissionPercent: 0, commissionBasis: 'SalesExVat' } })
    expect(res.ok()).toBeTruthy()
    expect((await api.invoice(inv.id)).salespersonName).toBe(sp.name)
  })
})
