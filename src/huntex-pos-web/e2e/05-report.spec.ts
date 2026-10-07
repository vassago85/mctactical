import { test, expect } from '@playwright/test'
import { Api, signIn, uniq, zar, type ReportRow, type Salesperson } from './helpers'

let api: Api
const DAY = 24 * 60 * 60 * 1000
const wideFrom = () => new Date(Date.now() - DAY)
const wideTo = () => new Date(Date.now() + DAY)

function rowFor(rows: ReportRow[], sp: Salesperson): ReportRow {
  const row = rows.find((r) => r.salespersonId === sp.id)
  expect(row, `report row for ${sp.name}`).toBeTruthy()
  return row!
}

test.beforeAll(async () => {
  api = await Api.login()
})

// Product is R230 incl. VAT (R200 excl.), cost R100 excl. → R100 GP per unit.
test.describe('salesperson report — numbers', () => {
  test('commission on sales vs. gross profit, with a refund deducted', async () => {
    const product = await api.createProduct(230, 100)
    const onSales = await api.createSalesperson(uniq('Sal'), 5, 'SalesExVat')
    const onGp = await api.createSalesperson(uniq('Gp'), 10, 'GrossProfit')

    const first = await api.sale(product, 1, onSales.id)
    await api.sale(product, 1, onSales.id)
    await api.sale(product, 3, onGp.id)
    await api.exchange(first, 1, [], null)

    const rows = await api.report(wideFrom(), wideTo())
    expect(rowFor(rows, onSales)).toMatchObject({
      name: onSales.name,
      isActive: true,
      salesCount: 2,
      grossSalesInclVat: 460,
      returnsInclVat: 230,
      netSalesInclVat: 230,
      netSalesExVat: 200,
      grossProfitExVat: 100,
      commissionPercent: 5,
      commissionBasis: 'SalesExVat',
      commission: 10
    })
    expect(rowFor(rows, onGp)).toMatchObject({
      salesCount: 1,
      grossSalesInclVat: 690,
      returnsInclVat: 0,
      netSalesInclVat: 690,
      netSalesExVat: 600,
      grossProfitExVat: 300,
      commissionPercent: 10,
      commissionBasis: 'GrossProfit',
      commission: 30
    })
  })

  test('exchange: return comes off the original seller, replacement goes to the new one', async () => {
    const item = await api.createProduct(230, 100)
    const swap = await api.createProduct(460, 200)
    const a = await api.createSalesperson(uniq('Ea'), 5, 'SalesExVat')
    const b = await api.createSalesperson(uniq('Eb'), 10, 'GrossProfit')

    const original = await api.sale(item, 1, a.id)
    const res = await api.exchange(original, 1, [{ product: swap, qty: 1 }], b.id)
    expect(res.exchangeInvoice?.salespersonName).toBe(b.name)
    expect(res.netSettlement).toBe(230)

    const rows = await api.report(wideFrom(), wideTo())
    expect(rowFor(rows, a)).toMatchObject({ salesCount: 1, grossSalesInclVat: 230, returnsInclVat: 230, netSalesInclVat: 0, netSalesExVat: 0, grossProfitExVat: 0, commission: 0 })
    expect(rowFor(rows, b)).toMatchObject({ salesCount: 1, grossSalesInclVat: 460, netSalesExVat: 400, grossProfitExVat: 200, commission: 20 })
  })

  test('a return lands in the period it happened, not the period of the sale', async () => {
    const product = await api.createProduct(230, 100)
    const sp = await api.createSalesperson(uniq('Per'), 5, 'SalesExVat')
    const sale = await api.sale(product, 1, sp.id)
    const saleAt = new Date(sale.createdAt).getTime()
    await new Promise((r) => setTimeout(r, 1500))
    await api.exchange(sale, 1, [], null)
    const split = new Date(saleAt + 750)

    // "Last month": sale only — already paid out, must not change
    const before = rowFor(await api.report(new Date(saleAt - 60_000), split), sp)
    expect(before).toMatchObject({ salesCount: 1, returnsInclVat: 0, netSalesInclVat: 230, commission: 10 })

    // "This month": only the clawback
    const after = rowFor(await api.report(split, wideTo()), sp)
    expect(after).toMatchObject({ salesCount: 0, returnsInclVat: 230, netSalesInclVat: -230, netSalesExVat: -200, grossProfitExVat: -100, commission: -10 })
  })

  test('voided sales — and returns against them — are excluded', async () => {
    const product = await api.createProduct(230, 100)
    const sp = await api.createSalesperson(uniq('Vd'), 5, 'SalesExVat')
    const kept = await api.sale(product, 1, sp.id)
    const voided = await api.sale(product, 2, sp.id)
    await api.exchange(voided, 1, [], null)
    await api.void(voided)

    const row = rowFor(await api.report(wideFrom(), wideTo()), sp)
    expect(row).toMatchObject({ salesCount: 1, grossSalesInclVat: 230, returnsInclVat: 0, commission: 10 })
    expect(kept.salespersonId).toBe(sp.id)
  })

  test('inactive salespeople keep their history; active ones with no sales still appear; old sales are Unassigned', async () => {
    const product = await api.createProduct(230, 100)
    const idle = await api.createSalesperson(uniq('Idl'), 3)
    const leaver = await api.createSalesperson(uniq('Lv'), 5)
    await api.sale(product, 1, leaver.id)
    await api.setActive(leaver, false)

    const rows = await api.report(wideFrom(), wideTo())
    expect(rowFor(rows, idle)).toMatchObject({ salesCount: 0, netSalesInclVat: 0, commission: 0 })
    expect(rowFor(rows, leaver)).toMatchObject({ isActive: false, salesCount: 1, commission: 10 })

    // Sales from spec 01 (before salespeople existed) — Unassigned, always last
    const last = rows[rows.length - 1]
    expect(last.salespersonId).toBeNull()
    expect(last.name).toBe('Unassigned')
    expect(last.salesCount).toBeGreaterThanOrEqual(2)
    expect(last.commission).toBe(0)

    // Inactive people with nothing in the period drop off
    const empty = await api.report(new Date(Date.now() - 400 * DAY), new Date(Date.now() - 399 * DAY))
    expect(empty.some((r) => r.salespersonId === leaver.id)).toBe(false)
    expect(empty.some((r) => r.salespersonId === idle.id)).toBe(true)
  })
})

test.describe('salesperson report — Reports page', () => {
  test('Salespeople tab shows this month, totals and commission; previous month is empty', async ({ page }) => {
    const product = await api.createProduct(230, 100)
    const sp = await api.createSalesperson(uniq('Ui'), 10, 'GrossProfit')
    await api.sale(product, 1, sp.id)
    await api.sale(product, 1, sp.id)

    await signIn(page, api.token)
    await page.goto('/#/reports')
    await page.locator('.rep-tab', { hasText: 'Salespeople' }).click()
    await expect(page).toHaveURL(/#\/reports\/salespeople$/)

    const monthName = new Date().toLocaleDateString('en-ZA', { month: 'long', year: 'numeric' })
    await expect(page.getByText(`Salespeople — ${monthName}`)).toBeVisible()

    const row = page.locator('tbody tr', { hasText: sp.name })
    const cells = row.locator('td')
    await expect(cells.nth(1)).toHaveText('2')
    await expect(cells.nth(2)).toHaveText(zar(460))
    await expect(cells.nth(3)).toHaveText(zar(400))
    await expect(cells.nth(4)).toHaveText(zar(200))
    await expect(cells.nth(5)).toHaveText('10% of gross profit (excl. VAT)')
    await expect(cells.nth(6)).toHaveText(zar(20))

    const unassigned = page.locator('tbody tr', { hasText: 'Unassigned' })
    await expect(unassigned.locator('td').nth(6)).toHaveText('—')
    await expect(page.locator('tfoot')).toContainText('Total')

    // CSV export downloads a file named for the month
    const download = page.waitForEvent('download')
    await page.getByRole('button', { name: 'Export CSV' }).click()
    const file = await download
    const now = new Date()
    expect(file.suggestedFilename()).toBe(`salespeople-${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}.csv`)

    // Previous month: no sales for this person (active → still listed at zero)
    await page.getByRole('button', { name: 'Previous month' }).click()
    const prevName = new Date(now.getFullYear(), now.getMonth() - 1, 1).toLocaleDateString('en-ZA', { month: 'long', year: 'numeric' })
    await expect(page.getByText(`Salespeople — ${prevName}`)).toBeVisible()
    const prevRow = page.locator('tbody tr', { hasText: sp.name })
    await expect(prevRow.locator('td').nth(1)).toHaveText('0')
    await expect(prevRow.locator('td').nth(6)).toHaveText(zar(0))
  })
})
