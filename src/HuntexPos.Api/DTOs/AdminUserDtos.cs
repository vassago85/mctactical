using System.ComponentModel.DataAnnotations;

namespace HuntexPos.Api.DTOs;

public class AdminUserListDto
{
    public string Id { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? DisplayName { get; set; }
    public List<string> Roles { get; set; } = new();
    public bool LockoutEnabled { get; set; }
    public bool LockedOut { get; set; }
    public DateTimeOffset? LockoutEnd { get; set; }
}

public class CreateStaffUserRequest
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required, MinLength(10)]
    public string Password { get; set; } = string.Empty;

    public string? DisplayName { get; set; }

    /// <summary>Sales (cashiers) or Admin (back office). Owner/Dev cannot be created here.</summary>
    [Required, AllowedValues("Sales", "Admin")]
    public string Role { get; set; } = "Sales";
}

public class SetUserLockoutRequest
{
    public bool Locked { get; set; }
}

public class AdminResetPasswordRequest
{
    [Required, MinLength(10)]
    public string NewPassword { get; set; } = string.Empty;
}
