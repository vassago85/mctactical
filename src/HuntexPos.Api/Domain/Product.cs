namespace HuntexPos.Api.Domain;

public class Product
{
    public Guid Id { get; set; }
    public Guid? SupplierId { get; set; }
    public Supplier? Supplier { get; set; }

    public string Sku { get; set; } = string.Empty;
    public string? Barcode { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Category { get; set; }
    public string? Manufacturer { get; set; }
    public string? ItemType { get; set; }

    public decimal Cost { get; set; }
    /// <summary>Supplier-granted discount on wholesale cost (0–100). Effective cost = Cost × (1 − SupplierDiscountPercent / 100).</summary>
    public decimal SupplierDiscountPercent { get; set; }
    public decimal SellPrice { get; set; }
    public int QtyOnHand { get; set; }
    public int QtyConsignment { get; set; }

    /// <summary>
    /// How sell price is derived for this product:
    /// "default" = fall through pricing rule hierarchy,
    /// "custom_markup" = use <see cref="CustomMarkupPercent"/>,
    /// "fixed_price" = use <see cref="FixedSellPrice"/>.
    /// </summary>
    public string PricingMethod { get; set; } = "default";

    /// <summary>Overrides rule-derived markup when <see cref="PricingMethod"/> = "custom_markup".</summary>
    public decimal? CustomMarkupPercent { get; set; }

    /// <summary>Fixed sell price when <see cref="PricingMethod"/> = "fixed_price". Bypasses markup logic.</summary>
    public decimal? FixedSellPrice { get; set; }

    /// <summary>Optional floor — sell never goes below this, even with discounts.</summary>
    public decimal? MinSellPrice { get; set; }

    /// <summary>When true, recalculation operations must not change <see cref="SellPrice"/>.</summary>
    public bool PriceLocked { get; set; }

    public bool Active { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }

    // --- Shopify sync mapping (POS is source of truth) ---
    // Populated the first time a product is pushed to (or matched against) Shopify.
    // Null means "not yet linked to any Shopify product".

    /// <summary>Shopify Admin product id (int64) once this POS product is linked.</summary>
    public long? ShopifyProductId { get; set; }

    /// <summary>Shopify variant id — the level that actually carries SKU, price and barcode.</summary>
    public long? ShopifyVariantId { get; set; }

    /// <summary>Shopify inventory item id — the level inventory quantities are set against.</summary>
    public long? ShopifyInventoryItemId { get; set; }

    /// <summary>Last time this product was successfully pushed to Shopify.</summary>
    public DateTimeOffset? ShopifySyncedAt { get; set; }
}
