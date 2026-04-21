using HuntexPos.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HuntexPos.Api.Controllers;

[ApiController]
[Route("api/public/quotes")]
[AllowAnonymous]
public class PublicQuotesController : ControllerBase
{
    private readonly QuoteService _quotes;

    public PublicQuotesController(QuoteService quotes) => _quotes = quotes;

    [HttpGet("{token:guid}")]
    public async Task<IActionResult> Get(Guid token, CancellationToken ct)
    {
        var q = await _quotes.GetByPublicTokenAsync(token, ct);
        return q == null ? NotFound() : Ok(q);
    }

    [HttpGet("{token:guid}/pdf")]
    public async Task<IActionResult> Pdf(Guid token, CancellationToken ct)
    {
        var bytes = await _quotes.GetPdfByPublicTokenAsync(token, ct);
        if (bytes == null) return NotFound();
        return File(bytes, "application/pdf", $"quote-{token:N}.pdf");
    }
}
