using HuntexPos.Api.Data;
using HuntexPos.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace HuntexPos.Api.Services;

/// <summary>
/// Resolved pricing parameters for a single product — the merged view of
/// product overrides + supplier/manufacturer/category/global rules + legacy PricingSettings.
/// </summary>
public sealed record EffectivePricing(
    decimal MarkupPercent,
    decimal MaxDiscountPercent,
    decimal RoundToNearest,
    decimal? MinMarginPercent,
    string Source);

/// <summary>
/// Computed result for a product: the derived sell price and which rule produced it.
/// </summary>
public sealed record PricingResolution(
    decimal SellPrice,
    decimal MinAllowedPrice,
    string Source,
    string PricingMethod,
    EffectivePricing Effective);

public interface IPricingService
{
    Task<PricingResolution> ResolveAsync(Product product, CancellationToken ct = default);

    /// <summary>Preview without a saved product — used by forms.</summary>
    Task<PricingResolution> PreviewAsync(
        decimal cost,
        string? category,
        string? manufacturer,
        Guid? supplierId,
        string pricingMethod,
        decimal? customMarkupPercent,
        decimal? fixedSellPrice,
        decimal? minSellPrice,
        CancellationToken ct = default);
}

public class PricingService : IPricingService
{
    private readonly HuntexDbContext _db;

    public PricingService(HuntexDbContext db) => _db = db;

    public async Task<PricingResolution> ResolveAsync(Product product, CancellationToken ct = default)
    {
        return await PreviewAsync(
            product.Cost,
            product.Category,
            product.Manufacturer,
            product.SupplierId,
            product.PricingMethod,
            product.CustomMarkupPercent,
            product.FixedSellPrice,
            product.MinSellPrice,
            ct);
    }

    public async Task<PricingResolution> PreviewAsync(
        decimal cost,
        string? category,
        string? manufacturer,
        Guid? supplierId,
        string pricingMethod,
        decimal? customMarkupPercent,
        decimal? fixedSellPrice,
        decimal? minSellPrice,
        CancellationToken ct = default)
    {
        var (effective, hierarchySource) = await ResolveEffectiveAsync(category, manufacturer, supplierId, ct);
        var method = NormalizeMethod(pricingMethod);

        decimal sell;
        string source;

        switch (method)
        {
            case "fixed_price" when fixedSellPrice.HasValue && fixedSellPrice.Value > 0:
                sell = PricingCalculator.Round2(fixedSellPrice.Value);
                source = "Product (fixed price)";
                break;

            case "custom_markup" when customMarkupPercent.HasValue:
                sell = ApplyMarkup(cost, customMarkupPercent.Value, effective.RoundToNearest);
                source = "Product (custom markup)";
                break;

            default:
                sell = ApplyMarkup(cost, effective.MarkupPercent, effective.RoundToNearest);
                source = hierarchySource;
                break;
        }

        var min = ComputeMinAllowed(cost, sell, minSellPrice, effective);
        if (min > 0 && sell < min) sell = min;

        return new PricingResolution(sell, min, source, method, effective);
    }

    private async Task<(EffectivePricing Effective, string Source)> ResolveEffectiveAsync(
        string? category, string? manufacturer, Guid? supplierId, CancellationToken ct)
    {
        var rules = await _db.PricingRules.AsNoTracking()
            .Where(r => r.IsActive)
            .ToListAsync(ct);

        var legacy = await _db.PricingSettings.AsNoTracking().FirstOrDefaultAsync(ct)
                     ?? new PricingSettings();
        var legacyMarkup = legacy.UseMarginPercent ? legacy.DefaultMarginPercent : 0m;

        decimal markup = legacyMarkup;
        decimal maxDiscount = 100m;
        decimal round = legacy.RoundSellToNearest > 0 ? legacy.RoundSellToNearest : 10m;
        decimal? minMargin = null;
        string source = "Global (legacy)";

        var global = rules.FirstOrDefault(r => r.Scope == PricingRuleScope.Global);
        if (global != null)
        {
            if (global.DefaultMarkupPercent.HasValue) markup = global.DefaultMarkupPercent.Value;
            if (global.MaxDiscountPercent.HasValue) maxDiscount = global.MaxDiscountPercent.Value;
            if (global.RoundToNearest.HasValue) round = global.RoundToNearest.Value;
            if (global.MinMarginPercent.HasValue) minMargin = global.MinMarginPercent.Value;
            source = "Global";
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            var cat = rules.FirstOrDefault(r => r.Scope == PricingRuleScope.Category &&
                string.Equals(r.ScopeKey, category, StringComparison.OrdinalIgnoreCase));
            if (cat != null)
            {
                if (cat.DefaultMarkupPercent.HasValue) { markup = cat.DefaultMarkupPercent.Value; source = $"Category: {category}"; }
                if (cat.MaxDiscountPercent.HasValue) maxDiscount = cat.MaxDiscountPercent.Value;
                if (cat.RoundToNearest.HasValue) round = cat.RoundToNearest.Value;
                if (cat.MinMarginPercent.HasValue) minMargin = cat.MinMarginPercent.Value;
            }
        }

        if (!string.IsNullOrWhiteSpace(manufacturer))
        {
            var mfg = rules.FirstOrDefault(r => r.Scope == PricingRuleScope.Manufacturer &&
                string.Equals(r.ScopeKey, manufacturer, StringComparison.OrdinalIgnoreCase));
            if (mfg != null)
            {
                if (mfg.DefaultMarkupPercent.HasValue) { markup = mfg.DefaultMarkupPercent.Value; source = $"Manufacturer: {manufacturer}"; }
                if (mfg.MaxDiscountPercent.HasValue) maxDiscount = mfg.MaxDiscountPercent.Value;
                if (mfg.RoundToNearest.HasValue) round = mfg.RoundToNearest.Value;
                if (mfg.MinMarginPercent.HasValue) minMargin = mfg.MinMarginPercent.Value;
            }
        }

        if (supplierId.HasValue)
        {
            var sup = rules.FirstOrDefault(r => r.Scope == PricingRuleScope.Supplier && r.SupplierId == supplierId.Value);
            if (sup != null)
            {
                if (sup.DefaultMarkupPercent.HasValue) { markup = sup.DefaultMarkupPercent.Value; source = "Supplier"; }
                if (sup.MaxDiscountPercent.HasValue) maxDiscount = sup.MaxDiscountPercent.Value;
                if (sup.RoundToNearest.HasValue) round = sup.RoundToNearest.Value;
                if (sup.MinMarginPercent.HasValue) minMargin = sup.MinMarginPercent.Value;
            }
        }

        if (round <= 0) round = 1m;
        if (maxDiscount < 0) maxDiscount = 0;
        if (maxDiscount > 100) maxDiscount = 100;

        return (new EffectivePricing(markup, maxDiscount, round, minMargin, source), source);
    }

    private static string NormalizeMethod(string? raw) => (raw ?? "default").Trim().ToLowerInvariant() switch
    {
        "fixed_price" or "fixed" => "fixed_price",
        "custom_markup" or "custom" => "custom_markup",
        _ => "default"
    };

    private static decimal ApplyMarkup(decimal cost, decimal markupPercent, decimal round)
    {
        if (cost <= 0) return 0;
        var sell = PricingCalculator.Round2(cost * (1 + markupPercent / 100m));
        return RoundUpTo(sell, round);
    }

    private static decimal RoundUpTo(decimal value, decimal increment)
    {
        if (increment <= 0 || value <= 0) return value;
        return Math.Ceiling(value / increment) * increment;
    }

    private static decimal ComputeMinAllowed(decimal cost, decimal sell, decimal? productFloor, EffectivePricing eff)
    {
        decimal floor = 0m;
        if (productFloor.HasValue && productFloor.Value > 0) floor = productFloor.Value;

        if (eff.MinMarginPercent.HasValue && cost > 0)
        {
            var m = eff.MinMarginPercent.Value;
            if (m > 0 && m < 100)
            {
                var minByMargin = PricingCalculator.Round2(cost / (1 - m / 100m));
                if (minByMargin > floor) floor = minByMargin;
            }
        }

        if (eff.MaxDiscountPercent < 100 && sell > 0)
        {
            var minByDiscount = PricingCalculator.Round2(sell * (1 - eff.MaxDiscountPercent / 100m));
            if (minByDiscount > floor) floor = minByDiscount;
        }

        return floor;
    }
}
