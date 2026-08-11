# Shopify item matching + channel-split reports — design

Date: 2026-08-11
Status: Draft for review

## Context

MC Tactical POS is the source of truth; Shopify is a sales channel. Paid Shopify
orders are already imported into the POS as invoices tagged `Source = "Shopify"`
(visibility only — POS stock is never changed). During import, each Shopify line
item is matched to a POS product by Shopify variant id, then by SKU. Items with
no match are attached to a hidden placeholder product
(`SHOPIFY-UNLINKED`, "Shopify online item (not in POS)") so receipts stay
complete.

A read-only diagnostic (`GET /api/shopify/match-analysis`) measured matching
headroom against live data:

| Metric | Value |
|---|---|
| POS active products with SKU | 1,089 |
| POS products with barcode | 488 |
| Shopify variants | 2,304 |
| Shopify variants with SKU | 1,748 |
| Shopify variants with barcode | 34 |
| Currently matched (exact SKU) | 255 |
| Recoverable by barcode (confident) | 2 |
| Recoverable by normalized SKU (confident) | 7 |

Conclusions:
- **Barcode is a dead end**: only 34 of 2,304 Shopify variants carry a barcode,
  and some POS barcodes are junk placeholders (e.g. `0000000000277`).
- **Normalized SKU is small but 100% clean**: the 7 samples are all genuine
  (`SG Pulse` ↔ `SG-Pulse`, `RDSL34` ↔ `RDSL-34`, `4PL6022` ↔ `4 PL 6022`, …).
- The two catalogs genuinely diverge; automatic string matching is largely
  exhausted at ~264. The remaining volume needs a human, prioritized by what
  actually sells online.

## Goals

1. Squeeze the remaining safe automatic matches (normalized SKU + real barcode).
2. Give staff a fast, prioritized way to manually link the Shopify sale items
   that actually generate revenue, and fix historical reports when they do.
3. Report in-store vs Shopify vs combined sales accurately.
4. Keep product grouping/filtering consistent across the POS and Shopify.

## Non-goals

- Fuzzy name/title matching (too noisy given the data; explicitly rejected).
- Two-way stock/price sync for newly linked items (separate future work).
- Matching Shopify variants that have never sold (not worth the effort).
- Changing how POS stock is affected by Shopify sales (still untouched).

## Part A — Safe auto-linking (extend `reconcile`)

`POST /api/shopify/reconcile` currently links only on exact SKU. Enhance it:

- Fetch variants via `GetAllVariantDetailsAsync` (includes barcode + title,
  keeps blank-SKU variants so barcode-only matches are possible).
- For each unmatched POS product, attempt in priority order, linking only when
  **exactly one** Shopify variant matches (ambiguous → reported, never linked):
  1. Exact SKU (existing behaviour).
  2. Real barcode. A barcode is "real" only if it is 8+ characters, digits only,
     and not all identical characters (rejects `0000000000277`).
  3. Normalized SKU: uppercase, strip non-alphanumeric, drop leading zeros.
- On link, persist `ShopifyProductId`, `ShopifyVariantId`,
  `ShopifyInventoryItemId`, `ShopifySyncedAt` on the POS product (unchanged).
- Response gains per-method counts: `linkedByExactSku`, `linkedByBarcode`,
  `linkedByNormalizedSku`, plus existing fields. `apply=false` stays a dry run.

Behaviour is additive and safe: no product fields are overwritten, only the
Shopify mapping ids are filled where currently null.

## Part B — Manual linking tool (prioritized by Shopify sales)

### Data model change

Add nullable `ShopifyVariantId` (`long?`) to `InvoiceLine`. Populated for every
Shopify-imported line (matched and unmatched) so unlinked sales can be
aggregated and linked later. Added to existing DBs via
`EnsureInvoiceLineShopifyColumnsAsync` (SQLite `ALTER TABLE ADD COLUMN`, following
the project's existing `Ensure*ColumnsAsync` convention — no EF migrations).

`ShopifyOrderImportService.BuildLines` sets `ShopifyVariantId = item.VariantId`
on every line it creates (including placeholder lines). Existing placeholder
lines from earlier imports have a null variant id; they are backfilled on the
next sync's repair pass (repair rebuilds lines from the order, which carries the
variant id).

### Endpoints (Owner/Dev)

- `GET /api/shopify/unlinked-sales` — aggregate `InvoiceLines` where
  `Invoice.Source == "Shopify"` and `ProductId == <placeholder id>`, grouped by
  `ShopifyVariantId` (falling back to `SkuAtSale` when variant id is null),
  returning: `shopifyVariantId`, `shopifySku` (`SkuAtSale`), `title`
  (`Description`), `qtySold`, `revenue`, `orderCount`. Sorted by `revenue`
  descending. This list is short because most variants never sell.
- `POST /api/shopify/link-variant` `{ shopifyVariantId, shopifySku, posProductId }`
  — validation: `posProductId` must exist; the POS product must not already be
  linked to a *different* Shopify variant (else 409 with a clear message).
  Actions (single transaction):
  1. Set `ShopifyVariantId` (+ `ShopifySyncedAt`) on the POS product. Product/
     inventory ids are left as-is (only needed for pushing up, out of scope).
  2. Reclassify past sales: for every Shopify-sourced `InvoiceLine` currently on
     the placeholder whose `ShopifyVariantId` matches (or, when null, whose
     `SkuAtSale` matches `shopifySku`), set `ProductId = posProductId` and
     `CostAtSale = round(product.Cost * (1 - SupplierDiscountPercent/100), 2)`.
  Returns count of reclassified lines. Future imports then match by variant id
  automatically.

### UI — Settings

A new "Match Shopify sales" section under Settings (Owner/Dev only):
- Loads `unlinked-sales`; shows a table sorted by revenue: Item (title + Shopify
  SKU), Qty sold, Revenue, Orders.
- Each row has a "Link to POS product" control: a search box (reusing the
  existing product search API) to pick the POS product, then confirm.
- On success the row disappears and a toast reports how many past sales were
  reclassified. Empty state when nothing is unlinked.

## Part D — Category/tag sync (POS → Shopify)

Goal: products group/filter the same way in the POS and Shopify. POS is the
source of truth, so this is one-way (POS → Shopify).

The POS already categorizes products with `Category`, `Manufacturer`, and
`ItemType`. Product push already sends `vendor = Manufacturer` and
`productType = ItemType` but sends no Shopify **tags** and never updates these on
already-linked products.

Changes:
- **On push (create and update)**, set Shopify `tags` to the distinct, non-empty
  set of `{ Category, Manufacturer, ItemType }`, and keep `vendor = Manufacturer`
  and `productType = ItemType` in sync. No new POS fields (reuse the existing
  three — no data re-entry).
- **`UpdateExistingAsync`** must also issue a `productUpdate` for
  `tags`/`productType`/`vendor` (today it only updates the variant), so
  re-pushing refreshes categorization.
- **Bulk action** `POST /api/shopify/sync-tags?apply=` (Owner/Dev): iterate
  linked products (`ShopifyProductId != null`) and `productUpdate` their
  tags/type/vendor. `apply=false` is a dry-run returning a count + samples.
- **UI:** a "Sync categories/tags to Shopify" button in the Settings integration
  area (alongside the Match tool), with a confirmation and result toast.

Non-goal: pulling Shopify tags back into the POS, and tagging Shopify-only
products (they have no POS record).

## Part C — Channel-split reports

Sales carry `Source` (null/"POS" = in-store, "Shopify" = online).

### Backend

Add optional `channel` query param (`all` | `instore` | `shopify`, default
`all`) to `GET /api/reports/daily`, `GET /api/reports/payments`, and the sold
section of `GET /api/reports/stock`. Filter invoices:
- `instore`: `Source` is null or `"POS"`.
- `shopify`: `Source == "Shopify"`.

`daily` and `payments` responses also gain a per-channel breakdown object
(`inStore`, `shopify`, `combined` subtotals) so summary cards can show all three
without extra requests.

Vendor and Consignment reports are unchanged (supplier-scoped;
placeholder/unlinked Shopify items have no supplier).

### Frontend

On the Financial Report and Reports pages: a segmented **All / In-shop /
Shopify** toggle that re-runs the data, plus summary cards showing the
In-shop / Shopify / Combined split.

### Accuracy note

Imported Shopify sales never reduced POS stock, and unmatched (placeholder) lines
have zero cost, so they read as 100% GP. Today they are silently mixed into
combined GP and units-sold. The channel toggle lets the user view **In-shop
only** for accurate stock/GP, while linking (Part B) progressively corrects the
Shopify figures as top sellers are matched.

## Edge cases

- Junk barcodes (`0000000000277`, short, non-numeric) excluded from auto-link.
- Ambiguous matches (one key → multiple Shopify variants) are reported, never
  auto-linked.
- Re-running any step is idempotent: reconcile only fills null mappings;
  link-variant is a no-op if already linked to the same variant; order sync skips
  complete orders and repairs incomplete ones.
- Linking a POS product already linked to a different variant returns 409.
- Placeholder lines with a null `ShopifyVariantId` (pre-change imports) are
  linkable by `SkuAtSale` and get their variant id on the next sync repair.

## Testing

- `match-analysis` already validated against live data.
- Manual: run reconcile (preview then apply); confirm new links appear with
  per-method counts and no product fields change.
- Manual: import/repair a Shopify order with unmatched items; confirm placeholder
  lines carry the variant id; link one via the Settings tool; confirm past
  invoice line reclassifies and `unlinked-sales` total drops.
- Manual: toggle report channels; confirm In-shop/Shopify/Combined totals add up
  and that In-shop excludes Shopify-sourced invoices.
- Manual: run "Sync categories/tags"; confirm linked Shopify products show the
  POS Category/Manufacturer/ItemType as tags and that re-pushing updates them.
- Build gates: `npm run build` (web) and `dotnet build` (API via SDK container).

## Rollout

Standard: commit to `main`, `git pull` + `docker compose up -d --build` on the
server. No manual DB migration — `Ensure*ColumnsAsync` adds the new column on
startup.
