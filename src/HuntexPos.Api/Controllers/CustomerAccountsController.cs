using System.Security.Claims;
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

        // Open invoices = AR-charged invoices that are not fully paid. SQLite EF Core
        // can't translate ORDER BY DateTimeOffset, so we materialise then sort in memory.
        var openInvoiceRows = await _db.Invoices
            .AsNoTracking()
            .Where(i => i.CustomerId == customerId
                        && i.PaymentStatus != InvoicePaymentStatus.Paid
                        && i.PaymentStatus != InvoicePaymentStatus.WrittenOff)
            .ToListAsync(ct);

        var openInvoices = openInvoiceRows
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
            .ToList();

        var balance = openInvoices.Sum(i => i.AmountOutstanding);
        var nowLocal = DateTimeOffset.UtcNow;
        var overdueCount = openInvoices.Count(i => i.DueDate.HasValue && i.DueDate.Value < nowLocal);

        // Same SQLite translator limitation on PaidAt — materialise first.
        var recentPaymentRows = await _db.CustomerPayments
            .AsNoTracking()
            .Where(p => p.CustomerId == customerId && !p.IsVoided)
            .ToListAsync(ct);

        var recentPayments = recentPaymentRows
            .OrderByDescending(p => p.PaidAt)
            .Take(20)
            .ToList();

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

    /// <summary>
    /// Record a payment against the customer's account. Allocates oldest-first when no
    /// explicit invoice ids are supplied; supports partial payments and surplus (overpayment).
    /// </summary>
    /// <remarks>
    /// Authorisation note: hardcoded to Owner/Admin/Dev for MVP. Configurable per-business
    /// via <c>BusinessSettings.AccountsAllowSalesPayments</c> is a planned follow-up.
    /// </remarks>
    [HttpPost("/api/customers/{customerId:guid}/payments")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Owner},{Roles.Dev}")]
    public async Task<ActionResult<CustomerPaymentResultDto>> Create(
        Guid customerId,
        [FromBody] CreateCustomerPaymentRequest req,
        CancellationToken ct)
    {
        if (req.Amount <= 0m)
            return BadRequest(new { error = "Payment amount must be greater than zero." });

        var method = (req.Method ?? "").Trim();
        if (!IsValidMethod(method))
            return BadRequest(new { error = "Method must be one of Cash, Card, EFT, Other." });

        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == customerId, ct);
        if (customer is null) return NotFound();
        if (!customer.AccountEnabled)
            return BadRequest(new { error = "This customer is not set up for account sales." });

        // Build the allocation queue. If the caller specified invoices we honour their order
        // (and verify each belongs to this customer); otherwise fall back to oldest-first.
        List<Invoice> queue;
        if (req.ApplyToInvoiceIds is { Count: > 0 })
        {
            var requested = req.ApplyToInvoiceIds.Distinct().ToList();
            var loaded = await _db.Invoices
                .Where(i => i.CustomerId == customerId && requested.Contains(i.Id))
                .ToListAsync(ct);
            var loadedById = loaded.ToDictionary(i => i.Id);
            var missing = requested.Where(id => !loadedById.ContainsKey(id)).ToList();
            if (missing.Count > 0)
                return BadRequest(new { error = $"Invoice(s) {string.Join(", ", missing)} do not belong to this customer." });
            queue = requested.Select(id => loadedById[id]).ToList();
        }
        else
        {
            // Materialise then sort — SQLite cannot translate ORDER BY DateTimeOffset.
            var rows = await _db.Invoices
                .Where(i => i.CustomerId == customerId
                            && i.Status != InvoiceStatus.Voided
                            && i.PaymentStatus != InvoicePaymentStatus.Paid
                            && i.PaymentStatus != InvoicePaymentStatus.WrittenOff)
                .ToListAsync(ct);
            queue = rows.OrderBy(i => i.CreatedAt).ToList();
        }

        var paidAt = req.PaidAt ?? DateTimeOffset.UtcNow;
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var remaining = req.Amount;
        var created = new List<CustomerPayment>();

        foreach (var inv in queue)
        {
            if (remaining <= 0m) break;
            var outstanding = inv.GrandTotal - inv.AmountPaid;
            if (outstanding <= 0m) continue;

            var apply = Math.Min(remaining, outstanding);
            var payment = new CustomerPayment
            {
                CustomerId = customerId,
                InvoiceId = inv.Id,
                Amount = apply,
                Method = method,
                Reference = string.IsNullOrWhiteSpace(req.Reference) ? null : req.Reference!.Trim(),
                Notes = string.IsNullOrWhiteSpace(req.Notes) ? null : req.Notes!.Trim(),
                PaidAt = paidAt,
                CreatedByUserId = userId
            };
            _db.CustomerPayments.Add(payment);
            created.Add(payment);

            inv.AmountPaid += apply;
            inv.PaymentStatus = inv.AmountPaid >= inv.GrandTotal
                ? InvoicePaymentStatus.Paid
                : InvoicePaymentStatus.Partial;
            remaining -= apply;
        }

        // Surplus → unallocated credit row.
        var unallocated = remaining;
        if (unallocated > 0m)
        {
            var creditPayment = new CustomerPayment
            {
                CustomerId = customerId,
                InvoiceId = null,
                Amount = unallocated,
                Method = method,
                Reference = string.IsNullOrWhiteSpace(req.Reference) ? null : req.Reference!.Trim(),
                Notes = string.IsNullOrWhiteSpace(req.Notes) ? null : req.Notes!.Trim(),
                PaidAt = paidAt,
                CreatedByUserId = userId
            };
            _db.CustomerPayments.Add(creditPayment);
            created.Add(creditPayment);
        }

        await _db.SaveChangesAsync(ct);

        // SQLite EF can't translate Sum(decimal). Project the two columns then sum client-side.
        var openSnapshots = await _db.Invoices.AsNoTracking()
            .Where(i => i.CustomerId == customerId
                        && i.Status != InvoiceStatus.Voided
                        && i.PaymentStatus != InvoicePaymentStatus.Paid
                        && i.PaymentStatus != InvoicePaymentStatus.WrittenOff)
            .Select(i => new { i.GrandTotal, i.AmountPaid })
            .ToListAsync(ct);
        var newBalance = openSnapshots.Sum(i => i.GrandTotal - i.AmountPaid);

        var creditAvailable = customer.CreditLimit > 0m
            ? Math.Max(0m, customer.CreditLimit - newBalance)
            : 0m;

        return Ok(new CustomerPaymentResultDto(
            created.Select(p => p.Id).ToList(),
            newBalance,
            creditAvailable,
            unallocated));
    }

    private static bool IsValidMethod(string m) =>
        m.Equals("Cash", StringComparison.OrdinalIgnoreCase)
        || m.Equals("Card", StringComparison.OrdinalIgnoreCase)
        || m.Equals("EFT", StringComparison.OrdinalIgnoreCase)
        || m.Equals("Other", StringComparison.OrdinalIgnoreCase);
}
