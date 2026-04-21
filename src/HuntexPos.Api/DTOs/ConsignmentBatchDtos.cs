using System.ComponentModel.DataAnnotations;

namespace HuntexPos.Api.DTOs;

public class CreateConsignmentBatchRequest
{
    [Required] public string Type { get; set; } = string.Empty;
    [Required] public Guid SupplierId { get; set; }
    public string? Notes { get; set; }
    public string? SourceDocumentRef { get; set; }
}

public class AddBatchLineRequest
{
    [Required] public Guid ProductId { get; set; }
    [Range(1, 999_999)] public int ExpectedQty { get; set; } = 1;
    public string? Notes { get; set; }
    [Range(0, 10_000_000)] public decimal? UnitCost { get; set; }
}

public class UpdateBatchLineRequest
{
    [Range(0, 999_999)] public int? CheckedQty { get; set; }
    [Range(0, 999_999)] public int? ExpectedQty { get; set; }
    public string? Notes { get; set; }
    [Range(0, 10_000_000)] public decimal? UnitCost { get; set; }
}

public class ScanBatchRequest
{
    [Required] public string Barcode { get; set; } = string.Empty;
    [Range(1, 999_999)] public int Qty { get; set; } = 1;
    [Range(0, 10_000_000)] public decimal? UnitCost { get; set; }
}

public class InlineCreateProductRequest
{
    [Required, MinLength(1)] public string Sku { get; set; } = string.Empty;
    [Required, MinLength(1)] public string Name { get; set; } = string.Empty;
    public string? Barcode { get; set; }
    [Range(0, 10_000_000)] public decimal? UnitCost { get; set; }
    [Range(1, 999_999)] public int Qty { get; set; } = 1;
}

public class ConsignmentBatchDto
{
    public Guid Id { get; set; }
    public Guid SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public string? CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CommittedAt { get; set; }
    public string? SourceDocumentRef { get; set; }
    public bool HasSourceDocument { get; set; }
    public List<ConsignmentBatchLineDto> Lines { get; set; } = new();
}

public class ConsignmentBatchLineDto
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string? Barcode { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public int ExpectedQty { get; set; }
    public int CheckedQty { get; set; }
    public string? Notes { get; set; }
    public decimal? UnitCost { get; set; }
    public decimal CurrentProductCost { get; set; }
    public bool UnitCostChanged { get; set; }
}

public class PdfImportResultDto
{
    public ConsignmentBatchDto Batch { get; set; } = new();
    public int Parsed { get; set; }
    public List<string> NotFound { get; set; } = new();
    public List<string> UnparsedLines { get; set; } = new();
    public string? RawText { get; set; }
}

public class ReturnableStockLineDto
{
    public Guid ProductId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string? Barcode { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public int OnHand { get; set; }
}
