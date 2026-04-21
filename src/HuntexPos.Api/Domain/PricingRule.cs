namespace HuntexPos.Api.Domain;

/// <summary>
/// Scope of a <see cref="PricingRule"/>. Resolution order when deriving a product's
/// effective markup/round/min-margin is:
///   Supplier → Manufacturer → Category → Global
/// Later scopes override earlier ones for that field, so a Supplier rule wins over
/// a Category rule where both specify a value.
/// </summary>
public enum PricingRuleScope
{
    Global = 0,
    Category = 1,
    Manufacturer = 2,
    Supplier = 3,
}

/// <summary>
/// A pricing override at global/category/manufacturer/supplier level. Nullable fields
/// indicate "inherit from higher scope" so each rule can override only what it needs.
/// The identity is (<see cref="Scope"/>, <see cref="ScopeKey"/>, <see cref="SupplierId"/>).
/// </summary>
public class PricingRule
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public PricingRuleScope Scope { get; set; }

    /// <summary>Category name (case-insensitive) or manufacturer name; null for Global/Supplier rules.</summary>
    public string? ScopeKey { get; set; }

    /// <summary>Supplier id for <see cref="PricingRuleScope.Supplier"/> rules; null otherwise.</summary>
    public Guid? SupplierId { get; set; }

    /// <summary>Default markup percent applied to cost. e.g. 50 means sell = cost × 1.5.</summary>
    public decimal? DefaultMarkupPercent { get; set; }

    /// <summary>Max discount (0-100) a sales user may apply before hitting the floor.</summary>
    public decimal? MaxDiscountPercent { get; set; }

    /// <summary>Round derived sell price UP to the nearest multiple of this (e.g. R10).</summary>
    public decimal? RoundToNearest { get; set; }

    /// <summary>Minimum gross margin percent (0-100). Sell price floor = cost / (1 - m/100).</summary>
    public decimal? MinMarginPercent { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
