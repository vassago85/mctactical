import { test, expect } from '@playwright/test'
import { Api, addToCart, checkoutButton, signIn } from './helpers'

// Must run first: proves deploying the feature does not block the till before anyone is set up.
test.describe('before any salespeople exist', () => {
  test('API has no salespeople and accepts a sale without one', async () => {
    const api = await Api.login()
    expect(await api.listSalespeople(true)).toHaveLength(0)
    const product = await api.createProduct()
    const inv = await api.sale(product, 1, null)
    expect(inv.salespersonName).toBeNull()
  })

  test('POS hides the picker and completes a sale as before', async ({ page }) => {
    const api = await Api.login()
    const product = await api.createProduct()
    await signIn(page, api.token)
    await page.goto('/#/pos')
    await addToCart(page, product.sku)

    await expect(page.getByRole('group', { name: 'Salesperson' })).toHaveCount(0)
    await expect(checkoutButton(page)).toHaveText('Complete sale')
    await checkoutButton(page).click()

    const summary = page.locator('.sale-summary')
    await expect(summary).toBeVisible()
    await expect(page.locator('.sale-summary__served')).toHaveCount(0)
  })
})
