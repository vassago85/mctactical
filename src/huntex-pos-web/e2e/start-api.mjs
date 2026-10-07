// Builds the API image from the production Dockerfile and runs it in the foreground with a
// fresh database. Playwright's webServer owns this process; the container is removed on exit.
import { execSync, spawn } from 'node:child_process'
import { fileURLToPath } from 'node:url'
import path from 'node:path'

const API_PORT = 5299
const CONTAINER = 'huntex-pos-e2e-api'
const IMAGE = 'huntex-pos-api:e2e'
const apiDir = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../HuntexPos.Api')

execSync(`docker rm -f ${CONTAINER}`, { stdio: 'ignore' })
execSync(`docker build -q -t ${IMAGE} "${apiDir}"`, { stdio: ['ignore', 'ignore', 'inherit'] })

const child = spawn('docker', [
  'run', '--rm', '--name', CONTAINER, '-p', `${API_PORT}:8080`,
  '-e', 'ConnectionStrings__Default=Data Source=/app/data/huntex.db',
  '-e', 'App__PdfStoragePath=/app/data/pdfs',
  '-e', 'App__BrandingStoragePath=/app/data/branding',
  '-e', 'Seed__OwnerEmail=owner@e2e.local',
  '-e', 'Seed__OwnerPassword=E2e-Owner-Pass!1',
  '-e', 'Jwt__Key=e2e-test-signing-key-0123456789-abcdefghij',
  // Production-like POS rules so Sales-role tests exercise the real limits.
  '-e', 'PosRules__MaxCartDiscountPercent=25',
  '-e', 'PosRules__MaxLineDiscountPercent=50',
  '-e', 'PosRules__MaxPriceDecreasePercentFromList=15',
  '-e', 'PosRules__MaxPriceIncreasePercentFromList=10',
  IMAGE
], { stdio: 'inherit' })

const stop = () => {
  try { execSync(`docker rm -f ${CONTAINER}`, { stdio: 'ignore' }) } catch { /* already gone */ }
  process.exit(0)
}
process.on('SIGINT', stop)
process.on('SIGTERM', stop)
child.on('exit', (code) => process.exit(code ?? 0))
