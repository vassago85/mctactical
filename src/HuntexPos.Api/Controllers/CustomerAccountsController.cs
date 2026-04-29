using HuntexPos.Api.Data;
using HuntexPos.Api.Domain;
using HuntexPos.Api.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HuntexPos.Api.Controllers;

/// <summary>
/// Read-only customer account summary. Phase 3B.1 deliberately exposes a zero-balance
/// snapshot — the schema is in place, but no account sales or payments have been made yet.
/// 3B.2 introduces account sales (writes to <c>Invoices.IsAccountSale</c>); 3B.3 introduces
/// payments (writes to <c>CustomerPayments</c>). At that point this endpoint starts returning
/// real numbers without any frontend change.
/// </summary>
[ApiController]
[Route("api/customers/{customerId:guid}/account")]
[Authorize(Roles = $"{Roles.Admin},{Roles.Owner},{Roles.Dev},{Roles.Sales}")]
public class CustomerAccountsController : ControllerBase
{
    private readonly HuntexDbContext _db;

    public CustomerAccountsController(HuntexDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<CustomerAccountDto>> Get(Guid customerId, CancellationToken ct)
    {
        var customer = await _db.Customers
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == customerId, ct);

        if (customer is null)
            return NotFound();

        // Open invoices = AR-charged invoices that are not fully paid. Until 3B.2 ships
        // there are zero of these because the backfill marked everything as Paid.
        var openInvoices = await _db.Invoices
            .AsNoTracking()
            .Where(i => i.CustomerId == customerId
                        && i.PaymentStatus != InvoicePaymentStatus.Paid
                        && i.PaymentStatus != InvoicePaymentStatus.WrittenOff)
            .OrderBy(i => i.CreatedAt)
            .Select(i => new CustomerAccountInvoiceDto(
                i.Id,
                i.InvoiceNumber,
                i.CreatedAt,
                i.DueDate,
                i.GrandTotal,
                i.AmountPaid,
                i.GrandTotal - i.AmountPaid,
                i.PaymentStatus.ToString()))
            .ToListAsync(ct);

        var balance = openInvoices.Sum(i => i.AmountOutstanding);
        var nowLocal = DateTimeOffset.UtcNow;
        var overdueCount = openInvoices.Count(i => i.DueDate.HasValue && i.DueDate.Value < nowLocal);

        var recentPayments = await _db.CustomerPayments
            .AsNoTracking()
            .Where(p => p.CustomerId == customerId && !p.IsVoided)
            .OrderByDescending(p => p.PaidAt)
            .Take(20)
            .ToListAsync(ct);

        var invoiceNumberById = await _db.Invoices
            .AsNoTracking()
            .Where(i => recentPayments.Select(p => p.InvoiceId).Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, i => i.InvoiceNumber, ct);

        var recentPaymentDtos = recentPayments
            .Select(p => new CustomerAccountPaymentDto(
                p.Id,
                p.InvoiceId,
                p.InvoiceId.HasValue && invoiceNumberById.TryGetValue(p.InvoiceId.Value, out var num) ? num : null,
                p.Amount,
                p.Method,
                p.Reference,
                p.PaidAt))
            .ToList();

        var creditAvailable = customer.CreditLimit > 0
            ? Math.Max(0m, customer.CreditLimit - balance)
            : 0m;

        var dto = new CustomerAccountDto(
            customer.Id,
            customer.Name,
            customer.Email,
            customer.Company,
            customer.TradeAccount,
            customer.AccountEnabled,
            customer.CreditLimit,
            customer.PaymentTermsDays,
            balance,
            creditAvailable,
            openInvoices.Count,
            overdueCount,
            openInvoices,
            recentPaymentDtos);

        return Ok(dto);
    }
}
