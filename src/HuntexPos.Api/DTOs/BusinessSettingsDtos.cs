using System.ComponentModel.DataAnnotations;

namespace HuntexPos.Api.DTOs;

/// <summary>Full settings read/write shape (authenticated, Owner/Admin/Dev).</summary>
public class BusinessSettingsDto
{
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

    public string? LogoUrl { get; set; }
    public string? FaviconUrl { get; set; }

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

    /// <summary>Master toggle for Accounts Receivable (Phase 3B). Default off.</summary>
    public bool AccountsEnabled { get; set; } = false;
}

/// <summary>Public branding payload for the login page, public invoice/quote views, and app boot (no secrets).</summary>
public class PublicBrandingDto
{
    public string BusinessName { get; set; } = string.Empty;
    public string? LogoUrl { get; set; }
    public string? FaviconUrl { get; set; }
    public string PrimaryColor { get; set; } = string.Empty;
    public string SecondaryColor { get; set; } = string.Empty;
    public string AccentColor { get; set; } = string.Empty;
    public BrandingTerminologyDto Terminology { get; set; } = new();
    public BrandingFeaturesDto Features { get; set; } = new();
}

public class BrandingTerminologyDto
{
    public string Quote { get; set; } = "Quote";
    public string Invoice { get; set; } = "Invoice";
    public string Customer { get; set; } = "Customer";
}

public class BrandingFeaturesDto
{
    public bool Quotes { get; set; } = true;
    public bool Discounts { get; set; } = true;
    public bool BrandPricingRules { get; set; } = true;

    /// <summary>True when Accounts Receivable is enabled for this deployment.</summary>
    public bool Accounts { get; set; } = false;
}
