using HuntexPos.Api.Data;
using HuntexPos.Api.Domain;
using HuntexPos.Api.DTOs;
using HuntexPos.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HuntexPos.Api.Controllers;

/// <summary>
/// Items that were sold while their cost was zero, so reports show them at ~100% gross profit.
/// Managers enter the real cost afterwards; it is written onto the past sale lines that had no
/// cost (and onto the product, for future sales). Lines that already carry a cost are never changed.
/// </summary>
[ApiController]
[Route("api/missing-costs")]
[Authorize(Roles = $"{Roles.Admin},{Roles.Owner},{Roles.Dev}")]
public class MissingCostsController : ControllerBase
{
    private const decimal MaxCost = 10_000_000m;
    private readonly HuntexDbContext _db;

    public MissingCostsController(HuntexDbContext db) => _db = db;

    /// <summary>
    /// A line is "missing a cost" when nothing was recorded at sale time and the report fallback
    /// (the product's current cost) is also zero. Unlinked Shopify lines have no real product to
    /// fall back to. Shipping lines are revenue with a genuine zero cost.
    /// </summary>
    private static bool IsMissingCost(InvoiceLine l) =>
        l.CostAtSale <= 0
        && !SoldItemKey.IsShopifyShipping(l)
        && (SoldItemKey.IsUnlinkedShopify(l) || (l.Product?.Cost ?? 0) <= 0);

    [HttpGet]
    public async Task<List<MissingCostRowDto>> List(
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        CancellationToken ct)
    {
        // Decimal and DateTimeOffset comparisons can't be translated by SQLite; filter in memory.
        var lines = (await _db.InvoiceLines.AsNoTracking()
                .Include(l => l.Invoice)
                .Include(l => l.Product)
                .Where(l => l.Invoice!.Status == InvoiceStatus.Final)
                .ToListAsync(ct))
            .Where(l => (!from.HasValue || l.Invoice!.CreatedAt >= from.Value)
                        && (!to.HasValue || l.Invoice!.CreatedAt <= to.Value))
            .Where(IsMissingCost);

        return lines
            .GroupBy(SoldItemKey.For)
            .Select(g =>
            {
                var latest = g.OrderByDescending(l => l.Invoice!.CreatedAt).First();
                var unlinked = SoldItemKey.IsUnlinkedShopify(latest);
                return new MissingCostRowDto
                {
                    Key = g.Key,
                    IsUnlinkedShopify = unlinked,
                    ProductId = unlinked ? null : latest.ProductId,
                    ShopifyVariantId = latest.ShopifyVariantId,
                    Sku = unlinked ? latest.SkuAtSale : latest.Product?.Sku,
                    Name = unlinked ? latest.Description : latest.Product?.Name ?? latest.Description,
                    QtySold = g.Sum(l => l.Quantity),
                    SaleCount = g.Select(l => l.InvoiceId).Distinct().Count(),
                    Revenue = g.Sum(l => l.LineTotal),
                    LastSoldAt = latest.Invoice!.CreatedAt
                };
            })
            .OrderByDescending(r => r.Revenue)
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    [HttpPost]
    public async Task<ActionResult<SaveMissingCostsResponse>> Save([FromBody] SaveMissingCostsRequest req, CancellationToken ct)
    {
        if (req.Items.Count == 0)
            return BadRequest(new { error = "Enter at least one cost." });
        if (req.Items.Any(i => i.CostExVat <= 0 || i.CostExVat > MaxCost))
            return BadRequest(new { error = "Each cost must be more than R0." });
        if (req.Items.Select(i => i.Key).Distinct().Count() != req.Items.Count)
            return BadRequest(new { error = "Each item can only be listed once." });

        var productIds = new Dictionary<Guid, decimal>();
        var unlinkedKeys = new Dictionary<string, decimal>();
        foreach (var item in req.Items)
        {
            if (item.Key.StartsWith("p:") && Guid.TryParse(item.Key[2..], out var pid))
                productIds[pid] = item.CostExVat;
            else if (item.Key.StartsWith("u:"))
                unlinkedKeys[item.Key] = item.CostExVat;
            else
                return BadRequest(new { error = "Unknown item." });
        }

        var requestedIds = productIds.Keys.ToList();
        var products = await _db.Products.Where(p => requestedIds.Contains(p.Id)).ToListAsync(ct);
        if (products.Count != productIds.Count)
            return BadRequest(new { error = "One of these products no longer exists. Refresh and try again." });
        if (products.Any(p => p.Sku == ShopifyOrderImportService.UnlinkedPlaceholderSku))
            return BadRequest(new { error = "Unknown item." });

        var placeholderId = unlinkedKeys.Count == 0
            ? (Guid?)null
            : await _db.Products
                .Where(p => p.Sku == ShopifyOrderImportService.UnlinkedPlaceholderSku)
                .Select(p => (Guid?)p.Id)
                .FirstOrDefaultAsync(ct);

        var lineProductIds = requestedIds.ToList();
        if (placeholderId.HasValue) lineProductIds.Add(placeholderId.Value);
        var candidates = (await _db.InvoiceLines
                .Include(l => l.Product)
                .Where(l => lineProductIds.Contains(l.ProductId))
                .ToListAsync(ct))
            .Where(l => l.CostAtSale <= 0 && !SoldItemKey.IsShopifyShipping(l))
            .ToList();

        var linesUpdated = 0;
        var now = DateTimeOffset.UtcNow;
        foreach (var p in products)
        {
            // Sell price is deliberately left alone: fixing history must not reprice the till.
            p.Cost = productIds[p.Id];
            p.UpdatedAt = now;
            var lineCost = Math.Round(p.Cost * (1 - p.SupplierDiscountPercent / 100m), 2);
            foreach (var l in candidates.Where(l => l.ProductId == p.Id))
            {
                l.CostAtSale = lineCost;
                linesUpdated++;
            }
        }

        foreach (var l in candidates.Where(l => l.ProductId == placeholderId))
        {
            if (unlinkedKeys.TryGetValue(SoldItemKey.For(l), out var cost))
            {
                l.CostAtSale = Math.Round(cost, 2);
                linesUpdated++;
            }
        }

        await _db.SaveChangesAsync(ct);
        return new SaveMissingCostsResponse { ProductsUpdated = products.Count, LinesUpdated = linesUpdated };
    }
}
