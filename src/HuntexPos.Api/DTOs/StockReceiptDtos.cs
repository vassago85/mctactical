using System.ComponentModel.DataAnnotations;

namespace HuntexPos.Api.DTOs;

public class CreateStockReceiptRequest
{
    [Required]
    public string Type { get; set; } = string.Empty;

    public Guid? SupplierId { get; set; }

    [Range(1, 999_999)]
    public int Quantity { get; set; }

    public string? Notes { get; set; }
}

public class StockReceiptDto
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public Guid? SupplierId { get; set; }
    public string? SupplierName { get; set; }
    public string Type { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public string? Notes { get; set; }
    public string? ProcessedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class ConsignmentSummaryLineDto
{
    public Guid SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public int TotalIn { get; set; }
    public int TotalMovedToStock { get; set; }
    public int TotalReturned { get; set; }
    public int OnHand { get; set; }
}
