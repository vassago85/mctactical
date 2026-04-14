namespace HuntexPos.Api.Options;

/// <summary>Retail POS controls aligned with common 2020s practice: caps on discounts, price changes, and staff session length.</summary>
public class PosRulesOptions
{
    public const string SectionName = "PosRules";

    /// <summary>Max cart-level discount as % of line total (after line discounts), e.g. 25 = 25%.</summary>
    public decimal MaxCartDiscountPercent { get; set; } = 25;

    /// <summary>Max line discount as % of (unit price × qty) before that discount.</summary>
    public decimal MaxLineDiscountPercent { get; set; } = 50;

    /// <summary>Sales cannot price below list × (1 − this/100).</summary>
    public decimal MaxPriceDecreasePercentFromList { get; set; } = 15;

    /// <summary>Sales cannot price above list × (1 + this/100).</summary>
    public decimal MaxPriceIncreasePercentFromList { get; set; } = 10;

    /// <summary>Reject sales where grand total is zero or negative after tax.</summary>
    public bool BlockZeroOrNegativeTotal { get; set; } = true;
}
