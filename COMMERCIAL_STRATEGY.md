# MC Tactical POS → Commercial White-Label Product Strategy

*Generated from full codebase audit — April 2026*

---

## 1. Brutal Truth Assessment

### What this product currently is

A **production-grade, single-tenant retail back-office + POS system** running live for MC Tactical (pos.mctactical.co.za). It is deployed as two Docker containers (ASP.NET 8 API + Nginx/Vue SPA), backed by a single SQLite file, with JWT auth, Mailgun email, and QuestPDF invoice generation. A second deployment (Axionis) already runs from a forked repo with its own branding.

### What level it's at now

**Operational internal tool bordering on single-customer commercial product.** It handles real money, real stock, real VAT invoices daily. It is not a prototype or MVP — it processes live sales transactions.

### What makes it valuable

1. **Domain depth that takes months to build from scratch:** consignment stock movements (in/out/return/to-stock), supplier discount %, pricing rules (global/supplier/category scoped with markup/discount/rounding), promotion engine with specials per-product, special-order delivery tracking, void with automatic stock reversal inside a transaction, GP reporting that factors VAT and discounts correctly.
2. **Full document pipeline:** QuestPDF invoice/quote/consignment/label PDFs, auto-emailed to customer on sale, public-token shareable links for invoices and quotes, branded with uploaded logo.
3. **White-label foundation already exists:** `BusinessSettings` entity holds business name, legal name, VAT number, currency, timezone, colors, logo, favicon, receipt footer, quote/invoice terms, terminology overrides (rename "Quote"→"Estimate"), and feature flags (quotes on/off, discounts on/off). `useBranding.ts` on the frontend fetches this at runtime and applies CSS variables. PWA manifest is generated dynamically from business name + accent color.
4. **Role system that works for retail:** Dev > Owner > Admin > Sales, with POS rules that cap Sales staff discounts/price changes per environment variable. Vendor-scoped users see only their supplier report.
5. **Import pipeline for real supplier data:** column mapping presets, batch import from CSV/workbook with number parsing tests.
6. **Already proven across two brands:** MC Tactical + Axionis run the same core with different `branding.ts` + `AppOptions` defaults + `docker-compose.yml` env vars.

### What makes it weak

1. **VAT rate hardcoded:** `const decimal taxRate = 15m;` in `InvoiceService.cs` line 40 and `1.15m` scattered across ~15 lines in `ReportsController`, `PricingCalculator`, `QuoteService`. Cannot serve a client with different VAT without code changes.
2. **SQLite at scale:** Fine for one shop; risky above ~50k invoices for reports that do `ToListAsync` then LINQ in memory.
3. **No automated backups in the stack.**
4. **CORS is `AllowAnyOrigin()`** in `Program.cs` — acceptable for kiosk/LAN use; dangerous for internet-exposed multi-customer.
5. **No structured audit trail.** Void records `VoidedByUserId`, stock adjustments write `StockReceipt` rows, but there's no unified append-only event log.
6. **No payment terminal integration.** `PaymentMethod` is a free-text string, not a PSP flow. This is "invoice POS" not "card-terminal POS."
7. **Test coverage is narrow** — 6 focused API test files; no frontend tests, no E2E.
8. **`AppOptions` defaults contain MC Tactical specifics** (`CompanyDisplayName = "MC Tactical"`, phone, email, address, VAT number). These are compile-time defaults that leak into a fresh DB via `DbSeeder`.

### What competitors would outperform it at

- **Payment processing:** Lightspeed, Shopify POS, iKhokha have card terminal + payment gateway integrations.
- **Offline-first POS:** This is PWA with asset caching but has zero offline transaction queue.
- **Mobile-first UX:** Purpose-built tablet POS apps (e.g., Yoco) have simpler, touch-optimized flows.
- **Scale and multi-store:** Any cloud SaaS with Postgres + tenant isolation.

### What it beats many competitors at

- **Consignment stock:** Most POS systems treat all stock as owned. This one tracks consignment in/out/return per supplier, reports on it, generates consignment batch PDFs, and has vendor-scoped user accounts. This is a rare and genuinely useful differentiator for specialty retail.
- **Supplier pricing rules:** Granular markup/rounding/min-margin per supplier or category is uncommon in sub-R5000/mo POS products.
- **White-label readiness:** BusinessSettings + branding endpoint + dynamic PWA manifest + feature flags already exist. Competitors like iKhokha or Yoco don't offer white-label at all.
- **South African specifics:** ZAR, VAT 15%, address format, Mailgun, timezone — all baked in and tested in production.
- **Total cost of ownership:** Docker + SQLite on a R200/mo VPS versus R1500-R3000/mo for Lightspeed/Vend.

---

## 2. Fastest Path to Revenue

### The three options

| Option | Time to first customer | Ops cost per customer | Engineering to build |
|--------|------------------------|----------------------|---------------------|
| **A: One hosted instance per customer** | 2–4 weeks | ~R200/mo VPS + 1hr ops | Minimal — already works (Axionis proves it) |
| **B: True multi-tenant SaaS** | 3–6 months | Lower marginal | Massive — new DB strategy, tenant middleware, billing, onboarding portal |
| **C: Hybrid** — hosted single-instance now, prepare migration path | Same as A now, B later | A costs now, B costs later | Small upfront, large later |

### Recommendation: **Option C (Hybrid), executed as Option A first**

**Why:** You already have proof of concept. Axionis is literally Option A running in production from a separate repo + compose file. The gap between "MC Tactical + Axionis" and "10 paying customers" is operational, not architectural.

**The plan:**
1. **Weeks 1–4:** Productize the single-instance deployment with a provisioning script and configuration-driven branding (no more forking repos).
2. **Months 2–6:** Sell 3–10 customers on single-instance hosting.
3. **Month 6+:** If demand warrants, migrate to shared Postgres with tenant isolation.

**Revenue starts in month 1, not month 6.**

---

## 3. White-Label Readiness Audit

### Already white-label ready (code exists, works)

| Dimension | Implementation | Code location |
|-----------|---------------|---------------|
| **Business name** | `BusinessSettings.BusinessName` → PDF headers, SPA topbar, PWA manifest | `Domain/BusinessSettings.cs`, `BusinessSettingsController.cs`, `useBranding.ts`, `AppShell.vue` |
| **Logo / favicon** | Upload endpoint, stored in `{DATA}/branding/`, served via `/api/settings/branding/logo` | `BusinessSettingsController.cs` lines 87–113 |
| **Colors** | Primary / secondary / accent saved to DB, applied as CSS vars at runtime | `BusinessSettingsController.cs`, `useBranding.ts` `applyCssVariables()` |
| **Company info** | Legal name, VAT number, email, phone, address, website — all on `BusinessSettings` | `BusinessSettingsController.cs` PUT |
| **Invoice terms / footer** | `ReceiptFooter`, `QuoteTerms`, `InvoiceTerms`, `ReturnPolicy` on `BusinessSettings` | Same controller, rendered in PDF services |
| **Email settings** | Per-deployment Mailgun (API key, domain, from address) stored in `MailSettings` table, configurable from UI | `SettingsController.cs`, `MailgunEmailSender.cs` |
| **User roles** | 4 roles, enforced via `[Authorize(Roles=...)]` on every controller | `Roles.cs`, all controllers |
| **Pricing rules** | Global / supplier / category scoped, with markup %, max discount, round-to-nearest, min margin | `PricingRulesController.cs`, `PricingService.cs`, `PricingRule` entity |
| **Modules on/off** | `EnableQuotes`, `EnableDiscounts`, `EnableBrandPricingRules` flags | `BusinessSettings.cs` lines 40–42, exposed via branding endpoint |
| **Terminology** | `QuoteLabel`, `InvoiceLabel`, `CustomerLabel` overrides | `BusinessSettings.cs` lines 36–38, consumed by `useBranding.ts` |
| **PWA branding** | Dynamic manifest from `/api/settings/branding/manifest.webmanifest` | `BusinessSettingsController.cs` line 115 |

### Needs work to be fully white-label

| Dimension | Current state | What to fix | Effort |
|-----------|--------------|-------------|--------|
| **Tax rate** | Hardcoded `15m` in `InvoiceService.cs:40`, `QuoteService.cs:40`, and `1.15m` scattered in 15+ lines | Add `TaxRate` to `BusinessSettings`, pass it through `InvoiceService.CreateAsync`, `QuoteService`, all report calculations | **Medium** (2–3 days, high-touch because of scattered `1.15m`) |
| **Currency formatting** | `BusinessSettings.Currency` exists but frontend uses `R` / `formatZAR()` in many views | Change `formatZAR()` to read currency from branding state | **Small** (1 day) |
| **Fallback logo** | `branding.ts` hardcodes `'/MCTactical Light.png'` | If `brandingLogoUrl` is null, show `BusinessSettings.BusinessName` text or a generic icon | **Small** (hours) |
| **Swagger title** | `Program.cs:73` hardcodes `"MC Tactical POS API"` | Pull from `IEffectiveBusinessSettings` or generic | **Trivial** |
| **localStorage key** | `usePrivacyMode.ts` uses `'mctactical:privacy-mode'`, `auth.ts` uses `'huntex_token'` | Change to generic prefix | **Trivial** |
| **AppOptions defaults** | `CompanyDisplayName = "MC Tactical"`, phone, email, address, VAT — these seed into DB for fresh deploys | Make defaults empty/generic; each deployment gets populated via Setup wizard or env vars | **Small** |
| **Invoice number prefix** | `"INV-{date}-{seq}"` — works generically | Add optional prefix to `BusinessSettings` if a client wants custom prefixes | **Later** |
| **Compose container names** | `mctactical-pos-api`, `mctactical-pos-web` | Parameterize in provisioning script | **Trivial** |

---

## 4. What to Build Next (Priority Order)

| # | Task | Category | Effort | Impact |
|---|------|----------|--------|--------|
| 1 | **Extract tax rate from hardcode to `BusinessSettings.TaxRate`** | REQUIRED | 2–3 days | Unlocks non-15% VAT clients and international |
| 2 | **Provisioning script:** Docker Compose template + `.env` generator for new customer deployments | HIGH VALUE | 1–2 days | Turns "spin up a new client" from manual ops into a repeatable command |
| 3 | **Harden CORS:** Replace `AllowAnyOrigin` with env-configured origin(s) | REQUIRED | 2 hours | Security — any client's API is currently open to cross-origin requests |
| 4 | **Neutralize `AppOptions` defaults:** Remove MC Tactical–specific values from compiled defaults | FAST WIN | 2 hours | Every new deploy currently seeds "MC Tactical" into a fresh DB |
| 5 | **Generic branding fallback:** Replace `branding.ts` hardcoded logo paths with runtime-only resolution | FAST WIN | 1 hour | Eliminates MC Tactical assets from the generic product binary |
| 6 | **Neutralize localStorage keys:** Change `huntex_token` → `pos_token`, `mctactical:privacy-mode` → `pos:privacy-mode` | FAST WIN | 30 min | Cosmetic but professional for a white-label product |
| 7 | **Currency display composable:** Replace scattered `formatZAR()` with `useCurrency()` reading from branding state | HIGH VALUE | 1 day | Required for any non-ZAR market |
| 8 | **Automated backup script:** cron → `sqlite3 .backup` + rsync to offsite | REQUIRED | 3 hours | Without this, one disk failure = total customer data loss |
| 9 | **`vue-tsc` clean + CI gate:** Fix ~10 type errors, add to GitHub Actions | FAST WIN | 4 hours | Quality signal; prevents regressions shipping to paying clients |
| 10 | **Client onboarding wizard:** First-launch page that collects business name, logo, VAT number, email settings, owner password | HIGH VALUE | 2–3 days | Eliminates the need for manual DB/env setup per client |
| 11 | **Security headers in nginx:** CSP, `X-Content-Type-Options`, `X-Frame-Options` | FAST WIN | 1 hour | Standard hardening for commercial software |
| 12 | **Rate limiting on `/api/auth/login`** | REQUIRED | 2 hours | Brute-force protection for client systems on the internet |
| 13 | **Refactor `ReportsView.vue`** into sub-components/lazy-loaded tabs | HIGH VALUE | 2–3 days | Largest and most fragile frontend file; hurts maintainability |
| 14 | **DB-side report aggregation:** Replace `ToListAsync` → LINQ-in-memory patterns in `ReportsController.Daily` and `ReportsController.Stock` | HIGH VALUE | 2 days | Performance ceiling for clients with 10k+ invoices |
| 15 | **E2E smoke test:** Playwright: login → sell → PDF → void | HIGH VALUE | 2 days | Regression safety net before shipping to paying customers |
| 16 | **Audit log table:** Append-only log for void, price change, stock adjust, user create/delete | LATER | 3–4 days | Professional requirement; not blocking first sale |
| 17 | **2FA for Owner/Admin** | LATER | 2–3 days | Expected by security-conscious clients |
| 18 | **Partial returns / credit notes** | LATER | 4–5 days | Current void is all-or-nothing; retail wants line-level returns |
| 19 | **Postgres migration path** | LATER | 3–5 days | For clients outgrowing SQLite or for future shared-DB multi-tenant |
| 20 | **Subscription billing (Stripe/PayFast)** | LATER | 5+ days | Only needed once you have >5 clients and want automated collection |

---

## 5. Zero-Risk MC Tactical Strategy

### Current situation

- MC Tactical runs from `github.com/vassago85/mctactical.git`, branch `main`, deployed at `/opt/mctactical` on `cha021-truserv1230-jhb1-001`.
- Axionis runs from `github.com/vassago85/axionis.git`, a forked repo with brand-specific changes.
- Both are currently maintained by manually syncing changes.

### Recommended approach: **Config-driven white-label from a single repo**

**Do NOT fork per customer.** The Axionis fork already creates a sync burden — every feature needs to be ported. With 5+ customers this becomes untenable.

#### Architecture

```
Single repo: github.com/vassago85/posproduct.git (or keep mctactical.git)
│
├── src/PosProduct.Api/           (renamed from HuntexPos.Api)
│   ├── appsettings.json          (generic defaults, no MC Tactical specifics)
│   └── Dockerfile
│
├── src/pos-web/                  (renamed from huntex-pos-web)
│   ├── src/branding.ts           (fallback to /api/settings/branding only)
│   └── Dockerfile
│
├── docker-compose.yml            (parameterized via .env)
├── deploy/
│   ├── provision.sh              (creates .env, data dirs, first owner)
│   └── backup.sh
│
└── deployments/                  (gitignored, or separate ops repo)
    ├── mctactical.env
    ├── axionis.env
    └── newclient.env
```

Each customer gets:
- Their own **server or VPS** (or namespace on a shared box)
- Their own **`.env`** file (JWT key, owner email, Mailgun creds, public URL)
- Their own **`data/` directory** (SQLite DB, PDFs, branding assets)
- Same Docker images, pulled from same registry or built from same commit

MC Tactical keeps running exactly as today. The only change is that `AppOptions` defaults become generic, and MC Tactical's specific values come from env vars (which they already do via `docker-compose.yml`).

#### Migration path (zero downtime for MC Tactical)

1. Neutralize compiled defaults in `AppOptions` (empty strings / generic)
2. Verify MC Tactical `.env` already supplies all values (it does — checked in `docker-compose.yml`)
3. MC Tactical redeploy: `git pull && docker compose up -d --build` — zero functional change because env vars override defaults
4. Axionis: point at same repo, different `.env` — eliminate the fork
5. New clients: `provision.sh customer-name domain.com owner@email.com` → generates `.env` + data dir + compose override

#### Database strategy

- **Keep SQLite for single-instance deploys.** It's zero-ops, proven, and sufficient for shops doing <500 sales/day.
- **Add Postgres support later** (EF Core makes this a connection string + provider swap) for high-volume clients or the eventual shared multi-tenant tier.

---

## 6. Commercial Product Packages (South African Market)

### Pricing research context

South African POS market (2026): iKhokha charges ~R99–R449/mo. Lightspeed is R1500+/mo. Yoco is free-with-hardware. Custom-hosted solutions for specialty retail are underserved.

### Suggested tiers

| | **Starter** | **Growth** | **Enterprise** |
|---|---|---|---|
| **Monthly** | R799/mo | R1,499/mo | R2,999/mo |
| **Setup fee** | R2,500 once-off | R5,000 once-off | R10,000 once-off |
| **Users** | 3 | 10 | Unlimited |
| **Products** | 2,000 | 10,000 | Unlimited |
| **Modules** | POS, Stock, Invoices, Labels | + Quotes, Consignment, Reports, Imports, Financial Dashboard | + Vendor Portal, Custom Pricing Rules, Priority Support |
| **Branding** | Their logo + colors | + Custom domain + PWA | + Full white-label (their name on everything) |
| **Email** | Shared Mailgun | Own Mailgun domain | Own domain + custom templates |
| **Support** | Email (48hr) | Email + WhatsApp (24hr) | Dedicated (4hr) |
| **Backups** | Daily (7-day retention) | Daily (30-day) | Daily + on-demand |
| **Hosting** | Shared VPS | Dedicated VPS | Dedicated + failover |

### Value proposition over alternatives

- vs. **Yoco/iKhokha:** "Those are payment terminals with basic POS. We're a full back-office: consignment, supplier pricing, quotes, customer invoices, financial reporting."
- vs. **Lightspeed/Vend:** "Same features for specialty retail at 1/3 the price, with South African support and ZAR-first."
- vs. **Spreadsheets:** "Stop losing money to Excel stock counts and handwritten invoices."

---

## 7. Industries to Sell to First

Ranked by feature fit with what's already built:

| Rank | Industry | Why it fits | Key selling features |
|------|----------|-------------|---------------------|
| **1** | Tactical / firearm retail | Literally built for this. MC Tactical is the reference customer. | Consignment from importers, pricing rules per supplier, VAT invoicing, barcode scanning |
| **2** | Hunting / outdoor gear shops | Same supplier ecosystem (consignment from brands like Leupold, Bushnell), similar stock management | Same as above + multi-supplier import |
| **3** | Fishing tackle stores | High SKU count, consignment from distributors, need for barcode labels | Label printing, consignment, stock batches |
| **4** | Airsoft / paintball retailers | Niche specialty retail, consignment models, similar demographics to tactical | Consignment, promotions/specials, customer invoicing |
| **5** | Auto parts stores | High SKU count, supplier pricing tiers, need cost/sell tracking | Pricing rules, supplier discount %, import from supplier CSV |
| **6** | Specialty food / deli / butchery | VAT invoicing, supplier consignment (fresh goods on consignment), walk-in POS | POS + consignment + invoicing |
| **7** | Vape / tobacco shops | Regulated inventory tracking, consignment from distributors | Stock movement audit trail, consignment batches |
| **8** | Pet shops | Mix of own stock + consignment pet food, need supplier reporting | Consignment + vendor report |
| **9** | Hardware / tools (small independent) | Supplier pricing tiers, high SKU, import from wholesaler lists | Import pipeline, pricing rules, stocktake |
| **10** | Art / craft supply stores | Consignment from local artists, need quote system for custom orders | Consignment + quotes + customer CRM |

**Go-to-market:** Start with **tactical + hunting + outdoor** (you already know the industry, have the reference customer, understand the supplier ecosystem). Expand to adjacent specialty retail from there.

---

## 8. Immediate UI Upgrades (No Backend Changes)

These increase perceived value without touching the API:

| # | Upgrade | Effort | Impact |
|---|---------|--------|--------|
| 1 | **Login page branding:** Show uploaded logo + business name (currently shows hardcoded MC Tactical logo) | 2 hours | First impression for every user |
| 2 | **Dashboard as landing page option:** Add a home/dashboard route showing today's sales count, revenue, top products — before user goes to POS | 1–2 days | Owners see value immediately on login |
| 3 | **Empty states for all lists:** "No products yet — import your first catalogue" with action buttons | 3–4 hours | New client experience is currently blank tables |
| 4 | **Keyboard shortcut hints on POS:** Show F1=Scanner, F2=Complete, etc. in a subtle bar | 2 hours | Power user perception |
| 5 | **Print-optimized invoice view:** The public invoice page should have a clean print stylesheet | 3 hours | Clients printing invoices for walk-in customers |
| 6 | **Toast messages polish:** Success messages should confirm action ("Invoice #INV-20260428-0003 created") not just "Success" | 1–2 hours | Professional feel |
| 7 | **Settings page restructure:** Break into tabs (Business, Pricing, Email, Team) instead of separate routes | 1 day | Reduces confusion about where settings live |
| 8 | **Loading skeletons on all views:** Several views show blank then pop content. Use `McSkeleton` component (already exists) everywhere | 3–4 hours | Perceived performance |

---

## 9. Technical Debt Risks

Things that would **scare a paying customer** if discovered:

| Risk | Severity | Details | Location |
|------|----------|---------|----------|
| **CORS `AllowAnyOrigin()`** | HIGH | Any website can make authenticated API calls if the user's token is leaked | `Program.cs` line 112 |
| **Default JWT key in local compose** | HIGH | `docker-compose.local.yml` line 19 contains a real-looking key in plain text | `docker-compose.local.yml:19` |
| **No backup automation** | HIGH | Single SQLite file; disk failure = total data loss | Absent from codebase |
| **VAT rate hardcoded** | MEDIUM | Cannot onboard a non-15% client; wrong GP calculations if rate changes | `InvoiceService.cs:40` + 15 scattered `1.15m` |
| **Reports load entire invoice table into memory** | MEDIUM | `await _db.Invoices.AsNoTracking().Include(i => i.Lines).ToListAsync(ct)` in `ReportsController.Daily` | `ReportsController.cs:79-81` |
| **`DbSeeder` uses try/catch ALTER TABLE** | LOW | Every startup attempts ~30 ALTER TABLE statements that silently fail. Works but noisy and fragile. | `DbSeeder.cs` (entire file) |
| **No HTTPS enforcement in API** | MEDIUM | TLS terminated at proxy; if proxy misconfigured, API accepts plain HTTP | `Program.cs` — no `UseHttpsRedirection()` |
| **Swagger enabled only in Development** | OK | But if accidentally deployed as Development, full API docs are public | `Program.cs:127-130` |
| **Owner password in local compose** | HIGH | `PaulCharsley2026!` visible in `docker-compose.local.yml:23` | Should be in `.env` / secrets manager |

---

## 10. 90-Day Action Plan

### Phase 1: Productize (Days 1–21)

**Goal:** Ship to first external client without breaking MC Tactical.

| Day | Action | Owner |
|-----|--------|-------|
| 1–2 | Neutralize `AppOptions` defaults (empty company info), fix CORS, fix localStorage keys, fix `branding.ts` fallback | Dev |
| 3–4 | Extract VAT rate to `BusinessSettings.TaxRate`, replace all hardcoded `15m` / `1.15m` with config value | Dev |
| 5 | Currency formatting composable (`useCurrency()` from branding state) | Dev |
| 6–7 | Provisioning script: `provision.sh <name> <domain> <email>` → generates `.env`, creates data dirs | Dev/Ops |
| 8 | Backup script: `backup.sh` + cron + offsite copy | Ops |
| 9 | Redeploy MC Tactical from generic repo (verify zero change) | Ops |
| 10–11 | Migrate Axionis from forked repo → same repo with its own `.env` | Dev |
| 12–14 | Client onboarding wizard (first-launch setup page) | Dev |
| 15–16 | `vue-tsc` clean + GitHub Actions CI (build + typecheck + tests) | Dev |
| 17–18 | Login page branding + empty states for new client experience | Dev |
| 19–20 | Rate limiting on auth + security headers in nginx | Dev |
| 21 | Internal test: provision a test client, complete full workflow (import → POS sale → invoice → void → reports) | QA |

### Phase 2: First Sale (Days 22–45)

**Goal:** Close first paying customer.

| Day | Action |
|-----|--------|
| 22–23 | Create product landing page (adapt Axionis marketing page — it already exists) |
| 24–25 | Create demo instance with sample data (tactical shop, 50 products, 100 invoices) |
| 26–30 | Reach out to 10 shops in the tactical/hunting/outdoor space; offer **3-month pilot at R499/mo** with free setup |
| 31–35 | Onboard first pilot client: provision, import their catalogue, train over WhatsApp video call |
| 36–45 | Support first client through their first 2 weeks of daily use; fix issues same-day |

### Phase 3: Stabilize + Scale (Days 46–90)

**Goal:** 3 paying clients, reliable ops.

| Day | Action |
|-----|--------|
| 46–50 | E2E Playwright test suite covering sale, void, quote, import |
| 51–55 | Refactor `ReportsView.vue` into maintainable sub-components |
| 56–60 | DB-side report aggregation for heavy endpoints |
| 61–65 | Audit log MVP (void, price change, stock adjust, user admin) |
| 66–70 | Onboard clients 2 and 3 from pilot outreach |
| 71–80 | Iterate on UX feedback from live clients |
| 81–85 | Add monitoring (uptime check, disk space alert, log shipping) |
| 86–90 | Evaluate: continue single-instance or begin Postgres multi-tenant work based on demand |

### Revenue projection (conservative)

| Month | Clients | Monthly revenue | Cumulative setup fees |
|-------|---------|----------------|-----------------------|
| 1 | 1 pilot | R499 | R0 (free setup for pilot) |
| 2 | 2 | R998 | R2,500 |
| 3 | 3 | R2,397 | R5,000 |
| 6 | 6 | R5,994 | R15,000 |
| 12 | 12 | R11,988 | R35,000 |

This is conservative. The real value inflection happens when you can charge Growth tier (R1,499/mo) to clients who need consignment + reports + quotes, which is most specialty retailers.

---

## Summary

This is not a rewrite project. This is a **packaging project**. The product exists, runs in production, handles real money. The gap to commercial is:

1. **Remove MC Tactical specifics from compiled defaults** (2 hours)
2. **Extract hardcoded VAT rate** (2–3 days)
3. **Write a provisioning script** (1–2 days)
4. **Harden security basics** (1 day)
5. **Add backup automation** (3 hours)
6. **Build an onboarding wizard** (2–3 days)

That's ~2 weeks of focused work to have a deployable product. The rest is sales and iteration.
