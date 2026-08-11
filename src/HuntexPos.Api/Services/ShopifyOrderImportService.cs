using HuntexPos.Api.Data;
using HuntexPos.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace HuntexPos.Api.Services;

/// <summary>
/// Imports paid Shopify orders into the POS as invoices tagged <c>Source = "Shopify"</c> so they
/// appear in Sales History alongside in-store sales. This is visibility-only: it never changes POS
/// stock. Every Shopify line item is captured: those that match a POS product (by Shopify variant
/// id, then SKU) link to it; items that have no POS product are still recorded against a single
/// hidden placeholder product, keeping the real Shopify title/SKU in the line snapshot so receipts
/// show what was actually sold. Orders already imported (by Shopify order id) are skipped unless the
/// stored invoice is missing items — those are repaired in place — so re-running is safe.
/// </summary>
public class ShopifyOrderImportService
{
    private const string ShopifySource = "Shopify";
    private const decimal TaxRate = 15m;

    /// <summary>SKU of the hidden catalog row unmatched Shopify line items are attached to.</summary>
    private const string UnlinkedPlaceholderSku = "SHOPIFY-UNLINKED";

    private readonly HuntexDbContext _db;
    private readonly ShopifyClient _shopify;

    public ShopifyOrderImportService(HuntexDbContext db, ShopifyClient shopify)
    {
        _db = db;
        _shopify = shopify;
    }

    public async Task<ShopifyImportSummary> ImportPaidOrdersAsync(int maxOrders, bool apply, CancellationToken ct)
    {
        var orders = await _shopify.GetPaidOrdersAsync(maxOrders, ct);

        // Existing Shopify invoices keyed by order id (with lines) so we can repair any that were
        // imported before unmatched items were captured — those show a total but no line items.
        var existingInvoices = await _db.Invoices
            .Include(i => i.Lines)
            .Where(i => i.ShopifyOrderId != null)
            .ToDictionaryAsync(i => i.ShopifyOrderId!.Value, ct);

        var products = await _db.Products.AsNoTracking().ToListAsync(ct);
        var byVariant = products
            .Where(p => p.ShopifyVariantId.HasValue)
            .GroupBy(p => p.ShopifyVariantId!.Value)
            .ToDictionary(g => g.Key, g => g.First());
        var bySku = products
            .Where(p => !string.IsNullOrWhiteSpace(p.Sku))
            .GroupBy(p => p.Sku.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var placeholder = await GetOrCreateUnlinkedPlaceholderAsync(apply, ct);

        var summary = new ShopifyImportSummary { FetchedCount = orders.Count };
        var newInvoices = new List<Invoice>();

        foreach (var order in orders)
        {
            var (lines, matched, unmatched) = BuildLines(order, byVariant, bySku, placeholder);

            if (existingInvoices.TryGetValue(order.Id, out var existing))
            {
                // Repair when the stored invoice is missing items OR its lines no longer match what
                // Shopify reports (e.g. a description that now includes the variant like "6 DASHER").
                // Once repaired the lines match, so subsequent syncs skip it — this stays idempotent.
                if (!NeedsRepair(existing, lines))
                {
                    summary.SkippedExistingCount++;
                    continue;
                }

                summary.MatchedLineCount += matched;
                summary.UnmatchedLineCount += unmatched;
                if (unmatched > 0) summary.OrdersWithUnmatchedLines++;
                summary.RepairedCount++;

                if (apply)
                {
                    // Delete the old lines exactly once. Do NOT also Clear() the nav collection:
                    // on a required relationship Clear() orphans the children and triggers a second
                    // cascade delete of the same rows, which surfaces as a DbUpdateConcurrencyException
                    // ("expected 1 row, affected 0"). Add the rebuilt lines straight to the context.
                    _db.InvoiceLines.RemoveRange(existing.Lines.ToList());
                    foreach (var line in lines)
                        line.InvoiceId = existing.Id;
                    _db.InvoiceLines.AddRange(lines);

                    existing.SubTotal = Math.Round(order.TotalPrice + order.TotalDiscounts, 2);
                    existing.TaxRate = TaxRate;
                    existing.TaxAmount = Math.Round(order.TotalTax, 2);
                    existing.DiscountTotal = Math.Round(order.TotalDiscounts, 2);
                    existing.GrandTotal = Math.Round(order.TotalPrice, 2);
                }

                if (summary.SampleImported.Count < 25)
                    summary.SampleImported.Add($"{order.Name} \u2192 repaired ({lines.Count} item{(lines.Count == 1 ? "" : "s")})");
                continue;
            }

            summary.MatchedLineCount += matched;
            summary.UnmatchedLineCount += unmatched;
            if (unmatched > 0) summary.OrdersWithUnmatchedLines++;

            var invoice = new Invoice
            {
                Id = Guid.NewGuid(),
                InvoiceNumber = await BuildInvoiceNumberAsync(order, newInvoices, ct),
                Status = InvoiceStatus.Final,
                Source = ShopifySource,
                PaymentMethod = ShopifySource,
                CustomerName = order.CustomerName,
                CustomerEmail = order.Email,
                ShopifyOrderId = order.Id,
                ShopifyOrderName = order.Name,
                SubTotal = Math.Round(order.TotalPrice + order.TotalDiscounts, 2),
                TaxRate = TaxRate,
                TaxAmount = Math.Round(order.TotalTax, 2),
                DiscountTotal = Math.Round(order.TotalDiscounts, 2),
                GrandTotal = Math.Round(order.TotalPrice, 2),
                CreatedAt = order.CreatedAt,
                Lines = lines
            };

            newInvoices.Add(invoice);
            summary.ImportedCount++;
            if (summary.SampleImported.Count < 25)
                summary.SampleImported.Add($"{order.Name} \u2192 {invoice.InvoiceNumber} (R{invoice.GrandTotal:0.00})");
        }

        if (apply)
        {
            if (newInvoices.Count > 0)
                _db.Invoices.AddRange(newInvoices);
            await _db.SaveChangesAsync(ct);
        }

        summary.Applied = apply;
        return summary;
    }

    /// <summary>
    /// Turn a Shopify order's line items into invoice lines. Matched items link to their POS product;
    /// unmatched items link to <paramref name="placeholder"/> but keep the real Shopify title/SKU so
    /// the receipt still shows what was sold. Returns the lines plus matched/unmatched counts.
    /// </summary>
    private static (List<InvoiceLine> Lines, int Matched, int Unmatched) BuildLines(
        ShopifyOrder order,
        Dictionary<long, Product> byVariant,
        Dictionary<string, Product> bySku,
        Product placeholder)
    {
        var lines = new List<InvoiceLine>();
        var matched = 0;
        var unmatched = 0;

        foreach (var item in order.Lines)
        {
            Product? product = null;
            if (item.VariantId.HasValue && byVariant.TryGetValue(item.VariantId.Value, out var pv))
                product = pv;
            else if (!string.IsNullOrWhiteSpace(item.Sku) && bySku.TryGetValue(item.Sku.Trim(), out var ps))
                product = ps;

            var isMatch = product != null;
            if (isMatch) matched++; else unmatched++;

            var target = product ?? placeholder;
            var lineTotal = Math.Round(item.Price * item.Quantity, 2);

            lines.Add(new InvoiceLine
            {
                Id = Guid.NewGuid(),
                ProductId = target.Id,
                Description = string.IsNullOrWhiteSpace(item.Title)
                    ? (isMatch ? product!.Name : "Shopify item")
                    : item.Title,
                SkuAtSale = string.IsNullOrWhiteSpace(item.Sku)
                    ? (isMatch ? product!.Sku : null)
                    : item.Sku.Trim(),
                Quantity = item.Quantity,
                UnitPrice = item.Price,
                OriginalUnitPrice = item.Price,
                LineDiscount = 0,
                LineTotal = lineTotal,
                CostAtSale = isMatch
                    ? Math.Round(product!.Cost * (1 - product.SupplierDiscountPercent / 100m), 2)
                    : 0m
            });
        }

        // Shipping/courier is charged on the order, not on a product, so it never matched above and the
        // item lines alone won't sum to the order total. Add it as its own line (on the placeholder) so
        // the receipt balances and the fee is visible. Zero cost — it is revenue, not a purchased good.
        if (order.TotalShipping != 0m)
        {
            lines.Add(new InvoiceLine
            {
                Id = Guid.NewGuid(),
                ProductId = placeholder.Id,
                Description = string.IsNullOrWhiteSpace(order.ShippingTitle)
                    ? "Shipping"
                    : $"Shipping \u2013 {order.ShippingTitle}",
                SkuAtSale = null,
                Quantity = 1,
                UnitPrice = order.TotalShipping,
                OriginalUnitPrice = order.TotalShipping,
                LineDiscount = 0,
                LineTotal = order.TotalShipping,
                CostAtSale = 0m
            });
        }

        return (lines, matched, unmatched);
    }

    /// <summary>
    /// Whether a stored Shopify invoice needs rebuilding: true if the line count differs or any line's
    /// description, SKU or quantity no longer matches the freshly-built lines. Compared order-insensitively
    /// on display fields only (no money) so it converges after one repair and won't rewrite forever.
    /// </summary>
    private static bool NeedsRepair(Invoice existing, List<InvoiceLine> fresh)
    {
        if (existing.Lines.Count != fresh.Count) return true;

        static string Key(InvoiceLine l) => $"{l.Description}\u0001{l.SkuAtSale}\u0001{l.Quantity}";
        var stored = existing.Lines.Select(Key).OrderBy(s => s, StringComparer.Ordinal);
        var built = fresh.Select(Key).OrderBy(s => s, StringComparer.Ordinal);
        return !stored.SequenceEqual(built, StringComparer.Ordinal);
    }

    /// <summary>
    /// Find (or, when <paramref name="apply"/>, create) the single hidden product that unmatched
    /// Shopify line items are attached to. It is inactive so it stays out of POS search and stock
    /// lists; import never changes its stock. In preview mode a transient instance is returned so
    /// counts can be produced without writing anything.
    /// </summary>
    private async Task<Product> GetOrCreateUnlinkedPlaceholderAsync(bool apply, CancellationToken ct)
    {
        var existing = await _db.Products.FirstOrDefaultAsync(p => p.Sku == UnlinkedPlaceholderSku, ct);
        if (existing != null) return existing;

        var placeholder = new Product
        {
            Id = Guid.NewGuid(),
            Sku = UnlinkedPlaceholderSku,
            Name = "Shopify online item (not in POS)",
            ItemType = "Shopify",
            Active = false,
            Cost = 0,
            SellPrice = 0,
            QtyOnHand = 0,
            CreatedAt = DateTimeOffset.UtcNow
        };
        if (apply) _db.Products.Add(placeholder);
        return placeholder;
    }

    /// <summary>
    /// Derive a unique POS invoice number from the Shopify order name (e.g. "#1001" → "SHOP-1001"),
    /// guarding against collisions with existing invoices and others in the same batch.
    /// </summary>
    private async Task<string> BuildInvoiceNumberAsync(ShopifyOrder order, List<Invoice> batch, CancellationToken ct)
    {
        var raw = string.IsNullOrWhiteSpace(order.Name) ? order.Id.ToString() : order.Name.TrimStart('#').Trim();
        var candidate = $"SHOP-{raw}";

        var batchNumbers = batch.Select(i => i.InvoiceNumber).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var exists = batchNumbers.Contains(candidate)
                     || await _db.Invoices.AnyAsync(i => i.InvoiceNumber == candidate, ct);
        if (!exists) return candidate;

        // Fall back to the globally-unique Shopify order id to guarantee no clash.
        return $"SHOP-{raw}-{order.Id}";
    }
}

public class ShopifyImportSummary
{
    public bool Applied { get; set; }
    public int FetchedCount { get; set; }
    public int ImportedCount { get; set; }
    public int RepairedCount { get; set; }
    public int SkippedExistingCount { get; set; }
    public int MatchedLineCount { get; set; }
    public int UnmatchedLineCount { get; set; }
    public int OrdersWithUnmatchedLines { get; set; }
    public List<string> SampleImported { get; set; } = new();
}
