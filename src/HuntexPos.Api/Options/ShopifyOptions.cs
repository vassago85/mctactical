namespace HuntexPos.Api.Options;

/// <summary>
/// Configuration for the Shopify sync bridge. The POS is the source of truth; these
/// credentials belong to a Shopify Dev Dashboard app with Admin API scopes:
/// read_products, write_products, read_inventory, write_inventory, read_locations,
/// read_orders, read_customers.
///
/// Auth: modern Dev Dashboard apps no longer expose a permanent access token. Instead you
/// supply <see cref="ClientId"/> + <see cref="ClientSecret"/> and the API exchanges them for
/// a short-lived (24h) token via the client-credentials grant, refreshing automatically.
/// For legacy stores that still issue a static token, set <see cref="AdminAccessToken"/>
/// instead and it will be used as-is. Secrets come from environment / .env, never committed.
/// </summary>
public class ShopifyOptions
{
    public const string SectionName = "Shopify";

    /// <summary>Master switch. When false, all Shopify calls short-circuit with a clear error.</summary>
    public bool Enabled { get; set; }

    /// <summary>Store domain, e.g. "mctactical-build.myshopify.com" (no scheme, no trailing slash).</summary>
    public string ShopDomain { get; set; } = string.Empty;

    /// <summary>Dev Dashboard app Client ID (used with <see cref="ClientSecret"/> for client-credentials auth).</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>Dev Dashboard app Client secret. Keep secret; supply via env / .env only.</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// Optional legacy static Admin API access token ("shpat_..."). When set, it is used directly
    /// and the client-credentials exchange is skipped. Leave blank for Dev Dashboard apps.
    /// </summary>
    public string AdminAccessToken { get; set; } = string.Empty;

    /// <summary>Admin API version to target.</summary>
    public string ApiVersion { get; set; } = "2025-07";

    /// <summary>
    /// Shopify Location id that inventory is tracked against. Required for stock pushes.
    /// Discover it via the /api/shopify/ping endpoint, which lists available locations.
    /// </summary>
    public long? LocationId { get; set; }
}
