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
    /// <summary>Round sell prices up to the nearest N rands (e.g. 10). 0 = no rounding.</summary>
    public decimal RoundSellToNearest { get; set; }
    public bool HideCostForSalesRole { get; set; }
}

public class MailSettingsDto
{
    public string Domain { get; set; } = string.Empty;
    public string From { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public bool AttachPdf { get; set; }
    /// <summary>True if an API key is configured (database and/or environment).</summary>
    public bool HasApiKey { get; set; }
}

public class MailSettingsUpdateDto
{
    /// <summary>Leave empty to keep the current API key unchanged.</summary>
    public string? ApiKey { get; set; }
    public string Domain { get; set; } = string.Empty;
    public string From { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public bool AttachPdf { get; set; }
}

public class TestEmailRequest
{
    public string? To { get; set; }
}
