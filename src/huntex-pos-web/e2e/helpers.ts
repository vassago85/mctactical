import { execFileSync } from 'node:child_process'
import { request, expect, type APIRequestContext, type Page } from '@playwright/test'
import { API_URL, DATA_VOLUME, OWNER_EMAIL, OWNER_PASSWORD, SQLITE_IMAGE } from './env'

export type CommissionBasis = 'SalesExVat' | 'GrossProfit'
export type Salesperson = { id: string; name: string; commissionPercent: number; commissionBasis: CommissionBasis; isActive: boolean }
export type Product = { id: string; sku: string; name: string; sellPrice: number }
export type InvoiceLine = { id: string; productId: string; description: string; quantity: number; returnedQuantity: number }
export type Invoice = {
  id: string
  invoiceNumber: string
  publicToken: string
  grandTotal: number
  createdAt: string
  salespersonId: string | null
  salespersonName: string | null
  lines: InvoiceLine[]
}
export type MissingCostRow = {
  key: string
  isUnlinkedShopify: boolean
  productId: string | null
  shopifyVariantId: number | null
  sku: string | null
  name: string
  qtySold: number
  saleCount: number
  revenue: number
  lastSoldAt: string
}
export type ReportRow = {
  salespersonId: string | null
  name: string
  isActive: boolean
  salesCount: number
  grossSalesInclVat: number
  returnsInclVat: number
  netSalesInclVat: number
  netSalesExVat: number
  grossProfitExVat: number
  commissionPercent: number
  commissionBasis: CommissionBasis
  commission: number
}

let seq = 0
/** Short unique label — salesperson names render on narrow till buttons. */
export function uniq(prefix: string): string {
  seq += 1
  return `${prefix}${seq}${Math.random().toString(36).slice(2, 5)}`
}

/** Matches formatZAR output (non-breaking-space thousands separator). */
export function zar(n: number): string {
  const [int, dec] = n.toFixed(2).split('.')
  return `R${int.replace(/\B(?=(\d{3})+(?!\d))/g, '\u00A0')}.${dec}`
}

export class Api {
  private constructor(readonly ctx: APIRequestContext, readonly token: string) {}

  static async login(email = OWNER_EMAIL, password = OWNER_PASSWORD): Promise<Api> {
    const anon = await request.newContext({ baseURL: API_URL })
    const res = await anon.post('/api/auth/login', { data: { email, password } })
    expect(res.status(), `login ${email}`).toBe(200)
    const { token } = await res.json()
    await anon.dispose()
    const ctx = await request.newContext({ baseURL: API_URL, extraHTTPHeaders: { Authorization: `Bearer ${token}` } })
    return new Api(ctx, token)
  }

  async ok<T>(res: Awaited<ReturnType<APIRequestContext['get']>>, what: string): Promise<T> {
    expect(res.ok(), `${what}: ${res.status()} ${await res.text()}`).toBeTruthy()
    const text = await res.text()
    return (text ? JSON.parse(text) : undefined) as T
  }

  async createProduct(price = 230, cost = 100): Promise<Product> {
    const sku = uniq('E2E-').toUpperCase()
    return this.ok<Product>(
      await this.ctx.post('/api/products', { data: { sku, name: `Test item ${sku}`, cost, sellPrice: price, qtyOnHand: 500 } }),
      'create product'
    )
  }

  async createSalesperson(name: string, commissionPercent = 0, commissionBasis: CommissionBasis = 'SalesExVat'): Promise<Salesperson> {
    return this.ok<Salesperson>(
      await this.ctx.post('/api/salespeople', { data: { name, commissionPercent, commissionBasis } }),
      'create salesperson'
    )
  }

  async setActive(sp: Salesperson, isActive: boolean): Promise<Salesperson> {
    return this.ok<Salesperson>(
      await this.ctx.put(`/api/salespeople/${sp.id}`, {
        data: { name: sp.name, commissionPercent: sp.commissionPercent, commissionBasis: sp.commissionBasis, isActive }
      }),
      'update salesperson'
    )
  }

  async listSalespeople(includeInactive = false): Promise<Salesperson[]> {
    return this.ok<Salesperson[]>(await this.ctx.get(`/api/salespeople?includeInactive=${includeInactive}`), 'list salespeople')
  }

  saleRaw(product: Product, qty: number, salespersonId: string | null, paymentMethod = 'Card') {
    return this.ctx.post('/api/invoices', {
      data: { paymentMethod, salespersonId, lines: [{ productId: product.id, quantity: qty, originalUnitPrice: product.sellPrice }] }
    })
  }

  async sale(product: Product, qty: number, salespersonId: string | null): Promise<Invoice> {
    return this.ok<Invoice>(await this.saleRaw(product, qty, salespersonId), 'create sale')
  }

  async exchange(invoice: Invoice, returnQty: number, newLines: { product: Product; qty: number }[], salespersonId: string | null) {
    return this.ok<{ creditTotal: number; netSettlement: number; exchangeInvoice: Invoice | null }>(
      await this.ctx.post(`/api/invoices/${invoice.id}/exchange`, {
        data: {
          reason: 'e2e return',
          paymentMethod: 'Card',
          salespersonId,
          returnLines: [{ invoiceLineId: invoice.lines[0].id, quantity: returnQty }],
          newLines: newLines.map((l) => ({ productId: l.product.id, quantity: l.qty, originalUnitPrice: l.product.sellPrice }))
        }
      }),
      'exchange'
    )
  }

  async void(invoice: Invoice) {
    await this.ok(await this.ctx.post(`/api/invoices/${invoice.id}/void`, { data: { reason: 'e2e void' } }), 'void')
  }

  async latestInvoice(): Promise<{ id: string; publicToken: string; invoiceNumber: string }> {
    const rows = await this.ok<{ id: string; publicToken: string; invoiceNumber: string }[]>(
      await this.ctx.get('/api/invoices/recent?take=1'),
      'recent invoices'
    )
    return rows[0]
  }

  async invoice(id: string): Promise<Invoice> {
    return this.ok<Invoice>(await this.ctx.get(`/api/invoices/${id}`), 'get invoice')
  }

  async missingCosts(from?: Date, to?: Date): Promise<MissingCostRow[]> {
    const params: Record<string, string> = {}
    if (from) params.from = from.toISOString()
    if (to) params.to = to.toISOString()
    return this.ok<MissingCostRow[]>(await this.ctx.get('/api/missing-costs', { params }), 'missing costs')
  }

  saveMissingCostsRaw(items: { key: string; costExVat: number }[]) {
    return this.ctx.post('/api/missing-costs', { data: { items } })
  }

  async report(from: Date, to: Date): Promise<ReportRow[]> {
    const res = await this.ctx.get('/api/reports/salespeople', { params: { from: from.toISOString(), to: to.toISOString() } })
    return (await this.ok<{ rows: ReportRow[] }>(res, 'salesperson report')).rows
  }

  /** Creates a user with a known password and returns a logged-in client for them. */
  async createUser(role: 'Sales' | 'Admin'): Promise<Api> {
    const email = `${uniq(role.toLowerCase())}@e2e.local`
    const password = 'E2e-Sales-Pass!1'
    const user = await this.ok<{ id: string }>(
      await this.ctx.post('/api/admin/users', { data: { email, displayName: `${role} staff`, role } }),
      `create ${role} user`
    )
    await this.ok(await this.ctx.post(`/api/admin/users/${user.id}/password`, { data: { newPassword: password } }), 'set password')
    return Api.login(email, password)
  }

  createSalesUser(): Promise<Api> {
    return this.createUser('Sales')
  }
}

/**
 * Runs SQL against the e2e API's SQLite file (shared volume) and returns stdout. For seeding rows the
 * API cannot create, such as Shopify-import lines. EF stores GUIDs as upper-case TEXT.
 */
export function sql(statements: string): string {
  return execFileSync(
    'docker',
    ['run', '--rm', '-i', '-v', `${DATA_VOLUME}:/d`, SQLITE_IMAGE, 'sqlite3', '-batch', '/d/huntex.db'],
    { input: `.timeout 10000\n${statements}\n`, encoding: 'utf8' }
  ).trim()
}

export const sqlId = (id: string) => `'${id.toUpperCase()}'`

/** Boots the SPA already signed in. Only seeds the token so later reloads keep app state. */
export async function signIn(page: Page, token: string) {
  await page.addInitScript((t) => {
    if (!localStorage.getItem('huntex_token')) localStorage.setItem('huntex_token', t)
  }, token)
}

export function salespersonButtons(page: Page) {
  return page.getByRole('group', { name: 'Salesperson' }).getByRole('button')
}

export async function addToCart(page: Page, sku: string) {
  const search = page.getByRole('searchbox', { name: /Scan barcode, or search products/ })
  await search.fill(sku)
  await search.press('Enter')
  await expect(page.locator('.pos-cart-table tbody tr', { hasText: sku }).first()).toBeVisible()
}

export function checkoutButton(page: Page) {
  return page.locator('.pos-checkout-btn')
}
