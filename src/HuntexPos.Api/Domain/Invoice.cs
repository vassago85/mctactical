namespace HuntexPos.Api.Domain;

public class Invoice
{
    public Guid Id { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public InvoiceStatus Status { get; set; } = InvoiceStatus.Final;

    public string? CustomerName { get; set; }
    public string? CustomerEmail { get; set; }
    public string? CustomerType { get; set; }

    public string PaymentMethod { get; set; } = "Cash";
    public decimal SubTotal { get; set; }
    public decimal TaxRate { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal DiscountTotal { get; set; }
    public decimal GrandTotal { get; set; }

    public Guid PublicToken { get; set; } = Guid.NewGuid();
    public string? PdfStorageKey { get; set; }

    public string? VoidReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? CreatedByUserId { get; set; }

    public ICollection<InvoiceLine> Lines { get; set; } = new List<InvoiceLine>();
}
