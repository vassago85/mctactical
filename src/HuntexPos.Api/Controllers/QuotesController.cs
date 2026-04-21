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
public class QuotesController : ControllerBase
{
    private readonly QuoteService _quotes;

    public QuotesController(QuoteService quotes)
    {
        _quotes = quotes;
    }

    [HttpGet]
    public async Task<ActionResult<List<QuoteListItemDto>>> List(
        [FromQuery] string? status,
        [FromQuery] string? search,
        [FromQuery] int take = 100,
        CancellationToken ct = default)
    {
        var list = await _quotes.ListAsync(status, search, take, ct);
        return Ok(list);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<QuoteDto>> Get(Guid id, CancellationToken ct)
    {
        var q = await _quotes.GetAsync(id, ct);
        return q == null ? NotFound() : Ok(q);
    }

    [HttpPost]
    public async Task<ActionResult<QuoteDto>> Create([FromBody] CreateQuoteRequest req, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        try
        {
            var dto = await _quotes.CreateAsync(req, userId, ct);
            return Ok(dto);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<QuoteDto>> Update(Guid id, [FromBody] UpdateQuoteRequest req, CancellationToken ct)
    {
        try
        {
            var dto = await _quotes.UpdateAsync(id, req, ct);
            return dto == null ? NotFound() : Ok(dto);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("{id:guid}/status")]
    public async Task<ActionResult<QuoteDto>> SetStatus(Guid id, [FromBody] UpdateQuoteStatusRequest req, CancellationToken ct)
    {
        try
        {
            var dto = await _quotes.SetStatusAsync(id, req.Status, ct);
            return dto == null ? NotFound() : Ok(dto);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("{id:guid}/convert")]
    public async Task<ActionResult<ConvertQuoteResult>> Convert(
        Guid id,
        [FromBody] ConvertQuoteRequest? req,
        CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var managerBypass = User.IsInRole(Roles.Admin) || User.IsInRole(Roles.Owner) || User.IsInRole(Roles.Dev);
        try
        {
            var result = await _quotes.ConvertToInvoiceAsync(
                id, req?.PaymentMethod ?? "Cash", userId, managerBypass, ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Owner},{Roles.Dev}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        try
        {
            await _quotes.DeleteAsync(id, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("{id:guid}/pdf")]
    public async Task<IActionResult> Pdf(Guid id, CancellationToken ct)
    {
        var bytes = await _quotes.GetPdfBytesAsync(id, ct);
        if (bytes == null) return NotFound();
        return File(bytes, "application/pdf", $"quote-{id:N}.pdf");
    }
}

public class ConvertQuoteRequest
{
    public string PaymentMethod { get; set; } = "Cash";
}
