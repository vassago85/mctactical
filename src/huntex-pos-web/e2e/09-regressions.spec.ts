import { test, expect, type Page } from '@playwright/test'
import { Api, signIn, sql, sqlId, uniq, type Product, type Salesperson } from './helpers'

let owner: Api
let product: Product
let seller: Salesperson
let supplier: { id: string; name: string }

/** 1×1 PNG — enough for the API to store and the PDF services to embed. */
const PNG = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8DwHwAFBQIAX8jx0gAAAABJRU5ErkJggg==', 'base64')

test.beforeAll(async () => {
  owner = await Api.login()
  product = await owner.createProduct(230, 100)
  seller = await owner.createSalesperson(uniq('Rg'))
  supplier = await owner.ok(await owner.ctx.post('/api/suppliers', { data: { name: uniq('Vendor ') } }), 'create supplier')
})

function trackPageErrors(page: Page): string[] {
  const errors: string[] = []
  page.on('pageerror', (e) => errors.push(e.message))
  return errors
}

test.describe('Receiving', () => {
  test('Products "Receive stock" opens a new owned-stock batch', async ({ page }) => {
    await signIn(page, owner.token)
    await page.goto('/#/stock')
    await page.getByRole('button', { name: 'Receive stock' }).click()
    await expect(page.getByRole('dialog', { name: 'New stock batch' }).locator('#cb-type')).toHaveValue('OwnedReceive')
    await expect(page).toHaveURL(/#\/receiving$/)
  })

  test('unknown SKUs are reported without breaking the import', async ({ page }) => {
    const errors = trackPageErrors(page)
    const ref = uniq('CSV-')
    await owner.ok(
      await owner.ctx.post('/api/consignment-batches', { data: { type: 'OwnedReceive', supplierId: supplier.id, sourceDocumentRef: ref } }),
      'create batch'
    )

    await signIn(page, owner.token)
    await page.goto('/#/receiving')
    await page.locator('tr', { hasText: ref }).getByRole('button', { name: 'Open' }).click()
    await page.getByRole('button', { name: 'Import CSV' }).click()
    const dialog = page.getByRole('dialog', { name: 'Import CSV' })
    await dialog.locator('#cb-file').setInputFiles({
      name: 'receive.csv',
      mimeType: 'text/csv',
      buffer: Buffer.from(`SKU,Qty\n${product.sku},3\nNOPE-${ref},1\n`)
    })
    await dialog.getByRole('button', { name: /Import/ }).click()

    await expect(page.getByText(`1 SKUs not found: NOPE-${ref}`)).toBeVisible()
    await expect(dialog).toHaveCount(0)
    await expect(page.locator('td', { hasText: product.sku }).first()).toBeVisible()
    expect(errors).toEqual([])
  })
})

test.describe('vendor account', () => {
  test('menu opens /vendor and the old /vendor-report link lands there too', async ({ page }) => {
    const errors = trackPageErrors(page)
    const vendor = await owner.createSalesUser()
    const me = await vendor.ok<{ id: string }>(await vendor.ctx.get('/api/auth/me'), 'me')
    await owner.ok(await owner.ctx.post(`/api/admin/users/${me.id}/supplier`, { data: { supplierId: supplier.id } }), 'link supplier')

    await signIn(page, vendor.token)
    await page.goto('/#/pos')
    await page.locator('.mc-sidebar__nav .mc-nav-link', { hasText: 'My vendor report' }).click()
    await expect(page).toHaveURL(/#\/vendor$/)
    await expect(page.getByRole('heading', { name: `${supplier.name} — vendor report`, level: 1 })).toBeVisible()

    await page.goto('/#/pos')
    await page.goto('/#/vendor-report')
    await expect(page).toHaveURL(/#\/vendor$/)
    expect(errors).toEqual([])
  })
})

test.describe('invoice PDF', () => {
  test('is generated and stored on demand when the invoice has none (e.g. Shopify imports)', async () => {
    const sale = await owner.sale(product, 1, seller.id)
    sql(`UPDATE Invoices SET PdfStorageKey = NULL WHERE Id = ${sqlId(sale.id)};`)

    const res = await owner.ctx.get(`/api/invoices/${sale.id}/pdf`)
    expect(res.status()).toBe(200)
    expect((await res.body()).subarray(0, 5).toString()).toBe('%PDF-')
    expect(sql(`SELECT PdfStorageKey FROM Invoices WHERE Id = ${sqlId(sale.id)};`)).toBe(`${sale.id.replace(/-/g, '')}.pdf`)

    sql(`UPDATE Invoices SET PdfStorageKey = NULL WHERE Id = ${sqlId(sale.id)};`)
    const pub = await owner.ctx.get(`/api/public/invoices/${sale.publicToken}/pdf`)
    expect(pub.status()).toBe(200)
    expect((await pub.body()).subarray(0, 5).toString()).toBe('%PDF-')
  })
})

test.describe('uploaded logo', () => {
  test.afterAll(async () => {
    await owner.ctx.delete('/api/settings/business/logo')
  })

  test('still reaches the label and batch PDFs', async () => {
    const batch = await owner.ok<{ id: string }>(
      await owner.ctx.post('/api/consignment-batches', { data: { type: 'OwnedReceive', supplierId: supplier.id } }),
      'create batch'
    )
    const pdfs = async () => Promise.all([
      owner.ctx.get(`/api/products/${product.id}/label`),
      owner.ctx.get(`/api/consignment-batches/${batch.id}/pdf`)
    ].map(async (p) => {
      const res = await p
      expect(res.status(), res.url()).toBe(200)
      return (await res.body()).length
    }))

    const before = await pdfs()
    await owner.ok(
      await owner.ctx.post('/api/settings/business/logo', { multipart: { file: { name: 'logo.png', mimeType: 'image/png', buffer: PNG } } }),
      'upload logo'
    )
    const after = await pdfs()
    expect(after[0], 'label PDF should change once a logo is uploaded').not.toBe(before[0])
    expect(after[1], 'batch PDF should change once a logo is uploaded').not.toBe(before[1])
  })
})
