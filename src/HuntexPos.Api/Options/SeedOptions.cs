namespace HuntexPos.Api.Options;

public class SeedOptions
{
    public const string SectionName = "Seed";

    /// <summary>If both are set, first-run creates this owner user. Leave empty to skip (create via ops / Team UI after first admin exists).</summary>
    public string OwnerEmail { get; set; } = string.Empty;
    public string OwnerPassword { get; set; } = string.Empty;
}
