namespace HuntexPos.Api.Options;

public class JwtOptions
{
    public const string SectionName = "Jwt";
    public string Key { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public int ExpiresMinutes { get; set; } = 10080;

    /// <summary>Session length for users who only have the Sales role (hours on a till). 0 = use ExpiresMinutes.</summary>
    public int SalesExpiresMinutes { get; set; } = 720;
}
