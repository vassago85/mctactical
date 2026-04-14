namespace HuntexPos.Api.DTOs;

public class PosRulesDto
{
    public decimal MaxCartDiscountPercent { get; set; }
    public decimal MaxLineDiscountPercent { get; set; }
    public decimal MaxPriceDecreasePercentFromList { get; set; }
    public decimal MaxPriceIncreasePercentFromList { get; set; }
    public bool BlockZeroOrNegativeTotal { get; set; }
}

public class PricingSettingsDto
{
    public decimal DefaultMarginPercent { get; set; }
    public decimal DefaultFixedMarkup { get; set; }
    public bool UseMarginPercent { get; set; }
    public decimal DefaultTaxRate { get; set; }
    public bool HideCostForSalesRole { get; set; }
}
