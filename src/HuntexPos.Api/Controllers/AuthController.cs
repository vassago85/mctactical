using System.Security.Claims;
using HuntexPos.Api.Domain;
using HuntexPos.Api.DTOs;
using HuntexPos.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace HuntexPos.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly JwtTokenService _jwt;

    public AuthController(UserManager<ApplicationUser> users, JwtTokenService jwt)
    {
        _users = users;
        _jwt = jwt;
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest req, CancellationToken ct)
    {
        var user = await _users.FindByEmailAsync(req.Email);
        if (user == null)
            return Unauthorized(new { error = "Invalid email or password." });

        if (await _users.IsLockedOutAsync(user))
            return Unauthorized(new { error = "Account is locked. Contact an administrator." });

        if (!await _users.CheckPasswordAsync(user, req.Password))
        {
            await _users.AccessFailedAsync(user);
            return Unauthorized(new { error = "Invalid email or password." });
        }

        await _users.ResetAccessFailedCountAsync(user);

        var roles = await _users.GetRolesAsync(user);
        var (token, exp) = _jwt.CreateToken(user, roles);
        return new LoginResponse
        {
            Token = token,
            ExpiresAt = exp,
            Roles = roles,
            UserId = user.Id,
            DisplayName = user.DisplayName
        };
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<object>> Me(CancellationToken ct)
    {
        var id = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (id == null) return Unauthorized();
        var user = await _users.FindByIdAsync(id);
        if (user == null) return NotFound();
        var roles = await _users.GetRolesAsync(user);
        return new { user.Id, user.Email, user.DisplayName, Roles = roles };
    }

    [AllowAnonymous]
    [HttpPost("setup-password")]
    public async Task<IActionResult> SetupPassword([FromBody] DTOs.SetupPasswordRequest req, CancellationToken ct)
    {
        var user = await _users.FindByEmailAsync(req.Email);
        if (user == null)
            return BadRequest(new { error = "Invalid or expired setup link." });

        var result = await _users.ResetPasswordAsync(user, req.Token, req.NewPassword);
        if (!result.Succeeded)
        {
            var errors = result.Errors.Select(e => e.Description).ToList();
            if (errors.Any(e => e.Contains("Invalid token", StringComparison.OrdinalIgnoreCase)))
                return BadRequest(new { error = "This setup link has expired or was already used. Ask your admin to resend the invite." });
            return BadRequest(new { errors });
        }
        return Ok(new { message = "Password set successfully. You can now log in." });
    }
}
