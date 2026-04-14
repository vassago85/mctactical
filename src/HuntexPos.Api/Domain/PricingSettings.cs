namespace HuntexPos.Api.Domain;

public class PricingSettings
{
    public Guid Id { get; set; } = Guid.Parse("00000000-0000-0000-0000-000000000001");
    public decimal DefaultMarginPercent { get; set; }
    public decimal DefaultFixedMarkup { get; set; }
    public bool UseMarginPercent { get; set; } = true;

    /// <summary>Round sell prices to the nearest N rands (e.g. 10 = R10). 0 = no rounding.</summary>
    public decimal RoundSellToNearest { get; set; }

    /// <summary>Unused: invoices are VAT-free. Column retained for existing databases.</summary>
    public decimal DefaultTaxRate { get; set; }
    public bool HideCostForSalesRole { get; set; } = true;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
