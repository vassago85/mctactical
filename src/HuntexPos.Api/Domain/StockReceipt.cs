namespace HuntexPos.Api.Domain;

public enum StockReceiptType
{
    OwnedIn,
    ConsignmentIn,
    ConsignmentToStock,
    ConsignmentReturn
}

public class StockReceipt
{
    public Guid Id { get; set; }

    public Guid ProductId { get; set; }
    public Product? Product { get; set; }

    public Guid? SupplierId { get; set; }
    public Supplier? Supplier { get; set; }

    public StockReceiptType Type { get; set; }
    public int Quantity { get; set; }
    public string? Notes { get; set; }
    public string? ProcessedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
