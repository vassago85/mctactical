namespace HuntexPos.Api.Domain;

public enum ConsignmentBatchType { Receive, Return }
public enum ConsignmentBatchStatus { Draft, Committed }

public class ConsignmentBatch
{
    public Guid Id { get; set; }
    public Guid SupplierId { get; set; }
    public Supplier? Supplier { get; set; }
    public ConsignmentBatchType Type { get; set; }
    public ConsignmentBatchStatus Status { get; set; }
    public string? Notes { get; set; }
    public string? CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CommittedAt { get; set; }
    public List<ConsignmentBatchLine> Lines { get; set; } = new();
}

public class ConsignmentBatchLine
{
    public Guid Id { get; set; }
    public Guid BatchId { get; set; }
    public ConsignmentBatch? Batch { get; set; }
    public Guid ProductId { get; set; }
    public Product? Product { get; set; }
    public int ExpectedQty { get; set; }
    public int CheckedQty { get; set; }
    public string? Notes { get; set; }
}
