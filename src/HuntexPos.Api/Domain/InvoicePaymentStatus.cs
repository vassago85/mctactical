namespace HuntexPos.Api.Domain;

/// <summary>
/// Payment lifecycle for an invoice. Existing pre-3B invoices are backfilled to <see cref="Paid"/>.
/// </summary>
public enum InvoicePaymentStatus
{
    Paid = 0,
    Unpaid = 1,
    Partial = 2,
    Overdue = 3,
    WrittenOff = 4
}
