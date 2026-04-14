using System.ComponentModel.DataAnnotations;

namespace HuntexPos.Api.DTOs;

public class LoginRequest
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;
    [Required]
    public string Password { get; set; } = string.Empty;
}

public class LoginResponse
{
    public string Token { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public IList<string> Roles { get; set; } = new List<string>();
    public string UserId { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
}
