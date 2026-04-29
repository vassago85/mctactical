# Phase 3 — Executive Dashboard + Customer Accounts

> **Status:** spec only — DO NOT BUILD until Phase 2 (thermal printing) is shipped, deployed and stable on MC Tactical for at least 7 days.
>
> **Scope:** applies to **both** MC Tactical (live) and Axionis (white-label).
>
> **Source:** ChatGPT mockup discussed 2026-04-28 — Executive Dashboard layout (panel 3) + Account tender + Trade/Wholesale credit accounts.

---

## 0. Executive summary

Phase 3 covers two parallel tracks. They can ship independently.

| Track | What | Risk | Effort | Touches sales flow? |
|---|---|---|---|---|
| **3A — Executive Dashboard** | Single-page exec overview matching the mockup | **LOW** | ~2 days | No (read-only) |
| **3B — Customer Accounts (AR)** | Real credit accounts: Account tender, balances, payments, statements, aging | **HIGH** | ~7–8 days | Yes (invoice creation path) |

Recommended order: **3A first** (low risk, immediate visible value), **3B second** (substantial domain change, feature-flagged, staged rollout).

---

## 1. What we already have

Reports infrastructure is already strong — Phase 3A is mostly UI work, not new data.

### Backend (`ReportsController`)
- ✅ `GET /api/reports/invoices` — invoice list with date filter
- ✅ `GET /api/reports/daily` — daily revenue + GP
- ✅ `GET /api/reports/payments` — payment method breakdown (Card/Cash/EFT)
- ✅ `GET /api/reports/stock` — on-hand, consignment by supplier, sold/received in period
- ✅ `GET /api/reports/consignment` — supplier-level
- ✅ `GET /api/reports/invoices/export` — CSV
- ✅ Payment method canonicalisation (legacy "Bank" → EFT)

### Frontend
- ✅ `FinancialReportView.vue` — daily revenue + GP charts
- ✅ `ReportsView.vue` — invoice list, stock, consignment, vendor reports
- ✅ Chart.js wired up
- ✅ Date range filter pattern established

### Domain model
- ✅ `Invoice` with `PaymentMethod` (string), `GrandTotal`, `Lines`, `Status`
- ✅ `Customer` with email, name, phone, company, VAT, type
- ✅ Invoice has denormalised customer fields (Name/Email/Company/VAT/Address)
- ❌ No `CustomerId` foreign key on Invoice (only denormalised copy)
- ❌ No payment ledger
- ❌ No account/credit fields on Customer

---

## 2. Phase 3A — Executive Dashboard

### Mockup mapping → reality

| Mockup element | Status | Source |
|---|---|---|
| KPI: Today Sales | ✅ have | `/reports/daily` (filter to today) |
| KPI: Month Sales | ✅ have | `/reports/daily` (filter to MTD) |
| KPI: Gross Profit | ✅ have | `/reports/daily` |
| KPI: Gross Profit % | ✅ derive | revenue ÷ GP |
| KPI: Avg Basket | ❌ new | total ÷ invoice count |
| KPI: Items Sold | ❌ new | sum of `InvoiceLine.Quantity` |
| KPI: Invoices | ✅ have | `/reports/daily` |
| KPI: Low Stock | ❌ new | count `Products` below `ReorderLevel` |
| Sales Trend (line) | ✅ have | `/reports/daily` |
| **Sales Trend "vs Previous period" overlay** | ❌ new | needs prior-period series |
| Payment Methods (donut) | ✅ have | `/reports/payments` |
| Top Categories (bar) | ❌ new | depends on Product.Category |
| Top Products (table) | ✅ derive | from `/reports/stock` SoldInPeriod |
| Low Stock Alerts (table) | ❌ new | products below reorder threshold |
| Recent Activity (feed) | ❌ new | unified stream |
| Date range picker | ✅ pattern exists | re-use |

### 3A.1 — Backend: single overview endpoint

**New:** `GET /api/dashboard/overview?from=&to=`

```jsonc
{
  "kpis": {
    "todaySales": 12450.00,
    "monthSales": 245760.00,
    "grossProfit": 56812.00,
    "grossProfitPct": 23.1,
    "avgBasket": 642.50,
    "itemsSold": 294,
    "invoiceCount": 182,
    "lowStockCount": 17,
    // deltas vs previous period for the colored trend pills
    "deltas": { "todaySales": 12.6, "monthSales": 19.7, "grossProfit": 15.3, ... }
  },
  "salesTrend": {
    "current":  [{ "date": "2026-04-21", "total": 8420 }, ...],
    "previous": [{ "date": "2026-03-22", "total": 7650 }, ...]   // same length, offset
  },
  "paymentMethods": [
    { "method": "Card", "count": 96, "total": 132600, "pct": 60 },
    ...
  ],
  "topCategories": [
    { "category": "Scopes",         "qty": 88, "revenue": 312400 },
    ...
  ],
  "topProducts": [
    { "sku": "VX-OB4-VS", "name": "Vortex Diamondback 4-16x44", "qty": 8, "revenue": 95992 },
    ...
  ],
  "lowStockAlerts": [
    { "sku": "HOR-NF-ELD", "name": "Hornady 143gr ELD", "qty": -2, "reorderLevel": 10 },
    ...
  ],
  "recentActivity": [
    { "ts": "2026-04-28T13:42Z", "type": "sale",   "actor": "John Smith", "summary": "Sale completed — R6 600.00 to John Vortex Optics", "link": "/reports/INV-1003" },
    { "ts": "2026-04-28T13:21Z", "type": "quote",  "actor": "Mark",       "summary": "Quote created — Q-1001 for Tian Tactical" },
    { "ts": "2026-04-28T12:58Z", "type": "restock","actor": "John Smith", "summary": "Sale completed — R2 990.00 by Mark" },
    ...
  ]
}
```

Implementation notes:
- **No DB changes.** Pure aggregation over existing tables.
- Reuse existing pattern from `ReportsController.Daily` (load → filter in memory because SQLite can't translate `DateTimeOffset`).
- "Previous period" = same length window immediately before `from`.
- "Recent Activity" = union of:
  - Final invoices (sale, void)
  - Quotes (created, converted)
  - StockReceipts (in/out/move)
  - StocktakeSessions (closed)
  - CustomerPayments (added in 3B)
  - Cashups (added if Cashup module ships)

### 3A.2 — Frontend: `ExecutiveDashboardView.vue`

New route `/dashboard` (or replaces existing Dashboard view, depending on what's there today — to verify).

Layout:
```
┌─────────────────────────────────────────────────────────────────────────┐
│ Dashboard                              [Date range ▾] [Filters ▾]       │
├─────────────────────────────────────────────────────────────────────────┤
│  Today    Month    Gross    GP %     Avg     Items    Inv    Low Stock  │
│  Sales    Sales    Profit            Basket  Sold                       │
│  ▲12.6%   ▲19.7%   ▲15.3%   ▲2.5%    ▲4.3%   ▲5.1%   ▲5.5%   needs attn │
├─────────────────────────────────────────────────┬───────────────────────┤
│  Sales Trend                                    │ Payment   Top         │
│  ▬ This period   ▬ Previous period              │ Methods   Categories  │
│  [line chart]                                   │ [donut]   [bar]       │
├─────────────────────────────────────────────────┴───────────────────────┤
│  Top Products       │ Low Stock Alerts     │ Recent Activity            │
│  [table]            │ [table]              │ [feed]                     │
└─────────────────────────────────────────────────────────────────────────┘
```

Components reused: `McMetricCard`, `McCard`, existing chart wrappers, table primitives.

### 3A.3 — Migration / rollout

- **Zero DB migration.**
- Ship behind `BusinessSettings.NewDashboardEnabled = true` for 1 week, then make default.
- Old `FinancialReportView` and `ReportsView` stay untouched.
- Privacy-mode aware (existing `usePrivacyMode` composable already redacts amounts).

### 3A.4 — Testing

- Unit: aggregation correctness on a fixture invoice set.
- E2E: load page in mobile + desktop, all widgets render with empty state, with data, with privacy mode on.
- Comparison sanity: KPIs on overview must match values on existing FinancialReportView for the same range.

### 3A.5 — Risk

| Risk | Severity | Mitigation |
|---|---|---|
| Slow on large date ranges (full table scan) | Medium | Cap range to 1 year; index `Invoices.CreatedAt` |
| Recent Activity feed expensive | Low | Hard limit 50 items, ORDER BY ts DESC |
| Top Categories needs Product.Category | Low | Verify field exists; if not, derive from Supplier or skip widget v1 |

---

## 3. Phase 3B — Customer Accounts (Accounts Receivable)

This is a **real AR module**, not just a button. Trade and wholesale customers can charge sales to their account, owners can record payments against the balance, and the system produces statements and aged receivables reports.

### 3B.0 — Vocabulary

| Term | Meaning |
|---|---|
| **Account customer** | Customer flagged `AccountEnabled = true` |
| **Credit limit** | Max outstanding the customer can owe |
| **Current balance** | Unpaid invoices on account − payments not yet applied |
| **Available credit** | `CreditLimit − CurrentBalance` |
| **Account tender** | Sale paid via "Account" payment method (no money received at till) |
| **Statement** | Periodic PDF of account activity |
| **Aging** | Outstanding invoices bucketed by age (Current / 30 / 60 / 90 / 120+) |

### 3B.1 — Domain model changes (additive, safe defaults)

**`Customer` — add fields:**

```csharp
public bool AccountEnabled { get; set; }    // default false
public decimal CreditLimit  { get; set; }   // default 0
public string? PaymentTerms { get; set; }   // free text e.g. "30 days", default null
```

**`Invoice` — add fields:**

```csharp
public Guid? CustomerId { get; set; }                   // FK, nullable for legacy/walk-in
public decimal AmountPaid { get; set; }                 // existing invoices: backfill = GrandTotal
public InvoicePaymentStatus PaymentStatus { get; set; } // existing invoices: backfill = Paid
public DateTimeOffset? DueDate { get; set; }            // nullable; set when on account
```

**New enum:**

```csharp
public enum InvoicePaymentStatus { Paid = 0, PartiallyPaid = 1, Unpaid = 2 }
```

**New entity `CustomerPayment`:**

```csharp
public class CustomerPayment {
  public Guid Id { get; set; }
  public Guid CustomerId { get; set; }
  public DateTimeOffset PaidAt { get; set; }
  public decimal Amount { get; set; }
  public string Method { get; set; } = "Cash";   // Card | Cash | EFT | Other
  public string? Reference { get; set; }
  public string? Notes { get; set; }
  public Guid? AppliedToInvoiceId { get; set; }  // optional — payment can sit unallocated
  public string? CreatedByUserId { get; set; }
  public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
```

**Migration safety:**
- `Invoice.AmountPaid` default = `GrandTotal` for ALL existing rows (one-shot SQL update during migration).
- `Invoice.PaymentStatus` default = `Paid` for ALL existing rows.
- `Customer.AccountEnabled` default = `false` → no customer is "on account" until owner explicitly enables it. **This means migration is functionally a no-op until someone flips a flag.**

### 3B.2 — API additions

**Customer account endpoints:**

```
GET  /api/customers/{id}/account
  → {
      balance: { totalOutstanding, oldestUnpaidDate, creditLimit, available },
      openInvoices: [{ id, number, date, total, paid, outstanding, ageDays }],
      aging: { current, days30, days60, days90, days120Plus },
      recentPayments: [{ id, paidAt, amount, method, reference, appliedToInvoice }]
    }

POST /api/customers/{id}/payments
  body { amount, method, reference?, notes?, applyToInvoiceIds?: [] }
  → returns updated balance + payment record

GET  /api/customers/{id}/statement.pdf?from=&to=
  → PDF stream (QuestPDF, branded via BusinessSettings)

GET  /api/customers/{id}/payments
  → paginated payment history
```

**Reports:**

```
GET /api/reports/aging
  → {
      asOf,
      summary: { current, days30, days60, days90, days120Plus, total },
      customers: [{ customerId, name, total, current, days30, days60, days90, days120Plus }]
    }
```

**Sale flow change:** when `Invoice.PaymentMethod == "Account"`:
- **Require** `CustomerId`
- **Validate** `customer.AccountEnabled == true` → 422 if not
- **Validate** `customer.CurrentBalance + invoice.GrandTotal <= customer.CreditLimit` (configurable: hard-block vs soft-warn — see open questions)
- Set `Invoice.AmountPaid = 0`, `PaymentStatus = Unpaid`, `DueDate = CreatedAt + customer.PaymentTerms` (parsed) or null

### 3B.3 — UI additions

**POS view:**
- Add 5th tender button: **Account** (icon: `BookOpen` or `User`)
- Disabled (greyed) unless: a customer is selected, customer has `AccountEnabled = true`, and current balance + cart total ≤ credit limit (or warn-only mode)
- When customer selected, show small balance pill in customer panel: "Balance: R 4 200 · Available: R 5 800"

**Customer detail page (new — `/customers/:id`):**

```
┌──────────────────────────────────────────────────────────┐
│ Tian Tactical (Pty) Ltd            [Edit] [Take payment] │
│ Account ON · Limit R10 000 · Balance R4 200 · Avail R5 800│
├──────────────────────────────────────────────────────────┤
│ [Statement] [Open invoices] [Payments] [Profile]         │
├──────────────────────────────────────────────────────────┤
│ ...tab content...                                        │
└──────────────────────────────────────────────────────────┘
```

**Take Payment modal:**
- Amount, method (Card/Cash/EFT), reference, notes
- Optional: select which invoices to apply to (default: oldest first)
- Confirm button writes a `CustomerPayment` and updates affected invoice `AmountPaid` + `PaymentStatus`

**New finance pages (sidebar matches mockup):**
- **Receivables** (`/finance/aging`) — aging report, top debtors
- **Payments** (`/finance/payments`) — list of all customer payments with date/method/customer/reference filter

**Statement PDF (QuestPDF):**
- Header: business branding (existing pattern)
- Customer block
- Period
- Opening balance
- Transactions table (date, type, ref, debit, credit, running balance)
- Aging summary
- Closing balance + payment instructions

### 3B.4 — Implementation stages (de-risked rollout)

| Stage | What ships | Behavioural change | Can ship without next |
|---|---|---|---|
| **3B.1** | Schema migration + new fields backfilled | None (every customer `AccountEnabled = false`) | ✅ |
| **3B.2** | Customer detail page with read-only balance / open invoices (still always 0) | None visible | ✅ |
| **3B.3** | Account tender button + validation, gated behind `BusinessSettings.AccountsEnabled` | Owners can manually flip a customer to Account and ring sales to it | ✅ |
| **3B.4** | Take-payment modal + payments list page | Owners can record payments | ✅ |
| **3B.5** | Statement PDF | Customers can be sent monthly statements | ✅ |
| **3B.6** | Aging report | Owner-level visibility into who owes what | ✅ |

This means we can stop after 3B.3 if we just want "ring up an account sale" without going full AR.

### 3B.5 — Risk

| Risk | Severity | Mitigation |
|---|---|---|
| **DB migration on live MC Tactical** | High | Stop the API container, snapshot DB, run migration, smoke-test, restart. Existing invoices backfilled to `Paid`. Rollback = restore snapshot. |
| **Sale flow regression** (Account tender breaks Cash/Card/EFT) | High | Feature flag `BusinessSettings.AccountsEnabled`. Off by default. Existing flow untouched. Comprehensive E2E suite before flip. |
| **Balance math drift** | Critical | Single source of truth: `Sum(invoice.AmountOutstanding) − Sum(unallocated payments)`. No "stored balance" field. Recompute every read. Add invariant tests. |
| **Refund/void of an account invoice** | Medium | Define explicitly: voiding an Unpaid account invoice removes it from balance. Voiding a Paid account invoice creates a credit note (a `CustomerPayment` with negative amount). |
| **Partial payments race condition** | Low | Wrap "apply payment to invoices" in a transaction; mark invoice `PartiallyPaid` if `AmountPaid > 0 && < GrandTotal`. |
| **Credit limit too restrictive (blocks legit sales)** | Medium | Two modes via `BusinessSettings.AccountsHardLimit` — true = block, false = warn but allow with manager approval. |
| **Statement amounts wrong (trust loss)** | Critical | PDF generation must use the same balance computation as the API. Same code path. Snapshot tests against fixtures. |

### 3B.6 — Effort

| Stage | Effort |
|---|---|
| 3B.1 schema + migration | 1.5 days |
| 3B.2 customer detail page + balance read | 1.5 days |
| 3B.3 Account tender + validation | 1 day |
| 3B.4 take payment + payments list | 2 days |
| 3B.5 statement PDF | 1.5 days |
| 3B.6 aging report | 1 day |
| **Total (full)** | **~8.5 days** |
| **MVP (3B.1–3B.3)** | **~4 days** |

---

## 4. Open decisions (must answer before starting)

These are scope/policy decisions, not technical. Each affects the build plan.

1. **Hard credit limit or soft?**
   - **Hard:** sale rejected if it pushes balance over limit. Manager has no override.
   - **Soft:** warning shown, manager can confirm. (recommended)
   - Configurable per-business via `BusinessSettings.AccountsHardLimit`.

2. **Statement period default**
   - Monthly (1st–end of month)?
   - Rolling 30 days?
   - Owner picks per statement?

3. **Aging buckets**
   - Standard 30/60/90/120+?
   - Custom per business?
   - Use due date or invoice date?

4. **Partial payments**
   - Allow partial (recommended — real-world AR demands it)?
   - Or full-invoice-only for v1?

5. **Refund of an Account-paid invoice**
   - Reduces balance only?
   - Refunds cash/card?
   - Both options offered at refund time? (recommended)

6. **Default: walk-in vs account-eligible customer**
   - Auto-create a customer record on every email entry, or stay with current "Customer is optional" flow?
   - Affects `Invoice.CustomerId` nullability long-term.

7. **Statement numbering**
   - Sequential per customer (`STMT-TIAN-2026-04`)?
   - Just date-stamped?

8. **Trade/Wholesale tier vs Account flag**
   - Mockup sidebar shows "Trade / Wholesale" as a separate item from Customers.
   - Are wholesale customers automatically account-enabled? Or independent flags?
   - Does Trade pricing automatically apply when on account?

9. **Who can take payments?**
   - Owner/Admin only?
   - Cashier with manager approval?
   - Configurable role gate?

10. **MC Tactical first or Axionis first?**
    - Build & ship to MC Tactical (real users find bugs fast)?
    - Build on Axionis (no live data risk)?
    - Recommended: build on Axionis, validate, then migrate to MC.

---

## 5. White-label considerations

Phase 3 ships to both products. Each business decides whether AR is on:

- `BusinessSettings.AccountsEnabled` — master toggle (false default for new tenants).
- `BusinessSettings.AccountsHardLimit` — hard or soft limit (soft default).
- `BusinessSettings.AccountsAgingBuckets` — array of day cutoffs (default `[30, 60, 90, 120]`).
- `BusinessSettings.AccountsStatementPeriod` — `monthly` | `rolling30` (monthly default).
- Statement PDF uses existing branding (logo, address, VAT number) — no new white-label work.
- All UI strings use the existing terminology system (`useBranding().terminology`) — Trade/Wholesale label can be customised per business.

---

## 6. Pre-flight checklist (before writing any code)

- [ ] Phase 2 (thermal printing) shipped, deployed, stable for 7+ days
- [ ] All 10 open decisions in §4 answered
- [ ] Off-server backup of MC Tactical taken within last 24h
- [ ] CI green on both repos
- [ ] Decide build order: Axionis → MC, or MC → Axionis
- [ ] Decide MVP cutoff: 3B.1–3B.3 (basic) or full 3B.1–3B.6
- [ ] Verify Product has Category field (affects 3A Top Categories widget)

---

## 7. Reading list (existing code to study before starting)

- `src/HuntexPos.Api/Controllers/ReportsController.cs` — aggregation patterns, DateTimeOffset workaround
- `src/HuntexPos.Api/Controllers/InvoicesController.cs` — invoice creation, payment method handling
- `src/HuntexPos.Api/Domain/{Invoice,Customer,InvoiceLine}.cs` — current model
- `src/huntex-pos-web/src/views/{ReportsView,FinancialReportView}.vue` — chart patterns, date filters
- `src/huntex-pos-web/src/views/PosView.vue` — tender picker (added in Phase 1A) — we'll add 5th button here
- `src/HuntexPos.Api/Services/PdfService.cs` (or equivalent) — QuestPDF patterns for invoice PDFs — reuse for statements
- `src/HuntexPos.Api/Domain/BusinessSettings.cs` — feature flag pattern

---

*Last updated: 2026-04-28 — same day as Phase 1A deploy + Phase 2 plan.*
