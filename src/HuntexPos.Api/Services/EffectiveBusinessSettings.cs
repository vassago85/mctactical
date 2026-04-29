namespace HuntexPos.Api.Services;

/// <summary>Merged view: DB <c>BusinessSettings</c> row wins per-field; blanks fall back to <c>AppOptions</c>.</summary>
public sealed class EffectiveBusinessSettings
{
    public string BusinessName { get; init; } = string.Empty;
    public string LegalName { get; init; } = string.Empty;
    public string VatNumber { get; init; } = string.Empty;
    public string Currency { get; init; } = "ZAR";
    public string TimeZone { get; init; } = "Africa/Johannesburg";

    public string Email { get; init; } = string.Empty;
    public string Phone { get; init; } = string.Empty;
    public string Address { get; init; } = string.Empty;
    public string Website { get; init; } = string.Empty;
    public string WebsiteLabel { get; init; } = string.Empty;

    public string? LogoStorageKey { get; init; }
    public string? FaviconStorageKey { get; init; }

    public string PrimaryColor { get; init; } = string.Empty;
    public string SecondaryColor { get; init; } = string.Empty;
    public string AccentColor { get; init; } = string.Empty;

    public string ReceiptFooter { get; init; } = string.Empty;
    public string QuoteTerms { get; init; } = string.Empty;
    public string InvoiceTerms { get; init; } = string.Empty;
    public string ReturnPolicy { get; init; } = string.Empty;

    public string QuoteLabel { get; init; } = "Quote";
    public string InvoiceLabel { get; init; } = "Invoice";
    public string CustomerLabel { get; init; } = "Customer";

    public bool EnableQuotes { get; init; } = true;
    public bool EnableDiscounts { get; init; } = true;
    public bool EnableBrandPricingRules { get; init; } = true;

    /// <summary>Master toggle for Accounts Receivable. Default off so existing deployments keep cash-only behaviour.</summary>
    public bool AccountsEnabled { get; init; } = false;
}
