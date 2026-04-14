using HuntexPos.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HuntexPos.Api.Controllers;

[ApiController]
[Route("api/public/invoices")]
[AllowAnonymous]
public class PublicInvoicesController : ControllerBase
{
    private readonly InvoiceService _invoices;

    public PublicInvoicesController(InvoiceService invoices) => _invoices = invoices;

    [HttpGet("{token:guid}")]
    public async Task<IActionResult> Get(Guid token, CancellationToken ct)
    {
        var inv = await _invoices.GetByPublicTokenAsync(token, ct);
        return inv == null ? NotFound() : Ok(inv);
    }

    [HttpGet("{token:guid}/pdf")]
    public async Task<IActionResult> Pdf(Guid token, CancellationToken ct)
    {
        var bytes = await _invoices.GetPdfByPublicTokenAsync(token, ct);
        if (bytes == null) return NotFound();
        return File(bytes, "application/pdf", $"invoice-{token:N}.pdf");
    }
}
