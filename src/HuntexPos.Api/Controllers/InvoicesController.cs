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
    /// Extra rows fetched so the exact in-memory date filter still has a full page to return.
    private const int DateTrimBuffer = 200;

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

    [HttpGet("recent")]
    public async Task<List<RecentInvoiceDto>> Recent([FromQuery] int take = 5, CancellationToken ct = default)
    {
        var clamped = Math.Clamp(take, 1, 20);
        return (await _db.Invoices.AsNoTracking()
            .Where(i => i.Status != InvoiceStatus.Voided)
            .ToListAsync(ct))
            .OrderByDescending(i => i.CreatedAt)
            .Take(clamped)
            .Select(i => new RecentInvoiceDto
            {
                Id = i.Id,
                InvoiceNumber = i.InvoiceNumber,
                CustomerName = i.CustomerName,
                GrandTotal = i.GrandTotal,
                PaymentMethod = i.PaymentMethod,
                CreatedAt = i.CreatedAt,
                PublicToken = i.PublicToken
            })
            .ToList();
    }

    /// <summary>
    /// Search every past sale line by SKU, barcode, item name, invoice number or customer name.
    /// Exists for the returns counter: a customer brings an item back without the thermal
    /// receipt, and staff need to find what they paid — including any discount given.
    /// Available to Sales because that is who works the counter.
    /// </summary>
    [HttpGet("search-lines")]
    public async Task<ActionResult<List<InvoiceLineSearchResultDto>>> SearchLines(
        [FromQuery] string? q,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] bool includeVoided = false,
        [FromQuery] int take = 100,
        CancellationToken ct = default)
    {
        var term = (q ?? string.Empty).Trim();
        if (term.Length < 2)
            return BadRequest(new { error = "Enter at least 2 characters to search." });

        // '%' and '_' are LIKE wildcards. Strip them so a search cannot scan every line.
        // Two-arg LIKE is what the SQLite provider can translate; the 3-arg ESCAPE form
        // was part of the untranslatable query that 500'd this endpoint.
        var safeTerm = term.Replace("%", string.Empty).Replace("_", string.Empty);
        if (safeTerm.Length < 2)
            return BadRequest(new { error = "Enter at least 2 characters to search." });
        var like = $"%{safeTerm}%";

        var limit = Math.Clamp(take, 1, 500);

        // SQLite stores DateTimeOffset as text, so comparisons are only reliable when every
        // row shares the same offset. Widen the SQL bounds by a day and re-filter exactly in
        // memory: SQL never drops a row it should keep, and the result set stays bounded.
        var fromBound = from?.AddDays(-1);
        var toBound = to?.AddDays(1);

        // Use navigations (not a query-syntax left join). The previous
        // DefaultIfEmpty + 3-arg LIKE shape cannot be translated by the SQLite
        // provider and 500s the sales-history search.
        var query = _db.InvoiceLines.AsNoTracking()
            .Where(line =>
                EF.Functions.Like(line.Description, like)
                || (line.SkuAtSale != null && EF.Functions.Like(line.SkuAtSale, like))
                || EF.Functions.Like(line.Invoice!.InvoiceNumber, like)
                || (line.Invoice.CustomerName != null && EF.Functions.Like(line.Invoice.CustomerName, like))
                || (line.Product != null && EF.Functions.Like(line.Product.Sku, like))
                || (line.Product != null && line.Product.Barcode != null && EF.Functions.Like(line.Product.Barcode, like)));

        if (!includeVoided)
            query = query.Where(line => line.Invoice!.Status != InvoiceStatus.Voided);
        if (fromBound.HasValue)
            query = query.Where(line => line.Invoice!.CreatedAt >= fromBound.Value);
        if (toBound.HasValue)
            query = query.Where(line => line.Invoice!.CreatedAt <= toBound.Value);

        var rows = await query
            .OrderByDescending(line => line.Invoice!.CreatedAt)
            .Take(limit + DateTrimBuffer)
            .Select(line => new
            {
                Line = line,
                Inv = line.Invoice!,
                CatalogSku = line.Product != null ? line.Product.Sku : null
            })
            .ToListAsync(ct);

        var filtered = rows.AsEnumerable();
        if (from.HasValue)
            filtered = filtered.Where(r => r.Inv.CreatedAt >= from.Value);
        if (to.HasValue)
            filtered = filtered.Where(r => r.Inv.CreatedAt <= to.Value);

        return filtered
            .OrderByDescending(r => r.Inv.CreatedAt)
            .Take(limit)
            .Select(r => new InvoiceLineSearchResultDto
            {
                InvoiceId = r.Inv.Id,
                InvoiceNumber = r.Inv.InvoiceNumber,
                CreatedAt = r.Inv.CreatedAt,
                Status = r.Inv.Status.ToString(),
                CustomerName = r.Inv.CustomerName,
                PaymentMethod = r.Inv.PaymentMethod,
                PublicToken = r.Inv.PublicToken,
                ProductId = r.Line.ProductId,
                Sku = r.Line.SkuAtSale ?? r.CatalogSku,
                Description = r.Line.Description,
                Quantity = r.Line.Quantity,
                OriginalUnitPrice = r.Line.OriginalUnitPrice,
                UnitPrice = r.Line.UnitPrice,
                LineDiscount = r.Line.LineDiscount,
                LineTotal = r.Line.LineTotal,
                EffectiveUnitPrice = r.Line.Quantity > 0
                    ? Math.Round(r.Line.LineTotal / r.Line.Quantity, 2)
                    : r.Line.UnitPrice
            })
            .ToList();
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
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        try
        {
            await _invoices.VoidAsync(id, req.Reason, userId, ct);
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
            ItemsSummary = string.Join(", ", i.Lines.Select(l => $"{l.Description} x{l.Quantity}")),
            PublicToken = i.PublicToken
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
