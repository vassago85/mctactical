// Upgrade rehearsal: boots the previous release (default HEAD) on a fresh SQLite volume, rings up
// sales and returns, then boots the working-tree API on the *same* volume and checks that the
// schema upgrade keeps old data intact and the salesperson feature works on top of it.
//
//   node e2e/db-upgrade.mjs [git-ref]
import { execSync } from 'node:child_process'
import { fileURLToPath } from 'node:url'
import { mkdtempSync, rmSync, writeFileSync } from 'node:fs'
import os from 'node:os'
import path from 'node:path'
import assert from 'node:assert/strict'

const here = path.dirname(fileURLToPath(import.meta.url))
const repoRoot = path.resolve(here, '../../..')
const newApiDir = path.resolve(here, '../../HuntexPos.Api')
const baseRef = process.argv[2] ?? 'HEAD'

const PORT = 5301
const BASE = `http://localhost:${PORT}`
const CONTAINER = 'huntex-upgrade-api'
const VOLUME = 'huntex-upgrade-data'
const OLD_IMAGE = 'huntex-pos-api:upgrade-old'
const NEW_IMAGE = 'huntex-pos-api:upgrade-new'
const OWNER = { email: 'owner@upgrade.local', password: 'Upgrade-Owner-Pass!1' }

const worktree = path.join(os.tmpdir(), `huntex-upgrade-${Date.now()}`)
const scratch = mkdtempSync(path.join(os.tmpdir(), 'huntex-upgrade-pdf-'))

const sh = (cmd, opts = {}) => execSync(cmd, { stdio: ['ignore', 'pipe', 'pipe'], encoding: 'utf8', ...opts })
const quiet = (cmd) => { try { sh(cmd) } catch { /* best effort */ } }
const step = (msg) => console.log(`\n▶ ${msg}`)
const ok = (msg) => console.log(`  ✓ ${msg}`)

function startApi(image) {
  quiet(`docker rm -f ${CONTAINER}`)
  sh([
    'docker run -d', `--name ${CONTAINER}`, `-p ${PORT}:8080`, `-v ${VOLUME}:/app/data`,
    '-e TZ=Africa/Johannesburg',
    '-e "ConnectionStrings__Default=Data Source=/app/data/huntex.db"',
    '-e App__PdfStoragePath=/app/data/pdfs',
    '-e App__BrandingStoragePath=/app/data/branding',
    `-e Seed__OwnerEmail=${OWNER.email}`,
    `-e "Seed__OwnerPassword=${OWNER.password}"`,
    '-e Jwt__Key=upgrade-test-signing-key-0123456789-abcdefghij',
    image
  ].join(' '))
}

async function waitUp() {
  const deadline = Date.now() + 90_000
  while (Date.now() < deadline) {
    try {
      const res = await fetch(`${BASE}/api/auth/me`)
      if (res.status === 401) return
    } catch { /* not listening yet */ }
    const state = sh(`docker inspect -f "{{.State.Running}}" ${CONTAINER}`).trim()
    if (state !== 'true') throw new Error(`API container exited during startup:\n${sh(`docker logs ${CONTAINER}`)}`)
    await new Promise((r) => setTimeout(r, 500))
  }
  throw new Error(`API did not come up:\n${sh(`docker logs ${CONTAINER}`)}`)
}

function stopApi() {
  sh(`docker stop -t 10 ${CONTAINER}`)
  sh(`docker rm ${CONTAINER}`)
}

async function client() {
  const res = await fetch(`${BASE}/api/auth/login`, {
    method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(OWNER)
  })
  assert.equal(res.status, 200, 'owner login')
  const { token } = await res.json()
  const call = async (method, url, body) => {
    const r = await fetch(`${BASE}${url}`, {
      method,
      headers: { authorization: `Bearer ${token}`, ...(body ? { 'content-type': 'application/json' } : {}) },
      body: body ? JSON.stringify(body) : undefined
    })
    const bytes = Buffer.from(await r.arrayBuffer())
    const text = bytes.toString('utf8')
    let json
    try { json = text ? JSON.parse(text) : undefined } catch { json = undefined }
    return { status: r.status, json, text, bytes }
  }
  const must = async (method, url, body) => {
    const r = await call(method, url, body)
    assert.ok(r.status >= 200 && r.status < 300, `${method} ${url} → ${r.status} ${r.text}`)
    return r.json
  }
  return { call, must, token }
}

const saleBody = (product, qty, salespersonId) => ({
  paymentMethod: 'Card',
  ...(salespersonId !== undefined ? { salespersonId } : {}),
  lines: [{ productId: product.id, quantity: qty, originalUnitPrice: product.sellPrice }]
})

async function main() {
  step(`Building the previous release (${baseRef}) and the working tree`)
  sh(`git -C "${repoRoot}" worktree add --detach "${worktree}" ${baseRef}`)
  sh(`docker build -q -t ${OLD_IMAGE} "${path.join(worktree, 'src/HuntexPos.Api')}"`)
  sh(`docker build -q -t ${NEW_IMAGE} "${newApiDir}"`)
  quiet(`docker volume rm -f ${VOLUME}`)
  sh(`docker volume create ${VOLUME}`)
  ok('images built, empty data volume created')

  // ── Old release: create real history ──────────────────────────────────────
  step('Old release: first boot + trading')
  startApi(OLD_IMAGE)
  await waitUp()
  let api = await client()
  const product = await api.must('POST', '/api/products', { sku: 'UPG-1', name: 'Upgrade knife', cost: 100, sellPrice: 230, qtyOnHand: 50 })
  const swap = await api.must('POST', '/api/products', { sku: 'UPG-2', name: 'Upgrade torch', cost: 200, sellPrice: 460, qtyOnHand: 50 })
  const oldSaleA = await api.must('POST', '/api/invoices', saleBody(product, 2))
  const oldSaleB = await api.must('POST', '/api/invoices', saleBody(product, 1))
  const refund = await api.must('POST', `/api/invoices/${oldSaleA.id}/exchange`, {
    reason: 'old refund', paymentMethod: 'Card',
    returnLines: [{ invoiceLineId: oldSaleA.lines[0].id, quantity: 1 }], newLines: []
  })
  const exch = await api.must('POST', `/api/invoices/${oldSaleB.id}/exchange`, {
    reason: 'old swap', paymentMethod: 'Card',
    returnLines: [{ invoiceLineId: oldSaleB.lines[0].id, quantity: 1 }],
    newLines: [{ productId: swap.id, quantity: 1, originalUnitPrice: swap.sellPrice }]
  })
  const oldPdf = await api.call('GET', `/api/invoices/${oldSaleA.id}/pdf`)
  assert.equal(oldPdf.status, 200, 'old release renders PDF')
  const oldInvoices = [oldSaleA, oldSaleB, exch.exchangeInvoice]
  const snapshot = await Promise.all(oldInvoices.map((i) => api.must('GET', `/api/invoices/${i.id}`)))
  const oldProduct = await api.must('GET', `/api/products/${product.id}`)
  ok(`3 invoices (${oldInvoices.map((i) => i.invoiceNumber).join(', ')}), 1 refund (R${refund.creditTotal}), 1 exchange`)
  stopApi()

  // ── New release on the same database ─────────────────────────────────────
  step('New release: upgrade boot on the old database')
  startApi(NEW_IMAGE)
  await waitUp()
  // The seeder re-runs every historic ALTER TABLE on each boot and swallows "duplicate column", so
  // `fail:` lines are normal. On this first upgrade boot the salesperson steps must all succeed.
  const startupLog = sh(`docker logs ${CONTAINER}`)
  assert.ok(!/crit:|Unhandled exception/i.test(startupLog), `startup crashed:\n${startupLog}`)
  const failedSql = startupLog.split(/^(?=\w+: )/m).filter((b) => b.startsWith('fail:'))
  assert.deepEqual(failedSql.filter((b) => /Salesperson/i.test(b)), [], 'salesperson schema steps failed')
  writeFileSync(path.join(scratch, 'schema.sql'), [
    '.schema Salespeople',
    "SELECT name FROM pragma_table_info('Invoices') WHERE name LIKE 'Salesperson%';",
    "SELECT name FROM pragma_index_list('Invoices');",
    'SELECT COUNT(*) FROM Invoices WHERE SalespersonId IS NOT NULL;'
  ].join('\n'))
  const schema = sh(
    `docker run --rm -v ${VOLUME}:/d -v "${scratch}:/w" alpine:3.20 sh -c "apk add -q --no-cache sqlite >/dev/null && sqlite3 -readonly /d/huntex.db < /w/schema.sql"`,
    { timeout: 180_000 }
  ).replace(/\r/g, '')
  assert.match(schema, /CREATE TABLE (IF NOT EXISTS )?"Salespeople"/)
  for (const col of ['"CommissionPercent"', '"CommissionBasis"', '"IsActive"']) assert.ok(schema.includes(col), `Salespeople.${col}`)
  assert.match(schema, /^SalespersonId$/m)
  assert.match(schema, /^SalespersonName$/m)
  assert.match(schema, /^IX_Invoices_SalespersonId$/m)
  assert.match(schema.trim(), /\n0$/, 'old invoices must not be back-filled with a salesperson')
  ok('started cleanly; schema now has Salespeople table, Invoices.SalespersonId/SalespersonName and index')

  api = await client()
  for (const [i, before] of snapshot.entries()) {
    const after = await api.must('GET', `/api/invoices/${oldInvoices[i].id}`)
    assert.equal(after.invoiceNumber, before.invoiceNumber)
    assert.equal(after.grandTotal, before.grandTotal)
    assert.equal(after.status, before.status)
    assert.deepEqual(after.lines.map((l) => [l.quantity, l.returnedQuantity, l.lineTotal]), before.lines.map((l) => [l.quantity, l.returnedQuantity, l.lineTotal]))
    assert.equal(after.salespersonId, null)
    assert.equal(after.salespersonName, null)
  }
  ok('old invoices unchanged (numbers, totals, lines, returned qty); salesperson empty')

  assert.equal((await api.must('GET', `/api/products/${product.id}`)).qtyOnHand, oldProduct.qtyOnHand)
  ok('stock unchanged')

  assert.equal((await api.call('GET', `/api/invoices/${oldSaleA.id}/pdf`)).status, 200)
  ok('old invoice PDF still opens')

  assert.deepEqual(await api.must('GET', '/api/salespeople?includeInactive=true'), [])
  const preSetup = await api.must('POST', '/api/invoices', saleBody(product, 1, null))
  assert.equal(preSetup.salespersonName, null)
  ok('no salespeople yet → till works exactly as before (sale without salesperson accepted)')

  step('New release: set up salespeople and trade')
  const sp = await api.must('POST', '/api/salespeople', { name: 'Upgrade Pieter', commissionPercent: 10, commissionBasis: 'GrossProfit' })
  const blocked = await api.call('POST', '/api/invoices', saleBody(product, 1, null))
  assert.equal(blocked.status, 400)
  const credited = await api.must('POST', '/api/invoices', saleBody(product, 1, sp.id))
  assert.equal(credited.salespersonName, 'Upgrade Pieter')
  // Returning an item from a pre-upgrade sale must still work and stay Unassigned
  await api.must('POST', `/api/invoices/${oldSaleA.id}/exchange`, {
    reason: 'post-upgrade refund', paymentMethod: 'Card',
    returnLines: [{ invoiceLineId: oldSaleA.lines[0].id, quantity: 1 }], newLines: []
  })
  ok('salesperson created; sale now requires one; credited sale stored; old-sale return still works')

  const day = 86_400_000
  const report = await api.must('GET', `/api/reports/salespeople?from=${new Date(Date.now() - day).toISOString()}&to=${new Date(Date.now() + day).toISOString()}`)
  const row = report.rows.find((r) => r.salespersonId === sp.id)
  assert.deepEqual(
    { c: row.salesCount, ex: row.netSalesExVat, gp: row.grossProfitExVat, com: row.commission },
    { c: 1, ex: 200, gp: 100, com: 10 }
  )
  const un = report.rows.at(-1)
  assert.equal(un.name, 'Unassigned')
  // oldSaleA (460) + oldSaleB (230) + old exchange invoice (460) + pre-setup sale (230)
  assert.equal(un.salesCount, 4)
  assert.equal(un.grossSalesInclVat, 1380)
  // old refund 230 + old swap 230 + post-upgrade refund 230
  assert.equal(un.returnsInclVat, 690)
  ok(`report: Pieter 1 sale / R10 commission; Unassigned ${un.salesCount} sales, R${un.returnsInclVat} returns`)

  step('PDF shows SERVED BY for the new sale')
  const pdf = await api.call('GET', `/api/invoices/${credited.id}/pdf`)
  assert.equal(pdf.status, 200)
  writeFileSync(path.join(scratch, 'new.pdf'), pdf.bytes)
  const oldPdf2 = await api.call('GET', `/api/invoices/${oldSaleB.id}/pdf`)
  writeFileSync(path.join(scratch, 'old.pdf'), oldPdf2.bytes)
  const text = sh(
    `docker run --rm -v "${scratch}:/w" debian:bookworm-slim sh -c "apt-get update -qq >/dev/null && apt-get install -y -qq poppler-utils >/dev/null 2>&1 && pdftotext -layout /w/new.pdf - && echo ===OLD=== && pdftotext -layout /w/old.pdf -"`,
    { timeout: 300_000 }
  )
  const [newText, oldText] = text.split('===OLD===')
  assert.match(newText, /SERVED BY/)
  assert.match(newText, /Upgrade Pieter/)
  assert.doesNotMatch(oldText, /SERVED BY/)
  ok('new invoice PDF prints "SERVED BY Upgrade Pieter"; pre-upgrade invoice PDF has no Served-by cell')

  step('Second boot of the new release (schema step must be repeatable)')
  stopApi()
  startApi(NEW_IMAGE)
  await waitUp()
  api = await client()
  const again = await api.must('GET', '/api/salespeople')
  assert.equal(again.length, 1)
  assert.equal((await api.must('GET', `/api/invoices/${credited.id}`)).salespersonName, 'Upgrade Pieter')
  ok('restart is clean and data persists')

  console.log('\nDB upgrade rehearsal PASSED')
}

let failed = false
try {
  await main()
} catch (e) {
  failed = true
  console.error('\nDB upgrade rehearsal FAILED\n', e)
} finally {
  quiet(`docker rm -f ${CONTAINER}`)
  quiet(`docker volume rm -f ${VOLUME}`)
  quiet(`docker image rm ${OLD_IMAGE} ${NEW_IMAGE}`)
  quiet(`git -C "${repoRoot}" worktree remove --force "${worktree}"`)
  rmSync(scratch, { recursive: true, force: true })
}
process.exit(failed ? 1 : 0)
