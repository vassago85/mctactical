namespace HuntexPos.Api.Options;

public class AppOptions
{
    public const string SectionName = "App";
    public string PublicBaseUrl { get; set; } = "https://pos.mctactical.co.za";
    public string PdfStoragePath { get; set; } = "storage/pdfs";
    public string BrandingStoragePath { get; set; } = "storage/branding";
    public bool HideCostForSalesRole { get; set; } = true;

    /// <summary>Shown on customer receipts (email, PDF, public invoice page).</summary>
    public string CompanyDisplayName { get; set; } = "MC Tactical";

    public string CompanyPhone { get; set; } = "082 821 0420";
    public string CompanyEmail { get; set; } = "sales@mctactical.co.za";

    /// <summary>Single line or short block for the shop address.</summary>
    public string CompanyAddress { get; set; } =
        "873A Patryshond Street, Garsfontein, Pretoria, 0042";

    public string CompanyVatNumber { get; set; } = "4030296307";

    public string CompanyWebsite { get; set; } = "https://www.mctactical.co.za";

    /// <summary>Short link text for <see cref="CompanyWebsite"/> (e.g. mctactical.co.za). If empty, derived from the URL.</summary>
    public string CompanyWebsiteLabel { get; set; } = "";
}
