using HuntexPos.Api.Data;
using HuntexPos.Api.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HuntexPos.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SalespeopleController : ControllerBase
{
    private const string ManagerRoles = $"{Roles.Admin},{Roles.Owner},{Roles.Dev}";
    private readonly HuntexDbContext _db;

    public SalespeopleController(HuntexDbContext db) => _db = db;

    public record SalespersonDto(
        Guid Id,
        string Name,
        decimal CommissionPercent,
        string CommissionBasis,
        bool IsActive);

    /// <summary>
    /// Active salespeople for the checkout picker. Sales staff need this too, so it is open to
    /// every till role; they only get names (no commission rates, no inactive people). The
    /// management page passes <paramref name="includeInactive"/>.
    /// </summary>
    [HttpGet]
    [Authorize(Roles = $"{Roles.Sales},{ManagerRoles}")]
    public async Task<List<SalespersonDto>> List([FromQuery] bool includeInactive = false, CancellationToken ct = default)
    {
        var isManager = User.IsInRole(Roles.Admin) || User.IsInRole(Roles.Owner) || User.IsInRole(Roles.Dev);
        var query = _db.Salespeople.AsNoTracking();
        if (!includeInactive || !isManager) query = query.Where(s => s.IsActive);
        var rows = await query.ToListAsync(ct);
        return rows
            .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .Select(s => isManager ? ToDto(s) : ToDto(s) with { CommissionPercent = 0 })
            .ToList();
    }

    [HttpPost]
    [Authorize(Roles = ManagerRoles)]
    public async Task<ActionResult<SalespersonDto>> Create([FromBody] UpsertSalespersonRequest req, CancellationToken ct)
    {
        var name = (req.Name ?? string.Empty).Trim();
        var error = Validate(name, req);
        if (error != null) return BadRequest(new { error });

        var exists = await _db.Salespeople.AnyAsync(s => s.Name.ToLower() == name.ToLower(), ct);
        if (exists) return Conflict(new { error = $"A salesperson named \"{name}\" already exists." });

        var s = new Salesperson
        {
            Id = Guid.NewGuid(),
            Name = name,
            CommissionPercent = req.CommissionPercent,
            CommissionBasis = req.CommissionBasis,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        _db.Salespeople.Add(s);
        await _db.SaveChangesAsync(ct);
        return ToDto(s);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = ManagerRoles)]
    public async Task<ActionResult<SalespersonDto>> Update(Guid id, [FromBody] UpsertSalespersonRequest req, CancellationToken ct)
    {
        var s = await _db.Salespeople.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (s == null) return NotFound();

        var name = (req.Name ?? string.Empty).Trim();
        var error = Validate(name, req);
        if (error != null) return BadRequest(new { error });

        if (!string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase))
        {
            var clash = await _db.Salespeople.AnyAsync(x => x.Id != id && x.Name.ToLower() == name.ToLower(), ct);
            if (clash) return Conflict(new { error = $"A salesperson named \"{name}\" already exists." });
        }

        s.Name = name;
        s.CommissionPercent = req.CommissionPercent;
        s.CommissionBasis = req.CommissionBasis;
        if (req.IsActive.HasValue) s.IsActive = req.IsActive.Value;
        s.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return ToDto(s);
    }

    private static string? Validate(string name, UpsertSalespersonRequest req)
    {
        if (name.Length == 0) return "Name is required.";
        if (name.Length > 128) return "Name is too long.";
        if (req.CommissionPercent < 0 || req.CommissionPercent > 100) return "Commission must be between 0% and 100%.";
        if (!Enum.IsDefined(req.CommissionBasis)) return "Unknown commission basis.";
        return null;
    }

    private static SalespersonDto ToDto(Salesperson s) =>
        new(s.Id, s.Name, s.CommissionPercent, s.CommissionBasis.ToString(), s.IsActive);

    public class UpsertSalespersonRequest
    {
        public string Name { get; set; } = string.Empty;
        public decimal CommissionPercent { get; set; }
        public CommissionBasis CommissionBasis { get; set; } = CommissionBasis.SalesExVat;
        public bool? IsActive { get; set; }
    }
}
