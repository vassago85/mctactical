namespace HuntexPos.Api.Domain;

public class Supplier
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? DefaultCurrency { get; set; }
    public string? Notes { get; set; }

    /// <summary>
    /// Soft-delete flag. Inactive wholesalers stay in the database so historical
    /// references (stock receipts, consignment batches, invoices via products)
    /// keep working, but they're hidden from pickers used to assign new records.
    /// </summary>
    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
