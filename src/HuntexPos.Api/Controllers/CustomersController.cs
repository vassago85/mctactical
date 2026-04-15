using HuntexPos.Api.Data;
using HuntexPos.Api.Domain;
using HuntexPos.Api.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HuntexPos.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = $"{Roles.Sales},{Roles.Admin},{Roles.Owner},{Roles.Dev}")]
public class CustomersController : ControllerBase
{
    private readonly HuntexDbContext _db;
    public CustomersController(HuntexDbContext db) => _db = db;

    [HttpGet]
    public async Task<List<CustomerDto>> List([FromQuery] string? q, CancellationToken ct)
    {
        var query = _db.Customers.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim().ToLower();
            query = query.Where(c =>
                c.Email.ToLower().Contains(term) ||
                (c.Name != null && c.Name.ToLower().Contains(term)) ||
                (c.Company != null && c.Company.ToLower().Contains(term)) ||
                (c.Phone != null && c.Phone.Contains(term)));
        }
        var customers = await query.ToListAsync(ct);
        return customers
            .OrderByDescending(c => c.UpdatedAt)
            .Take(50)
            .Select(Map)
            .ToList();
    }

    [HttpGet("by-email")]
    public async Task<ActionResult<CustomerDto>> GetByEmail([FromQuery] string email, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(email))
            return BadRequest(new { error = "Email is required" });

        var customer = await _db.Customers.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Email == email.Trim().ToLower(), ct);
        if (customer == null)
            return NotFound();
        return Map(customer);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CustomerDto>> Get(Guid id, CancellationToken ct)
    {
        var customer = await _db.Customers.FindAsync(new object[] { id }, ct);
        if (customer == null) return NotFound();
        return Map(customer);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Owner},{Roles.Dev}")]
    public async Task<ActionResult<CustomerDto>> Update(Guid id, [FromBody] UpdateCustomerRequest req, CancellationToken ct)
    {
        var customer = await _db.Customers.FindAsync(new object[] { id }, ct);
        if (customer == null) return NotFound();

        if (!string.IsNullOrWhiteSpace(req.Email))
        {
            var normalised = req.Email.Trim().ToLower();
            if (normalised != customer.Email)
            {
                var existing = await _db.Customers.AnyAsync(c => c.Email == normalised && c.Id != id, ct);
                if (existing)
                    return Conflict(new { error = "Another customer already uses that email." });
                customer.Email = normalised;
            }
        }

        if (req.Name != null) customer.Name = req.Name;
        if (req.Phone != null) customer.Phone = req.Phone;
        if (req.Company != null) customer.Company = req.Company;
        if (req.Address != null) customer.Address = req.Address;
        if (req.VatNumber != null) customer.VatNumber = req.VatNumber;
        if (req.CustomerType != null) customer.CustomerType = req.CustomerType;
        customer.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);
        return Map(customer);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Owner},{Roles.Dev}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var customer = await _db.Customers.FindAsync(new object[] { id }, ct);
        if (customer == null) return NotFound();
        _db.Customers.Remove(customer);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    private static CustomerDto Map(Customer c) => new()
    {
        Id = c.Id,
        Email = c.Email,
        Name = c.Name,
        Phone = c.Phone,
        Company = c.Company,
        Address = c.Address,
        VatNumber = c.VatNumber,
        CustomerType = c.CustomerType,
        CreatedAt = c.CreatedAt,
        UpdatedAt = c.UpdatedAt
    };
}
