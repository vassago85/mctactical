using System.Security.Claims;
using HuntexPos.Api.Data;
using HuntexPos.Api.Domain;
using HuntexPos.Api.DTOs;
using HuntexPos.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HuntexPos.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = $"{Roles.Sales},{Roles.Admin},{Roles.Owner},{Roles.Dev}")]
public class InvoicesController : ControllerBase
{
    private readonly InvoiceService _invoices;
    private readonly HuntexDbContext _db;
    private readonly InvoicePdfService _pdf;

    public InvoicesController(InvoiceService invoices, HuntexDbContext db, InvoicePdfService pdf)
    {
        _invoices = invoices;
        _db = db;
        _pdf = pdf;
    }

    [HttpPost]
    public async Task<ActionResult<InvoiceDto>> Create([FromBody] CreateInvoiceRequest req, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var managerBypass = User.IsInRole(Roles.Admin) || User.IsInRole(Roles.Owner) || User.IsInRole(Roles.Dev);
        try
        {
            var inv = await _invoices.CreateAsync(req, userId, managerBypass, ct);
            return Ok(inv);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<InvoiceDto>> Get(Guid id, CancellationToken ct)
    {
        var inv = await _invoices.GetAsync(id, ct);
        return inv == null ? NotFound() : inv;
    }

    [HttpGet("{id:guid}/pdf")]
    public async Task<IActionResult> Pdf(Guid id, CancellationToken ct)
    {
        var bytes = await _invoices.GetPdfBytesAsync(id, ct);
        if (bytes == null) return NotFound();
        return File(bytes, "application/pdf", $"invoice-{id:N}.pdf");
    }

    [HttpPost("{id:guid}/void")]
    [Authorize(Roles = $"{Roles.Owner},{Roles.Admin},{Roles.Dev}")]
    public async Task<IActionResult> Void(Guid id, [FromBody] VoidInvoiceRequest req, CancellationToken ct)
    {
        try
        {
            await _invoices.VoidAsync(id, req.Reason, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("pending-deliveries")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Owner},{Roles.Dev}")]
    public async Task<ActionResult<List<PendingDeliveryDto>>> PendingDeliveries(
        [FromQuery] string? filter, CancellationToken ct)
    {
        var q = _db.Invoices.AsNoTracking()
            .Include(i => i.Lines)
            .Where(i => i.IsSpecialOrder && i.Status != InvoiceStatus.Voided);

        if (string.IsNullOrWhiteSpace(filter) || filter == "pending")
            q = q.Where(i => !i.IsDelivered);
        else if (filter == "delivered")
            q = q.Where(i => i.IsDelivered);

        var invoices = (await q.ToListAsync(ct))
            .OrderByDescending(i => i.CreatedAt).ToList();

        return invoices.Select(i => new PendingDeliveryDto
        {
            Id = i.Id,
            InvoiceNumber = i.InvoiceNumber,
            CustomerName = i.CustomerName,
            CustomerEmail = i.CustomerEmail,
            GrandTotal = i.GrandTotal,
            CreatedAt = i.CreatedAt,
            IsDelivered = i.IsDelivered,
            DeliveredAt = i.DeliveredAt,
            DeliveryNotes = i.DeliveryNotes,
            ItemsSummary = string.Join(", ", i.Lines.Select(l => $"{l.Description} x{l.Quantity}"))
        }).ToList();
    }

    [HttpPost("{id:guid}/mark-delivered")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Owner},{Roles.Dev}")]
    public async Task<IActionResult> MarkDelivered(Guid id, [FromBody] MarkDeliveredRequest req, CancellationToken ct)
    {
        var inv = await _db.Invoices.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (inv == null) return NotFound();
        if (!inv.IsSpecialOrder) return BadRequest(new { error = "Not a special order." });

        inv.IsDelivered = true;
        inv.DeliveredAt = DateTimeOffset.UtcNow;
        inv.DeliveryNotes = req.Notes;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("{id:guid}/order-confirmation-pdf")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Owner},{Roles.Dev}")]
    public async Task<IActionResult> OrderConfirmationPdf(Guid id, CancellationToken ct)
    {
        var inv = await _db.Invoices.AsNoTracking().Include(i => i.Lines).FirstOrDefaultAsync(i => i.Id == id, ct);
        if (inv == null) return NotFound();
        var bytes = _pdf.BuildOrderConfirmationPdf(inv);
        return File(bytes, "application/pdf", $"order-confirmation-{inv.InvoiceNumber}.pdf");
    }
}
