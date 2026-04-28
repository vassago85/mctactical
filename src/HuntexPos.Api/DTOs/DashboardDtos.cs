namespace HuntexPos.Api.DTOs;

/// <summary>
/// Single payload that backs the Executive Dashboard view. All sections cover
/// the same [from, to] window. <see cref="SalesTrend.Previous"/> is the same
/// length window immediately before <c>from</c>, used for the "vs previous
/// period" overlay on the line chart.
/// </summary>
public class DashboardOverviewDto
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public DashboardKpisDto Kpis { get; set; } = new();
    public DashboardSalesTrendDto SalesTrend { get; set; } = new();
    public List<DashboardPaymentMethodDto> PaymentMethods { get; set; } = new();
    public List<DashboardCategoryDto> TopCategories { get; set; } = new();
    public List<DashboardTopProductDto> TopProducts { get; set; } = new();
    public List<DashboardLowStockDto> LowStockAlerts { get; set; } = new();
    public List<DashboardActivityDto> RecentActivity { get; set; } = new();
}

public class DashboardKpisDto
{
    public decimal TodaySales { get; set; }
    public decimal MonthSales { get; set; }
    public decimal GrossProfit { get; set; }
    public decimal GrossProfitPct { get; set; }
    public decimal AvgBasket { get; set; }
    public int ItemsSold { get; set; }
    public int InvoiceCount { get; set; }
    public int LowStockCount { get; set; }

    /// <summary>Period totals over [from, to] — what the deltas compare against.</summary>
    public decimal PeriodSales { get; set; }
    public decimal PeriodGrossProfit { get; set; }

    /// <summary>
    /// Percentage change vs the immediately preceding period of the same length.
    /// Null when the previous period had a zero baseline.
    /// </summary>
    public DashboardDeltasDto Deltas { get; set; } = new();
}

public class DashboardDeltasDto
{
    public decimal? PeriodSales { get; set; }
    public decimal? PeriodGrossProfit { get; set; }
    public decimal? AvgBasket { get; set; }
    public decimal? ItemsSold { get; set; }
    public decimal? InvoiceCount { get; set; }
}

public class DashboardSalesTrendDto
{
    public List<DashboardTrendPointDto> Current { get; set; } = new();
    public List<DashboardTrendPointDto> Previous { get; set; } = new();
}

public class DashboardTrendPointDto
{
    public DateOnly Date { get; set; }
    public decimal Total { get; set; }
}

public class DashboardPaymentMethodDto
{
    public string Method { get; set; } = string.Empty;
    public int Count { get; set; }
    public decimal Total { get; set; }
    public decimal Pct { get; set; }
}

public class DashboardCategoryDto
{
    public string Category { get; set; } = string.Empty;
    public int Qty { get; set; }
    public decimal Revenue { get; set; }
}

public class DashboardTopProductDto
{
    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Qty { get; set; }
    public decimal Revenue { get; set; }
}

public class DashboardLowStockDto
{
    public Guid ProductId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int QtyOnHand { get; set; }
    public string? SupplierName { get; set; }
}

public class DashboardActivityDto
{
    /// <summary>One of: sale, void, quote-created, quote-converted, restock, restock-out, stocktake.</summary>
    public string Type { get; set; } = string.Empty;
    public DateTimeOffset Ts { get; set; }
    public string? Actor { get; set; }
    public string Summary { get; set; } = string.Empty;
    /// <summary>Optional in-app deep link (frontend route, e.g. /reports?invoice=INV-1003).</summary>
    public string? Link { get; set; }
}
