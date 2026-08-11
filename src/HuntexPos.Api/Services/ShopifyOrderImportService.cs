using HuntexPos.Api.Data;
using HuntexPos.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace HuntexPos.Api.Services;

/// <summary>
/// Imports paid Shopify orders into the POS as invoices tagged <c>Source = "Shopify"</c> so they
/// appear in Sales History alongside in-store sales. This is visibility-only: it never changes POS
/// stock. Line items are attached for products that can be matched (by Shopify variant id, then
/// SKU); unmatched items are counted and reported but not fabricated as catalog products. Orders
/// already imported (by Shopify order id) are skipped, so re-running is safe.
/// </summary>
public class ShopifyOrderImportService
{
    private const string ShopifySource = "Shopify";
    private const decimal TaxRate = 15m;

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

        var existingOrderIds = await _db.Invoices
            .Where(i => i.ShopifyOrderId != null)
            .Select(i => i.ShopifyOrderId!.Value)
            .ToListAsync(ct);
        var alreadyImported = new HashSet<long>(existingOrderIds);

        var products = await _db.Products.AsNoTracking().ToListAsync(ct);
        var byVariant = products
            .Where(p => p.ShopifyVariantId.HasValue)
            .GroupBy(p => p.ShopifyVariantId!.Value)
            .ToDictionary(g => g.Key, g => g.First());
        var bySku = products
            .Where(p => !string.IsNullOrWhiteSpace(p.Sku))
            .GroupBy(p => p.Sku.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var summary = new ShopifyImportSummary { FetchedCount = orders.Count };
        var newInvoices = new List<Invoice>();

        foreach (var order in orders)
        {
            if (alreadyImported.Contains(order.Id))
            {
                summary.SkippedExistingCount++;
                continue;
            }

            var lines = new List<InvoiceLine>();
            var unmatchedInThisOrder = 0;

            foreach (var item in order.Lines)
            {
                Product? product = null;
                if (item.VariantId.HasValue && byVariant.TryGetValue(item.VariantId.Value, out var pv))
                    product = pv;
                else if (!string.IsNullOrWhiteSpace(item.Sku) && bySku.TryGetValue(item.Sku.Trim(), out var ps))
                    product = ps;

                if (product == null)
                {
                    unmatchedInThisOrder++;
                    continue;
                }

                var lineTotal = Math.Round(item.Price * item.Quantity, 2);
                lines.Add(new InvoiceLine
                {
                    Id = Guid.NewGuid(),
                    ProductId = product.Id,
                    Description = string.IsNullOrWhiteSpace(item.Title) ? product.Name : item.Title,
                    SkuAtSale = product.Sku,
                    Quantity = item.Quantity,
                    UnitPrice = item.Price,
                    OriginalUnitPrice = item.Price,
                    LineDiscount = 0,
                    LineTotal = lineTotal,
                    CostAtSale = Math.Round(product.Cost * (1 - product.SupplierDiscountPercent / 100m), 2)
                });
            }

            summary.MatchedLineCount += lines.Count;
            summary.UnmatchedLineCount += unmatchedInThisOrder;
            if (unmatchedInThisOrder > 0)
                summary.OrdersWithUnmatchedLines++;

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

        if (apply && newInvoices.Count > 0)
        {
            _db.Invoices.AddRange(newInvoices);
            await _db.SaveChangesAsync(ct);
        }

        summary.Applied = apply;
        return summary;
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
    public int SkippedExistingCount { get; set; }
    public int MatchedLineCount { get; set; }
    public int UnmatchedLineCount { get; set; }
    public int OrdersWithUnmatchedLines { get; set; }
    public List<string> SampleImported { get; set; } = new();
}
