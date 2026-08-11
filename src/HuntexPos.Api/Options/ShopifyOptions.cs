namespace HuntexPos.Api.Options;

/// <summary>
/// Configuration for the Shopify sync bridge. The POS is the source of truth; these
/// credentials belong to a Shopify custom app (Settings → Apps → Develop apps) with
/// Admin API scopes: read_products, write_products, read_inventory, write_inventory.
/// Secrets are supplied via environment / .env on the server, never committed.
/// </summary>
public class ShopifyOptions
{
    public const string SectionName = "Shopify";

    /// <summary>Master switch. When false, all Shopify calls short-circuit with a clear error.</summary>
    public bool Enabled { get; set; }

    /// <summary>Store domain, e.g. "mctactical-build.myshopify.com" (no scheme, no trailing slash).</summary>
    public string ShopDomain { get; set; } = string.Empty;

    /// <summary>Admin API access token from the custom app ("shpat_...").</summary>
    public string AdminAccessToken { get; set; } = string.Empty;

    /// <summary>Admin API version to target.</summary>
    public string ApiVersion { get; set; } = "2025-07";

    /// <summary>
    /// Shopify Location id that inventory is tracked against. Required for stock pushes.
    /// Discover it via the /api/shopify/ping endpoint, which lists available locations.
    /// </summary>
    public long? LocationId { get; set; }
}
