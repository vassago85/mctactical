namespace HuntexPos.Api.Domain;

/// <summary>
/// A payment made by a customer against their account. May be allocated to a specific invoice
/// (<see cref="InvoiceId"/> set) or applied as a generic credit (<see cref="InvoiceId"/> null,
/// allocation handled by the AR engine).
/// </summary>
public class CustomerPayment
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid CustomerId { get; set; }
    public Guid? InvoiceId { get; set; }

    public decimal Amount { get; set; }
    public string Method { get; set; } = "Cash";
    public string? Reference { get; set; }
    public string? Notes { get; set; }

    public DateTimeOffset PaidAt { get; set; } = DateTimeOffset.UtcNow;
    public string? CreatedByUserId { get; set; }

    public bool IsVoided { get; set; }
    public DateTimeOffset? VoidedAt { get; set; }
    public string? VoidedByUserId { get; set; }
    public string? VoidReason { get; set; }
}
