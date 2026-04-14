using System.Linq;
using System.Security.Claims;
using HuntexPos.Api.Domain;
using HuntexPos.Api.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HuntexPos.Api.Controllers;

[ApiController]
[Route("api/admin/users")]
[Authorize(Roles = $"{Roles.Admin},{Roles.Owner},{Roles.Dev}")]
public class AdminUsersController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _users;

    public AdminUsersController(UserManager<ApplicationUser> users) => _users = users;

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
        if (req.Role == Roles.Admin && !User.IsInRole(Roles.Owner) && !User.IsInRole(Roles.Dev))
            return Forbid();

        var user = new ApplicationUser
        {
            UserName = req.Email.Trim(),
            Email = req.Email.Trim(),
            EmailConfirmed = true,
            DisplayName = string.IsNullOrWhiteSpace(req.DisplayName) ? null : req.DisplayName.Trim()
        };
        var result = await _users.CreateAsync(user, req.Password);
        if (!result.Succeeded)
            return BadRequest(new { errors = result.Errors.Select(e => e.Description).ToList() });

        await _users.AddToRoleAsync(user, req.Role);
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
}
