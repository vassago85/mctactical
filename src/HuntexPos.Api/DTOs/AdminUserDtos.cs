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

    public string? DisplayName { get; set; }

    /// <summary>Sales (cashiers), Admin (back office), or Owner (full access). Owner and Dev can assign Owner.</summary>
    [Required, AllowedValues("Sales", "Admin", "Owner")]
    public string Role { get; set; } = "Sales";
}

public class SetupPasswordRequest
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string Token { get; set; } = string.Empty;

    [Required, MinLength(10)]
    public string NewPassword { get; set; } = string.Empty;
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
