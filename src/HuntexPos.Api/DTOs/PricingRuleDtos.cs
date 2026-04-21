namespace HuntexPos.Api.DTOs;

public class PricingRuleDto
{
    public Guid Id { get; set; }
    public string Scope { get; set; } = "Global";
    public string? ScopeKey { get; set; }
    public Guid? SupplierId { get; set; }
    public string? SupplierName { get; set; }
    public decimal? DefaultMarkupPercent { get; set; }
    public decimal? MaxDiscountPercent { get; set; }
    public decimal? RoundToNearest { get; set; }
    public decimal? MinMarginPercent { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset UpdatedAt { get; set; }
}

public class UpsertPricingRuleDto
{
    public string Scope { get; set; } = "Global";
    public string? ScopeKey { get; set; }
    public Guid? SupplierId { get; set; }
    public decimal? DefaultMarkupPercent { get; set; }
    public decimal? MaxDiscountPercent { get; set; }
    public decimal? RoundToNearest { get; set; }
    public decimal? MinMarginPercent { get; set; }
    public bool IsActive { get; set; } = true;
}

public class PricingPreviewRequestDto
{
    public decimal Cost { get; set; }
    public string? Category { get; set; }
    public string? Manufacturer { get; set; }
    public Guid? SupplierId { get; set; }
    public string PricingMethod { get; set; } = "default";
    public decimal? CustomMarkupPercent { get; set; }
    public decimal? FixedSellPrice { get; set; }
    public decimal? MinSellPrice { get; set; }
}

public class PricingPreviewResponseDto
{
    public decimal SellPrice { get; set; }
    public decimal MinAllowedPrice { get; set; }
    public string Source { get; set; } = string.Empty;
    public string PricingMethod { get; set; } = "default";
    public decimal EffectiveMarkupPercent { get; set; }
    public decimal EffectiveMaxDiscountPercent { get; set; }
    public decimal EffectiveRoundToNearest { get; set; }
    public decimal? EffectiveMinMarginPercent { get; set; }
}
