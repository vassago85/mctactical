import { test, expect } from '@playwright/test'
import { Api, signIn, uniq } from './helpers'

test.describe('Manage → Salespeople', () => {
  test('add, validate, edit, deactivate and reactivate through the UI', async ({ page }) => {
    const api = await Api.login()
    await signIn(page, api.token)
    page.on('dialog', (d) => d.accept())

    await page.goto('/#/pos')
    await page.getByRole('link', { name: 'Salespeople' }).click()
    await expect(page).toHaveURL(/#\/salespeople$/)
    await expect(page.getByRole('heading', { name: 'Salespeople', level: 1 })).toBeVisible()

    const name = uniq('Thandi')

    // Add with a gross-profit commission basis
    await page.getByRole('button', { name: 'Add salesperson' }).click()
    await page.locator('#sp-name').fill(name)
    await page.locator('#sp-pct').fill('7.5')
    await page.locator('#sp-basis').selectOption('GrossProfit')
    await page.getByRole('button', { name: 'Add', exact: true }).click()

    const row = page.locator('.sp-row', { hasText: name })
    await expect(row).toBeVisible()
    await expect(row).toContainText('7.5% of gross profit (excl. VAT)')

    // Duplicate name (case-insensitive) is rejected and the modal stays open with the error
    await page.getByRole('button', { name: 'Add salesperson' }).click()
    await page.locator('#sp-name').fill(name.toLowerCase())
    await page.getByRole('button', { name: 'Add', exact: true }).click()
    await expect(page.getByText(`A salesperson named "${name.toLowerCase()}" already exists.`)).toBeVisible()

    // Out-of-range commission is caught client-side
    await page.locator('#sp-name').fill(uniq('Bad'))
    await page.locator('#sp-pct').fill('150')
    await page.getByRole('button', { name: 'Add', exact: true }).click()
    await expect(page.getByText('Commission must be between 0% and 100%.')).toBeVisible()
    await page.getByRole('button', { name: 'Cancel' }).click()

    // Edit: switch to sales basis at 5%
    await row.getByTitle('Edit').click()
    await expect(page.locator('#sp-name')).toHaveValue(name)
    await expect(page.locator('#sp-basis')).toHaveValue('GrossProfit')
    await page.locator('#sp-pct').fill('5')
    await page.locator('#sp-basis').selectOption('SalesExVat')
    await page.getByRole('button', { name: 'Save', exact: true }).click()
    await expect(row).toContainText('5% of sales (excl. VAT)')

    // Deactivate: stays listed (Show inactive switches on) with an Inactive badge
    await row.getByTitle('Deactivate').click()
    await expect(row).toContainText('Inactive')
    const apiRow = (await api.listSalespeople(true)).find((s) => s.name === name)
    expect(apiRow?.isActive).toBe(false)
    expect((await api.listSalespeople()).some((s) => s.name === name)).toBe(false)

    // Hidden once "Show inactive" is off
    await page.getByLabel('Show inactive').uncheck()
    await expect(page.locator('.sp-row', { hasText: name })).toHaveCount(0)
    await page.getByLabel('Show inactive').check()

    // Reactivate
    await page.locator('.sp-row', { hasText: name }).getByTitle('Reactivate').click()
    await expect(page.locator('.sp-row', { hasText: name })).not.toContainText('Inactive')
    expect((await api.listSalespeople()).some((s) => s.name === name)).toBe(true)
  })

  test('API rejects invalid input', async () => {
    const api = await Api.login()
    const name = uniq('Val')
    await api.createSalesperson(name)

    const dup = await api.ctx.post('/api/salespeople', { data: { name: ` ${name.toUpperCase()} `, commissionPercent: 0 } })
    expect(dup.status()).toBe(409)

    for (const data of [
      { name: '', commissionPercent: 0 },
      { name: uniq('Neg'), commissionPercent: -1 },
      { name: uniq('Big'), commissionPercent: 100.01 },
      { name: 'x'.repeat(129), commissionPercent: 0 }
    ]) {
      const res = await api.ctx.post('/api/salespeople', { data })
      expect(res.status(), JSON.stringify(data).slice(0, 60)).toBe(400)
    }

    const badBasis = await api.ctx.post('/api/salespeople', { data: { name: uniq('Bb'), commissionPercent: 1, commissionBasis: 'Turnover' } })
    expect(badBasis.status()).toBe(400)

    const missing = await api.ctx.put('/api/salespeople/00000000-0000-0000-0000-000000000001', { data: { name: 'Nobody' } })
    expect(missing.status()).toBe(404)
  })
})
