# Missing costs — assign cost after a sale

## Problem

Many items show ~100% gross profit. A sale line records `CostAtSale` from the product at the
moment of sale; when that was zero, reports fall back to the product's current cost, which is
also zero for (a) POS products created without a cost (often from Shopify) and (b) Shopify order
lines that matched no POS product and sit on the shared `SHOPIFY-UNLINKED` placeholder.

## Design

**Reports → Missing costs** tab (Owner/Admin/Dev). The Financial Report shows a banner linking to
it when any item in its period is missing a cost.

**List** (`GET /api/missing-costs?from&to`, default all time): sold items in Final invoices whose
line `CostAtSale <= 0` and, for POS products, `Product.Cost <= 0`. Shopify shipping lines are
excluded (zero cost is correct). Grouped with the same item key the Financial Report uses
(POS product, or Shopify variant → SKU → title for unlinked lines). Columns: item, SKU, qty,
revenue, last sold, cost input. Sorted by revenue desc.

**Save** (`POST /api/missing-costs`, `{ items: [{ key, costExVat }] }`, cost > 0, ex VAT):

- POS product: set `Product.Cost` only (sell price is not re-resolved), and back-fill every line
  of that product with `CostAtSale <= 0` using the till formula
  `round(cost × (1 − SupplierDiscountPercent/100), 2)`. Lines with a cost are never changed.
- Unlinked Shopify item: back-fill matching placeholder lines with `CostAtSale <= 0` to the cost.
  Row shows "Online item, not linked" and, for Owner/Dev, a "Link to product" shortcut to the
  Shopify matching tool so future orders carry the cost.

Back-filled costs flow into the Financial Report, salesperson commission and returns (which read
the original line's cost). The Daily report gets the same product-cost fallback as the others.

## Out of scope

Marking genuine zero-cost items (services, freebies) as "no cost"; they stay listed.

## Testing

Playwright: list contents (incl. shipping excluded, costed lines excluded), save for product and
unlinked Shopify item, existing costs untouched, sell price unchanged, reports reflect the cost,
Sales role forbidden.
