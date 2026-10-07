import { test, expect, type Page } from '@playwright/test'
import { Api, signIn, uniq, type Invoice, type Product, type Salesperson } from './helpers'

let api: Api
let seller: Salesperson
let other: Salesperson

test.beforeAll(async () => {
  api = await Api.login()
  seller = await api.createSalesperson(uniq('Sel'))
  other = await api.createSalesperson(uniq('Oth'))
})

async function openExchange(page: Page, original: Invoice, sku: string) {
  await signIn(page, api.token)
  await page.goto('/#/find-sale')
  await page.locator('#hist-q').fill(sku)
  await page.getByRole('button', { name: 'Find', exact: true }).click()
  const receipt = page.locator('article.hist-receipt', { hasText: original.invoiceNumber })
  await expect(receipt).toBeVisible()
  await receipt.getByRole('button', { name: 'Return / exchange' }).click()
  const dialog = page.locator('.rx-dialog')
  await expect(dialog).toBeVisible()
  await expect(dialog.getByText('Bring back')).toBeVisible()
  return dialog
}

test.describe('return / exchange', () => {
  test('replacement sale needs a salesperson and is credited to whoever is picked', async ({ page }) => {
    const item: Product = await api.createProduct(230, 100)
    const swap: Product = await api.createProduct(460, 200)
    const original = await api.sale(item, 1, seller.id)

    const dialog = await openExchange(page, original, item.sku)
    await dialog.getByRole('button', { name: `Increase return qty for ${original.lines[0].description}` }).click()
    await dialog.locator('#rx-reason').fill('Wrong size')

    // Refund only so far: no salesperson field, confirm allowed
    await expect(dialog.locator('#rx-salesperson')).toHaveCount(0)
    const confirm = dialog.getByRole('button', { name: 'Confirm exchange' })
    await expect(confirm).toBeEnabled()

    // Add a replacement → salesperson becomes required
    await dialog.getByPlaceholder('Scan barcode or type SKU / item name…').fill(swap.sku)
    await dialog.locator('.rx-search__hit', { hasText: swap.sku }).click()
    await expect(dialog.locator('.rx-newline', { hasText: swap.name })).toBeVisible()
    const select = dialog.locator('#rx-salesperson')
    await expect(select).toBeVisible()
    await expect(select.locator('option:checked')).toHaveText('Choose…')
    await expect(confirm).toBeDisabled()

    await select.selectOption({ label: other.name })
    await expect(confirm).toBeEnabled()
    await confirm.click()

    await expect(page.getByText('Exchange complete')).toBeVisible()
    await expect(page.getByText('Collected from customer')).toBeVisible()

    const latest = await api.latestInvoice()
    const newInv = await api.invoice(latest.id)
    expect(newInv.id).not.toBe(original.id)
    expect(newInv.salespersonId).toBe(other.id)
    expect(newInv.salespersonName).toBe(other.name)
    expect(newInv.lines[0].productId).toBe(swap.id)

    // Original stays with the original seller
    expect((await api.invoice(original.id)).salespersonName).toBe(seller.name)
  })

  test('refund-only return through the UI does not ask for a salesperson', async ({ page }) => {
    const item = await api.createProduct()
    const original = await api.sale(item, 2, seller.id)
    const dialog = await openExchange(page, original, item.sku)
    await dialog.getByRole('button', { name: `Increase return qty for ${original.lines[0].description}` }).click()
    await dialog.locator('#rx-reason').fill('Changed mind')
    await expect(dialog.locator('#rx-salesperson')).toHaveCount(0)
    await dialog.getByRole('button', { name: 'Confirm exchange' }).click()
    await expect(page.getByText('Exchange complete')).toBeVisible()
    await expect(page.getByText('Refunded to customer')).toBeVisible()
  })

  test('API: exchange without a salesperson is rejected before anything is returned', async () => {
    const item = await api.createProduct()
    const swap = await api.createProduct()
    const original = await api.sale(item, 1, seller.id)

    const body = (salespersonId: string | null) => ({
      reason: 'swap',
      paymentMethod: 'Card',
      salespersonId,
      returnLines: [{ invoiceLineId: original.lines[0].id, quantity: 1 }],
      newLines: [{ productId: swap.id, quantity: 1, originalUnitPrice: swap.sellPrice }]
    })

    const missing = await api.ctx.post(`/api/invoices/${original.id}/exchange`, { data: body(null) })
    expect(missing.status()).toBe(400)
    expect((await missing.json()).error).toBe('Choose the salesperson who made this sale.')

    const gone = await api.createSalesperson(uniq('Gx'))
    await api.setActive(gone, false)
    const inactive = await api.ctx.post(`/api/invoices/${original.id}/exchange`, { data: body(gone.id) })
    expect(inactive.status()).toBe(400)
    expect((await inactive.json()).error).toContain('no longer available')

    // Neither failed attempt may have recorded a return or moved stock
    const after = await api.invoice(original.id)
    expect(after.lines[0]).toMatchObject({ returnedQuantity: 0 })

    // Refund-only is still fine with no salesperson
    const refund = await api.exchange(original, 1, [], null)
    expect(refund.exchangeInvoice).toBeNull()
    expect(refund.creditTotal).toBe(230)
  })
})
