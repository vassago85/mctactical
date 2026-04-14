using System.Security.Claims;
using HuntexPos.Api.Domain;
using HuntexPos.Api.DTOs;
using HuntexPos.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HuntexPos.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = $"{Roles.Sales},{Roles.Admin},{Roles.Owner},{Roles.Dev}")]
public class InvoicesController : ControllerBase
{
    private readonly InvoiceService _invoices;

    public InvoicesController(InvoiceService invoices) => _invoices = invoices;

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
}
