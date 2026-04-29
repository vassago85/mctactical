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
