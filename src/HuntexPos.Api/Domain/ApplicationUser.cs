using Microsoft.AspNetCore.Identity;

namespace HuntexPos.Api.Domain;

public class ApplicationUser : IdentityUser
{
    public string? DisplayName { get; set; }

    /// <summary>
    /// Optional link to a <see cref="Supplier"/>. When set, the user has read-only access
    /// to a vendor-scoped report that only shows data for this supplier. Used for
    /// consignment sub-vendors helping at events (e.g. Venatics Gear at Huntex).
    /// </summary>
    public Guid? SupplierId { get; set; }
}
