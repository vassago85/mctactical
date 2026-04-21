using HuntexPos.Api.Data;
using HuntexPos.Api.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HuntexPos.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = $"{Roles.Admin},{Roles.Owner},{Roles.Dev}")]
public class SuppliersController : ControllerBase
{
    private readonly HuntexDbContext _db;

    public SuppliersController(HuntexDbContext db) => _db = db;

    public record SupplierDto(
        Guid Id,
        string Name,
        string? DefaultCurrency,
        string? Notes,
        bool IsActive,
        int ProductCount,
        int ReceiptCount,
        int ConsignmentBatchCount,
        int PricingRuleCount);

    /// <summary>
    /// List wholesalers. By default only active ones are returned so pickers
    /// stay clean; set <paramref name="includeInactive"/> = true from the
    /// management page to also list soft-deleted ones.
    /// </summary>
    [HttpGet]
    public async Task<List<SupplierDto>> List([FromQuery] bool includeInactive = false, CancellationToken ct = default)
    {
        var query = _db.Suppliers.AsNoTracking();
        if (!includeInactive) query = query.Where(s => s.IsActive);

        // Counts are aggregated via grouped subqueries to keep this one round-trip.
        return await query
            .OrderBy(s => s.Name)
            .Select(s => new SupplierDto(
                s.Id,
                s.Name,
                s.DefaultCurrency,
                s.Notes,
                s.IsActive,
                _db.Products.Count(p => p.SupplierId == s.Id),
                _db.StockReceipts.Count(r => r.SupplierId == s.Id),
                _db.ConsignmentBatches.Count(b => b.SupplierId == s.Id),
                _db.PricingRules.Count(r => r.SupplierId == s.Id)))
            .ToListAsync(ct);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SupplierDto>> Get(Guid id, CancellationToken ct)
    {
        var s = await _db.Suppliers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (s == null) return NotFound();
        return new SupplierDto(
            s.Id, s.Name, s.DefaultCurrency, s.Notes, s.IsActive,
            await _db.Products.CountAsync(p => p.SupplierId == s.Id, ct),
            await _db.StockReceipts.CountAsync(r => r.SupplierId == s.Id, ct),
            await _db.ConsignmentBatches.CountAsync(b => b.SupplierId == s.Id, ct),
            await _db.PricingRules.CountAsync(r => r.SupplierId == s.Id, ct));
    }

    [HttpPost]
    public async Task<ActionResult<SupplierDto>> Create([FromBody] UpsertSupplierRequest req, CancellationToken ct)
    {
        var name = (req.Name ?? string.Empty).Trim();
        if (name.Length == 0) return BadRequest(new { error = "Name is required." });

        var exists = await _db.Suppliers.AnyAsync(s => s.Name.ToLower() == name.ToLower(), ct);
        if (exists) return Conflict(new { error = $"A wholesaler named \"{name}\" already exists." });

        var s = new Supplier
        {
            Id = Guid.NewGuid(),
            Name = name,
            DefaultCurrency = Trim(req.DefaultCurrency),
            Notes = Trim(req.Notes),
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        _db.Suppliers.Add(s);
        await _db.SaveChangesAsync(ct);
        return await Get(s.Id, ct);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<SupplierDto>> Update(Guid id, [FromBody] UpsertSupplierRequest req, CancellationToken ct)
    {
        var s = await _db.Suppliers.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (s == null) return NotFound();

        var name = (req.Name ?? string.Empty).Trim();
        if (name.Length == 0) return BadRequest(new { error = "Name is required." });

        if (!string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase))
        {
            var clash = await _db.Suppliers.AnyAsync(x => x.Id != id && x.Name.ToLower() == name.ToLower(), ct);
            if (clash) return Conflict(new { error = $"A wholesaler named \"{name}\" already exists." });
        }

        s.Name = name;
        s.DefaultCurrency = Trim(req.DefaultCurrency);
        s.Notes = Trim(req.Notes);
        if (req.IsActive.HasValue) s.IsActive = req.IsActive.Value;
        s.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return await Get(s.Id, ct);
    }

    /// <summary>
    /// Soft-delete: flips <c>IsActive</c> to false so the wholesaler disappears
    /// from new-record pickers but historical references stay intact.
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        var s = await _db.Suppliers.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (s == null) return NotFound();
        s.IsActive = false;
        s.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/reactivate")]
    public async Task<ActionResult<SupplierDto>> Reactivate(Guid id, CancellationToken ct)
    {
        var s = await _db.Suppliers.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (s == null) return NotFound();
        s.IsActive = true;
        s.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return await Get(s.Id, ct);
    }

    private static string? Trim(string? v)
    {
        if (v == null) return null;
        var t = v.Trim();
        return t.Length == 0 ? null : t;
    }

    public class UpsertSupplierRequest
    {
        public string Name { get; set; } = string.Empty;
        public string? DefaultCurrency { get; set; }
        public string? Notes { get; set; }
        public bool? IsActive { get; set; }
    }
}
