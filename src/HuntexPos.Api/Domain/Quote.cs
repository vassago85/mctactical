namespace HuntexPos.Api.Domain;

public enum QuoteStatus
{
    Draft = 0,
    Sent = 1,
    Accepted = 2,
    Rejected = 3,
    Expired = 4,
    Converted = 5,
}

/// <summary>
/// A customer-facing price quote / estimate. When accepted it can be converted into
/// an <see cref="Invoice"/>; the original quote is preserved and marked Converted.
/// </summary>
public class Quote
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string QuoteNumber { get; set; } = string.Empty;
    public QuoteStatus Status { get; set; } = QuoteStatus.Draft;

    public Guid? CustomerId { get; set; }
    public Customer? Customer { get; set; }

    // Customer snapshot (populated at creation from Customer if linked, or typed directly).
    public string? CustomerName { get; set; }
    public string? CustomerEmail { get; set; }
    public string? CustomerPhone { get; set; }
    public string? CustomerCompany { get; set; }
    public string? CustomerAddress { get; set; }
    public string? CustomerVatNumber { get; set; }

    public decimal SubTotal { get; set; }
    public decimal DiscountTotal { get; set; }
    public decimal TaxRate { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal GrandTotal { get; set; }

    public string? PublicNotes { get; set; }
    public string? InternalNotes { get; set; }

    public DateTimeOffset? ValidUntil { get; set; }

    public Guid PublicToken { get; set; } = Guid.NewGuid();
    public string? PdfStorageKey { get; set; }

    /// <summary>Set when this quote has been converted into an invoice.</summary>
    public Guid? ConvertedInvoiceId { get; set; }
    public DateTimeOffset? ConvertedAt { get; set; }

    public string? CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }

    public ICollection<QuoteLine> Lines { get; set; } = new List<QuoteLine>();
}
