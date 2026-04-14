using System.Globalization;
using System.Text;
using HuntexPos.Api.Data;
using HuntexPos.Api.Domain;
using HuntexPos.Api.DTOs;
using HuntexPos.Api.Options;
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
        if (!string.IsNullOrWhiteSpace(q.Q))
        {
            var s = q.Q.Trim();
            query = query.Where(p =>
                EF.Functions.Like(p.Name, $"%{s}%") ||
                EF.Functions.Like(p.Sku, $"%{s}%") ||
                (p.Barcode != null && EF.Functions.Like(p.Barcode, $"%{s}%")));
        }

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
        if (!string.IsNullOrWhiteSpace(q.Q))
        {
            var s = q.Q.Trim();
            query = query.Where(p =>
                EF.Functions.Like(p.Name, $"%{s}%") ||
                EF.Functions.Like(p.Sku, $"%{s}%") ||
                (p.Barcode != null && EF.Functions.Like(p.Barcode, $"%{s}%")) ||
                (p.Category != null && EF.Functions.Like(p.Category, $"%{s}%")));
        }

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
        if (!string.IsNullOrWhiteSpace(q))
        {
            var s = q.Trim();
            query = query.Where(p =>
                EF.Functions.Like(p.Name, $"%{s}%") ||
                EF.Functions.Like(p.Sku, $"%{s}%") ||
                (p.Barcode != null && EF.Functions.Like(p.Barcode, $"%{s}%")) ||
                (p.Category != null && EF.Functions.Like(p.Category, $"%{s}%")));
        }

        var list = await query.OrderBy(p => p.Name).ToListAsync(ct);
        var sb = new StringBuilder();
        sb.AppendLine("Sku,Barcode,Name,Category,Supplier,Cost,SellPrice,QtyOnHand,Active");
        foreach (var p in list)
        {
            sb.AppendLine(string.Join(",",
                Csv(p.Sku),
                Csv(p.Barcode ?? ""),
                Csv(p.Name),
                Csv(p.Category ?? ""),
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

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ProductDto>> Get(Guid id, CancellationToken ct)
    {
        var hideCost = await ShouldHideCostAsync(ct);
        var p = await _db.Products.AsNoTracking().Include(x => x.Supplier).FirstOrDefaultAsync(x => x.Id == id, ct);
        return p == null ? NotFound() : Map(p, hideCost);
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
        Cost = hideCost ? null : p.Cost,
        SellPrice = p.SellPrice,
        QtyOnHand = p.QtyOnHand,
        Active = p.Active
    };
}
