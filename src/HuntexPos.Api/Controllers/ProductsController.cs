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

    public ProductsController(HuntexDbContext db, IOptions<AppOptions> app)
    {
        _db = db;
        _app = app.Value;
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
    public async Task<List<ProductSpecialDto>> GetProductSpecials(Guid productId, CancellationToken ct)
    {
        var specials = await _db.ProductSpecials.AsNoTracking()
            .Include(s => s.Product)
            .Include(s => s.Promotion)
            .Where(s => s.ProductId == productId)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(ct);
        return specials.Select(s =>
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

    /// <summary>Generate a 62mm label PDF for one product (Brother QL-800 / DK-22205).</summary>
    [HttpGet("{productId:guid}/label")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Owner},{Roles.Dev}")]
    public async Task<IActionResult> GetLabel(Guid productId, [FromQuery] int copies = 1, [FromQuery] bool promo = false, CancellationToken ct = default)
    {
        var product = await _db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == productId, ct);
        if (product == null) return NotFound();
        copies = Math.Clamp(copies, 1, 50);

        try
        {
            var pricing = promo
                ? await ResolvePromoPricing(product, ct)
                : new LabelPdfService.LabelPricing(product.SellPrice, null, null);

            var pdf = LabelPdfService.BuildSingleLabel(product, pricing, copies);
            return File(pdf, "application/pdf", $"label-{product.Sku}.pdf");
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message, detail = ex.InnerException?.Message });
        }
    }

    /// <summary>Generate a multi-product label PDF (one label per product).</summary>
    [HttpPost("labels")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Owner},{Roles.Dev}")]
    public async Task<IActionResult> GetLabels([FromBody] LabelBatchRequest req, CancellationToken ct = default)
    {
        if (req.ProductIds == null || req.ProductIds.Count == 0)
            return BadRequest(new { error = "At least one productId is required." });

        var ids = req.ProductIds.Take(200).ToList();
        var products = await _db.Products.AsNoTracking().Where(p => ids.Contains(p.Id)).ToListAsync(ct);
        if (products.Count == 0) return NotFound();

        try
        {
            var pricingMap = req.UsePromo
                ? await ResolvePromoPricingBatch(products, ct)
                : products.ToDictionary(p => p.Id, p => new LabelPdfService.LabelPricing(p.SellPrice, null, null));

            var items = ids
                .Select(id => products.FirstOrDefault(x => x.Id == id))
                .Where(p => p != null)
                .Select(p => (p!, pricingMap.GetValueOrDefault(p!.Id, new LabelPdfService.LabelPricing(p.SellPrice, null, null))));

            var pdf = LabelPdfService.BuildMultipleLabels(items);
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

    private static LabelPdfService.LabelPricing ComputeLabelPricing(Product product, Promotion? promo, ProductSpecial? special)
    {
        var baseSell = product.SellPrice;
        decimal effective;

        if (special != null)
        {
            if (special.SpecialPrice.HasValue)
                effective = special.SpecialPrice.Value;
            else if (special.DiscountPercent.HasValue)
                effective = Math.Round(baseSell * (1 - special.DiscountPercent.Value / 100m), 2);
            else
                effective = baseSell;
        }
        else if (promo != null && promo.DiscountPercent > 0)
        {
            effective = Math.Round(baseSell * (1 - promo.DiscountPercent / 100m), 2);
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
        return p == null ? NotFound() : Map(p, hideCost);
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

        var settings = await _db.PricingSettings.AsNoTracking().FirstOrDefaultAsync(ct) ?? new PricingSettings();
        var sell = req.SellPrice > 0
            ? PricingCalculator.ApplyRounding(req.SellPrice, settings)
            : req.Cost > 0
                ? PricingCalculator.ComputeSellPrice(req.Cost, settings)
                : 0m;

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
            SellPrice = sell,
            QtyOnHand = req.QtyOnHand,
            Active = true
        };
        _db.Products.Add(product);
        await _db.SaveChangesAsync(ct);
        return Map(product, false);
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
        if (req.SellPrice.HasValue)
        {
            var settings = await _db.PricingSettings.AsNoTracking().FirstOrDefaultAsync(ct) ?? new PricingSettings();
            p.SellPrice = req.SellPrice.Value > 0
                ? PricingCalculator.ApplyRounding(req.SellPrice.Value, settings)
                : p.Cost > 0
                    ? PricingCalculator.ComputeSellPrice(p.Cost, settings)
                    : 0m;
        }
        else if (req.Cost.HasValue && p.Cost > 0)
        {
            var settings = await _db.PricingSettings.AsNoTracking().FirstOrDefaultAsync(ct) ?? new PricingSettings();
            p.SellPrice = PricingCalculator.ComputeSellPrice(p.Cost, settings);
        }
        if (req.QtyOnHand.HasValue) p.QtyOnHand = req.QtyOnHand.Value;
        if (req.Active.HasValue) p.Active = req.Active.Value;
        p.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);
        return Map(p, false);
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
            else if (s.DiscountPercent.HasValue) effective = Math.Round(baseSell * (1 - s.DiscountPercent.Value / 100m), 2);
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
                var effective = Math.Round(p.SellPrice * (1 - promo.DiscountPercent / 100m), 2);
                result[p.Id] = new ActiveSpecialInfo(effective, promo.Name);
            }
        }

        return result;
    }

    private async Task<bool> ShouldHideCostAsync(CancellationToken ct)
    {
        if (!User.IsInRole(Roles.Sales)) return false;
        var settings = await _db.PricingSettings.AsNoTracking().FirstOrDefaultAsync(ct);
        var hide = settings?.HideCostForSalesRole ?? _app.HideCostForSalesRole;
        return hide;
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
        SpecialLabel = special?.Label
    };
}
