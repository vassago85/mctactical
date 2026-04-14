namespace HuntexPos.Api.Domain;

public class InvoiceLine
{
    public Guid Id { get; set; }
    public Guid InvoiceId { get; set; }
    public Invoice? Invoice { get; set; }

    public Guid ProductId { get; set; }
    public Product? Product { get; set; }

    public string Description { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineDiscount { get; set; }
    public decimal LineTotal { get; set; }

    /// <summary>Wholesale cost (excl VAT) snapshotted at time of sale for GP reporting.</summary>
    public decimal CostAtSale { get; set; }
}
