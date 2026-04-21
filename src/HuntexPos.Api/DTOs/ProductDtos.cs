namespace HuntexPos.Api.DTOs;

public class ProductDto
{
    public Guid Id { get; set; }
    public Guid? SupplierId { get; set; }
    public string? SupplierName { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string? Barcode { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Category { get; set; }
    public string? Manufacturer { get; set; }
    public string? ItemType { get; set; }
    public decimal? Cost { get; set; }
    public decimal SellPrice { get; set; }
    public int QtyOnHand { get; set; }
    public int QtyConsignment { get; set; }
    public bool Active { get; set; }
    /// <summary>Non-null if sell price is below distributor cost (ex-VAT + 15%).</summary>
    public string? Warning { get; set; }

    /// <summary>Effective price after special/promotion (null = no active special).</summary>
    public decimal? SpecialPrice { get; set; }
    /// <summary>Name of the special's promotion, or "Standalone" for non-promo specials.</summary>
    public string? SpecialLabel { get; set; }

    public string PricingMethod { get; set; } = "default";
    public decimal? CustomMarkupPercent { get; set; }
    public decimal? FixedSellPrice { get; set; }
    public decimal? MinSellPrice { get; set; }
    public bool PriceLocked { get; set; }

    /// <summary>Human-readable label describing which pricing rule / override produced <see cref="SellPrice"/>.</summary>
    public string? PricingSource { get; set; }
    /// <summary>Minimum price a sales user may discount to — computed from max-discount, min-margin, and product floor.</summary>
    public decimal? MinAllowedPrice { get; set; }
}

public class ProductSearchQuery
{
    public string? Q { get; set; }
    public Guid? SupplierId { get; set; }
    public string? Barcode { get; set; }
    public int Take { get; set; } = 50;
}

public class ProductStocklistQuery
{
    public string? Q { get; set; }
    public Guid? SupplierId { get; set; }
    public bool IncludeInactive { get; set; }
    public bool? HasSpecial { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; } = 500;
}

public class StocklistPageDto
{
    public int Total { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; }
    public List<ProductDto> Items { get; set; } = new();
}

public class CreateProductRequest
{
    public string Sku { get; set; } = string.Empty;
    public string? Barcode { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Category { get; set; }
    public string? Manufacturer { get; set; }
    public string? ItemType { get; set; }
    public Guid? SupplierId { get; set; }
    public decimal Cost { get; set; }
    public decimal SellPrice { get; set; }
    public int QtyOnHand { get; set; }
    public string? PricingMethod { get; set; }
    public decimal? CustomMarkupPercent { get; set; }
    public decimal? FixedSellPrice { get; set; }
    public decimal? MinSellPrice { get; set; }
    public bool? PriceLocked { get; set; }
}

public class UpdateProductRequest
{
    public string? Sku { get; set; }
    public string? Barcode { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? Category { get; set; }
    public string? Manufacturer { get; set; }
    public string? ItemType { get; set; }
    public Guid? SupplierId { get; set; }
    public decimal? Cost { get; set; }
    public decimal? SellPrice { get; set; }
    public int? QtyOnHand { get; set; }
    public bool? Active { get; set; }
    public string? PricingMethod { get; set; }
    public decimal? CustomMarkupPercent { get; set; }
    public decimal? FixedSellPrice { get; set; }
    public decimal? MinSellPrice { get; set; }
    public bool? PriceLocked { get; set; }
}

public class LabelBatchRequest
{
    public List<Guid> ProductIds { get; set; } = new();
    public bool UsePromo { get; set; }

    /// <summary>
    /// How many labels to print per selected product. Ignored when <see cref="CopiesFromQtyOnHand"/> is true.
    /// Clamped to 1..50. Defaults to 1.
    /// </summary>
    public int? CopiesPerProduct { get; set; }

    /// <summary>
    /// When true, print one label per unit currently on hand for each selected product.
    /// Products with 0 on hand are skipped. Per-product copies are clamped to 1..<see cref="MaxCopiesPerProduct"/>.
    /// </summary>
    public bool CopiesFromQtyOnHand { get; set; }

    /// <summary>
    /// Safety cap for <see cref="CopiesFromQtyOnHand"/>. Defaults server-side to 50 per product.
    /// </summary>
    public int? MaxCopiesPerProduct { get; set; }
}

/// <summary>
/// Manual correction to QtyOnHand. Writes a StockReceipt audit row so the change
/// is visible on the product's movement history.
/// </summary>
public class AdjustStockRequest
{
    /// <summary>New absolute quantity on hand after adjustment (must be ≥ 0).</summary>
    public int NewQtyOnHand { get; set; }

    /// <summary>Required free-text reason (e.g. "Stocktake variance", "Damaged").</summary>
    public string Reason { get; set; } = string.Empty;
}
