namespace HuntexPos.Api.Domain;

public class InvoiceLine
{
    public Guid Id { get; set; }
    public Guid InvoiceId { get; set; }
    public Invoice? Invoice { get; set; }

    public Guid ProductId { get; set; }
    public Product? Product { get; set; }

    public string Description { get; set; } = string.Empty;
    /// <summary>Product SKU snapshotted at time of sale, so historical lookups still
    /// resolve after the product is deleted or its SKU is changed.</summary>
    public string? SkuAtSale { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    /// <summary>Catalog SellPrice at time of sale (before any promotion discount).</summary>
    public decimal OriginalUnitPrice { get; set; }
    /// <summary>Total rand discount on this line: operator price concessions plus any
    /// explicit line discount. Price overrides below the going price are recorded here
    /// rather than by lowering <see cref="UnitPrice"/>, so the concession stays auditable.</summary>
    public decimal LineDiscount { get; set; }
    public decimal LineTotal { get; set; }

    /// <summary>Wholesale cost (excl VAT) snapshotted at time of sale for GP reporting.</summary>
    public decimal CostAtSale { get; set; }
}
