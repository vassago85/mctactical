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
        var q = _db.Invoices.AsNoTracking().AsQueryable();
        if (from.HasValue) q = q.Where(i => i.CreatedAt >= from);
        if (to.HasValue) q = q.Where(i => i.CreatedAt <= to);
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<InvoiceStatus>(status, true, out var st))
            q = q.Where(i => i.Status == st);

        var rows = await q.OrderByDescending(i => i.CreatedAt).Take(500).ToListAsync(ct);
        return rows.Select(i => new InvoiceListItemDto
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
        var list = await _db.Invoices.AsNoTracking()
            .Where(i => i.Status == InvoiceStatus.Final && i.CreatedAt >= from)
            .ToListAsync(ct);
        return list
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
        var q = _db.Invoices.AsNoTracking().Include(i => i.Lines).AsQueryable();
        if (from.HasValue) q = q.Where(i => i.CreatedAt >= from);
        if (to.HasValue) q = q.Where(i => i.CreatedAt <= to);

        var list = await q.OrderByDescending(i => i.CreatedAt).Take(2000).ToListAsync(ct);
        var sb = new StringBuilder();
        sb.AppendLine("InvoiceNumber,Status,CreatedAt,Customer,GrandTotal,Lines");
        foreach (var inv in list)
        {
            var lineSummary = string.Join(";", inv.Lines.Select(l => $"{l.Description}x{l.Quantity}"));
            sb.AppendLine(string.Join(",",
                Csv(inv.InvoiceNumber),
                inv.Status.ToString(),
                inv.CreatedAt.ToString("o", CultureInfo.InvariantCulture),
                Csv(inv.CustomerName ?? ""),
                inv.GrandTotal.ToString(CultureInfo.InvariantCulture),
                Csv(lineSummary)));
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
