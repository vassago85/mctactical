namespace HuntexPos.Api.Options;

/// <summary>
/// Settings for the Roll for It in-store integration. The Roll for It admin
/// generates <see cref="PosSecret"/> once and the merchant pastes it in here.
/// </summary>
public class RollForItOptions
{
    public const string SectionName = "RollForIt";

    /// <summary>Toggle the entire integration without wiping the secret.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>Base URL of the Roll for It POS API, including <c>/api/pos</c>.</summary>
    public string BaseUrl { get; set; } = "https://rollforit.app/api/pos";

    /// <summary>Shop domain Roll for It knows this store by (e.g. <c>mctactical.myshopify.com</c>).</summary>
    public string ShopDomain { get; set; } = string.Empty;

    /// <summary>Shared signing secret. Set once; rotating it in Roll for It admin requires updating this value.</summary>
    public string PosSecret { get; set; } = string.Empty;

    /// <summary>
    /// How long a successful roll result is held in memory before the till can no longer apply it
    /// to an invoice. Covers the "customer rolls, then takes 2 minutes deciding whether to buy"
    /// gap; longer than this and the operator should re-roll.
    /// </summary>
    public int WinningHoldMinutes { get; set; } = 10;

    public bool IsConfigured() =>
        Enabled && !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(ShopDomain) && !string.IsNullOrWhiteSpace(PosSecret);
}
