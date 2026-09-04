namespace HuntexPos.Api.Domain;

/// <summary>
/// A partial return from a previous sale. Records what came back, how much credit the customer
/// received, and — for exchanges — the linked replacement sale. Original invoice stays Final;
/// only the specific lines/qty returned are reflected in <see cref="InvoiceLine.ReturnedQuantity"/>.
/// </summary>
public class SaleReturn
{
    public Guid Id { get; set; }

    /// <summary>The prior sale being returned against.</summary>
    public Guid OriginalInvoiceId { get; set; }
    public Invoice? OriginalInvoice { get; set; }

    /// <summary>Set when this return funded a new sale (exchange) in the same transaction.</summary>
    public Guid? ExchangeInvoiceId { get; set; }
    public Invoice? ExchangeInvoice { get; set; }

    /// <summary>Sum of line credits, VAT-inclusive (matches how the customer sees prices).</summary>
    public decimal CreditTotal { get; set; }

    /// <summary>
    /// Net cash movement after applying the credit against the replacement sale, if any.
    /// Positive = customer paid extra (short difference collected).
    /// Negative = customer was refunded cash back.
    /// Zero = even swap.
    /// </summary>
    public decimal NetSettlement { get; set; }

    /// <summary>
    /// Tender used for a net refund (NetSettlement &lt; 0) or the "collect the difference" payment.
    /// Null for even swaps.
    /// </summary>
    public string? SettlementMethod { get; set; }

    public string Reason { get; set; } = string.Empty;

    public string? CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<SaleReturnLine> Lines { get; set; } = new List<SaleReturnLine>();
}

public class SaleReturnLine
{
    public Guid Id { get; set; }

    public Guid SaleReturnId { get; set; }
    public SaleReturn? SaleReturn { get; set; }

    /// <summary>Line on the original invoice being returned.</summary>
    public Guid OriginalInvoiceLineId { get; set; }
    public InvoiceLine? OriginalInvoiceLine { get; set; }

    public Guid ProductId { get; set; }
    public Product? Product { get; set; }

    /// <summary>Product SKU snapshotted at return time in case the product row is later removed.</summary>
    public string? SkuAtReturn { get; set; }
    public string Description { get; set; } = string.Empty;

    public int Quantity { get; set; }

    /// <summary>
    /// Per-unit credit = the effective unit price the customer paid on the original line
    /// (LineTotal / Quantity), VAT-inclusive. Guarantees the customer is credited exactly what
    /// they paid, including any discount given at the time.
    /// </summary>
    public decimal UnitCredit { get; set; }
    public decimal LineCredit { get; set; }
}
