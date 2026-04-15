using System.Globalization;
using System.Text;
using HuntexPos.Api.Data;
using HuntexPos.Api.Domain;
using HuntexPos.Api.DTOs;
using HuntexPos.Api.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HuntexPos.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = $"{Roles.Admin},{Roles.Owner},{Roles.Dev}")]
public class ReportsController : ControllerBase
{
    private readonly HuntexDbContext _db;
    private readonly AppOptions _app;

    public ReportsController(HuntexDbContext db, IOptions<AppOptions> app)
    {
        _db = db;
        _app = app.Value;
    }

    [HttpGet("invoices")]
    public async Task<List<InvoiceListItemDto>> ListInvoices(
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] string? status,
        CancellationToken ct)
    {
        var all = await _db.Invoices.AsNoTracking().ToListAsync(ct);
        IEnumerable<Invoice> rows = all;
        if (from.HasValue) rows = rows.Where(i => i.CreatedAt >= from.Value);
        if (to.HasValue) rows = rows.Where(i => i.CreatedAt <= to.Value);
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<InvoiceStatus>(status, true, out var st))
            rows = rows.Where(i => i.Status == st);

        return rows.OrderByDescending(i => i.CreatedAt).Take(500)
            .Select(i => new InvoiceListItemDto
            {
                Id = i.Id,
                InvoiceNumber = i.InvoiceNumber,
                Status = i.Status.ToString(),
                GrandTotal = i.GrandTotal,
                CreatedAt = i.CreatedAt,
                CustomerName = i.CustomerName,
                CreatedByUserId = i.CreatedByUserId
            }).ToList();
    }

    [HttpGet("daily")]
    public async Task<List<DailySummaryDto>> Daily(
        CancellationToken ct,
        [FromQuery] int days = 14,
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null)
    {
        var all = await _db.Invoices.AsNoTracking().ToListAsync(ct);
        IEnumerable<Invoice> rows = all.Where(i => i.Status == InvoiceStatus.Final);

        if (from.HasValue || to.HasValue)
        {
            if (from.HasValue) rows = rows.Where(i => i.CreatedAt >= from.Value);
            if (to.HasValue) rows = rows.Where(i => i.CreatedAt <= to.Value);
        }
        else
        {
            var cutoff = DateTimeOffset.UtcNow.AddDays(-Math.Clamp(days, 1, 365));
            rows = rows.Where(i => i.CreatedAt >= cutoff);
        }

        return rows
            .GroupBy(i => DateOnly.FromDateTime(i.CreatedAt.UtcDateTime))
            .Select(g => new DailySummaryDto
            {
                Date = g.Key,
                InvoiceCount = g.Count(),
                GrandTotal = g.Sum(x => x.GrandTotal)
            })
            .OrderByDescending(x => x.Date)
            .ToList();
    }

    [HttpGet("invoices/export")]
    public async Task<IActionResult> ExportCsv(
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        CancellationToken ct)
    {
        var all = await _db.Invoices.AsNoTracking()
            .Include(i => i.Lines).ThenInclude(l => l.Product)
            .ToListAsync(ct);
        IEnumerable<Invoice> rows = all;
        if (from.HasValue) rows = rows.Where(i => i.CreatedAt >= from.Value);
        if (to.HasValue) rows = rows.Where(i => i.CreatedAt <= to.Value);

        var list = rows.Where(i => i.Status == InvoiceStatus.Final)
            .OrderByDescending(i => i.CreatedAt).Take(2000).ToList();

        var sb = new StringBuilder();
        sb.AppendLine("Date,Invoice,Customer,SKU,Description,Qty,Cost Excl,Cost Incl (15% VAT),Sale Price,Line Total,GP");

        decimal sumCostIncl = 0, sumSaleTotal = 0, sumLineTotal = 0, sumGp = 0;

        foreach (var inv in list)
        {
            var date = inv.CreatedAt.ToOffset(TimeSpan.FromHours(2)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            foreach (var line in inv.Lines)
            {
                var costExcl = line.CostAtSale > 0 ? line.CostAtSale : (line.Product?.Cost ?? 0);
                var costIncl = Math.Round(costExcl * 1.15m, 2);
                var salePrice = line.UnitPrice;
                var lineTotal = line.LineTotal;
                var gp = Math.Round(salePrice * line.Quantity - costIncl * line.Quantity - line.LineDiscount, 2);

                sumCostIncl += costIncl * line.Quantity;
                sumSaleTotal += salePrice * line.Quantity;
                sumLineTotal += lineTotal;
                sumGp += gp;

                sb.AppendLine(string.Join(",",
                    date,
                    Csv(inv.InvoiceNumber),
                    Csv(inv.CustomerName ?? ""),
                    Csv(line.Product?.Sku ?? ""),
                    Csv(line.Description),
                    line.Quantity.ToString(CultureInfo.InvariantCulture),
                    costExcl.ToString("F2", CultureInfo.InvariantCulture),
                    costIncl.ToString("F2", CultureInfo.InvariantCulture),
                    salePrice.ToString("F2", CultureInfo.InvariantCulture),
                    lineTotal.ToString("F2", CultureInfo.InvariantCulture),
                    gp.ToString("F2", CultureInfo.InvariantCulture)));
            }
        }

        sb.AppendLine();
        sb.AppendLine(string.Join(",",
            "", "", "", "", "TOTALS", "",
            "", sumCostIncl.ToString("F2", CultureInfo.InvariantCulture),
            sumSaleTotal.ToString("F2", CultureInfo.InvariantCulture),
            sumLineTotal.ToString("F2", CultureInfo.InvariantCulture),
            sumGp.ToString("F2", CultureInfo.InvariantCulture)));

        sb.AppendLine();
        sb.AppendLine("Date,Invoices,Total Sales,Total Cost Incl,GP");
        var dailyGroups = list
            .GroupBy(i => i.CreatedAt.ToOffset(TimeSpan.FromHours(2)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
            .OrderByDescending(g => g.Key);
        foreach (var day in dailyGroups)
        {
            var dayLines = day.SelectMany(i => i.Lines).ToList();
            var daySales = dayLines.Sum(l => l.LineTotal);
            var dayCostIncl = dayLines.Sum(l => Math.Round((l.CostAtSale > 0 ? l.CostAtSale : (l.Product?.Cost ?? 0)) * 1.15m, 2) * l.Quantity);
            var dayGp = daySales - dayCostIncl;
            sb.AppendLine(string.Join(",",
                day.Key,
                day.Count().ToString(CultureInfo.InvariantCulture),
                daySales.ToString("F2", CultureInfo.InvariantCulture),
                dayCostIncl.ToString("F2", CultureInfo.InvariantCulture),
                dayGp.ToString("F2", CultureInfo.InvariantCulture)));
        }

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        return File(bytes, "text/csv", "invoices.csv");
    }

    [HttpGet("stock")]
    public async Task<StockReportDto> StockReport(
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        CancellationToken ct)
    {
        var products = await _db.Products.AsNoTracking()
            .Include(p => p.Supplier)
            .Where(p => p.Active)
            .OrderBy(p => p.Name)
            .ToListAsync(ct);

        var onHandLines = products.Select(p => new StockOnHandLineDto
        {
            Sku = p.Sku,
            Name = p.Name,
            SupplierName = p.Supplier?.Name,
            QtyOwned = p.QtyOnHand,
            QtyConsignment = p.QtyConsignment,
            Cost = p.Cost,
            SellPrice = p.SellPrice,
            OwnedValue = p.Cost * p.QtyOnHand
        }).ToList();

        var onHand = new StockOnHandSummaryDto
        {
            TotalOwnedQty = products.Sum(p => p.QtyOnHand),
            TotalOwnedValue = products.Sum(p => p.Cost * p.QtyOnHand),
            TotalConsignmentQty = products.Sum(p => p.QtyConsignment),
            ProductCount = products.Count,
            Lines = onHandLines
        };

        // Consignment breakdown by supplier (all time)
        var allReceipts = await _db.StockReceipts.AsNoTracking()
            .Include(r => r.Supplier)
            .Include(r => r.Product)
            .Where(r => r.SupplierId != null)
            .ToListAsync(ct);

        var consignmentBySupplier = allReceipts
            .GroupBy(r => r.SupplierId!.Value)
            .Select(supplierGroup =>
            {
                var supplierName = supplierGroup.First().Supplier?.Name ?? "Unknown";
                var productGroups = supplierGroup
                    .GroupBy(r => r.ProductId)
                    .Select(pg =>
                    {
                        var pIn = pg.Where(r => r.Type == StockReceiptType.ConsignmentIn).Sum(r => r.Quantity);
                        var pMoved = pg.Where(r => r.Type == StockReceiptType.ConsignmentToStock).Sum(r => r.Quantity);
                        var pRet = pg.Where(r => r.Type == StockReceiptType.ConsignmentReturn).Sum(r => r.Quantity);
                        return new ConsignmentProductLineDto
                        {
                            Sku = pg.First().Product?.Sku ?? "",
                            Name = pg.First().Product?.Name ?? "",
                            OnHand = pIn - pMoved - pRet,
                            TotalIn = pIn,
                            TotalMovedToStock = pMoved,
                            TotalReturned = pRet
                        };
                    })
                    .Where(p => p.TotalIn > 0)
                    .OrderBy(p => p.Name)
                    .ToList();

                return new ConsignmentBySupplierDto
                {
                    SupplierId = supplierGroup.Key,
                    SupplierName = supplierName,
                    TotalIn = productGroups.Sum(p => p.TotalIn),
                    TotalMovedToStock = productGroups.Sum(p => p.TotalMovedToStock),
                    TotalReturned = productGroups.Sum(p => p.TotalReturned),
                    OnHand = productGroups.Sum(p => p.OnHand),
                    Products = productGroups
                };
            })
            .Where(s => s.TotalIn > 0)
            .OrderBy(s => s.SupplierName)
            .ToList();

        // Period-filtered data
        var periodReceipts = allReceipts.AsEnumerable();
        if (from.HasValue) periodReceipts = periodReceipts.Where(r => r.CreatedAt >= from.Value);
        if (to.HasValue) periodReceipts = periodReceipts.Where(r => r.CreatedAt <= to.Value);

        // Also include receipts without supplier (OwnedIn)
        var ownedReceipts = await _db.StockReceipts.AsNoTracking()
            .Include(r => r.Product)
            .Where(r => r.SupplierId == null)
            .ToListAsync(ct);
        var allReceiptsCombined = allReceipts.Concat(ownedReceipts);
        var periodAll = allReceiptsCombined.AsEnumerable();
        if (from.HasValue) periodAll = periodAll.Where(r => r.CreatedAt >= from.Value);
        if (to.HasValue) periodAll = periodAll.Where(r => r.CreatedAt <= to.Value);

        var receivedInPeriod = periodAll
            .Where(r => r.Type is StockReceiptType.OwnedIn or StockReceiptType.ConsignmentIn)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new StockMovementSummaryDto
            {
                Sku = r.Product?.Sku ?? "",
                Name = r.Product?.Name ?? "",
                SupplierName = r.Supplier?.Name,
                Type = r.Type.ToString(),
                Quantity = r.Quantity,
                CreatedAt = r.CreatedAt
            })
            .ToList();

        // Sold in period
        var invoiceQuery = _db.Invoices.AsNoTracking()
            .Include(i => i.Lines).ThenInclude(l => l.Product)
            .Where(i => i.Status == InvoiceStatus.Final);
        if (from.HasValue) invoiceQuery = invoiceQuery.Where(i => i.CreatedAt >= from.Value);
        if (to.HasValue) invoiceQuery = invoiceQuery.Where(i => i.CreatedAt <= to.Value);

        var invoices = await invoiceQuery.ToListAsync(ct);
        var allSoldLines = invoices.SelectMany(i => i.Lines).ToList();

        var soldInPeriod = allSoldLines
            .GroupBy(l => l.ProductId)
            .Select(g =>
            {
                var first = g.First();
                return new ProductSoldSummaryDto
                {
                    Sku = first.Product?.Sku ?? "",
                    Name = first.Description,
                    QtySold = g.Sum(l => l.Quantity),
                    Revenue = g.Sum(l => l.LineTotal),
                    Cost = g.Sum(l => l.CostAtSale * l.Quantity)
                };
            })
            .OrderByDescending(p => p.QtySold)
            .ToList();

        return new StockReportDto
        {
            OnHand = onHand,
            ConsignmentBySupplier = consignmentBySupplier,
            ReceivedInPeriod = receivedInPeriod,
            SoldInPeriod = soldInPeriod
        };
    }

    [HttpPost("purge")]
    [Authorize(Roles = Roles.Dev)]
    public async Task<IActionResult> PurgeData(CancellationToken ct)
    {
        await _db.Database.ExecuteSqlRawAsync("DELETE FROM InvoiceLines", ct);
        await _db.Database.ExecuteSqlRawAsync("DELETE FROM Invoices", ct);
        await _db.Database.ExecuteSqlRawAsync("DELETE FROM StocktakeLines", ct);
        await _db.Database.ExecuteSqlRawAsync("DELETE FROM StocktakeSessions", ct);
        await _db.Database.ExecuteSqlRawAsync("DELETE FROM StockReceipts", ct);
        await _db.Database.ExecuteSqlRawAsync("UPDATE Products SET QtyOnHand = 0, QtyConsignment = 0", ct);

        var pdfDir = Path.Combine(Directory.GetCurrentDirectory(), _app.PdfStoragePath);
        if (Directory.Exists(pdfDir))
        {
            foreach (var file in Directory.GetFiles(pdfDir, "*.pdf"))
                System.IO.File.Delete(file);
        }

        return Ok(new { message = "All sales, stock receipts, stocktakes and PDFs purged. Product quantities reset to zero." });
    }

    private static string Csv(string s)
    {
        if (s.Contains('"') || s.Contains(',') || s.Contains('\n'))
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        return s;
    }
}
