namespace HuntexPos.Api.Options;

/// <summary>POS controls for sales staff. Managers (Admin/Owner/Dev) bypass all limits.</summary>
public class PosRulesOptions
{
    public const string SectionName = "PosRules";

    /// <summary>Max cart-level discount as % of subtotal. 0 = no cart discount for sales staff.</summary>
    public decimal MaxCartDiscountPercent { get; set; } = 0;

    /// <summary>Max line discount as % of (unit price × qty). 0 = no line discounts for sales staff.</summary>
    public decimal MaxLineDiscountPercent { get; set; } = 0;

    /// <summary>Sales cannot price below list × (1 − this/100). 0 = no price decrease allowed.</summary>
    public decimal MaxPriceDecreasePercentFromList { get; set; } = 0;

    /// <summary>Sales cannot price above list × (1 + this/100). 0 = no price increase allowed.</summary>
    public decimal MaxPriceIncreasePercentFromList { get; set; } = 0;

    /// <summary>Reject sales where grand total is zero or negative after discounts.</summary>
    public bool BlockZeroOrNegativeTotal { get; set; } = true;
}
