using System.Globalization;
using System.Linq;
using System.Text;
using HuntexPos.Api.Data;
using HuntexPos.Api.Domain;
using HuntexPos.Api.DTOs;
using HuntexPos.Api.Options;
using HuntexPos.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HuntexPos.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = $"{Roles.Sales},{Roles.Admin},{Roles.Owner},{Roles.Dev}")]
public class ProductsController : ControllerBase
{
    private readonly HuntexDbContext _db;
    private readonly AppOptions _app;
    private readonly IPricingService _pricing;

    public ProductsController(HuntexDbContext db, IOptions<AppOptions> app, IPricingService pricing)
    {
        _db = db;
        _app = app.Value;
        _pricing = pricing;
    }

    [HttpGet]
    public async Task<ActionResult<List<ProductDto>>> Search([FromQuery] ProductSearchQuery search, CancellationToken ct)
    {
        var hideCost = await ShouldHideCostAsync(ct);
        var query = _db.Products.AsNoTracking().Include(p => p.Supplier).Where(p => p.Active);

        if (search.SupplierId.HasValue)
            query = query.Where(p => p.SupplierId == search.SupplierId);
        if (!string.IsNullOrWhiteSpace(search.Barcode))
        {
            var b = search.Barcode.Trim();
            query = query.Where(p => p.Barcode == b || p.Sku == b);
        }
        query = ApplyPosSearchFilter(query, search.Q);

        var list = await query.OrderBy(p => p.Name).Take(Math.Clamp(search.Take, 1, 200)).ToListAsync(ct);
        return list.Select(p => Map(p, hideCost)).ToList();
    }

    /// <summary>Browse the full inventory with pagination (POS / stock screen).</summary>
    [HttpGet("stocklist")]
    public async Task<ActionResult<StocklistPageDto>> Stocklist([FromQuery] ProductStocklistQuery search, CancellationToken ct)
    {
        var hideCost = await ShouldHideCostAsync(ct);
        var query = _db.Products.AsNoTracking().Include(p => p.Supplier).AsQueryable();
        if (!search.IncludeInactive)
            query = query.Where(p => p.Active);
        if (search.SupplierId.HasValue)
            query = query.Where(p => p.SupplierId == search.SupplierId);
        query = ApplyStocklistSearchFilter(query, search.Q);

        if (search.HasSpecial == true)
        {
            var specialProductIds = await GetActiveSpecialProductIds(ct);
            query = query.Where(p => specialProductIds.Contains(p.Id));
        }

        var take = Math.Clamp(search.Take, 1, 10_000);
        var skip = Math.Max(0, search.Skip);
        var total = await query.CountAsync(ct);
        var list = await query.OrderBy(p => p.Name).Skip(skip).Take(take).ToListAsync(ct);

        var productIds = list.Select(p => p.Id).ToList();
        var specialMap = await LoadActiveSpecialsMap(productIds, ct);

        return new StocklistPageDto
        {
            Total = total,
            Skip = skip,
            Take = take,
            Items = list.Select(p => Map(p, hideCost, specialMap.GetValueOrDefault(p.Id))).ToList()
        };
    }

    /// <summary>Get all specials for a product (standalone + promotion-linked).</summary>
    [HttpGet("{productId:guid}/specials")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Owner},{Roles.Dev}")]
    public async Task<ActionResult<List<ProductSpecialDto>>> GetProductSpecials(Guid productId, CancellationToken ct)
    {
        try
        {
            var specials = await _db.ProductSpecials.AsNoTracking()
                .Include(s => s.Product)
                .Include(s => s.Promotion)
                .Where(s => s.ProductId == productId)
                .ToListAsync(ct);
            return specials.OrderByDescending(s => s.CreatedAt).Select(s =>
            {
                var basePrice = s.Product?.SellPrice ?? 0;
                decimal effective;
                if (s.SpecialPrice.HasValue)
                    effective = s.SpecialPrice.Value;
                else if (s.DiscountPercent.HasValue)
                    effective = Math.Round(basePrice * (1 - s.DiscountPercent.Value / 100m), 2);
                else
                    effective = basePrice;

                return new ProductSpecialDto
                {
                    Id = s.Id,
                    ProductId = s.ProductId,
                    ProductSku = s.Product?.Sku ?? "",
                    ProductName = s.Product?.Name ?? "",
                    BaseSellPrice = basePrice,
                    PromotionId = s.PromotionId,
                    PromotionName = s.Promotion?.Name,
                    SpecialPrice = s.SpecialPrice,
                    DiscountPercent = s.DiscountPercent,
                    EffectivePrice = effective,
                    IsActive = s.IsActive
                };
            }).ToList();
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message, detail = ex.InnerException?.Message });
        }
    }

    /// <summary>Generate a 62mm label PDF for one product (Brother QL-800 / DK-22205).</summary>
    [HttpGet("{productId:guid}/label")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Owner},{Roles.Dev}")]
    public async Task<IActionResult> GetLabel(Guid productId, [FromQuery] int copies = 1, [FromQuery] bool promo = false, CancellationToken ct = default)
    {
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == productId, ct);
        if (product == null) return NotFound();
        copies = Math.Clamp(copies, 1, 50);

        try
        {
            var oldBarcode = product.Barcode;
            var pricing = promo
                ? await ResolvePromoPricing(product, ct)
                : new LabelPdfService.LabelPricing(product.SellPrice, null, null);

            var pdf = LabelPdfService.BuildSingleLabel(product, pricing, copies);

            if (product.Barcode != oldBarcode)
                await _db.SaveChangesAsync(ct);

            return File(pdf, "application/pdf", $"label-{product.Sku}.pdf");
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message, detail = ex.InnerException?.Message });
        }
    }

    /// <summary>
    /// Generate a multi-product label PDF. Supports three copy modes:
    /// fixed <c>copiesPerProduct</c> (default 1), or one label per unit on hand when
    /// <c>copiesFromQtyOnHand</c> is true (capped at <c>maxCopiesPerProduct</c>, default 50).
    /// Up to 200 distinct products and 2000 total labels per request.
    /// </summary>
    [HttpPost("labels")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Owner},{Roles.Dev}")]
    public async Task<IActionResult> GetLabels([FromBody] LabelBatchRequest req, CancellationToken ct = default)
    {
        if (req.ProductIds == null || req.ProductIds.Count == 0)
            return BadRequest(new { error = "At least one productId is required." });

        const int MaxDistinctProducts = 200;
        const int MaxTotalLabels = 2000;
        const int DefaultPerProductCap = 50;

        var ids = req.ProductIds.Distinct().Take(MaxDistinctProducts).ToList();
        var products = await _db.Products.Where(p => ids.Contains(p.Id)).ToListAsync(ct);
        if (products.Count == 0) return NotFound();

        var perProductCap = Math.Clamp(req.MaxCopiesPerProduct ?? DefaultPerProductCap, 1, 200);
        var fixedCopies = Math.Clamp(req.CopiesPerProduct ?? 1, 1, 50);

        try
        {
            var pricingMap = req.UsePromo
                ? await ResolvePromoPricingBatch(products, ct)
                : products.ToDictionary(p => p.Id, p => new LabelPdfService.LabelPricing(p.SellPrice, null, null));

            var items = new List<(Product, LabelPdfService.LabelPricing, int)>();
            var totalLabels = 0;
            var skippedZeroStock = 0;

            foreach (var id in ids)
            {
                var p = products.FirstOrDefault(x => x.Id == id);
                if (p == null) continue;

                int copies;
                if (req.CopiesFromQtyOnHand)
                {
                    if (p.QtyOnHand <= 0) { skippedZeroStock++; continue; }
                    copies = Math.Clamp(p.QtyOnHand, 1, perProductCap);
                }
                else
                {
                    copies = fixedCopies;
                }

                if (totalLabels + copies > MaxTotalLabels)
                {
                    copies = MaxTotalLabels - totalLabels;
                    if (copies <= 0) break;
                }

                var pricing = pricingMap.GetValueOrDefault(p.Id, new LabelPdfService.LabelPricing(p.SellPrice, null, null));
                items.Add((p, pricing, copies));
                totalLabels += copies;
                if (totalLabels >= MaxTotalLabels) break;
            }

            if (items.Count == 0)
            {
                var msg = req.CopiesFromQtyOnHand
                    ? "None of the selected products have stock on hand."
                    : "Nothing to print.";
                return BadRequest(new { error = msg });
            }

            var pdf = LabelPdfService.BuildMultipleLabels(items);

            // EnsureEan13 may have mutated Barcode — persist if anything changed.
            await _db.SaveChangesAsync(ct);

            Response.Headers["X-Label-Count"] = totalLabels.ToString();
            Response.Headers["X-Products-Included"] = items.Count.ToString();
            if (skippedZeroStock > 0)
                Response.Headers["X-Products-Skipped-No-Stock"] = skippedZeroStock.ToString();
            return File(pdf, "application/pdf", "labels.pdf");
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message, detail = ex.InnerException?.Message });
        }
    }

    private async Task<LabelPdfService.LabelPricing> ResolvePromoPricing(Product product, CancellationToken ct)
    {
        var promo = await FindActivePromoAsync(ct);

        var specialsQuery = _db.ProductSpecials.AsNoTracking()
            .Where(s => s.IsActive && s.ProductId == product.Id);
        specialsQuery = promo != null
            ? specialsQuery.Where(s => s.PromotionId == null || s.PromotionId == promo.Id)
            : specialsQuery.Where(s => s.PromotionId == null);
        var special = await specialsQuery.FirstOrDefaultAsync(ct);

        return ComputeLabelPricing(product, promo, special);
    }

    private async Task<Dictionary<Guid, LabelPdfService.LabelPricing>> ResolvePromoPricingBatch(List<Product> products, CancellationToken ct)
    {
        var promo = await FindActivePromoAsync(ct);

        var productIds = products.Select(p => p.Id).ToList();
        var specialsQuery = _db.ProductSpecials.AsNoTracking()
            .Where(s => s.IsActive && productIds.Contains(s.ProductId));
        specialsQuery = promo != null
            ? specialsQuery.Where(s => s.PromotionId == null || s.PromotionId == promo.Id)
            : specialsQuery.Where(s => s.PromotionId == null);
        var specials = await specialsQuery.ToListAsync(ct);
        var specialMap = specials.GroupBy(s => s.ProductId).ToDictionary(g => g.Key, g => g.First());

        return products.ToDictionary(
            p => p.Id,
            p => ComputeLabelPricing(p, promo, specialMap.GetValueOrDefault(p.Id)));
    }

    private static decimal RoundUpR10(decimal v) => Math.Ceiling(v / 10m) * 10m;

    private static LabelPdfService.LabelPricing ComputeLabelPricing(Product product, Promotion? promo, ProductSpecial? special)
    {
        var baseSell = product.SellPrice;
        decimal effective;

        if (special != null)
        {
            if (special.SpecialPrice.HasValue)
                effective = special.SpecialPrice.Value;
            else if (special.DiscountPercent.HasValue)
                effective = RoundUpR10(baseSell * (1 - special.DiscountPercent.Value / 100m));
            else
                effective = baseSell;
        }
        else if (promo != null && promo.DiscountPercent > 0)
        {
            effective = RoundUpR10(baseSell * (1 - promo.DiscountPercent / 100m));
        }
        else
        {
            return new LabelPdfService.LabelPricing(baseSell, null, null);
        }

        var promoName = promo?.Name;
        return new LabelPdfService.LabelPricing(effective, baseSell, promoName);
    }

    /// <summary>Download full stock as CSV (cost columns only for Admin / Owner / Dev).</summary>
    [HttpGet("stocklist/export")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Owner},{Roles.Dev}")]
    public async Task<IActionResult> ExportStocklistCsv(
        [FromQuery] string? q,
        [FromQuery] Guid? supplierId,
        [FromQuery] bool includeInactive = false,
        CancellationToken ct = default)
    {
        var query = _db.Products.AsNoTracking().Include(p => p.Supplier).AsQueryable();
        if (!includeInactive)
            query = query.Where(p => p.Active);
        if (supplierId.HasValue)
            query = query.Where(p => p.SupplierId == supplierId);
        query = ApplyStocklistSearchFilter(query, q);

        var list = await query.OrderBy(p => p.Name).ToListAsync(ct);
        var sb = new StringBuilder();
        sb.AppendLine("Sku,Barcode,Name,Category,Manufacturer,ItemType,Supplier,Cost,SellPrice,QtyOwned,QtyConsignment,Active");
        foreach (var p in list)
        {
            sb.AppendLine(string.Join(",",
                Csv(p.Sku),
                Csv(p.Barcode ?? ""),
                Csv(p.Name),
                Csv(p.Category ?? ""),
                Csv(p.Manufacturer ?? ""),
                Csv(p.ItemType ?? ""),
                Csv(p.Supplier?.Name ?? ""),
                p.Cost.ToString(CultureInfo.InvariantCulture),
                p.SellPrice.ToString(CultureInfo.InvariantCulture),
                p.QtyOnHand.ToString(CultureInfo.InvariantCulture),
                p.QtyConsignment.ToString(CultureInfo.InvariantCulture),
                p.Active ? "yes" : "no"));
        }
        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        return File(bytes, "text/csv", "stocklist.csv");
    }

    private static string Csv(string s)
    {
        if (s.Contains('"') || s.Contains(',') || s.Contains('\n'))
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        return s;
    }

    private static readonly char[] SearchWhitespaceSeparators = { ' ', '\t', '\n', '\r', '\u00a0' };

    /// <summary>
    /// Split user search into terms (whitespace-separated). Each term must match as a substring (anywhere)
    /// in at least one searchable field — order of words does not matter.
    /// </summary>
    private static List<string> SplitSearchTerms(string q) =>
        q.Split(SearchWhitespaceSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => t.Length > 0)
            .ToList();

    /// <summary>POS / stocktake product search: name, SKU, barcode, description, supplier.</summary>
    private static IQueryable<Product> ApplyPosSearchFilter(IQueryable<Product> query, string? q)
    {
        var terms = string.IsNullOrWhiteSpace(q) ? null : SplitSearchTerms(q);
        if (terms is not { Count: > 0 })
            return query;

        foreach (var term in terms)
        {
            var pattern = $"%{term}%";
            query = query.Where(p =>
                EF.Functions.Like(p.Name, pattern) ||
                EF.Functions.Like(p.Sku, pattern) ||
                (p.Barcode != null && EF.Functions.Like(p.Barcode, pattern)) ||
                (p.Description != null && EF.Functions.Like(p.Description, pattern)) ||
                (p.Supplier != null && EF.Functions.Like(p.Supplier.Name, pattern)));
        }

        return query;
    }

    /// <summary>Stock list + CSV export: all POS fields plus category, manufacturer, item type.</summary>
    private static IQueryable<Product> ApplyStocklistSearchFilter(IQueryable<Product> query, string? q)
    {
        var terms = string.IsNullOrWhiteSpace(q) ? null : SplitSearchTerms(q);
        if (terms is not { Count: > 0 })
            return query;

        foreach (var term in terms)
        {
            var pattern = $"%{term}%";
            query = query.Where(p =>
                EF.Functions.Like(p.Name, pattern) ||
                EF.Functions.Like(p.Sku, pattern) ||
                (p.Barcode != null && EF.Functions.Like(p.Barcode, pattern)) ||
                (p.Category != null && EF.Functions.Like(p.Category, pattern)) ||
                (p.Manufacturer != null && EF.Functions.Like(p.Manufacturer, pattern)) ||
                (p.ItemType != null && EF.Functions.Like(p.ItemType, pattern)) ||
                (p.Description != null && EF.Functions.Like(p.Description, pattern)) ||
                (p.Supplier != null && EF.Functions.Like(p.Supplier.Name, pattern)));
        }

        return query;
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ProductDto>> Get(Guid id, CancellationToken ct)
    {
        var hideCost = await ShouldHideCostAsync(ct);
        var p = await _db.Products.AsNoTracking().Include(x => x.Supplier).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p == null) return NotFound();
        return await MapWithPricingAsync(p, hideCost, ct);
    }

    [HttpPost]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Owner},{Roles.Dev}")]
    public async Task<ActionResult<ProductDto>> Create([FromBody] CreateProductRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Sku))
            return BadRequest(new { error = "SKU is required." });
        if (string.IsNullOrWhiteSpace(req.Name))
            return BadRequest(new { error = "Name is required." });
        if (await _db.Products.AnyAsync(p => p.Sku == req.Sku.Trim(), ct))
            return BadRequest(new { error = $"SKU \"{req.Sku.Trim()}\" already exists." });

        var product = new Product
        {
            Id = Guid.NewGuid(),
            Sku = req.Sku.Trim(),
            Barcode = string.IsNullOrWhiteSpace(req.Barcode) ? null : req.Barcode.Trim(),
            Name = req.Name.Trim(),
            Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim(),
            Category = string.IsNullOrWhiteSpace(req.Category) ? null : req.Category.Trim(),
            Manufacturer = string.IsNullOrWhiteSpace(req.Manufacturer) ? null : req.Manufacturer.Trim(),
            ItemType = string.IsNullOrWhiteSpace(req.ItemType) ? null : req.ItemType.Trim(),
            SupplierId = req.SupplierId,
            Cost = req.Cost,
            QtyOnHand = req.QtyOnHand,
            Active = true,
            PricingMethod = string.IsNullOrWhiteSpace(req.PricingMethod) ? "default" : req.PricingMethod.Trim(),
            CustomMarkupPercent = req.CustomMarkupPercent,
            FixedSellPrice = req.FixedSellPrice,
            MinSellPrice = req.MinSellPrice,
            PriceLocked = req.PriceLocked ?? false
        };

        if (req.SellPrice > 0)
        {
            product.SellPrice = req.SellPrice;
        }
        else
        {
            var resolution = await _pricing.ResolveAsync(product, ct);
            product.SellPrice = resolution.SellPrice;
        }

        _db.Products.Add(product);
        await _db.SaveChangesAsync(ct);
        return await MapWithPricingAsync(product, false, ct);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Owner},{Roles.Dev}")]
    public async Task<ActionResult<ProductDto>> Update(Guid id, [FromBody] UpdateProductRequest req, CancellationToken ct)
    {
        var p = await _db.Products.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p == null) return NotFound();

        if (req.Sku != null)
        {
            var trimmed = req.Sku.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
                return BadRequest(new { error = "SKU cannot be empty." });
            if (trimmed != p.Sku && await _db.Products.AnyAsync(x => x.Sku == trimmed && x.Id != id, ct))
                return BadRequest(new { error = $"SKU \"{trimmed}\" already exists." });
            p.Sku = trimmed;
        }

        if (req.Name != null)
        {
            if (string.IsNullOrWhiteSpace(req.Name))
                return BadRequest(new { error = "Name cannot be empty." });
            p.Name = req.Name.Trim();
        }

        if (req.Barcode != null) p.Barcode = string.IsNullOrWhiteSpace(req.Barcode) ? null : req.Barcode.Trim();
        if (req.Description != null) p.Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim();
        if (req.Category != null) p.Category = string.IsNullOrWhiteSpace(req.Category) ? null : req.Category.Trim();
        if (req.Manufacturer != null) p.Manufacturer = string.IsNullOrWhiteSpace(req.Manufacturer) ? null : req.Manufacturer.Trim();
        if (req.ItemType != null) p.ItemType = string.IsNullOrWhiteSpace(req.ItemType) ? null : req.ItemType.Trim();
        if (req.SupplierId.HasValue) p.SupplierId = req.SupplierId;
        if (req.Cost.HasValue) p.Cost = req.Cost.Value;
        if (req.QtyOnHand.HasValue) p.QtyOnHand = req.QtyOnHand.Value;
        if (req.Active.HasValue) p.Active = req.Active.Value;

        if (req.PricingMethod != null) p.PricingMethod = string.IsNullOrWhiteSpace(req.PricingMethod) ? "default" : req.PricingMethod.Trim();
        if (req.CustomMarkupPercent.HasValue) p.CustomMarkupPercent = req.CustomMarkupPercent;
        if (req.FixedSellPrice.HasValue) p.FixedSellPrice = req.FixedSellPrice;
        if (req.MinSellPrice.HasValue) p.MinSellPrice = req.MinSellPrice;
        if (req.PriceLocked.HasValue) p.PriceLocked = req.PriceLocked.Value;

        if (req.SellPrice.HasValue && req.SellPrice.Value > 0)
        {
            p.SellPrice = req.SellPrice.Value;
        }
        else if (!p.PriceLocked)
        {
            var resolution = await _pricing.ResolveAsync(p, ct);
            if (resolution.SellPrice > 0)
                p.SellPrice = resolution.SellPrice;
        }

        p.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);
        return await MapWithPricingAsync(p, false, ct);
    }

    /// <summary>
    /// Owner/Dev-only manual stock-on-hand correction. Always writes a StockReceipt of type
    /// Adjustment so the change is visible on the product's movement history. Reason is
    /// required — there is no silent qty edit path for managers.
    /// </summary>
    [HttpPost("{id:guid}/adjust-stock")]
    [Authorize(Roles = $"{Roles.Owner},{Roles.Dev}")]
    public async Task<ActionResult<ProductDto>> AdjustStock(
        Guid id,
        [FromBody] AdjustStockRequest req,
        CancellationToken ct)
    {
        if (req.NewQtyOnHand < 0)
            return BadRequest(new { error = "New quantity cannot be negative." });
        var reason = (req.Reason ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(reason))
            return BadRequest(new { error = "A reason is required for stock adjustments." });
        if (reason.Length > 500)
            reason = reason[..500];

        var p = await _db.Products.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p == null) return NotFound();

        var delta = req.NewQtyOnHand - p.QtyOnHand;
        if (delta == 0)
            return await MapWithPricingAsync(p, false, ct);

        p.QtyOnHand = req.NewQtyOnHand;
        p.UpdatedAt = DateTimeOffset.UtcNow;

        _db.StockReceipts.Add(new StockReceipt
        {
            Id = Guid.NewGuid(),
            ProductId = p.Id,
            SupplierId = null,
            Type = StockReceiptType.Adjustment,
            Quantity = delta,
            CostPrice = null,
            Notes = reason,
            ProcessedBy = User.Identity?.Name,
            CreatedAt = DateTimeOffset.UtcNow
        });

        await _db.SaveChangesAsync(ct);
        return await MapWithPricingAsync(p, false, ct);
    }

    private async Task<ProductDto> MapWithPricingAsync(Product p, bool hideCost, CancellationToken ct, ActiveSpecialInfo? special = null)
    {
        var dto = Map(p, hideCost, special);
        try
        {
            var res = await _pricing.ResolveAsync(p, ct);
            dto.PricingSource = res.Source;
            dto.MinAllowedPrice = res.MinAllowedPrice > 0 ? res.MinAllowedPrice : null;
        }
        catch
        {
            /* best effort — never fail a product fetch over pricing metadata */
        }
        return dto;
    }

    private async Task<Promotion?> FindActivePromoAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var active = await _db.Promotions.AsNoTracking().Where(p => p.IsActive).ToListAsync(ct);
        return active
            .Where(p => !p.StartsAt.HasValue || p.StartsAt <= now)
            .Where(p => !p.EndsAt.HasValue || p.EndsAt >= now)
            .FirstOrDefault();
    }

    private async Task<HashSet<Guid>> GetActiveSpecialProductIds(CancellationToken ct)
    {
        var promo = await FindActivePromoAsync(ct);
        var specialsQuery = _db.ProductSpecials.AsNoTracking().Where(s => s.IsActive);
        specialsQuery = promo != null
            ? specialsQuery.Where(s => s.PromotionId == null || s.PromotionId == promo.Id)
            : specialsQuery.Where(s => s.PromotionId == null);
        var ids = await specialsQuery.Select(s => s.ProductId).Distinct().ToListAsync(ct);
        return ids.ToHashSet();
    }

    private record ActiveSpecialInfo(decimal EffectivePrice, string Label);

    private async Task<Dictionary<Guid, ActiveSpecialInfo>> LoadActiveSpecialsMap(List<Guid> productIds, CancellationToken ct)
    {
        if (productIds.Count == 0) return new();
        var promo = await FindActivePromoAsync(ct);

        var specialsQuery = _db.ProductSpecials.AsNoTracking()
            .Include(s => s.Promotion)
            .Include(s => s.Product)
            .Where(s => s.IsActive && productIds.Contains(s.ProductId));
        specialsQuery = promo != null
            ? specialsQuery.Where(s => s.PromotionId == null || s.PromotionId == promo.Id)
            : specialsQuery.Where(s => s.PromotionId == null);

        var specials = await specialsQuery.ToListAsync(ct);
        var result = new Dictionary<Guid, ActiveSpecialInfo>();
        foreach (var s in specials)
        {
            var baseSell = s.Product?.SellPrice ?? 0;
            decimal effective;
            if (s.SpecialPrice.HasValue) effective = s.SpecialPrice.Value;
            else if (s.DiscountPercent.HasValue) effective = RoundUpR10(baseSell * (1 - s.DiscountPercent.Value / 100m));
            else continue;
            var label = s.Promotion?.Name ?? "Special";
            result.TryAdd(s.ProductId, new ActiveSpecialInfo(effective, label));
        }

        if (promo != null && promo.DiscountPercent > 0)
        {
            var allProducts = await _db.Products.AsNoTracking()
                .Where(p => productIds.Contains(p.Id))
                .ToListAsync(ct);
            foreach (var p in allProducts)
            {
                if (result.ContainsKey(p.Id)) continue;
                var effective = RoundUpR10(p.SellPrice * (1 - promo.DiscountPercent / 100m));
                result[p.Id] = new ActiveSpecialInfo(effective, promo.Name);
            }
        }

        return result;
    }

    private async Task<bool> ShouldHideCostAsync(CancellationToken ct)
    {
        // Role hierarchy: Dev > Owner > Admin > Sales. Seeded/promoted users often carry Sales
        // alongside a higher role; cost must only be hidden from pure-Sales accounts.
        if (User.IsInRole(Roles.Dev) || User.IsInRole(Roles.Owner) || User.IsInRole(Roles.Admin))
            return false;
        if (!User.IsInRole(Roles.Sales)) return false;
        var settings = await _db.PricingSettings.AsNoTracking().FirstOrDefaultAsync(ct);
        var hide = settings?.HideCostForSalesRole ?? _app.HideCostForSalesRole;
        return hide;
    }

    /// <summary>
    /// Finds products that share the same SKU and merges them into a single record.
    /// Quantities are summed; all FK references are re-pointed to the survivor.
    /// </summary>
    [HttpPost("merge-duplicates")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Owner},{Roles.Dev}")]
    public async Task<ActionResult> MergeDuplicates(CancellationToken ct)
    {
        var dupes = await _db.Products
            .GroupBy(p => p.Sku)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToListAsync(ct);

        if (dupes.Count == 0)
            return Ok(new { merged = 0 });

        int merged = 0;

        foreach (var sku in dupes)
        {
            var group = (await _db.Products
                .Where(p => p.Sku == sku)
                .ToListAsync(ct))
                .OrderByDescending(p => p.UpdatedAt ?? p.CreatedAt)
                .ToList();

            var survivor = group[0];
            var victims = group.Skip(1).ToList();
            var victimIds = victims.Select(v => v.Id).ToList();

            survivor.QtyOnHand += victims.Sum(v => v.QtyOnHand);
            survivor.QtyConsignment += victims.Sum(v => v.QtyConsignment);
            survivor.UpdatedAt = DateTimeOffset.UtcNow;

            await _db.InvoiceLines
                .Where(il => victimIds.Contains(il.ProductId))
                .ExecuteUpdateAsync(s => s.SetProperty(il => il.ProductId, survivor.Id), ct);

            await _db.StockReceipts
                .Where(sr => victimIds.Contains(sr.ProductId))
                .ExecuteUpdateAsync(s => s.SetProperty(sr => sr.ProductId, survivor.Id), ct);

            await _db.StocktakeLines
                .Where(sl => victimIds.Contains(sl.ProductId))
                .ExecuteUpdateAsync(s => s.SetProperty(sl => sl.ProductId, survivor.Id), ct);

            await _db.ConsignmentBatchLines
                .Where(cl => victimIds.Contains(cl.ProductId))
                .ExecuteUpdateAsync(s => s.SetProperty(cl => cl.ProductId, survivor.Id), ct);

            var survivorSpecialProductIds = await _db.ProductSpecials
                .Where(ps => ps.ProductId == survivor.Id)
                .Select(ps => ps.ProductId)
                .ToListAsync(ct);

            await _db.ProductSpecials
                .Where(ps => victimIds.Contains(ps.ProductId))
                .ExecuteUpdateAsync(s => s.SetProperty(ps => ps.ProductId, survivor.Id), ct);

            _db.Products.RemoveRange(victims);
            merged += victims.Count;
        }

        await _db.SaveChangesAsync(ct);

        return Ok(new { merged, skus = dupes });
    }

    private static ProductDto Map(Domain.Product p, bool hideCost, ActiveSpecialInfo? special = null) => new()
    {
        Id = p.Id,
        SupplierId = p.SupplierId,
        SupplierName = p.Supplier?.Name,
        Sku = p.Sku,
        Barcode = p.Barcode,
        Name = p.Name,
        Description = p.Description,
        Category = p.Category,
        Manufacturer = p.Manufacturer,
        ItemType = p.ItemType,
        Cost = hideCost ? null : p.Cost,
        SellPrice = p.SellPrice,
        QtyOnHand = p.QtyOnHand,
        QtyConsignment = p.QtyConsignment,
        Active = p.Active,
        Warning = !hideCost && PricingCalculator.IsBelowDistributorCost(p.SellPrice, p.Cost)
            ? $"Sell R{p.SellPrice:0} < distributor R{PricingCalculator.DistributorFloor(p.Cost):0.00}"
            : null,
        SpecialPrice = special?.EffectivePrice,
        SpecialLabel = special?.Label,
        PricingMethod = string.IsNullOrWhiteSpace(p.PricingMethod) ? "default" : p.PricingMethod,
        CustomMarkupPercent = p.CustomMarkupPercent,
        FixedSellPrice = p.FixedSellPrice,
        MinSellPrice = p.MinSellPrice,
        PriceLocked = p.PriceLocked
    };
}
