namespace HuntexPos.Api.DTOs;

public class DailySummaryDto
{
    public DateOnly Date { get; set; }
    public int InvoiceCount { get; set; }
    public decimal GrandTotal { get; set; }
}

public class PaymentMethodBreakdownDto
{
    public string Method { get; set; } = string.Empty;
    public int Count { get; set; }
    public decimal GrandTotal { get; set; }
}

public class PaymentsSummaryDto
{
    public decimal TotalGrand { get; set; }
    public int TotalCount { get; set; }
    public List<PaymentMethodBreakdownDto> ByMethod { get; set; } = new();
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
    public string? VoidReason { get; set; }
    public DateTimeOffset? VoidedAt { get; set; }
    public string? VoidedByName { get; set; }
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
    public decimal TotalConsignmentValue { get; set; }
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
    public decimal Discount { get; set; }
    public decimal CostExVat { get; set; }
    public decimal CostInclVat { get; set; }
}

/* ── Consignment report DTOs ── */

public class ConsignmentReportDto
{
    public List<ConsignmentSupplierReportDto> Suppliers { get; set; } = new();
    public int TotalOnHand { get; set; }
    public decimal TotalOnHandValue { get; set; }
    public int TotalReceived { get; set; }
    public decimal TotalReceivedValue { get; set; }
    public int TotalSold { get; set; }
    public decimal TotalSoldRevenue { get; set; }
    public int TotalReturned { get; set; }
    public int TotalMovedFromStock { get; set; }
}

public class ConsignmentSupplierReportDto
{
    public Guid SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public int OnHand { get; set; }
    public decimal OnHandValue { get; set; }
    public int TotalReceived { get; set; }
    public decimal TotalReceivedValue { get; set; }
    public int TotalSold { get; set; }
    public decimal TotalSoldRevenue { get; set; }
    public int TotalMovedToStock { get; set; }
    public int TotalReturned { get; set; }
    public int TotalMovedFromStock { get; set; }
    public List<ConsignmentProductReportDto> Products { get; set; } = new();
}

/// <summary>
/// Trimmed report surface for a supplier-scoped user (e.g. a Venatics Gear helper) —
/// summary KPIs + the full per-product breakdown + recent sold lines for that supplier.
/// </summary>
public class VendorReportDto
{
    public Guid SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public DateTimeOffset? From { get; set; }
    public DateTimeOffset? To { get; set; }

    public int OnHand { get; set; }
    public decimal OnHandValue { get; set; }
    public int TotalReceived { get; set; }
    public int TotalSold { get; set; }
    public decimal TotalSoldRevenue { get; set; }
    public int TotalReturned { get; set; }

    public List<ConsignmentProductReportDto> Products { get; set; } = new();
    public List<VendorSoldLineDto> SoldLines { get; set; } = new();
}

public class VendorSoldLineDto
{
    public DateTimeOffset CreatedAt { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
}

public class ConsignmentProductReportDto
{
    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal SellPrice { get; set; }
    public decimal Cost { get; set; }
    public int OnHand { get; set; }
    public decimal OnHandValue { get; set; }
    public int Received { get; set; }
    public int MovedToStock { get; set; }
    public int Returned { get; set; }
    public int MovedFromStock { get; set; }
    public int Sold { get; set; }
    public decimal SoldRevenue { get; set; }
}
