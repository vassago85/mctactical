using HuntexPos.Api.Domain;

namespace HuntexPos.Api.Services;

public static class PricingCalculator
{
    public static decimal Round2(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Round <paramref name="price"/> to the nearest <paramref name="nearest"/> (e.g. 10 → R10).
    /// Uses midpoint-away-from-zero (R115 → R120). Returns unchanged if nearest &lt;= 0.
    /// </summary>
    public static decimal RoundToNearest(decimal price, decimal nearest)
    {
        if (nearest <= 0) return price;
        return Math.Ceiling(price / nearest) * nearest;
    }

    /// <summary>
    /// Default list price from wholesale <paramref name="cost"/>.
    /// Margin mode: sell = cost × (1 + margin/100). Then rounded to nearest R<c>settings.RoundSellToNearest</c> if set.
    /// </summary>
    public static decimal ComputeSellPrice(decimal cost, PricingSettings settings)
    {
        var raw = settings.UseMarginPercent
            ? Round2(cost * (1 + settings.DefaultMarginPercent / 100m))
            : Round2(cost + settings.DefaultFixedMarkup);
        return RoundToNearest(raw, settings.RoundSellToNearest);
    }

    /// <summary>Apply the configured rounding to an already-known sell price (e.g. from an import sheet).</summary>
    public static decimal ApplyRounding(decimal sellPrice, PricingSettings settings)
        => RoundToNearest(sellPrice, settings.RoundSellToNearest);
}
