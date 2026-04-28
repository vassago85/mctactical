# Phase 1 — 21-Day Implementation Plan

*Detailed technical checklist. No code changes yet.*

---

## Overarching Live-Safety Strategy (Task 10)

This applies to **every** task below. Read once, then refer back from each task.

### How MC Tactical stays safe

1. **Branch discipline.** Do all Phase 1 work on a branch, e.g. `phase-1/whitelabel`. MC Tactical's production deploys from `main`. We only merge to `main` after a clean local-compose run + a clean staging deploy.
2. **Data is never touched.** Phase 1 is **schema-additive** (one new column on `BusinessSettings`) and **code-only**. We never run a destructive migration. The only DB write is the existing `DbSeeder` adding a `TaxRate` column with default `15.00` — backwards compatible by design.
3. **Production redeploy = `git pull && docker compose up -d --build`.** That's it. The current `docker-compose.yml` already supplies all MC Tactical–specific values via env vars (`PUBLIC_BASE_URL`, `MAILGUN_*`, `OWNER_EMAIL`, etc.), so neutralizing compiled defaults has zero effect on a deployment whose env file fully overrides them.
4. **Staging gate.** Before merging to `main`: provision a throwaway test instance from the same branch, complete a sale → invoice → PDF → void cycle, verify totals match a parallel current-`main` instance.
5. **Backup before merge.** Daily backup script (Task 5) goes live **before** any other change touches production. So if anything regresses, we have last-night's `huntex.db`.
6. **Roll-forward, not roll-back.** Every change in Phase 1 is small and revertable with a single git revert + redeploy. We never need to roll the database back.

### What "live data" means in this plan

- **Live data** = `/opt/mctactical/data/huntex.db` and `/opt/mctactical/data/pdfs/` on the production server.
- **Cannot be done without touching live data:** Tasks that require redeploying the API container against the prod DB (Task 2 VAT, eventually). All other tasks are pure code and can be completed and tested locally.
- The plan defers all production redeploys to **Day 9** as a single batched cutover, after Days 1–8 of pure code/script work.

---

## Task 1 — Remove MC Tactical–specific defaults

### Goal

Strip every "MC Tactical" string baked into compiled code so a fresh build is brand-neutral. Production unchanged because env vars/DB row override defaults.

### Files to modify

| # | Path | What |
|---|------|------|
| 1.1 | `src/HuntexPos.Api/Options/AppOptions.cs` | Empty out hardcoded company defaults |
| 1.2 | `src/HuntexPos.Api/Program.cs` line 73 | Generic Swagger title |
| 1.3 | `src/HuntexPos.Api/Domain/MailSettings.cs` line 8 (XML doc comment) | Generic example |
| 1.4 | `src/HuntexPos.Api/Data/DbSeeder.cs` lines 88–104 | Only seed BusinessSettings if env-supplied values exist |
| 1.5 | `docker-compose.local.yml` lines 19, 22, 23 | Remove embedded keys/passwords (use only env interpolation) |

### Exact code changes

**1.1** `AppOptions.cs` — change defaults to empty strings:

```csharp
public string CompanyDisplayName { get; set; } = "";
public string CompanyPhone { get; set; } = "";
public string CompanyEmail { get; set; } = "";
public string CompanyAddress { get; set; } = "";
public string CompanyVatNumber { get; set; } = "";
public string CompanyWebsite { get; set; } = "";
public string CompanyWebsiteLabel { get; set; } = "";
public string PublicBaseUrl { get; set; } = "";
```

**1.2** `Program.cs` line 73:

```csharp
c.SwaggerDoc("v1", new OpenApiInfo { Title = "POS API", Version = "v1" });
```

**1.3** `MailSettings.cs` line 8 — replace XML comment example.

**1.4** `DbSeeder.cs` — guard against seeding empty BusinessSettings:

```csharp
if (!await db.BusinessSettings.AnyAsync(ct))
{
    var hasAnyAppCompanyValue =
        !string.IsNullOrWhiteSpace(appCfg.CompanyDisplayName) ||
        !string.IsNullOrWhiteSpace(appCfg.CompanyEmail) ||
        !string.IsNullOrWhiteSpace(appCfg.CompanyVatNumber);

    db.BusinessSettings.Add(new BusinessSettings
    {
        BusinessName = appCfg.CompanyDisplayName?.Trim() ?? "",
        // ... rest of fields ...
    });
    await db.SaveChangesAsync(ct);
}
```

(Functionally same row gets created; the new behaviour is that empty defaults produce an empty row, which the existing `EffectiveBusinessSettingsProvider.Merge()` already handles via its `Pick(dbVal, fallback)` pattern.)

**1.5** `docker-compose.local.yml` — remove embedded JWT key default and owner password, force them to come from `.env`:

```yaml
Jwt__Key: ${JWT_KEY:?JWT_KEY required in .env}
Seed__OwnerEmail: ${OWNER_EMAIL:?OWNER_EMAIL required in .env}
Seed__OwnerPassword: ${OWNER_PASSWORD:?OWNER_PASSWORD required in .env}
```

### Risk level

**LOW.** Production `docker-compose.yml` already supplies every MC Tactical value via env. The DB already has a populated `BusinessSettings` row. Compiled defaults are **never read at runtime in MC Tactical's deployment**.

### Testing steps

1. Run `dotnet build` on `HuntexPos.Api.csproj` — must succeed.
2. Run `dotnet test` on `tests/HuntexPos.Api.Tests/` — must pass.
3. Local docker-compose with a **clean data volume**: verify the seeded `BusinessSettings` row has empty-string fields (not "MC Tactical").
4. Local docker-compose with **MC Tactical's prod-style env file** (test copy): verify branding endpoint returns "MC Tactical" (because the DB row populates from env).
5. Hit `/api/settings/branding` → `businessName` should match env-supplied value, not compiled default.

### Rollback plan

`git revert <commit>` + `docker compose up -d --build` on prod. Total downtime ≈ 30s during container restart.

### Can be done without touching live data?

**Yes.** Pure code change. Production database row already has all MC Tactical values populated. Production env file already supplies them. Defaults become irrelevant.

---

## Task 2 — Extract VAT/tax rate into BusinessSettings

### Goal

Move the hardcoded `15m` from `InvoiceService.cs:40` and the ~15 scattered `1.15m` references to a configurable per-deployment value loaded from `BusinessSettings`.

### Files to modify

| # | Path | What |
|---|------|------|
| 2.1 | `src/HuntexPos.Api/Domain/BusinessSettings.cs` | Add `decimal TaxRate { get; set; } = 15m;` |
| 2.2 | `src/HuntexPos.Api/Data/HuntexDbContext.cs` (`OnModelCreating` BusinessSettings block ~line 98) | `e.Property(b => b.TaxRate).HasPrecision(18, 4);` |
| 2.3 | `src/HuntexPos.Api/Data/DbSeeder.cs` `EnsureBusinessSettingsTableAsync` (~line 595) | Add SQLite ALTER for old DBs: `ALTER TABLE BusinessSettings ADD COLUMN TaxRate TEXT NOT NULL DEFAULT '15';` |
| 2.4 | `src/HuntexPos.Api/Services/EffectiveBusinessSettings.cs` | Add `decimal TaxRate { get; init; } = 15m;` |
| 2.5 | `src/HuntexPos.Api/Services/EffectiveBusinessSettingsProvider.cs` (Merge method) | `TaxRate = row?.TaxRate > 0 ? row.TaxRate.Value : 15m,` |
| 2.6 | `src/HuntexPos.Api/DTOs/BusinessSettingsDtos.cs` | Add `TaxRate` to `BusinessSettingsDto` |
| 2.7 | `src/HuntexPos.Api/Controllers/BusinessSettingsController.cs` (PUT + ToDto) | Round-trip TaxRate |
| 2.8 | `src/HuntexPos.Api/Services/InvoiceService.cs` line 40 | `var taxRate = (await _business.GetAsync(ct)).TaxRate;` |
| 2.9 | `src/HuntexPos.Api/Services/QuoteService.cs` line 40 | Same pattern |
| 2.10 | `src/HuntexPos.Api/Services/PricingCalculator.cs` lines 31, 34 | Add overload taking `taxRate`; keep old methods for back-compat |
| 2.11 | `src/HuntexPos.Api/Controllers/ReportsController.cs` lines 104, 193, 200, 250, 251, 296, 407 | Replace `1.15m` → `(1m + taxRate / 100m)`, where `taxRate` is loaded once at the top of each action |
| 2.12 | `src/HuntexPos.Api/Services/InvoiceService.cs` line 254 | Same — replace `1.15m` |
| 2.13 | `src/huntex-pos-web/src/views/BusinessSettingsView.vue` | Add `taxRate` input next to `vatNumber`/`currency` |
| 2.14 | `src/huntex-pos-web/src/views/PosView.vue` (any client-side VAT-inclusive math) | Audit for client-side `15` or `1.15` references |

### Exact code change pattern

**InvoiceService.cs** line 38–40:

```csharp
public async Task<InvoiceDto> CreateAsync(CreateInvoiceRequest req, string userId, bool managerBypassPosRules, CancellationToken ct)
{
    var settings = await _business.GetAsync(ct);
    var taxRate = settings.TaxRate;
    // ... rest unchanged, taxRate is no longer const but local
```

**ReportsController.cs** Daily action — load once per request:

```csharp
public async Task<List<DailySummaryDto>> Daily(
    [FromServices] IEffectiveBusinessSettings business,
    CancellationToken ct, ...)
{
    var taxRate = (await business.GetAsync(ct)).TaxRate;
    var taxMultiplier = 1m + taxRate / 100m;
    // ... replace `1.15m` with `taxMultiplier` everywhere in this method
```

**PricingCalculator.cs** add overloads:

```csharp
public static decimal DistributorFloor(decimal cost, decimal taxRate) =>
    Round2(cost * (1m + taxRate / 100m));

public static bool IsBelowDistributorCost(decimal sellPrice, decimal cost, decimal taxRate) =>
    sellPrice > 0 && cost > 0 && sellPrice < DistributorFloor(cost, taxRate);

// keep existing 15%-hardcoded methods, mark [Obsolete] later
```

### Risk level

**MEDIUM.** Tax math is the heart of the business. Wrong rate = wrong VAT on every invoice. Mitigations:
- Default value is `15m` — identical behaviour for every existing client.
- `EnsureBusinessSettingsTableAsync` adds the column with default `'15'` for SQLite — old MC Tactical DB picks up `15m` automatically.
- Add a unit test in `tests/HuntexPos.Api.Tests/` that asserts a sale with `taxRate=15` produces identical `TaxAmount`/`GrandTotal` to before the refactor.

### Testing steps

1. `dotnet test` — existing tests must pass unchanged.
2. New test: `InvoiceService_CreateAsync_With15PercentTaxRate_MatchesLegacyTotals` — replays a known invoice scenario from prod (use an anonymized real example) and asserts byte-identical totals.
3. Local docker-compose with **a copy of MC Tactical's `huntex.db`**: run a sale, compare PDF VAT line vs. a sale on current `main`. Must match to the cent.
4. Set `TaxRate = 0` via the UI and verify a sale produces `TaxAmount = 0`, `GrandTotal == SubTotal − Discount`.
5. Set `TaxRate = 20` and verify VAT extraction math is correct.

### Rollback plan

Two-stage:
- **Code rollback:** `git revert` + redeploy. The `TaxRate` column remains in the DB but is unused. Harmless.
- **Data rollback:** Not needed — additive column with default value.

If a tax bug ships, immediately set `BusinessSettings.TaxRate = 15` via direct DB write or revert. The math returns to legacy behaviour.

### Can be done without touching live data?

**Mostly yes.** Code refactor + DTO changes are all local. The single live-DB change is one `ALTER TABLE BusinessSettings ADD COLUMN TaxRate` which is run by `DbSeeder` at startup of the new container — zero-downtime, additive, with safe default. The redeploy itself is a 30-second container restart on Day 9.

---

## Task 3 — Configurable currency formatting

### Goal

Replace `formatZAR(n)` (hardcoded `R` prefix) with `formatCurrency(n)` that reads currency from branding state. Default behaviour unchanged for ZAR users.

### Files to modify

| # | Path | What |
|---|------|------|
| 3.1 | `src/huntex-pos-web/src/utils/format.ts` | Add `formatCurrency(n, currencyCode?)` ; keep `formatZAR` alias for back-compat |
| 3.2 | `src/huntex-pos-web/src/composables/useBranding.ts` | Expose `currency` ref + `formatMoney` helper |
| 3.3 | `src/HuntexPos.Api/DTOs/BusinessSettingsDtos.cs` (PublicBrandingDto) | Add `Currency` so SPA boot loads it |
| 3.4 | `src/HuntexPos.Api/Controllers/BusinessSettingsController.cs` `GetBranding` (line 147) | Include `eff.Currency` in returned DTO |
| 3.5 | All views currently using `formatZAR` — **23 files** identified by grep | Migrate to `useBranding().formatMoney(n)` (see list below) |
| 3.6 | `src/HuntexPos.Api/Services/InvoicePdfService.cs` lines 244, 250, 265, 268, 274, 280, 295, 299, 303, 307, 322, 491, 495, 509 | Replace `R{...}` with `{currencySymbol}{...}` derived from `eff.Currency` |
| 3.7 | `src/HuntexPos.Api/Services/QuotePdfService.cs` (similar pattern) | Same |
| 3.8 | `src/HuntexPos.Api/Services/InvoiceService.cs` line 262 (warning text) | Use currency-aware formatter |

### Exact code change pattern

**`utils/format.ts`:**

```ts
const NBSP = '\u00A0'

const SYMBOLS: Record<string, string> = {
  ZAR: 'R', USD: '$', EUR: '€', GBP: '£', AUD: 'A$', NZD: 'NZ$',
}

export function currencySymbol(code: string | null | undefined): string {
  if (!code) return 'R'
  return SYMBOLS[code.toUpperCase()] ?? code.toUpperCase() + ' '
}

export function formatCurrency(n: number, code?: string): string {
  const sym = currencySymbol(code ?? 'ZAR')
  const [int, dec] = n.toFixed(2).split('.')
  return `${sym}${int.replace(/\B(?=(\d{3})+(?!\d))/g, NBSP)}.${dec}`
}

// Back-compat: keep formatZAR working until all callers migrate.
export const formatZAR = (n: number) => formatCurrency(n, 'ZAR')
```

**`useBranding.ts` additions:**

```ts
const currency = computed(() => state.value.currency || 'ZAR')

function formatMoney(n: number): string {
  return formatCurrency(n, currency.value)
}

// add to returned object:
return { ..., currency, formatMoney }
```

**Server-side currency symbol helper** in `InvoicePdfService.cs`:

```csharp
private static string Symbol(string currency) => currency?.ToUpperInvariant() switch
{
    "ZAR" or "" or null => "R",
    "USD" => "$",
    "EUR" => "€",
    "GBP" => "£",
    _ => currency.ToUpperInvariant() + " "
};
```

Then `$"R{x:N2}"` becomes `$"{Symbol(eff.Currency)}{x:N2}"`.

### Files using `formatZAR` (23 files, all in `src/huntex-pos-web/src/views/`)

`BusinessSettingsView.vue` is unaffected (no money rendering there).
PosView, FinancialReportView, ReportsView, StockListView, DeliveriesView, QuoteDetailView, QuoteEditView, QuotePublicView, QuotesListView, InvoicePublicView, LabelsPrintView, PriceLookupView, VendorReportView, SettingsView. Plus `format.ts` itself.

Migration is mechanical: import `useBranding`, replace `formatZAR(x)` → `formatMoney(x)`.

### Risk level

**LOW–MEDIUM.** Risk is rendering-only, not money math. A bug shows wrong symbol; numbers stay correct. Mitigation: keep `formatZAR` as alias of `formatCurrency(_, 'ZAR')` so MC Tactical continues to render `R` even if migration is incomplete.

### Testing steps

1. `npm run build` — must succeed.
2. `npx vue-tsc --noEmit` — no new errors.
3. Open every view in the app with `BusinessSettings.Currency = "ZAR"` — every price still displays as `R 1 234.56`.
4. Change to `"USD"` in settings — every price displays as `$ 1,234.56`.
5. Generate an invoice PDF in each currency and confirm symbols.

### Rollback plan

`git revert` + redeploy. `formatZAR` alias means partial-migration revert is still safe.

### Can be done without touching live data?

**Yes.** Frontend-only + PDF rendering. No DB changes (Currency column already exists on `BusinessSettings`). MC Tactical's `Currency` is already `"ZAR"` so behaviour is identical.

---

## Task 4 — Provisioning script for new clients

### Goal

Single command: `./deploy/provision.sh client-name domain.co.za owner@email.com` provisions a new instance on the same Docker host (or instructs deploy to a fresh VPS).

### Files to add

| # | Path | What |
|---|------|------|
| 4.1 | `deploy/provision.sh` | Bash script: prompts/args → generates `.env`, data dirs, compose override |
| 4.2 | `deploy/.env.template` | Template `.env` with placeholders |
| 4.3 | `deploy/compose.client-template.yml` | Per-client compose override (container names, volume paths, ports) |
| 4.4 | `deploy/README.md` | Operator docs |

### Exact script outline

```bash
#!/usr/bin/env bash
set -euo pipefail

CLIENT="$1"        # e.g. "acme-tactical"
DOMAIN="$2"        # e.g. "pos.acmetactical.co.za"
OWNER_EMAIL="$3"   # e.g. "owner@acmetactical.co.za"

ROOT="/opt/$CLIENT"
sudo mkdir -p "$ROOT/data/pdfs" "$ROOT/data/branding"
sudo chown -R 1654:1654 "$ROOT/data"

# Generate strong secrets
JWT_KEY=$(openssl rand -base64 48 | tr -d '\n')
OWNER_PASSWORD=$(openssl rand -base64 16 | tr -d '\n=' | head -c 16)

cat > "$ROOT/.env" <<EOF
PUBLIC_BASE_URL=https://$DOMAIN
WEB_PORT=$(shuf -i 8100-8999 -n 1)
JWT_KEY=$JWT_KEY
OWNER_EMAIL=$OWNER_EMAIL
OWNER_PASSWORD=$OWNER_PASSWORD
DATA_DIR=$ROOT/data
ALLOWED_ORIGIN=https://$DOMAIN
# Mailgun: fill in after onboarding
MAILGUN_API_KEY=
MAILGUN_DOMAIN=
MAILGUN_FROM=
EOF

cat > "$ROOT/docker-compose.yml" <<EOF
# Generated for $CLIENT — do not edit by hand
include:
  - $(realpath "$(dirname "$0")/../docker-compose.yml")
services:
  api:
    container_name: ${CLIENT}-api
    volumes:
      - \${DATA_DIR}:/app/data
  web:
    container_name: ${CLIENT}-web
    ports:
      - "\${WEB_PORT}:80"
EOF

echo "Provisioned $CLIENT at $ROOT"
echo "Owner password (record now, will not be shown again): $OWNER_PASSWORD"
echo "Bring up: cd $ROOT && sudo docker compose up -d"
```

### Files to modify (one)

`docker-compose.yml`:
- Generalize variable names: rename `MCTACTICAL_DATA_DIR` → `DATA_DIR` (with backwards-compat alias).
- Parameterize container names: `container_name: ${API_CONTAINER:-pos-api}`, `${WEB_CONTAINER:-pos-web}`.

### Risk level

**LOW.** New files only. Production `docker-compose.yml` is the same file with an additional alias var; existing prod env continues to work because `MCTACTICAL_DATA_DIR` still maps via fallback.

### Testing steps

1. Run script in a sandbox VM: `./provision.sh test-shop test.local owner@test.local`
2. Verify `/opt/test-shop/.env` has 64-char JWT key, random password, correct paths.
3. `cd /opt/test-shop && docker compose up -d` — both containers start.
4. Visit `http://localhost:<WEB_PORT>/` → login page loads.
5. Tear down: `docker compose down -v && rm -rf /opt/test-shop`.

### Rollback plan

Delete the script files. No code in the application changed.

### Can be done without touching live data?

**Yes.** The script works on **new** directories. MC Tactical at `/opt/mctactical` is never read or written by it.

---

## Task 5 — Automated backup script

### Goal

Daily SQLite backup with 7-day retention + offsite copy. Runs before any other Phase 1 change touches production.

### Files to add

| # | Path | What |
|---|------|------|
| 5.1 | `deploy/backup.sh` | `sqlite3 .backup` + tar PDFs + rsync/aws s3 cp |
| 5.2 | `deploy/restore.sh` | Documented restore procedure |
| 5.3 | `deploy/README.md` (Backup section) | Operator docs |

### Exact script outline

```bash
#!/usr/bin/env bash
set -euo pipefail

# Args: $1 = client root (e.g. /opt/mctactical)
# Env: BACKUP_REMOTE (e.g. s3://bucket/path or user@host:/path)
ROOT="${1:?client root required}"
NAME=$(basename "$ROOT")
TS=$(date -u +%Y%m%d-%H%M%S)
DEST="$ROOT/backups"
mkdir -p "$DEST"

# Hot SQLite backup (uses sqlite3 .backup, safe while DB is in use)
docker exec ${NAME}-api sqlite3 /app/data/huntex.db ".backup '/app/data/backup-${TS}.db'"
mv "$ROOT/data/backup-${TS}.db" "$DEST/db-${TS}.db"

# PDFs and branding
tar -czf "$DEST/files-${TS}.tar.gz" -C "$ROOT/data" pdfs branding 2>/dev/null || true

# Retention (keep 7 days local)
find "$DEST" -name "db-*.db" -mtime +7 -delete
find "$DEST" -name "files-*.tar.gz" -mtime +7 -delete

# Offsite if configured
if [ -n "${BACKUP_REMOTE:-}" ]; then
  rsync -avz "$DEST/db-${TS}.db" "$DEST/files-${TS}.tar.gz" "$BACKUP_REMOTE/$NAME/"
fi

echo "Backup complete: $DEST/db-${TS}.db"
```

Cron entry (added to server):

```
30 2 * * * /opt/scripts/backup.sh /opt/mctactical >> /var/log/mctactical-backup.log 2>&1
```

### Files to modify

`docker-compose.yml`: ensure `sqlite3` CLI is available inside the API container (already is — `dotnet/aspnet:8.0` doesn't ship it though). Two options:

- **Option A (preferred):** Run backup from host using `docker exec ${NAME}-api sh -c "sqlite3 ..."` — but needs `sqlite3` in container. Add to API Dockerfile: `RUN apt-get update && apt-get install -y --no-install-recommends sqlite3 && rm -rf /var/lib/apt/lists/*`.
- **Option B:** Install `sqlite3` on the host and use the bind-mounted DB file directly with file lock awareness.

Recommend **A** for cross-host portability (also works for any future client on any host).

### Risk level

**LOW.** Script runs read-only against the DB via `.backup` (which is the SQLite-recommended hot-backup mechanism). Adding `sqlite3` to the Docker image grows it by ~1MB.

### Testing steps

1. Build API image with `sqlite3` added: image size, startup unchanged.
2. Run `./backup.sh /opt/test-shop` against a test instance.
3. Verify `db-*.db` and `files-*.tar.gz` exist with correct sizes.
4. Run `./restore.sh /opt/test-shop /opt/test-shop/backups/db-<ts>.db` into a parallel instance: verify all sales/products/users present.
5. Trigger from cron once on staging, then enable on prod.

### Rollback plan

Disable cron entry, remove `sqlite3` install line from Dockerfile, redeploy. No data risk.

### Can be done without touching live data?

**Reads** live data (that's the point), but never modifies it. SQLite `.backup` is online-safe; no downtime on the source DB.

---

## Task 6 — Harden CORS and security headers

### Goal

Replace `AllowAnyOrigin()` with environment-configured allow-list. Add nginx security headers.

### Files to modify

| # | Path | What |
|---|------|------|
| 6.1 | `src/HuntexPos.Api/Program.cs` lines 111–114 | CORS reads from `App:AllowedOrigins` array; default deny |
| 6.2 | `src/HuntexPos.Api/Options/AppOptions.cs` | Add `string[] AllowedOrigins` property |
| 6.3 | `src/huntex-pos-web/nginx.conf` | Add CSP, X-Content-Type-Options, X-Frame-Options, Referrer-Policy headers |
| 6.4 | `docker-compose.yml` | Add `App__AllowedOrigins__0=${PUBLIC_BASE_URL}` |
| 6.5 | `deploy/.env.template` | Document `ALLOWED_ORIGIN` |
| 6.6 | `src/HuntexPos.Api/Program.cs` (after JWT setup) | Add ASP.NET rate limiter on `POST /api/auth/login` |

### Exact code change

**`Program.cs` CORS:**

```csharp
var allowedOrigins = builder.Configuration
    .GetSection("App:AllowedOrigins")
    .Get<string[]>() ?? Array.Empty<string>();

builder.Services.AddCors(o =>
{
    o.AddPolicy("default", p =>
    {
        if (allowedOrigins.Length == 0)
        {
            // Same-origin only (no CORS allowed). Safe default.
            p.WithOrigins("http://localhost").AllowAnyHeader().AllowAnyMethod();
        }
        else
        {
            p.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
        }
    });
});

// later:
app.UseCors("default");
```

**`Program.cs` rate limit (add before `MapControllers`):**

```csharp
builder.Services.AddRateLimiter(o =>
{
    o.AddFixedWindowLimiter("auth", w =>
    {
        w.PermitLimit = 8;
        w.Window = TimeSpan.FromMinutes(5);
        w.QueueLimit = 0;
    });
    o.RejectionStatusCode = 429;
});

// later:
app.UseRateLimiter();
```

Then on `AuthController.Login`:

```csharp
[EnableRateLimiting("auth")]
[AllowAnonymous]
[HttpPost("login")]
public async Task<ActionResult<LoginResponse>> Login(...)
```

**`nginx.conf` security headers** (add inside `server` block, before `location /`):

```nginx
add_header X-Content-Type-Options "nosniff" always;
add_header X-Frame-Options "DENY" always;
add_header Referrer-Policy "strict-origin-when-cross-origin" always;
add_header Permissions-Policy "camera=(self), microphone=(), geolocation=()" always;
add_header Strict-Transport-Security "max-age=31536000; includeSubDomains" always;
add_header Content-Security-Policy "default-src 'self'; img-src 'self' data: blob:; style-src 'self' 'unsafe-inline'; script-src 'self'; connect-src 'self'; font-src 'self' data:; manifest-src 'self'" always;
```

(Note: barcode scanner needs `camera=(self)` permission, hence not just `camera=()`.)

### Risk level

**MEDIUM.** Misconfigured CSP can break the SPA. Mitigations:
- Test CSP locally with the entire app before merging.
- Roll out with `Content-Security-Policy-Report-Only` first if uncertain.
- Rate limit on login is per-IP; unlikely to hurt legit users at 8/5min.

### Testing steps

1. Local: visit `/pos`, scan a barcode (camera), upload a logo, generate a PDF, send a test email — every browser-side feature must work without CSP violations in the console.
2. Hit `/api/products` from a malicious origin via `curl -H "Origin: https://attacker.com"` — must NOT get CORS headers in response.
3. From the legit origin, must get correct CORS headers.
4. Try `POST /api/auth/login` with wrong password 9 times in a minute — 9th must return 429.
5. Run `securityheaders.com` against staging URL — should score A or better.

### Rollback plan

If CSP breaks production: set `App__AllowedOrigins__0=*` (temporary), or comment out `add_header Content-Security-Policy` in nginx and redeploy. Site recovers in <1min. Then debug locally.

### Can be done without touching live data?

**Yes.** Pure code/config. No DB changes. Production redeploy adds env var + new nginx config.

---

## Task 7 — Fix localStorage keys and branding fallback

### Goal

Remove "huntex" / "mctactical" strings from compiled frontend bundle.

### Files to modify

| # | Path | What |
|---|------|------|
| 7.1 | `src/huntex-pos-web/src/stores/auth.ts` line 5 | `const TOKEN_KEY = 'pos_token'` |
| 7.2 | `src/huntex-pos-web/src/composables/usePrivacyMode.ts` line 3 | `const STORAGE_KEY = 'pos:privacy-mode'` |
| 7.3 | `src/huntex-pos-web/src/composables/useBranding.ts` line 38 | `const STORAGE_KEY = 'pos-branding-cache-v1'` |
| 7.4 | `src/huntex-pos-web/src/branding.ts` | Remove hardcoded `/MCTactical Light.png`; export null defaults; rely entirely on runtime branding endpoint |
| 7.5 | `src/huntex-pos-web/src/components/layout/AppShell.vue` line 4, line 23 | If `logoUrl` is null and `businessName` is empty, render generic "POS" wordmark |
| 7.6 | `src/huntex-pos-web/src/views/LoginView.vue` line 5, 41 | Same fallback — text logo if no upload |
| 7.7 | `src/huntex-pos-web/public/MCTactical Light.png` etc. | Move to `public/brand-defaults/` (keep MC Tactical assets server-side as fallback for their `BusinessSettings`) — **or** delete from public/, keep on prod server only |

### Exact code change

**`auth.ts`** add migration:

```ts
const TOKEN_KEY = 'pos_token'
const LEGACY_KEY = 'huntex_token'

// One-time migration so users don't get logged out
if (typeof localStorage !== 'undefined') {
  const legacy = localStorage.getItem(LEGACY_KEY)
  if (legacy && !localStorage.getItem(TOKEN_KEY)) {
    localStorage.setItem(TOKEN_KEY, legacy)
  }
  localStorage.removeItem(LEGACY_KEY)
}

export const useAuthStore = defineStore('auth', () => {
  const token = ref<string | null>(localStorage.getItem(TOKEN_KEY))
  // ... rest unchanged
})
```

**`usePrivacyMode.ts`** add migration:

```ts
const STORAGE_KEY = 'pos:privacy-mode'
const LEGACY_KEY = 'mctactical:privacy-mode'

const initial = (() => {
  try {
    const v = localStorage.getItem(STORAGE_KEY) ?? localStorage.getItem(LEGACY_KEY)
    if (v !== null) {
      localStorage.setItem(STORAGE_KEY, v)
      localStorage.removeItem(LEGACY_KEY)
    }
    return v === '1'
  } catch { return false }
})()
```

**`branding.ts`:**

```ts
/** Optional brand-default assets. Null = use BusinessSettings logo at runtime. */
export const logoLight: string | null = null
export const logoDark: string | null = null
```

**`LoginView.vue` line 41:**

```vue
<img v-if="logoUrl ?? logoDark"
     class="auth-panel__logo"
     :src="logoUrl ?? logoDark!"
     :alt="businessName"
     width="260" height="80" fetchpriority="high" />
<div v-else class="auth-panel__wordmark">{{ businessName || 'POS' }}</div>
```

### Risk level

**LOW.** Migration handlers preserve existing user sessions. Visual-only fallback for missing logo.

### Testing steps

1. Open app in browser with existing `huntex_token` set in localStorage → verify still logged in after refresh, `pos_token` now set, `huntex_token` removed.
2. Clear all storage → first load with no `BusinessSettings.LogoStorageKey` → wordmark shows "POS" or `BusinessName`.
3. Upload a logo → wordmark replaced with image without page reload (existing flow).
4. Run `grep -r "MCTactical\|huntex_token\|mctactical:" src/huntex-pos-web/src/` → only migration code references should remain.

### Rollback plan

Trivial revert. Migration code is one-way (legacy key removed) but harmless if reverted — old code still reads its own old key, new keys it didn't know about are ignored.

### Can be done without touching live data?

**Yes.** Frontend-only. Public/ asset move is filesystem-only, no DB.

---

## Task 8 — Onboarding wizard

### Goal

First-launch wizard for new clients: collects business name, VAT number, currency, tax rate, logo, owner password (if not set), Mailgun creds.

### Files to add

| # | Path | What |
|---|------|------|
| 8.1 | `src/huntex-pos-web/src/views/OnboardingView.vue` | Multi-step form (4 steps) |
| 8.2 | `src/huntex-pos-web/src/composables/useOnboardingState.ts` | Local-storage-backed step tracking |
| 8.3 | `src/HuntexPos.Api/Controllers/OnboardingController.cs` | `GET /api/onboarding/status` returns `{ needsSetup: bool, steps: [...] }` |

### Files to modify

| # | Path | What |
|---|------|------|
| 8.4 | `src/huntex-pos-web/src/router/index.ts` | Add route `/onboarding`; redirect logic in `beforeEach` |
| 8.5 | `src/HuntexPos.Api/Controllers/AuthController.cs` Login | If `OnboardingComplete` flag false, surface in login response |
| 8.6 | `src/HuntexPos.Api/Domain/BusinessSettings.cs` | Add `bool OnboardingComplete { get; set; } = false;` |
| 8.7 | `src/HuntexPos.Api/Data/DbSeeder.cs` `EnsureBusinessSettingsTableAsync` | ALTER TABLE add column |
| 8.8 | `src/HuntexPos.Api/Data/DbSeeder.cs` (existing-DB upgrade) | Set `OnboardingComplete = true` for any existing populated `BusinessSettings` row so MC Tactical does NOT see the wizard |

### Wizard steps

| Step | Fields | API call |
|------|--------|----------|
| 1. Business | BusinessName, LegalName, VatNumber, Currency, TaxRate, TimeZone | `PUT /api/settings/business` |
| 2. Branding | Logo upload, AccentColor, terminology overrides | `POST /api/settings/business/logo`, `PUT /api/settings/business` |
| 3. Email | Mailgun ApiKey, Domain, From, send test | `PUT /api/settings/mail`, `POST /api/settings/mail/test` |
| 4. Owner | (if owner password is the temporary one) New password | `PUT /api/admin/users/{id}/password` |

Final action: `POST /api/onboarding/complete` → sets `OnboardingComplete = true`, `_effective.Invalidate()`.

### Critical detail for MC Tactical safety

In the same migration that adds the `OnboardingComplete` column, run:

```sql
UPDATE BusinessSettings
SET OnboardingComplete = 1
WHERE BusinessName <> '' AND VatNumber <> '';
```

This ensures any existing populated row (i.e. MC Tactical's, Axionis') is auto-flagged complete and never sees the wizard. Only fresh deployments with empty settings get prompted.

### Risk level

**MEDIUM.** Wizard misfire could redirect existing users to a setup screen. Mitigation: backfill `OnboardingComplete=1` for any non-empty settings row at migration time.

### Testing steps

1. Local docker-compose with **MC Tactical's prod-style env file + populated DB** → first login goes straight to `/pos` (no wizard).
2. Local docker-compose with **clean data dir** → first login as Owner redirects to `/onboarding`. Complete all 4 steps. End at `/pos`. Refresh — never sees wizard again.
3. Verify wizard cannot be skipped via direct URL navigation (`/pos` while incomplete redirects back to `/onboarding`).
4. Verify Sales-role login while owner hasn't completed onboarding shows a "Setup pending — ask the owner to complete setup" screen, not the wizard.

### Rollback plan

If wizard misroutes valid sessions: hot-fix is `UPDATE BusinessSettings SET OnboardingComplete = 1` directly via sqlite CLI on the prod server. Zero-downtime fix.

### Can be done without touching live data?

**No** — the new column requires a migration on the prod DB. But it's additive with default `false`, then immediately backfilled to `true` for populated rows in the same startup. MC Tactical is unaffected at runtime.

---

## Task 9 — CI / typecheck cleanup

### Goal

GitHub Actions CI pipeline: build + typecheck + tests on every push. Fix existing `vue-tsc` errors (~10) so the gate is meaningful.

### Files to add

| # | Path | What |
|---|------|------|
| 9.1 | `.github/workflows/ci.yml` | Workflow: setup Node 20 + .NET 8, build both, run vue-tsc, run dotnet test |

### Files to modify

| # | Path | TS error to fix |
|---|------|-----------------|
| 9.2 | `src/huntex-pos-web/src/views/FinancialReportView.vue` line 2 | Remove unused `watch` import |
| 9.3 | Same file line 80 | Remove unused `totalCostEx` |
| 9.4 | Same file lines 194, 291 | `ctx.parsed.y ?? 0`, `ctx.parsed.x ?? 0` |
| 9.5 | `src/huntex-pos-web/src/views/PosView.vue` line 511 | Remove unused `searchEmpty` |
| 9.6 | `src/huntex-pos-web/src/views/StockListView.vue` line 241 | Add `supplierDiscountPercent: 0` to form initializer in `openEdit` |
| 9.7 | `src/huntex-pos-web/src/views/ConsignmentBatchView.vue` lines 274, 292 | Replace `toast.warning(...)` with `toast.info(...)` (or extend toast composable) |
| 9.8 | `src/huntex-pos-web/src/views/ConsignmentBatchView.vue` lines 509, 595, 634 | Replace McBadge `variant="error"` / `"info"` with `"danger"` / `"neutral"` |
| 9.9 | `src/huntex-pos-web/src/views/LabelsPrintView.vue` line 377 | Same McBadge fix |
| 9.10 | `src/huntex-pos-web/src/views/StockListView.vue` line 1097 | Same McBadge fix |
| 9.11 | `src/huntex-pos-web/src/components/layout/PwaInstallBanner.vue` line 58 | Add ambient type for `BeforeInstallPromptEvent` |

### CI workflow

```yaml
name: CI
on: [push, pull_request]
jobs:
  api:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: '8.0.x' }
      - run: dotnet build src/HuntexPos.Api/HuntexPos.Api.csproj -c Release
      - run: dotnet test tests/HuntexPos.Api.Tests/ -c Release --no-build --verbosity normal
  web:
    runs-on: ubuntu-latest
    defaults: { run: { working-directory: src/huntex-pos-web } }
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-node@v4
        with: { node-version: '20', cache: 'npm', cache-dependency-path: 'src/huntex-pos-web/package-lock.json' }
      - run: npm ci
      - run: npx vue-tsc --noEmit
      - run: npm run build
```

### Risk level

**LOW.** Pure quality gate. Doesn't touch runtime.

### Testing steps

1. Run `npx vue-tsc --noEmit` locally — must show 0 errors after fixes.
2. Run `dotnet test` — pass.
3. Push branch — CI runs green.
4. Push a deliberately broken commit — CI fails as expected.

### Rollback plan

Disable workflow file. No production impact.

### Can be done without touching live data?

**Yes.** No data, no production redeploy needed.

---

## 21-Day Schedule

This sequences the tasks for minimum risk.

| Day | Task(s) | Production touched? |
|-----|---------|--------------------|
| **1** | Task 5 (backup script) — deploy and verify on production **first** | Yes (script only, read-only) |
| **2** | Task 9 (CI workflow + TS fixes) | No |
| **3** | Task 7 (localStorage + branding fallback) | No |
| **4** | Task 1 (remove MC Tactical defaults) | No |
| **5** | Task 6 part A (CORS hardening) | No |
| **6** | Task 6 part B (nginx security headers + rate limit) | No |
| **7–8** | Task 2 (VAT extraction) | No |
| **9** | **Production cutover #1:** redeploy MC Tactical with Tasks 1+5+6+7+9 (all low-risk, no data change) | **Yes — cutover** |
| **10** | Verify MC Tactical runs cleanly for 24h | Observe only |
| **11–12** | Task 3 (currency formatting) | No |
| **13** | **Production cutover #2:** redeploy with Tasks 2+3 (additive `TaxRate` column, currency rendering) | **Yes — cutover** |
| **14–16** | Task 4 (provisioning script) + Task 8 (onboarding wizard) | No |
| **17** | **Production cutover #3:** redeploy with Tasks 4+8 (additive `OnboardingComplete` column with backfill) | **Yes — cutover** |
| **18** | Provision a test client end-to-end. Internal demo. | No |
| **19** | Buffer for fixes from cutover observations | Possibly |
| **20** | Documentation pass: `deploy/README.md`, operator runbook | No |
| **21** | Phase 1 retrospective; commit Phase 2 plan | No |

### Three production cutovers, all reversible

| Cutover | Tasks | DB change | Estimated downtime |
|---------|-------|-----------|--------------------|
| Day 9 | 1, 5, 6, 7, 9 | None | ~30s container restart |
| Day 13 | 2, 3 | +1 column `TaxRate`, default `15` | ~30s |
| Day 17 | 4, 8 | +1 column `OnboardingComplete`, auto-backfilled `1` for populated rows | ~30s |

**Each cutover is a single `git pull && docker compose up -d --build`. If anything regresses, `git checkout <prev-tag> && docker compose up -d --build` restores in <1min. Backups (Task 5) cover the impossible-but-possible DB corruption scenario.**

---

## Cross-Cutting Risk Register

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| TaxRate refactor produces wrong VAT on first invoice after cutover | LOW | HIGH | Default value 15m, migration backfill, parity unit test, manual A/B test on Day 8 with parallel container |
| Onboarding wizard catches MC Tactical | LOW | MEDIUM | Migration auto-sets `OnboardingComplete=1` for populated rows; tested Day 16 |
| CSP breaks SPA | MEDIUM | MEDIUM | Test locally; ship Report-Only first if doubt; revert is single nginx line |
| CORS allow-list misconfigured | MEDIUM | MEDIUM | Default-deny is safer than default-allow; test from prod URL on Day 9; documented env var |
| Backup script never runs / cron silent failure | MEDIUM | HIGH | Day 1 deploys script + cron + alert (e.g. email if last backup >36h old); verify Day 2, Day 10 |
| Docker image grows from sqlite3 install | CERTAIN | LOW (~1MB) | Acceptable |
| Two-week refactor distracts from MC Tactical bug requests | MEDIUM | MEDIUM | Branch isolation; hotfixes go to `main`, then rebase phase-1 branch |

---

## What NOT to do in Phase 1

To preserve scope and live safety:

- ❌ No Postgres migration (Phase 2)
- ❌ No multi-tenant DB strategy (Phase 2)
- ❌ No Stripe / billing (Phase 2)
- ❌ No payment terminal integration (out of scope)
- ❌ No partial returns workflow (Phase 2)
- ❌ No offline POS queue (Phase 2)
- ❌ No audit log table (Phase 2)
- ❌ No 2FA (Phase 2)

These are all valid follow-ups but each is multi-day and would jeopardize the 21-day window.

---

## Approval gate

Once you've reviewed this plan, the green-light signal for me to begin implementing is:

> "Start Phase 1, Task 1."

I'll work one task at a time, push to a feature branch, and stop for review at each cutover boundary (Day 9, 13, 17).
