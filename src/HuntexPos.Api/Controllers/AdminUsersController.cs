using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using HuntexPos.Api.Domain;
using HuntexPos.Api.DTOs;
using HuntexPos.Api.Options;
using HuntexPos.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HuntexPos.Api.Controllers;

[ApiController]
[Route("api/admin/users")]
[Authorize(Roles = $"{Roles.Admin},{Roles.Owner},{Roles.Dev}")]
public class AdminUsersController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly IEmailSender _email;
    private readonly AppOptions _app;

    public AdminUsersController(UserManager<ApplicationUser> users, IEmailSender email, IOptions<AppOptions> app)
    {
        _users = users;
        _email = email;
        _app = app.Value;
    }

    [HttpGet]
    public async Task<ActionResult<List<AdminUserListDto>>> List(CancellationToken ct)
    {
        var list = new List<AdminUserListDto>();
        foreach (var u in await _users.Users.AsNoTracking().OrderBy(x => x.Email).ToListAsync(ct))
        {
            var roles = await _users.GetRolesAsync(u);
            list.Add(new AdminUserListDto
            {
                Id = u.Id,
                Email = u.Email,
                DisplayName = u.DisplayName,
                Roles = roles.OrderBy(r => r).ToList(),
                LockoutEnabled = u.LockoutEnabled,
                LockedOut = u.LockoutEnd.HasValue && u.LockoutEnd > DateTimeOffset.UtcNow,
                LockoutEnd = u.LockoutEnd
            });
        }
        return list;
    }

    [HttpPost]
    public async Task<ActionResult<AdminUserListDto>> Create([FromBody] CreateStaffUserRequest req, CancellationToken ct)
    {
        if (req.Role == Roles.Owner && !User.IsInRole(Roles.Dev))
            return Forbid();
        if (req.Role == Roles.Admin && !User.IsInRole(Roles.Owner) && !User.IsInRole(Roles.Dev))
            return Forbid();

        var tempPassword = GenerateTempPassword();
        var user = new ApplicationUser
        {
            UserName = req.Email.Trim(),
            Email = req.Email.Trim(),
            EmailConfirmed = true,
            DisplayName = string.IsNullOrWhiteSpace(req.DisplayName) ? null : req.DisplayName.Trim()
        };
        var result = await _users.CreateAsync(user, tempPassword);
        if (!result.Succeeded)
            return BadRequest(new { errors = result.Errors.Select(e => e.Description).ToList() });

        await _users.AddToRoleAsync(user, req.Role);

        try
        {
            await SendSetupEmailAsync(user, ct);
        }
        catch
        {
            // account created successfully, email sending failed — admin can resend
        }

        var roles = await _users.GetRolesAsync(user);
        return new AdminUserListDto
        {
            Id = user.Id,
            Email = user.Email,
            DisplayName = user.DisplayName,
            Roles = roles.ToList(),
            LockoutEnabled = user.LockoutEnabled,
            LockedOut = false,
            LockoutEnd = user.LockoutEnd
        };
    }

    /// <summary>Resend the setup email for a user who hasn't set their password yet.</summary>
    [HttpPost("{id}/resend-invite")]
    public async Task<IActionResult> ResendInvite(string id, CancellationToken ct)
    {
        var user = await _users.FindByIdAsync(id);
        if (user == null) return NotFound();

        try
        {
            await SendSetupEmailAsync(user, ct);
            return Ok(new { message = $"Setup email sent to {user.Email}" });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("{id}/lock")]
    public async Task<IActionResult> SetLockout(string id, [FromBody] SetUserLockoutRequest body, CancellationToken ct)
    {
        var currentId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (id == currentId && body.Locked)
            return BadRequest(new { error = "You cannot lock your own account." });

        var user = await _users.FindByIdAsync(id);
        if (user == null) return NotFound();

        var roles = await _users.GetRolesAsync(user);
        if (roles.Contains(Roles.Owner))
            return BadRequest(new { error = "Owner accounts cannot be locked from this screen." });

        if (body.Locked)
        {
            await _users.SetLockoutEnabledAsync(user, true);
            await _users.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddYears(100));
        }
        else
        {
            await _users.SetLockoutEndDateAsync(user, null);
            await _users.ResetAccessFailedCountAsync(user);
        }
        return NoContent();
    }

    [HttpPost("{id}/password")]
    public async Task<IActionResult> ResetPassword(string id, [FromBody] AdminResetPasswordRequest body, CancellationToken ct)
    {
        var user = await _users.FindByIdAsync(id);
        if (user == null) return NotFound();
        var token = await _users.GeneratePasswordResetTokenAsync(user);
        var result = await _users.ResetPasswordAsync(user, token, body.NewPassword);
        if (!result.Succeeded)
            return BadRequest(new { errors = result.Errors.Select(e => e.Description).ToList() });
        return NoContent();
    }

    private async Task SendSetupEmailAsync(ApplicationUser user, CancellationToken ct)
    {
        var token = await _users.GeneratePasswordResetTokenAsync(user);
        var encoded = Uri.EscapeDataString(token);
        var emailEncoded = Uri.EscapeDataString(user.Email!);
        var link = $"{_app.PublicBaseUrl.TrimEnd('/')}/#/setup-password?token={encoded}&email={emailEncoded}";
        var name = user.DisplayName ?? user.Email;

        var html = $"""
        <div style="font-family:sans-serif;max-width:500px;margin:0 auto">
            <h2 style="color:#ff6600">Welcome to {_app.CompanyDisplayName}</h2>
            <p>Hi {name},</p>
            <p>An account has been created for you. Please set your password to get started:</p>
            <p style="text-align:center;margin:24px 0">
                <a href="{link}" style="display:inline-block;padding:12px 28px;
                    background:#ff6600;color:#fff;text-decoration:none;border-radius:4px;
                    font-weight:bold">Set my password</a>
            </p>
            <p style="font-size:0.85rem;color:#888">
                If the button doesn't work, copy this link into your browser:<br/>
                <a href="{link}">{link}</a>
            </p>
            <p style="font-size:0.85rem;color:#888">
                {_app.CompanyDisplayName} &bull; {_app.CompanyPhone}<br/>
                {_app.CompanyAddress}
            </p>
        </div>
        """;

        await _email.SendInvoiceEmailAsync(
            user.Email!,
            $"Set up your {_app.CompanyDisplayName} account",
            html,
            null, null, ct);
    }

    private static string GenerateTempPassword()
    {
        var bytes = RandomNumberGenerator.GetBytes(24);
        return Convert.ToBase64String(bytes) + "!Aa1";
    }
}
