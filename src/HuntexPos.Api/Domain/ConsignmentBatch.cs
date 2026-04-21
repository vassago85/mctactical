namespace HuntexPos.Api.Domain;

public enum ConsignmentBatchType { Receive, Return, OwnedReceive }
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

    /// <summary>Supplier-side invoice / PO reference typed by staff.</summary>
    public string? SourceDocumentRef { get; set; }

    /// <summary>Relative path (under the PDF data dir) to an uploaded supplier invoice PDF, if any.</summary>
    public string? SourceDocumentPath { get; set; }

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

    /// <summary>Per-line unit cost ex VAT, from the supplier's delivery note. Optional.</summary>
    public decimal? UnitCost { get; set; }

    /// <summary>Set at commit time when UnitCost differs from the product's current Cost.</summary>
    public bool UnitCostChanged { get; set; }
}
