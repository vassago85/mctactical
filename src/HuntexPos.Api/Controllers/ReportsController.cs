using System.Globalization;
using System.Security.Claims;
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

        var page = rows.OrderByDescending(i => i.CreatedAt).Take(500).ToList();

        var voidUserIds = page
            .Where(i => !string.IsNullOrEmpty(i.VoidedByUserId))
            .Select(i => i.VoidedByUserId!)
            .Distinct()
            .ToList();
        var voidUsers = voidUserIds.Count == 0
            ? new Dictionary<string, string?>()
            : await _db.Users.AsNoTracking()
                .Where(u => voidUserIds.Contains(u.Id))
                .Select(u => new { u.Id, Name = u.DisplayName ?? u.UserName ?? u.Email })
                .ToDictionaryAsync(x => x.Id, x => x.Name, ct);

        return page.Select(i => new InvoiceListItemDto
        {
            Id = i.Id,
            InvoiceNumber = i.InvoiceNumber,
            Status = i.Status.ToString(),
            GrandTotal = i.GrandTotal,
            CreatedAt = i.CreatedAt,
            CustomerName = i.CustomerName,
            CreatedByUserId = i.CreatedByUserId,
            VoidReason = i.VoidReason,
            VoidedAt = i.VoidedAt,
            VoidedByName = i.VoidedByUserId != null && voidUsers.TryGetValue(i.VoidedByUserId, out var n) ? n : null
        }).ToList();
    }

    [HttpGet("daily")]
    public async Task<List<DailySummaryDto>> Daily(
        CancellationToken ct,
        [FromQuery] int days = 14,
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null)
    {
        var all = await _db.Invoices.AsNoTracking()
            .Include(i => i.Lines)
            .ToListAsync(ct);
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
            .Select(g =>
            {
                var dayInvoices = g.ToList();
                var revenue = dayInvoices.Sum(i => i.Lines.Sum(l => l.LineTotal));
                var orderDisc = dayInvoices.Sum(i => i.DiscountTotal);
                var costEx = dayInvoices.Sum(i => i.Lines.Sum(l =>
                    (l.CostAtSale > 0 ? l.CostAtSale : 0m) * l.Quantity));
                var gp = Math.Round((revenue - orderDisc) - costEx * 1.15m, 2);

                return new DailySummaryDto
                {
                    Date = g.Key,
                    InvoiceCount = dayInvoices.Count,
                    GrandTotal = dayInvoices.Sum(x => x.GrandTotal),
                    GrossProfit = gp
                };
            })
            .OrderByDescending(x => x.Date)
            .ToList();
    }

    [HttpGet("payments")]
    public async Task<PaymentsSummaryDto> Payments(
        CancellationToken ct,
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null)
    {
        var all = await _db.Invoices.AsNoTracking().ToListAsync(ct);
        IEnumerable<Invoice> rows = all.Where(i => i.Status == InvoiceStatus.Final);
        if (from.HasValue) rows = rows.Where(i => i.CreatedAt >= from.Value);
        if (to.HasValue) rows = rows.Where(i => i.CreatedAt <= to.Value);

        var list = rows.ToList();
        var byMethod = list
            .GroupBy(i => NormalisePaymentMethod(i.PaymentMethod))
            .Select(g => new PaymentMethodBreakdownDto
            {
                Method = g.Key,
                Count = g.Count(),
                GrandTotal = g.Sum(x => x.GrandTotal)
            })
            .OrderBy(m => m.Method)
            .ToList();

        return new PaymentsSummaryDto
        {
            TotalGrand = list.Sum(i => i.GrandTotal),
            TotalCount = list.Count,
            ByMethod = byMethod
        };
    }

    /// <summary>
    /// Maps legacy/variant payment-method strings onto the canonical Card / Cash / EFT bucket.
    /// Historical invoices stored "Bank" for electronic transfer; treat those as EFT for rollups.
    /// </summary>
    private static string NormalisePaymentMethod(string? method)
    {
        if (string.IsNullOrWhiteSpace(method)) return "Unknown";
        var trimmed = method.Trim();
        if (trimmed.Equals("Bank", StringComparison.OrdinalIgnoreCase)) return "EFT";
        if (trimmed.Equals("EFT", StringComparison.OrdinalIgnoreCase)) return "EFT";
        if (trimmed.Equals("Cash", StringComparison.OrdinalIgnoreCase)) return "Cash";
        if (trimmed.Equals("Card", StringComparison.OrdinalIgnoreCase)) return "Card";
        return trimmed;
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
        sb.AppendLine("Date,Invoice,Customer,SKU,Description,Qty,Wholesale (ex VAT),Wholesale + VAT,Sale Price,Line Total,Line Discount,Order Discount,GP");

        decimal sumCostExcl = 0, sumCostIncl = 0, sumSaleTotal = 0, sumLineTotal = 0;
        decimal sumLineDisc = 0, sumOrderDisc = 0, sumGp = 0;

        foreach (var inv in list)
        {
            var date = inv.CreatedAt.ToOffset(TimeSpan.FromHours(2)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var invSub = inv.SubTotal;
            foreach (var line in inv.Lines)
            {
                var costExcl = line.CostAtSale > 0 ? line.CostAtSale : (line.Product?.Cost ?? 0);
                var costIncl = Math.Round(costExcl * 1.15m, 2);
                var salePrice = line.UnitPrice;
                var lineTotal = line.LineTotal;
                var lineDisc = line.LineDiscount;
                var orderDiscShare = invSub > 0
                    ? Math.Round(lineTotal / invSub * inv.DiscountTotal, 2)
                    : 0m;
                var gp = Math.Round((lineTotal - orderDiscShare) - costExcl * line.Quantity * 1.15m, 2);

                sumCostExcl += costExcl * line.Quantity;
                sumCostIncl += costIncl * line.Quantity;
                sumSaleTotal += salePrice * line.Quantity;
                sumLineTotal += lineTotal;
                sumLineDisc += lineDisc;
                sumOrderDisc += orderDiscShare;
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
                    lineDisc.ToString("F2", CultureInfo.InvariantCulture),
                    orderDiscShare.ToString("F2", CultureInfo.InvariantCulture),
                    gp.ToString("F2", CultureInfo.InvariantCulture)));
            }
        }

        sb.AppendLine();
        sb.AppendLine(string.Join(",",
            "", "", "", "", "TOTALS", "",
            sumCostExcl.ToString("F2", CultureInfo.InvariantCulture),
            sumCostIncl.ToString("F2", CultureInfo.InvariantCulture),
            sumSaleTotal.ToString("F2", CultureInfo.InvariantCulture),
            sumLineTotal.ToString("F2", CultureInfo.InvariantCulture),
            sumLineDisc.ToString("F2", CultureInfo.InvariantCulture),
            sumOrderDisc.ToString("F2", CultureInfo.InvariantCulture),
            sumGp.ToString("F2", CultureInfo.InvariantCulture)));

        sb.AppendLine();
        sb.AppendLine("Date,Invoices,Total Sales,Total Discount,Total Wholesale + VAT,GP");
        var dailyGroups = list
            .GroupBy(i => i.CreatedAt.ToOffset(TimeSpan.FromHours(2)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
            .OrderByDescending(g => g.Key);
        foreach (var day in dailyGroups)
        {
            var dayInvoices = day.ToList();
            var dayLines = dayInvoices.SelectMany(i => i.Lines).ToList();
            var daySales = dayLines.Sum(l => l.LineTotal);
            var dayDiscount = dayInvoices.Sum(i => i.DiscountTotal) + dayLines.Sum(l => l.LineDiscount);
            var dayCostExcl = dayLines.Sum(l => (l.CostAtSale > 0 ? l.CostAtSale : (l.Product?.Cost ?? 0)) * l.Quantity);
            var dayCostIncl = dayLines.Sum(l => Math.Round((l.CostAtSale > 0 ? l.CostAtSale : (l.Product?.Cost ?? 0)) * 1.15m, 2) * l.Quantity);
            var dayGp = Math.Round((daySales - dayInvoices.Sum(i => i.DiscountTotal)) - dayCostExcl * 1.15m, 2);
            sb.AppendLine(string.Join(",",
                day.Key,
                day.Count().ToString(CultureInfo.InvariantCulture),
                daySales.ToString("F2", CultureInfo.InvariantCulture),
                dayDiscount.ToString("F2", CultureInfo.InvariantCulture),
                dayCostIncl.ToString("F2", CultureInfo.InvariantCulture),
                dayGp.ToString("F2", CultureInfo.InvariantCulture)));
        }

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        return File(bytes, "text/csv", "invoices.csv");
    }

    [HttpGet("stock")]
    public async Task<ActionResult<StockReportDto>> StockReport(
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        CancellationToken ct)
    {
      try
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
            TotalConsignmentValue = products.Sum(p => p.Cost * 1.15m * p.QtyConsignment),
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

        // Sold in period (load all then filter — SQLite can't translate DateTimeOffset comparisons)
        var allInvoices = await _db.Invoices.AsNoTracking()
            .Include(i => i.Lines).ThenInclude(l => l.Product)
            .ToListAsync(ct);
        IEnumerable<Invoice> filteredInvoices = allInvoices.Where(i => i.Status == InvoiceStatus.Final);
        if (from.HasValue) filteredInvoices = filteredInvoices.Where(i => i.CreatedAt >= from.Value);
        if (to.HasValue) filteredInvoices = filteredInvoices.Where(i => i.CreatedAt <= to.Value);

        var invoices = filteredInvoices.ToList();

        // Build per-line records that include proportionally allocated order discount
        var soldLineRecords = invoices.SelectMany(inv =>
        {
            var sub = inv.SubTotal;
            return inv.Lines.Select(l =>
            {
                var orderDiscShare = sub > 0
                    ? Math.Round(l.LineTotal / sub * inv.DiscountTotal, 2)
                    : 0m;
                return new { Line = l, OrderDiscountShare = orderDiscShare };
            });
        }).ToList();

        var soldInPeriod = soldLineRecords
            .GroupBy(x => x.Line.ProductId)
            .Select(g =>
            {
                var first = g.First().Line;
                var costEx = g.Sum(x => (x.Line.CostAtSale > 0 ? x.Line.CostAtSale : (x.Line.Product?.Cost ?? 0)) * x.Line.Quantity);
                var costIncl = g.Sum(x => Math.Round((x.Line.CostAtSale > 0 ? x.Line.CostAtSale : (x.Line.Product?.Cost ?? 0)) * 1.15m, 2) * x.Line.Quantity);
                var discount = g.Sum(x => x.OrderDiscountShare);
                return new ProductSoldSummaryDto
                {
                    Sku = first.Product?.Sku ?? "",
                    Name = first.Description,
                    QtySold = g.Sum(x => x.Line.Quantity),
                    Revenue = g.Sum(x => x.Line.LineTotal),
                    Discount = discount,
                    CostExVat = costEx,
                    CostInclVat = costIncl
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
      catch (Exception ex)
      {
          return StatusCode(500, new { error = ex.Message, detail = ex.InnerException?.Message });
      }
    }

    [HttpGet("consignment")]
    public async Task<ActionResult<ConsignmentReportDto>> ConsignmentReport(
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] Guid? supplierId,
        CancellationToken ct)
    {
      try
      {
        var receiptsQuery = _db.StockReceipts.AsNoTracking()
            .Include(r => r.Supplier)
            .Include(r => r.Product)
            .Where(r => r.SupplierId != null);
        if (supplierId.HasValue)
            receiptsQuery = receiptsQuery.Where(r => r.SupplierId == supplierId.Value);

        var allReceipts = await receiptsQuery.ToListAsync(ct);

        var allInvoices = await _db.Invoices.AsNoTracking()
            .Include(i => i.Lines).ThenInclude(l => l.Product)
            .ToListAsync(ct);
        IEnumerable<Invoice> finalInvoices = allInvoices.Where(i => i.Status == InvoiceStatus.Final);
        if (from.HasValue) finalInvoices = finalInvoices.Where(i => i.CreatedAt >= from.Value);
        if (to.HasValue) finalInvoices = finalInvoices.Where(i => i.CreatedAt <= to.Value);
        var soldLines = finalInvoices.SelectMany(i => i.Lines).ToList();

        var soldByProduct = soldLines
            .GroupBy(l => l.ProductId)
            .ToDictionary(g => g.Key, g => new {
                Qty = g.Sum(l => l.Quantity),
                Revenue = g.Sum(l => l.LineTotal)
            });

        var supplierGroups = allReceipts
            .GroupBy(r => r.SupplierId!.Value)
            .Select(sg =>
            {
                var supplierName = sg.First().Supplier?.Name ?? "Unknown";
                var productGroups = sg
                    .GroupBy(r => r.ProductId)
                    .Select(pg =>
                    {
                        var product = pg.First().Product;
                        var consignIn = pg.Where(r => r.Type == StockReceiptType.ConsignmentIn).Sum(r => r.Quantity);
                        var movedToStock = pg.Where(r => r.Type == StockReceiptType.ConsignmentToStock).Sum(r => r.Quantity);
                        var returned = pg.Where(r => r.Type == StockReceiptType.ConsignmentReturn).Sum(r => r.Quantity);
                        var movedFromStock = pg.Where(r => r.Type == StockReceiptType.StockToConsignment).Sum(r => r.Quantity);
                        var onHand = consignIn + movedFromStock - movedToStock - returned;
                        var sellPrice = product?.SellPrice ?? 0;
                        var cost = product?.Cost ?? 0;

                        var sold = soldByProduct.TryGetValue(pg.Key, out var s) ? s : null;

                        return new ConsignmentProductReportDto
                        {
                            Sku = product?.Sku ?? "",
                            Name = product?.Name ?? "",
                            SellPrice = sellPrice,
                            Cost = cost,
                            OnHand = onHand,
                            OnHandValue = onHand * sellPrice,
                            Received = consignIn,
                            MovedToStock = movedToStock,
                            Returned = returned,
                            MovedFromStock = movedFromStock,
                            Sold = sold?.Qty ?? 0,
                            SoldRevenue = sold?.Revenue ?? 0
                        };
                    })
                    .Where(p => p.Received > 0 || p.MovedFromStock > 0)
                    .OrderBy(p => p.Name)
                    .ToList();

                return new ConsignmentSupplierReportDto
                {
                    SupplierId = sg.Key,
                    SupplierName = supplierName,
                    OnHand = productGroups.Sum(p => p.OnHand),
                    OnHandValue = productGroups.Sum(p => p.OnHandValue),
                    TotalReceived = productGroups.Sum(p => p.Received),
                    TotalReceivedValue = productGroups.Sum(p => p.Received * p.SellPrice),
                    TotalSold = productGroups.Sum(p => p.Sold),
                    TotalSoldRevenue = productGroups.Sum(p => p.SoldRevenue),
                    TotalMovedToStock = productGroups.Sum(p => p.MovedToStock),
                    TotalReturned = productGroups.Sum(p => p.Returned),
                    TotalMovedFromStock = productGroups.Sum(p => p.MovedFromStock),
                    Products = productGroups
                };
            })
            .Where(s => s.TotalReceived > 0 || s.TotalMovedFromStock > 0)
            .OrderBy(s => s.SupplierName)
            .ToList();

        return new ConsignmentReportDto
        {
            Suppliers = supplierGroups,
            TotalOnHand = supplierGroups.Sum(s => s.OnHand),
            TotalOnHandValue = supplierGroups.Sum(s => s.OnHandValue),
            TotalReceived = supplierGroups.Sum(s => s.TotalReceived),
            TotalReceivedValue = supplierGroups.Sum(s => s.TotalReceivedValue),
            TotalSold = supplierGroups.Sum(s => s.TotalSold),
            TotalSoldRevenue = supplierGroups.Sum(s => s.TotalSoldRevenue),
            TotalReturned = supplierGroups.Sum(s => s.TotalReturned),
            TotalMovedFromStock = supplierGroups.Sum(s => s.TotalMovedFromStock),
        };
      }
      catch (Exception ex)
      {
          return StatusCode(500, new { error = ex.Message, detail = ex.InnerException?.Message });
      }
    }

    /// <summary>
    /// Vendor-scoped report for any authenticated user whose account is linked to a supplier
    /// (see <see cref="ApplicationUser.SupplierId"/>). Returns that supplier's consignment
    /// stock + their sold lines for the period. Uses <c>AllowAnonymous</c> to opt out of
    /// this controller's class-level Admin/Owner/Dev role gate so a Sales-only consignee
    /// helper (e.g. Venatics Gear) can check on their own stock; the JWT bearer is still
    /// required and enforced manually below.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("my-vendor")]
    public async Task<ActionResult<VendorReportDto>> MyVendorReport(
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        CancellationToken ct)
    {
        if (User?.Identity?.IsAuthenticated != true) return Unauthorized();

        var supplierIdRaw = User.FindFirstValue("supplierId");
        if (string.IsNullOrWhiteSpace(supplierIdRaw))
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!string.IsNullOrWhiteSpace(userId))
            {
                supplierIdRaw = await _db.Users.AsNoTracking()
                    .Where(u => u.Id == userId && u.SupplierId != null)
                    .Select(u => u.SupplierId!.Value.ToString())
                    .FirstOrDefaultAsync(ct);
            }
        }

        if (string.IsNullOrWhiteSpace(supplierIdRaw) || !Guid.TryParse(supplierIdRaw, out var supplierId))
            return Forbid();

        var supplier = await _db.Suppliers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == supplierId, ct);
        if (supplier == null) return NotFound();

        var receipts = await _db.StockReceipts.AsNoTracking()
            .Include(r => r.Product)
            .Where(r => r.SupplierId == supplierId)
            .ToListAsync(ct);

        var allInvoices = await _db.Invoices.AsNoTracking()
            .Include(i => i.Lines).ThenInclude(l => l.Product)
            .ToListAsync(ct);
        IEnumerable<Invoice> finalInvoices = allInvoices.Where(i => i.Status == InvoiceStatus.Final);
        if (from.HasValue) finalInvoices = finalInvoices.Where(i => i.CreatedAt >= from.Value);
        if (to.HasValue) finalInvoices = finalInvoices.Where(i => i.CreatedAt <= to.Value);
        var invoiceList = finalInvoices.ToList();

        var soldLinesAll = invoiceList
            .SelectMany(i => i.Lines.Select(l => new { Invoice = i, Line = l }))
            .Where(x => x.Line.Product != null && x.Line.Product.SupplierId == supplierId)
            .ToList();

        var soldByProduct = soldLinesAll
            .GroupBy(x => x.Line.ProductId)
            .ToDictionary(g => g.Key, g => new
            {
                Qty = g.Sum(x => x.Line.Quantity),
                Revenue = g.Sum(x => x.Line.LineTotal)
            });

        var products = receipts
            .GroupBy(r => r.ProductId)
            .Select(pg =>
            {
                var product = pg.First().Product;
                var consignIn = pg.Where(r => r.Type == StockReceiptType.ConsignmentIn).Sum(r => r.Quantity);
                var movedToStock = pg.Where(r => r.Type == StockReceiptType.ConsignmentToStock).Sum(r => r.Quantity);
                var returned = pg.Where(r => r.Type == StockReceiptType.ConsignmentReturn).Sum(r => r.Quantity);
                var movedFromStock = pg.Where(r => r.Type == StockReceiptType.StockToConsignment).Sum(r => r.Quantity);
                var onHand = consignIn + movedFromStock - movedToStock - returned;
                var sellPrice = product?.SellPrice ?? 0;
                var cost = product?.Cost ?? 0;
                var sold = soldByProduct.TryGetValue(pg.Key, out var s) ? s : null;

                return new ConsignmentProductReportDto
                {
                    Sku = product?.Sku ?? "",
                    Name = product?.Name ?? "",
                    SellPrice = sellPrice,
                    Cost = cost,
                    OnHand = onHand,
                    OnHandValue = onHand * sellPrice,
                    Received = consignIn,
                    MovedToStock = movedToStock,
                    Returned = returned,
                    MovedFromStock = movedFromStock,
                    Sold = sold?.Qty ?? 0,
                    SoldRevenue = sold?.Revenue ?? 0
                };
            })
            .Where(p => p.Received > 0 || p.MovedFromStock > 0 || p.Sold > 0)
            .OrderBy(p => p.Name)
            .ToList();

        var soldLines = soldLinesAll
            .OrderByDescending(x => x.Invoice.CreatedAt)
            .Take(500)
            .Select(x => new VendorSoldLineDto
            {
                CreatedAt = x.Invoice.CreatedAt,
                InvoiceNumber = x.Invoice.InvoiceNumber,
                Sku = x.Line.Product?.Sku ?? "",
                Description = x.Line.Description,
                Quantity = x.Line.Quantity,
                UnitPrice = x.Line.UnitPrice,
                LineTotal = x.Line.LineTotal
            })
            .ToList();

        return new VendorReportDto
        {
            SupplierId = supplier.Id,
            SupplierName = supplier.Name,
            From = from,
            To = to,
            OnHand = products.Sum(p => p.OnHand),
            OnHandValue = products.Sum(p => p.OnHandValue),
            TotalReceived = products.Sum(p => p.Received),
            TotalSold = products.Sum(p => p.Sold),
            TotalSoldRevenue = products.Sum(p => p.SoldRevenue),
            TotalReturned = products.Sum(p => p.Returned),
            Products = products,
            SoldLines = soldLines
        };
    }

    [HttpPost("purge")]
    [Authorize(Roles = $"{Roles.Owner},{Roles.Dev}")]
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
