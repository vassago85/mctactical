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
public class StocktakeController : ControllerBase
{
    private readonly StocktakeService _stock;

    public StocktakeController(StocktakeService stock) => _stock = stock;

    [HttpPost("sessions")]
    public async Task<ActionResult<StocktakeSessionDto>> CreateSession([FromBody] CreateStocktakeSessionRequest req, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var s = await _stock.CreateSessionAsync(req.Name, userId, ct);
        return Ok(s);
    }

    [HttpGet("sessions/{id:guid}")]
    public async Task<ActionResult<StocktakeSessionDto>> GetSession(Guid id, CancellationToken ct)
    {
        var s = await _stock.GetSessionAsync(id, ct);
        return s == null ? NotFound() : s;
    }

    [HttpPost("sessions/{id:guid}/lines")]
    public async Task<ActionResult<StocktakeLineDto>> AddLine(Guid id, [FromBody] AddStocktakeLineRequest req, CancellationToken ct)
    {
        try
        {
            return await _stock.UpsertLineAsync(id, req, ct);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("sessions/{id:guid}/post")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Owner},{Roles.Dev}")]
    public async Task<IActionResult> Post(Guid id, CancellationToken ct)
    {
        try
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
            await _stock.PostSessionAsync(id, userId, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
