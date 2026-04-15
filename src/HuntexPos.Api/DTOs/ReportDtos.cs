namespace HuntexPos.Api.DTOs;

public class DailySummaryDto
{
    public DateOnly Date { get; set; }
    public int InvoiceCount { get; set; }
    public decimal GrandTotal { get; set; }
}

public class InvoiceListItemDto
{
    public Guid Id { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal GrandTotal { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string? CustomerName { get; set; }
    public string? CreatedByUserId { get; set; }
}

/* ── Stock report DTOs ── */

public class StockReportDto
{
    public StockOnHandSummaryDto OnHand { get; set; } = new();
    public List<ConsignmentBySupplierDto> ConsignmentBySupplier { get; set; } = new();
    public List<StockMovementSummaryDto> ReceivedInPeriod { get; set; } = new();
    public List<ProductSoldSummaryDto> SoldInPeriod { get; set; } = new();
}

public class StockOnHandSummaryDto
{
    public int TotalOwnedQty { get; set; }
    public decimal TotalOwnedValue { get; set; }
    public int TotalConsignmentQty { get; set; }
    public int ProductCount { get; set; }
    public List<StockOnHandLineDto> Lines { get; set; } = new();
}

public class StockOnHandLineDto
{
    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? SupplierName { get; set; }
    public int QtyOwned { get; set; }
    public int QtyConsignment { get; set; }
    public decimal Cost { get; set; }
    public decimal SellPrice { get; set; }
    public decimal OwnedValue { get; set; }
}

public class ConsignmentBySupplierDto
{
    public Guid SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public int TotalIn { get; set; }
    public int TotalMovedToStock { get; set; }
    public int TotalReturned { get; set; }
    public int OnHand { get; set; }
    public List<ConsignmentProductLineDto> Products { get; set; } = new();
}

public class ConsignmentProductLineDto
{
    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int OnHand { get; set; }
    public int TotalIn { get; set; }
    public int TotalMovedToStock { get; set; }
    public int TotalReturned { get; set; }
}

public class StockMovementSummaryDto
{
    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? SupplierName { get; set; }
    public string Type { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class ProductSoldSummaryDto
{
    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int QtySold { get; set; }
    public decimal Revenue { get; set; }
    public decimal Cost { get; set; }
}
