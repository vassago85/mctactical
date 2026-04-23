using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using HuntexPos.Api.Data;
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
    private readonly IEffectiveBusinessSettings _business;
    private readonly HuntexDbContext _db;

    public AdminUsersController(
        UserManager<ApplicationUser> users,
        IEmailSender email,
        IOptions<AppOptions> app,
        IEffectiveBusinessSettings business,
        HuntexDbContext db)
    {
        _users = users;
        _email = email;
        _app = app.Value;
        _business = business;
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<List<AdminUserListDto>>> List(CancellationToken ct)
    {
        var suppliers = await _db.Suppliers.AsNoTracking()
            .ToDictionaryAsync(s => s.Id, s => s.Name, ct);

        var list = new List<AdminUserListDto>();
        foreach (var u in await _users.Users.AsNoTracking().OrderBy(x => x.Email).ToListAsync(ct))
        {
            var roles = await _users.GetRolesAsync(u);
            string? supplierName = null;
            if (u.SupplierId.HasValue && suppliers.TryGetValue(u.SupplierId.Value, out var sn))
                supplierName = sn;
            list.Add(new AdminUserListDto
            {
                Id = u.Id,
                Email = u.Email,
                DisplayName = u.DisplayName,
                Roles = roles.OrderBy(r => r).ToList(),
                LockoutEnabled = u.LockoutEnabled,
                LockedOut = u.LockoutEnd.HasValue && u.LockoutEnd > DateTimeOffset.UtcNow,
                LockoutEnd = u.LockoutEnd,
                SupplierId = u.SupplierId,
                SupplierName = supplierName
            });
        }
        return list;
    }

    [HttpPost]
    public async Task<ActionResult<AdminUserListDto>> Create([FromBody] CreateStaffUserRequest req, CancellationToken ct)
    {
        if (req.Role == Roles.Owner && !User.IsInRole(Roles.Owner) && !User.IsInRole(Roles.Dev))
            return Forbid();
        if (req.Role == Roles.Admin && !User.IsInRole(Roles.Owner) && !User.IsInRole(Roles.Dev))
            return Forbid();

        var tempPassword = GenerateTempPassword();
        var user = new ApplicationUser
        {
            UserName = req.Email.Trim(),
            Email = req.Email.Trim(),
            EmailConfirmed = true,
            DisplayName = string.IsNullOrWhiteSpace(req.DisplayName) ? null : req.DisplayName.Trim(),
            SupplierId = req.SupplierId
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
        string? supplierName = null;
        if (user.SupplierId.HasValue)
            supplierName = await _db.Suppliers.AsNoTracking()
                .Where(s => s.Id == user.SupplierId.Value)
                .Select(s => s.Name)
                .FirstOrDefaultAsync(ct);
        return new AdminUserListDto
        {
            Id = user.Id,
            Email = user.Email,
            DisplayName = user.DisplayName,
            Roles = roles.ToList(),
            LockoutEnabled = user.LockoutEnabled,
            LockedOut = false,
            LockoutEnd = user.LockoutEnd,
            SupplierId = user.SupplierId,
            SupplierName = supplierName
        };
    }

    /// <summary>
    /// Update an existing user's display name and role assignments. Role changes are gated:
    /// only Owner/Dev may assign or remove the Owner or Admin role, and the caller cannot
    /// demote themselves out of Owner (prevents accidental self-lockout).
    /// </summary>
    [HttpPut("{id}")]
    public async Task<ActionResult<AdminUserListDto>> Update(string id, [FromBody] UpdateStaffUserRequest req, CancellationToken ct)
    {
        var user = await _users.FindByIdAsync(id);
        if (user == null) return NotFound();

        var requested = (req.Roles ?? new List<string>())
            .Select(r => r?.Trim() ?? "")
            .Where(r => !string.IsNullOrEmpty(r))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var unknown = requested.Where(r => !Roles.All.Contains(r, StringComparer.OrdinalIgnoreCase)).ToList();
        if (unknown.Count > 0)
            return BadRequest(new { error = $"Unknown role(s): {string.Join(", ", unknown)}" });
        if (requested.Count == 0)
            return BadRequest(new { error = "User must have at least one role." });

        var callerIsOwnerOrDev = User.IsInRole(Roles.Owner) || User.IsInRole(Roles.Dev);
        var existing = (await _users.GetRolesAsync(user)).ToList();
        var added = requested.Except(existing, StringComparer.OrdinalIgnoreCase).ToList();
        var removed = existing.Except(requested, StringComparer.OrdinalIgnoreCase).ToList();

        // Only Owner/Dev can mint or strip the elevated roles.
        if (!callerIsOwnerOrDev)
        {
            if (added.Any(r => string.Equals(r, Roles.Owner, StringComparison.OrdinalIgnoreCase)
                           || string.Equals(r, Roles.Admin, StringComparison.OrdinalIgnoreCase)
                           || string.Equals(r, Roles.Dev, StringComparison.OrdinalIgnoreCase))
                || removed.Any(r => string.Equals(r, Roles.Owner, StringComparison.OrdinalIgnoreCase)
                                 || string.Equals(r, Roles.Admin, StringComparison.OrdinalIgnoreCase)
                                 || string.Equals(r, Roles.Dev, StringComparison.OrdinalIgnoreCase)))
            {
                return Forbid();
            }
        }

        // Only Dev can assign the Dev role.
        if (added.Any(r => string.Equals(r, Roles.Dev, StringComparison.OrdinalIgnoreCase))
            && !User.IsInRole(Roles.Dev))
        {
            return Forbid();
        }

        var currentId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (id == currentId
            && existing.Contains(Roles.Owner, StringComparer.OrdinalIgnoreCase)
            && !requested.Contains(Roles.Owner, StringComparer.OrdinalIgnoreCase))
        {
            return BadRequest(new { error = "You cannot remove the Owner role from your own account." });
        }

        user.DisplayName = string.IsNullOrWhiteSpace(req.DisplayName) ? null : req.DisplayName.Trim();
        var updateResult = await _users.UpdateAsync(user);
        if (!updateResult.Succeeded)
            return BadRequest(new { errors = updateResult.Errors.Select(e => e.Description).ToList() });

        if (removed.Count > 0)
        {
            var rr = await _users.RemoveFromRolesAsync(user, removed);
            if (!rr.Succeeded)
                return BadRequest(new { errors = rr.Errors.Select(e => e.Description).ToList() });
        }
        if (added.Count > 0)
        {
            var ar = await _users.AddToRolesAsync(user, added);
            if (!ar.Succeeded)
                return BadRequest(new { errors = ar.Errors.Select(e => e.Description).ToList() });
        }

        var finalRoles = await _users.GetRolesAsync(user);
        string? supplierName = null;
        if (user.SupplierId.HasValue)
            supplierName = await _db.Suppliers.AsNoTracking()
                .Where(s => s.Id == user.SupplierId.Value)
                .Select(s => s.Name)
                .FirstOrDefaultAsync(ct);

        return new AdminUserListDto
        {
            Id = user.Id,
            Email = user.Email,
            DisplayName = user.DisplayName,
            Roles = finalRoles.OrderBy(r => r).ToList(),
            LockoutEnabled = user.LockoutEnabled,
            LockedOut = user.LockoutEnd.HasValue && user.LockoutEnd > DateTimeOffset.UtcNow,
            LockoutEnd = user.LockoutEnd,
            SupplierId = user.SupplierId,
            SupplierName = supplierName
        };
    }

    /// <summary>
    /// Links (or unlinks) a user to a Supplier so they see a vendor-scoped report filtered
    /// to that supplier's stock/sales. Pass <c>null</c> to clear the link.
    /// </summary>
    [HttpPost("{id}/supplier")]
    public async Task<IActionResult> SetSupplier(string id, [FromBody] SetUserSupplierRequest body, CancellationToken ct)
    {
        var user = await _users.FindByIdAsync(id);
        if (user == null) return NotFound();

        if (body.SupplierId.HasValue)
        {
            var exists = await _db.Suppliers.AnyAsync(s => s.Id == body.SupplierId.Value, ct);
            if (!exists) return BadRequest(new { error = "Supplier not found." });
        }

        user.SupplierId = body.SupplierId;
        var result = await _users.UpdateAsync(user);
        if (!result.Succeeded)
            return BadRequest(new { errors = result.Errors.Select(e => e.Description).ToList() });
        return NoContent();
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

    [HttpDelete("{id}")]
    [Authorize(Roles = $"{Roles.Owner},{Roles.Dev}")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        var currentId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (id == currentId)
            return BadRequest(new { error = "You cannot delete your own account." });

        var user = await _users.FindByIdAsync(id);
        if (user == null) return NotFound();

        var roles = await _users.GetRolesAsync(user);
        if (roles.Contains(Roles.Owner) && !User.IsInRole(Roles.Dev))
            return BadRequest(new { error = "Owner accounts can only be deleted by a Dev user." });

        var result = await _users.DeleteAsync(user);
        if (!result.Succeeded)
            return BadRequest(new { errors = result.Errors.Select(e => e.Description).ToList() });

        return NoContent();
    }

    private async Task SendSetupEmailAsync(ApplicationUser user, CancellationToken ct)
    {
        var eff = await _business.GetAsync(ct);
        var token = await _users.GeneratePasswordResetTokenAsync(user);
        var encoded = Uri.EscapeDataString(token);
        var emailEncoded = Uri.EscapeDataString(user.Email!);
        var link = $"{_app.PublicBaseUrl.TrimEnd('/')}/#/setup-password?token={encoded}&email={emailEncoded}";
        var name = user.DisplayName ?? user.Email;
        var accent = string.IsNullOrWhiteSpace(eff.AccentColor) ? "#ff6600" : eff.AccentColor;
        var shop = System.Net.WebUtility.HtmlEncode(eff.BusinessName);
        var phone = System.Net.WebUtility.HtmlEncode(eff.Phone);
        var addr = System.Net.WebUtility.HtmlEncode(eff.Address);

        var html = $"""
        <div style="font-family:sans-serif;max-width:500px;margin:0 auto">
            <h2 style="color:{accent}">Welcome to {shop}</h2>
            <p>Hi {System.Net.WebUtility.HtmlEncode(name!)},</p>
            <p>An account has been created for you. Please set your password to get started:</p>
            <p style="text-align:center;margin:24px 0">
                <a href="{link}" style="display:inline-block;padding:12px 28px;
                    background:{accent};color:#fff;text-decoration:none;border-radius:4px;
                    font-weight:bold">Set my password</a>
            </p>
            <p style="font-size:0.85rem;color:#888">
                If the button doesn't work, copy this link into your browser:<br/>
                <a href="{link}">{link}</a>
            </p>
            <p style="font-size:0.85rem;color:#888">
                {shop} &bull; {phone}<br/>
                {addr}
            </p>
        </div>
        """;

        await _email.SendInvoiceEmailAsync(
            user.Email!,
            $"Set up your {eff.BusinessName} account",
            html,
            null, null, ct);
    }

    private static string GenerateTempPassword()
    {
        var bytes = RandomNumberGenerator.GetBytes(24);
        return Convert.ToBase64String(bytes) + "!Aa1";
    }
}
