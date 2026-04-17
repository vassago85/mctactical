namespace HuntexPos.Api.Domain;

public class Invoice
{
    public Guid Id { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public InvoiceStatus Status { get; set; } = InvoiceStatus.Final;

    public string? CustomerName { get; set; }
    public string? CustomerEmail { get; set; }
    public string? CustomerType { get; set; }

    // Business customer fields (optional — for tax invoices)
    public string? CustomerCompany { get; set; }
    public string? CustomerAddress { get; set; }
    public string? CustomerVatNumber { get; set; }

    public string PaymentMethod { get; set; } = "Cash";
    public decimal SubTotal { get; set; }
    public decimal TaxRate { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal DiscountTotal { get; set; }
    public decimal GrandTotal { get; set; }

    /// <summary>Name of the active promotion at time of sale (e.g. "Huntex Show 2026").</summary>
    public string? PromotionName { get; set; }

    public Guid PublicToken { get; set; } = Guid.NewGuid();
    public string? PdfStorageKey { get; set; }

    public string? VoidReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? CreatedByUserId { get; set; }

    public bool IsSpecialOrder { get; set; }
    public bool IsDelivered { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
    public string? DeliveryNotes { get; set; }

    public ICollection<InvoiceLine> Lines { get; set; } = new List<InvoiceLine>();
}
