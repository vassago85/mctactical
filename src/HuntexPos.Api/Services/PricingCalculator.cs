using HuntexPos.Api.Domain;

namespace HuntexPos.Api.Services;

public static class PricingCalculator
{
    public static decimal Round2(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Default list price from wholesale <paramref name="cost"/> stored as <b>excluding VAT</b>.
    /// With margin mode: sell = cost × (1 + margin/100) — e.g. 50% ⇒ ×1.5 on ex-VAT cost (not on inc-VAT).
    /// </summary>
    public static decimal ComputeSellPrice(decimal cost, PricingSettings settings)
    {
        if (settings.UseMarginPercent)
            return Round2(cost * (1 + settings.DefaultMarginPercent / 100m));
        return Round2(cost + settings.DefaultFixedMarkup);
    }
}
