using HuntexPos.Api.Domain;
using HuntexPos.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HuntexPos.Api.Controllers;

/// <summary>
/// Thin pass-through the Vue till UI uses so the Roll for It POS secret never reaches the browser.
/// </summary>
[ApiController]
[Route("api/rollforit")]
[Authorize(Roles = $"{Roles.Admin},{Roles.Owner},{Roles.Dev},{Roles.Sales}")]
public class RollForItController : ControllerBase
{
    private readonly RollForItClient _rfi;
    private readonly ILogger<RollForItController> _logger;

    public RollForItController(RollForItClient rfi, ILogger<RollForItController> logger)
    {
        _rfi = rfi;
        _logger = logger;
    }

    [HttpGet("status")]
    public async Task<IActionResult> Status(CancellationToken ct)
    {
        if (!_rfi.IsConfigured)
        {
            return Ok(new { enabled = false });
        }

        try
        {
            var status = await _rfi.StatusAsync(ct);
            return Ok(new RollForItStatusEnvelope(true, status));
        }
        catch (RollForItRemoteException ex)
        {
            _logger.LogWarning(ex, "Roll for It status failed");
            return StatusCode(ex.Status, new { enabled = true, error = ex.Body });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Roll for It status failed");
            return StatusCode(503, new { enabled = true, error = ex.Message });
        }
    }

    [HttpPost("roll")]
    public async Task<IActionResult> Roll([FromBody] RollForItPosRollRequest body, CancellationToken ct)
    {
        if (!_rfi.IsConfigured)
        {
            return StatusCode(503, new { message = "Roll4It is not configured on this till." });
        }
        if (string.IsNullOrWhiteSpace(body.Email) && string.IsNullOrWhiteSpace(body.Phone))
        {
            return BadRequest(new { message = "Enter the customer's email or cell phone number." });
        }
        if (body.PickedNumber < 1 || body.PickedNumber > 10)
        {
            return BadRequest(new { message = "Pick a number from 1 to 10." });
        }
        if (body.SubtotalCents <= 0)
        {
            return BadRequest(new { message = "The sale must have at least one line before you can roll." });
        }

        var operatorId = User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        try
        {
            var result = await _rfi.RollAsync(new RollForItRollRequest
            {
                Email = body.Email,
                Phone = body.Phone,
                PickedNumber = body.PickedNumber,
                SubtotalCents = body.SubtotalCents,
                OperatorId = operatorId,
                CartToken = body.CartToken,
                Currency = body.Currency,
            }, ct);

            return Ok(result);
        }
        catch (RollForItRemoteException ex)
        {
            // Pass the structured Roll for It error through to the UI.
            return StatusCode(ex.Status, new { message = "Roll4It refused this roll.", detail = ex.Body });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Roll for It roll failed");
            return StatusCode(503, new { message = "Roll4It is unreachable right now. Please try again." });
        }
    }
}

public record RollForItStatusEnvelope(bool Enabled, RollForItStatusDto Status);

public class RollForItPosRollRequest
{
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public int PickedNumber { get; set; }
    public long SubtotalCents { get; set; }
    public string? CartToken { get; set; }
    public string? Currency { get; set; }
}
