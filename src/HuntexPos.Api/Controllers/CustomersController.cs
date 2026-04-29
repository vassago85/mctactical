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
    public async Task<List<CustomerDto>> List([FromQuery] string? q, [FromQuery] int? take, CancellationToken ct)
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
        var limit = take.GetValueOrDefault(50);
        if (limit <= 0) limit = 50;
        if (limit > 500) limit = 500;
        return customers
            .OrderByDescending(c => c.UpdatedAt)
            .Take(limit)
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

    [HttpPost]
    [Authorize(Roles = $"{Roles.Sales},{Roles.Admin},{Roles.Owner},{Roles.Dev}")]
    public async Task<ActionResult<CustomerDto>> Create([FromBody] CreateCustomerRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Email))
            return BadRequest(new { error = "Email is required." });

        var email = req.Email.Trim().ToLower();
        var existing = await _db.Customers.FirstOrDefaultAsync(c => c.Email == email, ct);
        if (existing != null)
            return Conflict(new { error = "A customer with that email already exists.", id = existing.Id });

        // Only Owner/Admin/Dev may set AR fields on create. Sales-role creators get the safe defaults.
        var userIsAdmin = User.IsInRole(Roles.Admin) || User.IsInRole(Roles.Owner) || User.IsInRole(Roles.Dev);

        var customer = new Customer
        {
            Id = Guid.NewGuid(),
            Email = email,
            Name = string.IsNullOrWhiteSpace(req.Name) ? null : req.Name.Trim(),
            Phone = string.IsNullOrWhiteSpace(req.Phone) ? null : req.Phone.Trim(),
            Company = string.IsNullOrWhiteSpace(req.Company) ? null : req.Company.Trim(),
            Address = string.IsNullOrWhiteSpace(req.Address) ? null : req.Address.Trim(),
            VatNumber = string.IsNullOrWhiteSpace(req.VatNumber) ? null : req.VatNumber.Trim(),
            CustomerType = string.IsNullOrWhiteSpace(req.CustomerType) ? null : req.CustomerType.Trim(),
            TradeAccount = userIsAdmin && req.TradeAccount,
            AccountEnabled = userIsAdmin && req.AccountEnabled,
            CreditLimit = userIsAdmin ? Math.Max(0m, req.CreditLimit) : 0m,
            PaymentTermsDays = userIsAdmin ? Math.Clamp(req.PaymentTermsDays, 0, 365) : 30,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _db.Customers.Add(customer);
        await _db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = customer.Id }, Map(customer));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = $"{Roles.Sales},{Roles.Admin},{Roles.Owner},{Roles.Dev}")]
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

        // AR fields: only Owner/Admin/Dev may change these. Sales role silently has them ignored.
        var userIsAdmin = User.IsInRole(Roles.Admin) || User.IsInRole(Roles.Owner) || User.IsInRole(Roles.Dev);
        if (userIsAdmin)
        {
            if (req.TradeAccount.HasValue) customer.TradeAccount = req.TradeAccount.Value;
            if (req.AccountEnabled.HasValue) customer.AccountEnabled = req.AccountEnabled.Value;
            if (req.CreditLimit.HasValue) customer.CreditLimit = Math.Max(0m, req.CreditLimit.Value);
            if (req.PaymentTermsDays.HasValue) customer.PaymentTermsDays = Math.Clamp(req.PaymentTermsDays.Value, 0, 365);
        }

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

        // Safety: do not let a customer be deleted if they own any invoice (preserves AR audit trail).
        var hasInvoices = await _db.Invoices.AnyAsync(i => i.CustomerId == id, ct);
        if (hasInvoices)
            return Conflict(new { error = "This customer has invoices and cannot be deleted." });

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
        TradeAccount = c.TradeAccount,
        AccountEnabled = c.AccountEnabled,
        CreditLimit = c.CreditLimit,
        PaymentTermsDays = c.PaymentTermsDays,
        CreatedAt = c.CreatedAt,
        UpdatedAt = c.UpdatedAt
    };
}
