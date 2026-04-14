using System.Globalization;
using System.Text;
using HuntexPos.Api.Data;
using HuntexPos.Api.Domain;
using HuntexPos.Api.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HuntexPos.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = $"{Roles.Admin},{Roles.Owner},{Roles.Dev}")]
public class ReportsController : ControllerBase
{
    private readonly HuntexDbContext _db;

    public ReportsController(HuntexDbContext db) => _db = db;

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
    public async Task<List<DailySummaryDto>> Daily(CancellationToken ct, [FromQuery] int days = 14)
    {
        var from = DateTimeOffset.UtcNow.AddDays(-Math.Clamp(days, 1, 90));
        var all = await _db.Invoices.AsNoTracking().ToListAsync(ct);
        return all
            .Where(i => i.Status == InvoiceStatus.Final && i.CreatedAt >= from)
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

    private static string Csv(string s)
    {
        if (s.Contains('"') || s.Contains(',') || s.Contains('\n'))
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        return s;
    }
}
