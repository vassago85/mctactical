namespace HuntexPos.Api.Domain;

/// <summary>
/// Singleton row holding per-deployment branding, contact details, document copy,
/// terminology overrides, and feature toggles. Empty/null fields fall back to <c>AppOptions</c>.
/// </summary>
public class BusinessSettings
{
    public Guid Id { get; set; } = Guid.Parse("00000000-0000-0000-0000-000000000003");

    public string BusinessName { get; set; } = string.Empty;
    public string LegalName { get; set; } = string.Empty;
    public string VatNumber { get; set; } = string.Empty;
    public string Currency { get; set; } = "ZAR";
    public string TimeZone { get; set; } = "Africa/Johannesburg";

    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Website { get; set; } = string.Empty;
    public string WebsiteLabel { get; set; } = string.Empty;

    /// <summary>Storage filename under {MCTACTICAL_DATA_DIR}/branding/ (e.g. "logo.png"). Null = use bundled default.</summary>
    public string? LogoStorageKey { get; set; }
    public string? FaviconStorageKey { get; set; }

    public string PrimaryColor { get; set; } = string.Empty;
    public string SecondaryColor { get; set; } = string.Empty;
    public string AccentColor { get; set; } = string.Empty;

    public string ReceiptFooter { get; set; } = string.Empty;
    public string QuoteTerms { get; set; } = string.Empty;
    public string InvoiceTerms { get; set; } = string.Empty;
    public string ReturnPolicy { get; set; } = string.Empty;

    public string QuoteLabel { get; set; } = "Quote";
    public string InvoiceLabel { get; set; } = "Invoice";
    public string CustomerLabel { get; set; } = "Customer";

    public bool EnableQuotes { get; set; } = true;
    public bool EnableDiscounts { get; set; } = true;
    public bool EnableBrandPricingRules { get; set; } = true;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
