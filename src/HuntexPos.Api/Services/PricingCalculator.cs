using HuntexPos.Api.Domain;

namespace HuntexPos.Api.Services;

public static class PricingCalculator
{
    public static decimal Round2(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

    /// <summary>Round up to the nearest R10 (always).</summary>
    public static decimal RoundToR10(decimal price) =>
        price <= 0 ? 0 : Math.Ceiling(price / 10m) * 10m;

    /// <summary>
    /// Compute sell price from ex-VAT wholesale cost.
    /// cost × markup → round up to nearest R10.
    /// </summary>
    public static decimal ComputeSellPrice(decimal cost, PricingSettings settings)
    {
        var sell = settings.UseMarginPercent
            ? Round2(cost * (1 + settings.DefaultMarginPercent / 100m))
            : Round2(cost + settings.DefaultFixedMarkup);

        return RoundToR10(sell);
    }

    /// <summary>Round an already-known sell price up to nearest R10.</summary>
    public static decimal ApplyRounding(decimal sellPrice, PricingSettings settings)
        => RoundToR10(sellPrice);

    /// <summary>Distributor cost floor = ex-VAT cost × 1.15. Sell below this means selling at a loss.</summary>
    public static decimal DistributorFloor(decimal cost) => Round2(cost * 1.15m);

    /// <summary>True if sell price is below the distributor cost (cost + 15% VAT).</summary>
    public static bool IsBelowDistributorCost(decimal sellPrice, decimal cost) =>
        sellPrice > 0 && cost > 0 && sellPrice < DistributorFloor(cost);

    public static bool IsHuntex(PricingSettings settings) =>
        string.Equals(settings.PricingMode, "huntex", StringComparison.OrdinalIgnoreCase);
}
