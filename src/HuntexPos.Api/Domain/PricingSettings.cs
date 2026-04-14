namespace HuntexPos.Api.Domain;

public class PricingSettings
{
    public Guid Id { get; set; } = Guid.Parse("00000000-0000-0000-0000-000000000001");
    public decimal DefaultMarginPercent { get; set; } = 50;
    public decimal DefaultFixedMarkup { get; set; }
    public bool UseMarginPercent { get; set; } = true;

    /// <summary>"normal" or "huntex". Normal = cost×1.5 rounded R10. Huntex = cost×1.5/1.1 rounded R10.</summary>
    public string PricingMode { get; set; } = "normal";

    /// <summary>All sell prices rounded up to nearest R10.</summary>
    public decimal RoundSellToNearest { get; set; } = 10;

    /// <summary>Unused: invoices are VAT-free. Column retained for existing databases.</summary>
    public decimal DefaultTaxRate { get; set; }
    public bool HideCostForSalesRole { get; set; } = true;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
