namespace HuntexPos.Api.DTOs;

/// <summary>
/// Read-only snapshot of a customer's AR posture. In Phase 3B.1 the balance and openInvoices/recentPayments
/// arrays are always empty / zero — the endpoint exists so the frontend can wire up the page shell, and so
/// the backend contract is locked in before 3B.2 introduces account sales.
/// </summary>
public sealed record CustomerAccountDto(
    Guid CustomerId,
    string? Name,
    string? Email,
    string? Company,
    bool TradeAccount,
    bool AccountEnabled,
    decimal CreditLimit,
    int PaymentTermsDays,
    decimal Balance,
    decimal CreditAvailable,
    int OpenInvoiceCount,
    int OverdueInvoiceCount,
    IReadOnlyList<CustomerAccountInvoiceDto> OpenInvoices,
    IReadOnlyList<CustomerAccountPaymentDto> RecentPayments);

public sealed record CustomerAccountInvoiceDto(
    Guid Id,
    string InvoiceNumber,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DueDate,
    decimal GrandTotal,
    decimal AmountPaid,
    decimal AmountOutstanding,
    string PaymentStatus);

public sealed record CustomerAccountPaymentDto(
    Guid Id,
    Guid? InvoiceId,
    string? InvoiceNumber,
    decimal Amount,
    string Method,
    string? Reference,
    DateTimeOffset PaidAt);

/// <summary>
/// Inputs for recording a payment against a customer's account. Amount must be &gt; 0.
/// Method is one of <c>Cash</c>, <c>Card</c>, <c>EFT</c>, <c>Other</c>.
/// When <see cref="ApplyToInvoiceIds"/> is empty/null the payment is auto-allocated
/// oldest-invoice-first; any surplus becomes an unallocated credit row.
/// </summary>
public sealed record CreateCustomerPaymentRequest(
    decimal Amount,
    string Method,
    string? Reference,
    string? Notes,
    DateTimeOffset? PaidAt,
    IReadOnlyList<Guid>? ApplyToInvoiceIds);

/// <summary>
/// Result of recording one or more payment rows. <see cref="UnallocatedCredit"/> is the
/// portion that did not land on an invoice (overpayment surplus).
/// </summary>
public sealed record CustomerPaymentResultDto(
    IReadOnlyList<Guid> CreatedPaymentIds,
    decimal NewBalance,
    decimal CreditAvailable,
    decimal UnallocatedCredit);
