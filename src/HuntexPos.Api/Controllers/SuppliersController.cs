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

    public record SupplierDto(Guid Id, string Name, string? DefaultCurrency, string? Notes);

    [HttpGet]
    public async Task<List<SupplierDto>> List(CancellationToken ct) =>
        await _db.Suppliers.AsNoTracking().OrderBy(s => s.Name)
            .Select(s => new SupplierDto(s.Id, s.Name, s.DefaultCurrency, s.Notes))
            .ToListAsync(ct);

    [HttpPost]
    public async Task<SupplierDto> Create([FromBody] CreateSupplierRequest req, CancellationToken ct)
    {
        var s = new Supplier
        {
            Id = Guid.NewGuid(),
            Name = req.Name.Trim(),
            DefaultCurrency = req.DefaultCurrency,
            Notes = req.Notes
        };
        _db.Suppliers.Add(s);
        await _db.SaveChangesAsync(ct);
        return new SupplierDto(s.Id, s.Name, s.DefaultCurrency, s.Notes);
    }

    public class CreateSupplierRequest
    {
        public string Name { get; set; } = string.Empty;
        public string? DefaultCurrency { get; set; }
        public string? Notes { get; set; }
    }
}
