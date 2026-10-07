using HuntexPos.Api.Domain;

namespace HuntexPos.Api.Services;

/// <summary>
/// Identity of "the item that was sold" on an invoice line, shared by the sold-in-period report and
/// the missing-costs tool. Unlinked Shopify items all sit on one placeholder product, so those are
/// keyed by their Shopify identity (variant id, else SKU, else title) instead of the product.
/// Requires <see cref="InvoiceLine.Product"/> to be loaded.
/// </summary>
public static class SoldItemKey
{
    public static bool IsUnlinkedShopify(InvoiceLine l) =>
        l.Product?.Sku == ShopifyOrderImportService.UnlinkedPlaceholderSku;

    /// <summary>Courier fee line added by the Shopify import: revenue with a genuine zero cost.</summary>
    public static bool IsShopifyShipping(InvoiceLine l) =>
        IsUnlinkedShopify(l)
        && l.ShopifyVariantId == null
        && string.IsNullOrWhiteSpace(l.SkuAtSale)
        && l.Description.StartsWith("Shipping", StringComparison.OrdinalIgnoreCase);

    public static string For(InvoiceLine l)
    {
        if (!IsUnlinkedShopify(l)) return $"p:{l.ProductId}";
        if (l.ShopifyVariantId.HasValue) return $"u:v:{l.ShopifyVariantId.Value}";
        if (!string.IsNullOrWhiteSpace(l.SkuAtSale)) return $"u:s:{l.SkuAtSale.Trim().ToLowerInvariant()}";
        return $"u:d:{(l.Description ?? string.Empty).Trim().ToLowerInvariant()}";
    }
}
