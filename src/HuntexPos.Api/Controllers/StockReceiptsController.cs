using HuntexPos.Api.Data;
using HuntexPos.Api.Domain;
using HuntexPos.Api.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HuntexPos.Api.Controllers;

[ApiController]
[Route("api/products/{productId:guid}/stock-receipts")]
[Authorize(Roles = $"{Roles.Admin},{Roles.Owner},{Roles.Dev}")]
public class StockReceiptsController : ControllerBase
{
    private readonly HuntexDbContext _db;

    public StockReceiptsController(HuntexDbContext db) => _db = db;

    [HttpPost]
    public async Task<ActionResult<StockReceiptDto>> Create(
        Guid productId,
        [FromBody] CreateStockReceiptRequest req,
        CancellationToken ct)
    {
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == productId, ct);
        if (product == null)
            return NotFound(new { error = "Product not found." });

        if (!Enum.TryParse<StockReceiptType>(req.Type, ignoreCase: true, out var type))
            return BadRequest(new { error = $"Invalid type '{req.Type}'. Valid: OwnedIn, ConsignmentIn, ConsignmentToStock, ConsignmentReturn." });

        if (req.Quantity < 1)
            return BadRequest(new { error = "Quantity must be at least 1." });

        if (type is StockReceiptType.ConsignmentIn or StockReceiptType.ConsignmentToStock or StockReceiptType.ConsignmentReturn)
        {
            if (!req.SupplierId.HasValue)
                return BadRequest(new { error = "SupplierId is required for consignment movements." });
            if (!await _db.Suppliers.AnyAsync(s => s.Id == req.SupplierId.Value, ct))
                return BadRequest(new { error = "Supplier not found." });
        }

        if (type is StockReceiptType.ConsignmentToStock or StockReceiptType.ConsignmentReturn)
        {
            var supplierBalance = await GetSupplierConsignmentBalance(productId, req.SupplierId!.Value, ct);
            if (req.Quantity > supplierBalance)
                return BadRequest(new { error = $"Only {supplierBalance} consignment units available from this supplier." });
        }

        switch (type)
        {
            case StockReceiptType.OwnedIn:
                product.QtyOnHand += req.Quantity;
                if (req.CostPrice.HasValue && req.CostPrice.Value > 0)
                    product.Cost = req.CostPrice.Value;
                break;
            case StockReceiptType.ConsignmentIn:
                product.QtyConsignment += req.Quantity;
                break;
            case StockReceiptType.ConsignmentToStock:
                product.QtyConsignment -= req.Quantity;
                product.QtyOnHand += req.Quantity;
                break;
            case StockReceiptType.ConsignmentReturn:
                product.QtyConsignment -= req.Quantity;
                break;
        }

        product.UpdatedAt = DateTimeOffset.UtcNow;

        var receipt = new StockReceipt
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            SupplierId = req.SupplierId,
            Type = type,
            Quantity = req.Quantity,
            CostPrice = req.CostPrice,
            Notes = string.IsNullOrWhiteSpace(req.Notes) ? null : req.Notes.Trim(),
            ProcessedBy = User.Identity?.Name,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _db.StockReceipts.Add(receipt);
        await _db.SaveChangesAsync(ct);

        var supplier = req.SupplierId.HasValue
            ? await _db.Suppliers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == req.SupplierId.Value, ct)
            : null;

        return Ok(MapReceipt(receipt, supplier?.Name));
    }

    [HttpGet]
    public async Task<ActionResult<List<StockReceiptDto>>> List(Guid productId, CancellationToken ct)
    {
        if (!await _db.Products.AnyAsync(p => p.Id == productId, ct))
            return NotFound(new { error = "Product not found." });

        var receipts = await _db.StockReceipts
            .AsNoTracking()
            .Include(r => r.Supplier)
            .Where(r => r.ProductId == productId)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);

        return receipts.Select(r => MapReceipt(r, r.Supplier?.Name)).ToList();
    }

    [HttpGet("/api/products/{productId:guid}/consignment-summary")]
    public async Task<ActionResult<List<ConsignmentSummaryLineDto>>> ConsignmentSummary(Guid productId, CancellationToken ct)
    {
        if (!await _db.Products.AnyAsync(p => p.Id == productId, ct))
            return NotFound(new { error = "Product not found." });

        var receipts = await _db.StockReceipts
            .AsNoTracking()
            .Include(r => r.Supplier)
            .Where(r => r.ProductId == productId && r.SupplierId != null)
            .ToListAsync(ct);

        var summary = receipts
            .GroupBy(r => r.SupplierId!.Value)
            .Select(g =>
            {
                var totalIn = g.Where(r => r.Type == StockReceiptType.ConsignmentIn).Sum(r => r.Quantity);
                var totalMoved = g.Where(r => r.Type == StockReceiptType.ConsignmentToStock).Sum(r => r.Quantity);
                var totalReturned = g.Where(r => r.Type == StockReceiptType.ConsignmentReturn).Sum(r => r.Quantity);
                return new ConsignmentSummaryLineDto
                {
                    SupplierId = g.Key,
                    SupplierName = g.First().Supplier?.Name ?? "Unknown",
                    TotalIn = totalIn,
                    TotalMovedToStock = totalMoved,
                    TotalReturned = totalReturned,
                    OnHand = totalIn - totalMoved - totalReturned
                };
            })
            .Where(s => s.TotalIn > 0)
            .OrderBy(s => s.SupplierName)
            .ToList();

        return summary;
    }

    private async Task<int> GetSupplierConsignmentBalance(Guid productId, Guid supplierId, CancellationToken ct)
    {
        var receipts = await _db.StockReceipts
            .AsNoTracking()
            .Where(r => r.ProductId == productId && r.SupplierId == supplierId)
            .ToListAsync(ct);

        var totalIn = receipts.Where(r => r.Type == StockReceiptType.ConsignmentIn).Sum(r => r.Quantity);
        var totalOut = receipts
            .Where(r => r.Type is StockReceiptType.ConsignmentToStock or StockReceiptType.ConsignmentReturn)
            .Sum(r => r.Quantity);

        return totalIn - totalOut;
    }

    private static StockReceiptDto MapReceipt(StockReceipt r, string? supplierName) => new()
    {
        Id = r.Id,
        ProductId = r.ProductId,
        SupplierId = r.SupplierId,
        SupplierName = supplierName,
        Type = r.Type.ToString(),
        Quantity = r.Quantity,
        CostPrice = r.CostPrice,
        Notes = r.Notes,
        ProcessedBy = r.ProcessedBy,
        CreatedAt = r.CreatedAt
    };
}
