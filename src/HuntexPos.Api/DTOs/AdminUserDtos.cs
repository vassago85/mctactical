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

    /// <summary>When set, the user sees a vendor-scoped report for this supplier only.</summary>
    public Guid? SupplierId { get; set; }
    public string? SupplierName { get; set; }
}

public class CreateStaffUserRequest
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    public string? DisplayName { get; set; }

    /// <summary>Sales (cashiers), Admin (back office), or Owner (full access). Owner and Dev can assign Owner.</summary>
    [Required, AllowedValues("Sales", "Admin", "Owner")]
    public string Role { get; set; } = "Sales";

    /// <summary>Optional — link a Sales-role user to a supplier for vendor-scoped reporting.</summary>
    public Guid? SupplierId { get; set; }
}

public class SetUserSupplierRequest
{
    public Guid? SupplierId { get; set; }
}

public class UpdateStaffUserRequest
{
    /// <summary>New display name. Pass null/empty to clear it.</summary>
    public string? DisplayName { get; set; }

    /// <summary>Replacement list of role names. Must contain at least one valid role.</summary>
    [Required]
    public List<string> Roles { get; set; } = new();
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
