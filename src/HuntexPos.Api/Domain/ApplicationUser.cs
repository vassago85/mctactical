using Microsoft.AspNetCore.Identity;

namespace HuntexPos.Api.Domain;

public class ApplicationUser : IdentityUser
{
    public string? DisplayName { get; set; }
}
