using HuntexPos.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HuntexPos.Api.Controllers;

/// <summary>
/// Anonymous read for the printable return slip. Mirrors the public invoice endpoint so the
/// thermal print view can be opened in a fresh tab from Find sale without carrying a JWT.
/// The <c>Guid</c> slug is unguessable and appears only on the till after an exchange.
/// </summary>
[ApiController]
[Route("api/public/returns")]
[AllowAnonymous]
public class PublicReturnsController : ControllerBase
{
    private readonly InvoiceService _invoices;

    public PublicReturnsController(InvoiceService invoices) => _invoices = invoices;

    [HttpGet("{token:guid}")]
    public async Task<IActionResult> Get(Guid token, CancellationToken ct)
    {
        var slip = await _invoices.GetSaleReturnByPublicTokenAsync(token, ct);
        return slip == null ? NotFound() : Ok(slip);
    }
}
