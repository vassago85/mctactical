namespace HuntexPos.Api.Domain;

/// <summary>
/// A single line on a <see cref="Quote"/>. Products are optional so quotes can
/// include ad-hoc custom items (labour, custom bundles, etc).
/// </summary>
public class QuoteLine
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid QuoteId { get; set; }
    public Quote? Quote { get; set; }

    public Guid? ProductId { get; set; }
    public Product? Product { get; set; }

    /// <summary>SKU snapshot at time of quote (so the quote still renders if the product is later deleted).</summary>
    public string? Sku { get; set; }

    public string ItemName { get; set; } = string.Empty;
    public string? Description { get; set; }

    public int Quantity { get; set; } = 1;

    public decimal? UnitCost { get; set; }
    public decimal UnitPrice { get; set; }

    public decimal? DiscountPercent { get; set; }
    public decimal? DiscountAmount { get; set; }

    public decimal TaxRate { get; set; }
    public decimal LineTotal { get; set; }

    public int SortOrder { get; set; }
}
