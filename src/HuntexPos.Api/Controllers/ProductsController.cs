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
    public async Task<ActionResult<List<ProductDto>>> Search([FromQuery] ProductSearchQuery q, CancellationToken ct)
    {
        var hideCost = await ShouldHideCostAsync(ct);
        var query = _db.Products.AsNoTracking().Include(p => p.Supplier).Where(p => p.Active);

        if (q.SupplierId.HasValue)
            query = query.Where(p => p.SupplierId == q.SupplierId);
        if (!string.IsNullOrWhiteSpace(q.Barcode))
        {
            var b = q.Barcode.Trim();
            query = query.Where(p => p.Barcode == b || p.Sku == b);
        }
        query = ApplyPosSearchFilter(query, q.Q);

        var list = await query.OrderBy(p => p.Name).Take(Math.Clamp(q.Take, 1, 200)).ToListAsync(ct);
        return list.Select(p => Map(p, hideCost)).ToList();
    }

    /// <summary>Browse the full inventory with pagination (POS / stock screen).</summary>
    [HttpGet("stocklist")]
    public async Task<ActionResult<StocklistPageDto>> Stocklist([FromQuery] ProductStocklistQuery q, CancellationToken ct)
    {
        var hideCost = await ShouldHideCostAsync(ct);
        var query = _db.Products.AsNoTracking().Include(p => p.Supplier).AsQueryable();
        if (!q.IncludeInactive)
            query = query.Where(p => p.Active);
        if (q.SupplierId.HasValue)
            query = query.Where(p => p.SupplierId == q.SupplierId);
        query = ApplyStocklistSearchFilter(query, q.Q);

        var take = Math.Clamp(q.Take, 1, 10_000);
        var skip = Math.Max(0, q.Skip);
        var total = await query.CountAsync(ct);
        var list = await query.OrderBy(p => p.Name).Skip(skip).Take(take).ToListAsync(ct);
        return new StocklistPageDto
        {
            Total = total,
            Skip = skip,
            Take = take,
            Items = list.Select(p => Map(p, hideCost)).ToList()
        };
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
        sb.AppendLine("Sku,Barcode,Name,Category,Manufacturer,ItemType,Supplier,Cost,SellPrice,QtyOnHand,Active");
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

    private static string EscapeForLike(string s) =>
        s.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

    private const string LikeEscapeChar = "\\";

    /// <summary>POS / stocktake product search: name, SKU, barcode, description, supplier.</summary>
    private static IQueryable<Product> ApplyPosSearchFilter(IQueryable<Product> query, string? q)
    {
        var terms = string.IsNullOrWhiteSpace(q) ? null : SplitSearchTerms(q);
        if (terms is not { Count: > 0 })
            return query;

        foreach (var term in terms)
        {
            var pattern = $"%{EscapeForLike(term)}%";
            query = query.Where(p =>
                EF.Functions.Like(p.Name, pattern, LikeEscapeChar) ||
                EF.Functions.Like(p.Sku, pattern, LikeEscapeChar) ||
                (p.Barcode != null && EF.Functions.Like(p.Barcode, pattern, LikeEscapeChar)) ||
                (p.Description != null && EF.Functions.Like(p.Description, pattern, LikeEscapeChar)) ||
                (p.Supplier != null && EF.Functions.Like(p.Supplier.Name, pattern, LikeEscapeChar)));
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
            var pattern = $"%{EscapeForLike(term)}%";
            query = query.Where(p =>
                EF.Functions.Like(p.Name, pattern, LikeEscapeChar) ||
                EF.Functions.Like(p.Sku, pattern, LikeEscapeChar) ||
                (p.Barcode != null && EF.Functions.Like(p.Barcode, pattern, LikeEscapeChar)) ||
                (p.Category != null && EF.Functions.Like(p.Category, pattern, LikeEscapeChar)) ||
                (p.Manufacturer != null && EF.Functions.Like(p.Manufacturer, pattern, LikeEscapeChar)) ||
                (p.ItemType != null && EF.Functions.Like(p.ItemType, pattern, LikeEscapeChar)) ||
                (p.Description != null && EF.Functions.Like(p.Description, pattern, LikeEscapeChar)) ||
                (p.Supplier != null && EF.Functions.Like(p.Supplier.Name, pattern, LikeEscapeChar)));
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
        var sell = PricingCalculator.ApplyRounding(req.SellPrice, settings);

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
            p.SellPrice = PricingCalculator.ApplyRounding(req.SellPrice.Value, settings);
        }
        if (req.QtyOnHand.HasValue) p.QtyOnHand = req.QtyOnHand.Value;
        if (req.Active.HasValue) p.Active = req.Active.Value;
        p.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);
        return Map(p, false);
    }

    private async Task<bool> ShouldHideCostAsync(CancellationToken ct)
    {
        if (!User.IsInRole(Roles.Sales)) return false;
        var settings = await _db.PricingSettings.AsNoTracking().FirstOrDefaultAsync(ct);
        var hide = settings?.HideCostForSalesRole ?? _app.HideCostForSalesRole;
        return hide;
    }

    private static ProductDto Map(Domain.Product p, bool hideCost) => new()
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
        Active = p.Active,
        Warning = !hideCost && PricingCalculator.IsBelowDistributorCost(p.SellPrice, p.Cost)
            ? $"Sell R{p.SellPrice:0} < distributor R{PricingCalculator.DistributorFloor(p.Cost):0.00}"
            : null
    };
}
