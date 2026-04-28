using HuntexPos.Api.Data;
using HuntexPos.Api.Domain;
using HuntexPos.Api.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HuntexPos.Api.Controllers;

/// <summary>
/// Backs the Executive Dashboard (single-page overview). All work is read-only
/// aggregation over existing tables — no schema changes. Heavy queries are
/// materialised once and reduced in memory because SQLite cannot translate
/// <c>DateTimeOffset</c> comparisons (the same workaround used by
/// <see cref="ReportsController"/>).
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = $"{Roles.Admin},{Roles.Owner},{Roles.Dev}")]
public class DashboardController : ControllerBase
{
    private const int LowStockThreshold = 5;
    private const int TopProductsLimit = 8;
    private const int TopCategoriesLimit = 8;
    private const int LowStockLimit = 12;
    private const int RecentActivityLimit = 12;
    private const decimal VatMultiplier = 1.15m;

    private static readonly TimeSpan LocalOffset = TimeSpan.FromHours(2); // SAST — matches existing reports

    private readonly HuntexDbContext _db;

    public DashboardController(HuntexDbContext db)
    {
        _db = db;
    }

    [HttpGet("overview")]
    public async Task<DashboardOverviewDto> Overview(
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        CancellationToken ct)
    {
        var nowUtc = DateTimeOffset.UtcNow;
        var (windowFrom, windowTo) = ResolveWindow(from, to, nowUtc);
        var span = windowTo - windowFrom;
        var prevFrom = windowFrom - span;
        var prevTo = windowFrom;

        // Materialise once — SQLite cannot translate DateTimeOffset comparisons.
        var allInvoices = await _db.Invoices.AsNoTracking()
            .Include(i => i.Lines).ThenInclude(l => l.Product)
            .ToListAsync(ct);

        var finals = allInvoices.Where(i => i.Status == InvoiceStatus.Final).ToList();
        var inWindow = finals.Where(i => i.CreatedAt >= windowFrom && i.CreatedAt <= windowTo).ToList();
        var inPrev = finals.Where(i => i.CreatedAt >= prevFrom && i.CreatedAt < prevTo).ToList();

        // ── KPIs ─────────────────────────────────────────────────────────────
        var todayLocal = TodayBoundsLocal(nowUtc);
        var monthLocal = MonthBoundsLocal(nowUtc);

        var todayInvoices = finals.Where(i => i.CreatedAt >= todayLocal.from && i.CreatedAt < todayLocal.to).ToList();
        var monthInvoices = finals.Where(i => i.CreatedAt >= monthLocal.from && i.CreatedAt < monthLocal.to).ToList();

        var todaySales = todayInvoices.Sum(i => i.GrandTotal);
        var monthSales = monthInvoices.Sum(i => i.GrandTotal);

        var periodSales = inWindow.Sum(i => i.GrandTotal);
        var periodGp = ComputeGrossProfit(inWindow);
        var periodGpPct = periodSales > 0 ? Math.Round(periodGp / periodSales * 100m, 1) : 0m;
        var periodInvoiceCount = inWindow.Count;
        var periodItemsSold = inWindow.SelectMany(i => i.Lines).Sum(l => l.Quantity);
        var periodAvgBasket = periodInvoiceCount > 0 ? Math.Round(periodSales / periodInvoiceCount, 2) : 0m;

        var prevSales = inPrev.Sum(i => i.GrandTotal);
        var prevGp = ComputeGrossProfit(inPrev);
        var prevInvoiceCount = inPrev.Count;
        var prevItemsSold = inPrev.SelectMany(i => i.Lines).Sum(l => l.Quantity);
        var prevAvgBasket = prevInvoiceCount > 0 ? prevSales / prevInvoiceCount : 0m;

        var lowStockCount = await _db.Products
            .AsNoTracking()
            .CountAsync(p => p.Active && p.QtyOnHand <= LowStockThreshold, ct);

        var kpis = new DashboardKpisDto
        {
            TodaySales = todaySales,
            MonthSales = monthSales,
            GrossProfit = periodGp,
            GrossProfitPct = periodGpPct,
            AvgBasket = periodAvgBasket,
            ItemsSold = periodItemsSold,
            InvoiceCount = periodInvoiceCount,
            LowStockCount = lowStockCount,
            PeriodSales = periodSales,
            PeriodGrossProfit = periodGp,
            Deltas = new DashboardDeltasDto
            {
                PeriodSales = PercentDelta(periodSales, prevSales),
                PeriodGrossProfit = PercentDelta(periodGp, prevGp),
                AvgBasket = PercentDelta(periodAvgBasket, prevAvgBasket),
                ItemsSold = PercentDelta(periodItemsSold, prevItemsSold),
                InvoiceCount = PercentDelta(periodInvoiceCount, prevInvoiceCount)
            }
        };

        // ── Sales trend ──────────────────────────────────────────────────────
        var trend = new DashboardSalesTrendDto
        {
            Current = BucketByDay(inWindow, windowFrom, windowTo),
            Previous = BucketByDay(inPrev, prevFrom, prevTo)
        };

        // ── Payment methods ──────────────────────────────────────────────────
        var totalForPct = inWindow.Sum(i => i.GrandTotal);
        var paymentMethods = inWindow
            .GroupBy(i => NormalisePaymentMethod(i.PaymentMethod))
            .Select(g => new DashboardPaymentMethodDto
            {
                Method = g.Key,
                Count = g.Count(),
                Total = g.Sum(x => x.GrandTotal),
                Pct = totalForPct > 0 ? Math.Round(g.Sum(x => x.GrandTotal) / totalForPct * 100m, 1) : 0m
            })
            .OrderByDescending(p => p.Total)
            .ToList();

        // ── Top categories ──────────────────────────────────────────────────
        var topCategories = inWindow
            .SelectMany(i => i.Lines)
            .Where(l => !string.IsNullOrWhiteSpace(l.Product?.Category))
            .GroupBy(l => l.Product!.Category!.Trim())
            .Select(g => new DashboardCategoryDto
            {
                Category = g.Key,
                Qty = g.Sum(l => l.Quantity),
                Revenue = g.Sum(l => l.LineTotal)
            })
            .OrderByDescending(c => c.Revenue)
            .Take(TopCategoriesLimit)
            .ToList();

        // ── Top products ────────────────────────────────────────────────────
        var topProducts = inWindow
            .SelectMany(i => i.Lines)
            .Where(l => l.Product != null)
            .GroupBy(l => l.Product!.Id)
            .Select(g => new
            {
                first = g.First().Product!,
                qty = g.Sum(x => x.Quantity),
                revenue = g.Sum(x => x.LineTotal)
            })
            .OrderByDescending(x => x.revenue)
            .Take(TopProductsLimit)
            .Select(x => new DashboardTopProductDto
            {
                Sku = x.first.Sku,
                Name = x.first.Name,
                Qty = x.qty,
                Revenue = x.revenue
            })
            .ToList();

        // ── Low stock ───────────────────────────────────────────────────────
        var lowStock = await _db.Products.AsNoTracking()
            .Include(p => p.Supplier)
            .Where(p => p.Active && p.QtyOnHand <= LowStockThreshold)
            .OrderBy(p => p.QtyOnHand)
            .ThenBy(p => p.Name)
            .Take(LowStockLimit)
            .Select(p => new DashboardLowStockDto
            {
                ProductId = p.Id,
                Sku = p.Sku,
                Name = p.Name,
                QtyOnHand = p.QtyOnHand,
                SupplierName = p.Supplier != null ? p.Supplier.Name : null
            })
            .ToListAsync(ct);

        // ── Recent activity ─────────────────────────────────────────────────
        var recentActivity = await BuildRecentActivityAsync(allInvoices, ct);

        return new DashboardOverviewDto
        {
            From = windowFrom,
            To = windowTo,
            Kpis = kpis,
            SalesTrend = trend,
            PaymentMethods = paymentMethods,
            TopCategories = topCategories,
            TopProducts = topProducts,
            LowStockAlerts = lowStock,
            RecentActivity = recentActivity
        };
    }

    /// <summary>Default range = last 30 days, ending today end-of-day local time.</summary>
    private static (DateTimeOffset from, DateTimeOffset to) ResolveWindow(DateTimeOffset? from, DateTimeOffset? to, DateTimeOffset nowUtc)
    {
        if (from.HasValue && to.HasValue && to.Value > from.Value) return (from.Value, to.Value);

        var nowLocal = nowUtc.ToOffset(LocalOffset);
        var endLocal = new DateTimeOffset(nowLocal.Year, nowLocal.Month, nowLocal.Day, 23, 59, 59, LocalOffset);
        var startLocal = endLocal.AddDays(-29).Date;
        var startWithOffset = new DateTimeOffset(startLocal, LocalOffset);
        return (from ?? startWithOffset, to ?? endLocal);
    }

    private static (DateTimeOffset from, DateTimeOffset to) TodayBoundsLocal(DateTimeOffset nowUtc)
    {
        var local = nowUtc.ToOffset(LocalOffset);
        var start = new DateTimeOffset(local.Year, local.Month, local.Day, 0, 0, 0, LocalOffset);
        return (start, start.AddDays(1));
    }

    private static (DateTimeOffset from, DateTimeOffset to) MonthBoundsLocal(DateTimeOffset nowUtc)
    {
        var local = nowUtc.ToOffset(LocalOffset);
        var start = new DateTimeOffset(local.Year, local.Month, 1, 0, 0, 0, LocalOffset);
        return (start, start.AddMonths(1));
    }

    /// <summary>
    /// GP = (revenue - order discount) - (cost ex VAT × 1.15) summed over all
    /// invoice lines. Mirrors <see cref="ReportsController.Daily"/> formula so
    /// the dashboard agrees with the existing Financial overview report.
    /// </summary>
    private static decimal ComputeGrossProfit(IReadOnlyCollection<Invoice> invoices)
    {
        if (invoices.Count == 0) return 0m;
        var revenue = invoices.Sum(i => i.Lines.Sum(l => l.LineTotal));
        var orderDisc = invoices.Sum(i => i.DiscountTotal);
        var costEx = invoices.Sum(i => i.Lines.Sum(l =>
            (l.CostAtSale > 0 ? l.CostAtSale : (l.Product?.Cost ?? 0m)) * l.Quantity));
        return Math.Round((revenue - orderDisc) - costEx * VatMultiplier, 2);
    }

    private static decimal? PercentDelta(decimal current, decimal previous)
    {
        if (previous == 0m) return null;
        return Math.Round((current - previous) / previous * 100m, 1);
    }

    private static decimal? PercentDelta(int current, int previous) => PercentDelta((decimal)current, (decimal)previous);

    /// <summary>
    /// Buckets invoices into a per-day total covering the entire requested
    /// span, including days with zero sales (so the chart line doesn't gap).
    /// </summary>
    private static List<DashboardTrendPointDto> BucketByDay(IEnumerable<Invoice> invoices, DateTimeOffset from, DateTimeOffset to)
    {
        var grouped = invoices
            .GroupBy(i => DateOnly.FromDateTime(i.CreatedAt.ToOffset(LocalOffset).DateTime))
            .ToDictionary(g => g.Key, g => g.Sum(x => x.GrandTotal));

        var points = new List<DashboardTrendPointDto>();
        var startDay = DateOnly.FromDateTime(from.ToOffset(LocalOffset).DateTime);
        var endDay = DateOnly.FromDateTime(to.ToOffset(LocalOffset).DateTime);
        for (var d = startDay; d <= endDay; d = d.AddDays(1))
        {
            points.Add(new DashboardTrendPointDto
            {
                Date = d,
                Total = grouped.TryGetValue(d, out var v) ? v : 0m
            });
        }
        return points;
    }

    /// <summary>
    /// Maps legacy/variant payment-method strings onto canonical buckets.
    /// Mirrors <see cref="ReportsController"/> so charts agree across pages.
    /// </summary>
    private static string NormalisePaymentMethod(string? method)
    {
        if (string.IsNullOrWhiteSpace(method)) return "Unknown";
        var t = method.Trim();
        if (t.Equals("Bank", StringComparison.OrdinalIgnoreCase)) return "EFT";
        if (t.Equals("EFT", StringComparison.OrdinalIgnoreCase)) return "EFT";
        if (t.Equals("Cash", StringComparison.OrdinalIgnoreCase)) return "Cash";
        if (t.Equals("Card", StringComparison.OrdinalIgnoreCase)) return "Card";
        return t;
    }

    /// <summary>
    /// Unified recent-activity feed: most recent invoices (sale/void), quotes
    /// (created/converted) and stock receipts. Capped at <see cref="RecentActivityLimit"/>
    /// items, no time window — this is "what's happening now", not historical.
    /// </summary>
    private async Task<List<DashboardActivityDto>> BuildRecentActivityAsync(
        IReadOnlyCollection<Invoice> allInvoices,
        CancellationToken ct)
    {
        // Pull the most recent few of each type then merge — much cheaper than
        // sorting full tables in memory.
        var invoices = allInvoices
            .OrderByDescending(i => i.VoidedAt ?? i.CreatedAt)
            .Take(RecentActivityLimit * 2)
            .ToList();

        var quotes = await _db.Quotes.AsNoTracking()
            .OrderByDescending(q => q.ConvertedAt ?? q.CreatedAt)
            .Take(RecentActivityLimit)
            .ToListAsync(ct);

        var receipts = await _db.StockReceipts.AsNoTracking()
            .Include(r => r.Product)
            .OrderByDescending(r => r.CreatedAt)
            .Take(RecentActivityLimit)
            .ToListAsync(ct);

        // Resolve display names for any user IDs we'll cite.
        var userIds = invoices
            .SelectMany(i => new[] { i.CreatedByUserId, i.VoidedByUserId })
            .Concat(quotes.Select(q => q.CreatedByUserId))
            .Concat(receipts.Select(r => r.ProcessedBy))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .Distinct()
            .ToList();

        var users = userIds.Count == 0
            ? new Dictionary<string, string?>()
            : await _db.Users.AsNoTracking()
                .Where(u => userIds.Contains(u.Id))
                .Select(u => new { u.Id, Name = u.DisplayName ?? u.UserName ?? u.Email })
                .ToDictionaryAsync(x => x.Id, x => x.Name, ct);

        string? Who(string? id) =>
            id != null && users.TryGetValue(id, out var n) ? n : null;

        var items = new List<DashboardActivityDto>();

        foreach (var i in invoices)
        {
            if (i.Status == InvoiceStatus.Voided && i.VoidedAt.HasValue)
            {
                items.Add(new DashboardActivityDto
                {
                    Type = "void",
                    Ts = i.VoidedAt.Value,
                    Actor = Who(i.VoidedByUserId),
                    Summary = $"Invoice voided — {i.InvoiceNumber}{(i.VoidReason != null ? ": " + i.VoidReason : "")}",
                    Link = "/reports"
                });
            }
            else if (i.Status == InvoiceStatus.Final)
            {
                var who = !string.IsNullOrWhiteSpace(i.CustomerName) ? i.CustomerName : Who(i.CreatedByUserId);
                var summary = string.IsNullOrWhiteSpace(who)
                    ? $"Sale completed — {i.InvoiceNumber}"
                    : $"Sale completed — {i.InvoiceNumber} for {who}";
                items.Add(new DashboardActivityDto
                {
                    Type = "sale",
                    Ts = i.CreatedAt,
                    Actor = Who(i.CreatedByUserId),
                    Summary = summary,
                    Link = "/reports"
                });
            }
        }

        foreach (var q in quotes)
        {
            if (q.Status == QuoteStatus.Converted && q.ConvertedAt.HasValue)
            {
                items.Add(new DashboardActivityDto
                {
                    Type = "quote-converted",
                    Ts = q.ConvertedAt.Value,
                    Actor = Who(q.CreatedByUserId),
                    Summary = $"Quote converted — {q.QuoteNumber}",
                    Link = $"/quotes/{q.Id}"
                });
            }
            else
            {
                items.Add(new DashboardActivityDto
                {
                    Type = "quote-created",
                    Ts = q.CreatedAt,
                    Actor = Who(q.CreatedByUserId),
                    Summary = $"Quote created — {q.QuoteNumber}{(q.CustomerName != null ? " for " + q.CustomerName : "")}",
                    Link = $"/quotes/{q.Id}"
                });
            }
        }

        foreach (var r in receipts)
        {
            var label = r.Type switch
            {
                StockReceiptType.OwnedIn          => "Stock received",
                StockReceiptType.ConsignmentIn    => "Consignment received",
                StockReceiptType.ConsignmentToStock => "Moved to stock",
                StockReceiptType.ConsignmentReturn => "Consignment returned",
                StockReceiptType.StockToConsignment => "Moved to consignment",
                StockReceiptType.Adjustment       => "Stock adjusted",
                _                                 => "Stock movement"
            };
            items.Add(new DashboardActivityDto
            {
                Type = r.Quantity < 0 ? "restock-out" : "restock",
                Ts = r.CreatedAt,
                Actor = Who(r.ProcessedBy),
                Summary = $"{label} — {Math.Abs(r.Quantity)}× {r.Product?.Name ?? r.Product?.Sku ?? "(unknown)"}",
                Link = "/stock"
            });
        }

        return items
            .OrderByDescending(a => a.Ts)
            .Take(RecentActivityLimit)
            .ToList();
    }
}
