using HuntexPos.Api.Data;
using HuntexPos.Api.Domain;
using HuntexPos.Api.DTOs;
using HuntexPos.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HuntexPos.Api.Controllers;

[ApiController]
[Route("api/consignment-batches")]
[Authorize(Roles = $"{Roles.Admin},{Roles.Owner},{Roles.Dev}")]
public class ConsignmentBatchesController : ControllerBase
{
    private readonly HuntexDbContext _db;
    private readonly ConsignmentPdfService _pdf;

    public ConsignmentBatchesController(HuntexDbContext db, ConsignmentPdfService pdf)
    {
        _db = db;
        _pdf = pdf;
    }

    [HttpPost]
    public async Task<ActionResult<ConsignmentBatchDto>> Create([FromBody] CreateConsignmentBatchRequest req, CancellationToken ct)
    {
        if (!Enum.TryParse<ConsignmentBatchType>(req.Type, true, out var type))
            return BadRequest(new { error = "Type must be 'Receive' or 'Return'." });

        if (!await _db.Suppliers.AnyAsync(s => s.Id == req.SupplierId, ct))
            return BadRequest(new { error = "Supplier not found." });

        var batch = new ConsignmentBatch
        {
            Id = Guid.NewGuid(),
            SupplierId = req.SupplierId,
            Type = type,
            Status = ConsignmentBatchStatus.Draft,
            Notes = req.Notes?.Trim(),
            CreatedBy = User.Identity?.Name,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _db.ConsignmentBatches.Add(batch);
        await _db.SaveChangesAsync(ct);

        return Ok(await MapBatchAsync(batch.Id, ct));
    }

    [HttpGet]
    public async Task<ActionResult<List<ConsignmentBatchDto>>> List(
        [FromQuery] string? status,
        [FromQuery] string? type,
        [FromQuery] Guid? supplierId,
        CancellationToken ct)
    {
        var q = _db.ConsignmentBatches.AsNoTracking()
            .Include(b => b.Supplier)
            .Include(b => b.Lines).ThenInclude(l => l.Product)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<ConsignmentBatchStatus>(status, true, out var s))
            q = q.Where(b => b.Status == s);
        if (!string.IsNullOrWhiteSpace(type) && Enum.TryParse<ConsignmentBatchType>(type, true, out var t))
            q = q.Where(b => b.Type == t);
        if (supplierId.HasValue)
            q = q.Where(b => b.SupplierId == supplierId.Value);

        var batches = (await q.ToListAsync(ct))
            .OrderByDescending(b => b.CreatedAt).Take(100).ToList();
        return batches.Select(MapBatch).ToList();
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ConsignmentBatchDto>> Get(Guid id, CancellationToken ct)
    {
        var dto = await MapBatchAsync(id, ct);
        if (dto == null) return NotFound(new { error = "Batch not found." });
        return dto;
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var batch = await _db.ConsignmentBatches.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (batch == null) return NotFound();
        if (batch.Status == ConsignmentBatchStatus.Committed)
            return BadRequest(new { error = "Cannot delete a committed batch." });
        _db.ConsignmentBatches.Remove(batch);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/lines")]
    public async Task<ActionResult<ConsignmentBatchDto>> AddLine(Guid id, [FromBody] AddBatchLineRequest req, CancellationToken ct)
    {
        var batch = await _db.ConsignmentBatches.Include(b => b.Lines).FirstOrDefaultAsync(b => b.Id == id, ct);
        if (batch == null) return NotFound(new { error = "Batch not found." });
        if (batch.Status == ConsignmentBatchStatus.Committed)
            return BadRequest(new { error = "Batch is already committed." });

        var product = await _db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == req.ProductId, ct);
        if (product == null) return BadRequest(new { error = "Product not found." });

        var existing = batch.Lines.FirstOrDefault(l => l.ProductId == req.ProductId);
        if (existing != null)
        {
            existing.ExpectedQty += req.ExpectedQty;
        }
        else
        {
            batch.Lines.Add(new ConsignmentBatchLine
            {
                Id = Guid.NewGuid(),
                BatchId = id,
                ProductId = req.ProductId,
                ExpectedQty = req.ExpectedQty,
                Notes = req.Notes?.Trim()
            });
        }

        await _db.SaveChangesAsync(ct);
        return Ok(await MapBatchAsync(id, ct));
    }

    [HttpPut("{id:guid}/lines/{lineId:guid}")]
    public async Task<ActionResult<ConsignmentBatchDto>> UpdateLine(Guid id, Guid lineId, [FromBody] UpdateBatchLineRequest req, CancellationToken ct)
    {
        var batch = await _db.ConsignmentBatches.Include(b => b.Lines).FirstOrDefaultAsync(b => b.Id == id, ct);
        if (batch == null) return NotFound(new { error = "Batch not found." });
        if (batch.Status == ConsignmentBatchStatus.Committed)
            return BadRequest(new { error = "Batch is already committed." });

        var line = batch.Lines.FirstOrDefault(l => l.Id == lineId);
        if (line == null) return NotFound(new { error = "Line not found." });

        if (req.CheckedQty.HasValue) line.CheckedQty = req.CheckedQty.Value;
        if (req.ExpectedQty.HasValue) line.ExpectedQty = req.ExpectedQty.Value;
        if (req.Notes != null) line.Notes = req.Notes.Trim();

        await _db.SaveChangesAsync(ct);
        return Ok(await MapBatchAsync(id, ct));
    }

    [HttpDelete("{id:guid}/lines/{lineId:guid}")]
    public async Task<ActionResult<ConsignmentBatchDto>> RemoveLine(Guid id, Guid lineId, CancellationToken ct)
    {
        var batch = await _db.ConsignmentBatches.Include(b => b.Lines).FirstOrDefaultAsync(b => b.Id == id, ct);
        if (batch == null) return NotFound(new { error = "Batch not found." });
        if (batch.Status == ConsignmentBatchStatus.Committed)
            return BadRequest(new { error = "Batch is already committed." });

        var line = batch.Lines.FirstOrDefault(l => l.Id == lineId);
        if (line == null) return NotFound(new { error = "Line not found." });

        batch.Lines.Remove(line);
        await _db.SaveChangesAsync(ct);
        return Ok(await MapBatchAsync(id, ct));
    }

    [HttpPost("{id:guid}/scan")]
    public async Task<ActionResult<ConsignmentBatchDto>> Scan(Guid id, [FromBody] ScanBatchRequest req, CancellationToken ct)
    {
        var batch = await _db.ConsignmentBatches.Include(b => b.Lines).FirstOrDefaultAsync(b => b.Id == id, ct);
        if (batch == null) return NotFound(new { error = "Batch not found." });
        if (batch.Status == ConsignmentBatchStatus.Committed)
            return BadRequest(new { error = "Batch is already committed." });

        var barcode = req.Barcode.Trim();
        var product = await _db.Products.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Barcode == barcode || p.Sku == barcode, ct);
        if (product == null)
            return BadRequest(new { error = $"No product found for barcode '{barcode}'." });

        var line = batch.Lines.FirstOrDefault(l => l.ProductId == product.Id);
        if (line != null)
        {
            line.CheckedQty += req.Qty;
        }
        else
        {
            batch.Lines.Add(new ConsignmentBatchLine
            {
                Id = Guid.NewGuid(),
                BatchId = id,
                ProductId = product.Id,
                ExpectedQty = 0,
                CheckedQty = req.Qty
            });
        }

        await _db.SaveChangesAsync(ct);
        return Ok(await MapBatchAsync(id, ct));
    }

    [HttpPost("{id:guid}/import")]
    [RequestSizeLimit(50_000_000)]
    public async Task<ActionResult<ConsignmentBatchDto>> Import(Guid id, [FromForm] IFormFile file, CancellationToken ct)
    {
        var batch = await _db.ConsignmentBatches.Include(b => b.Lines).FirstOrDefaultAsync(b => b.Id == id, ct);
        if (batch == null) return NotFound(new { error = "Batch not found." });
        if (batch.Status == ConsignmentBatchStatus.Committed)
            return BadRequest(new { error = "Batch is already committed." });
        if (file == null || file.Length == 0)
            return BadRequest(new { error = "File required." });

        using var reader = new StreamReader(file.OpenReadStream());
        var header = await reader.ReadLineAsync(ct);
        if (header == null) return BadRequest(new { error = "Empty file." });

        var cols = header.Split(',').Select(c => c.Trim().ToUpperInvariant()).ToList();
        var skuIdx = cols.FindIndex(c => c is "SKU" or "ITEM CODE" or "CODE" or "PRODUCT CODE");
        var qtyIdx = cols.FindIndex(c => c is "QTY" or "QUANTITY" or "COUNT");

        if (skuIdx < 0) return BadRequest(new { error = "CSV must have a SKU/Code column." });
        if (qtyIdx < 0) return BadRequest(new { error = "CSV must have a Qty/Quantity column." });

        var added = 0;
        var notFound = new List<string>();

        while (!reader.EndOfStream)
        {
            var row = await reader.ReadLineAsync(ct);
            if (string.IsNullOrWhiteSpace(row)) continue;

            var parts = row.Split(',');
            if (parts.Length <= Math.Max(skuIdx, qtyIdx)) continue;

            var sku = parts[skuIdx].Trim().Trim('"');
            if (!int.TryParse(parts[qtyIdx].Trim().Trim('"'), out var qty) || qty < 1) qty = 1;

            var product = await _db.Products.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Sku == sku || p.Barcode == sku, ct);

            if (product == null)
            {
                notFound.Add(sku);
                continue;
            }

            var existing = batch.Lines.FirstOrDefault(l => l.ProductId == product.Id);
            if (existing != null)
            {
                existing.ExpectedQty += qty;
            }
            else
            {
                batch.Lines.Add(new ConsignmentBatchLine
                {
                    Id = Guid.NewGuid(),
                    BatchId = id,
                    ProductId = product.Id,
                    ExpectedQty = qty
                });
                added++;
            }
        }

        await _db.SaveChangesAsync(ct);
        var dto = await MapBatchAsync(id, ct);
        return Ok(new { batch = dto, added, notFound });
    }

    [HttpPost("{id:guid}/commit")]
    public async Task<ActionResult<ConsignmentBatchDto>> Commit(Guid id, CancellationToken ct)
    {
        var batch = await _db.ConsignmentBatches
            .Include(b => b.Lines)
            .FirstOrDefaultAsync(b => b.Id == id, ct);
        if (batch == null) return NotFound(new { error = "Batch not found." });
        if (batch.Status == ConsignmentBatchStatus.Committed)
            return BadRequest(new { error = "Batch is already committed." });
        if (!batch.Lines.Any())
            return BadRequest(new { error = "Batch has no lines." });

        var receiptType = batch.Type == ConsignmentBatchType.Receive
            ? StockReceiptType.ConsignmentIn
            : StockReceiptType.ConsignmentReturn;

        foreach (var line in batch.Lines)
        {
            var qty = batch.Type == ConsignmentBatchType.Receive ? line.CheckedQty : line.ExpectedQty;
            if (qty < 1) continue;

            var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == line.ProductId, ct);
            if (product == null) continue;

            if (receiptType == StockReceiptType.ConsignmentIn)
                product.QtyConsignment += qty;
            else
                product.QtyConsignment = Math.Max(0, product.QtyConsignment - qty);

            product.UpdatedAt = DateTimeOffset.UtcNow;

            _db.StockReceipts.Add(new StockReceipt
            {
                Id = Guid.NewGuid(),
                ProductId = line.ProductId,
                SupplierId = batch.SupplierId,
                Type = receiptType,
                Quantity = qty,
                Notes = $"Batch {batch.Id:N}",
                ProcessedBy = User.Identity?.Name,
                CreatedAt = DateTimeOffset.UtcNow
            });
        }

        batch.Status = ConsignmentBatchStatus.Committed;
        batch.CommittedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        return Ok(await MapBatchAsync(id, ct));
    }

    [HttpGet("{id:guid}/pdf")]
    public async Task<IActionResult> Pdf(Guid id, CancellationToken ct)
    {
        var batch = await _db.ConsignmentBatches.AsNoTracking()
            .Include(b => b.Supplier)
            .Include(b => b.Lines).ThenInclude(l => l.Product)
            .FirstOrDefaultAsync(b => b.Id == id, ct);
        if (batch == null) return NotFound(new { error = "Batch not found." });

        var pdf = _pdf.BuildPdf(batch);
        var label = batch.Type == ConsignmentBatchType.Receive ? "receive-check" : "return-packing";
        return File(pdf, "application/pdf", $"consignment-{label}-{batch.CreatedAt:yyyyMMdd}.pdf");
    }

    [HttpGet("returnable-stock")]
    public async Task<ActionResult<List<ReturnableStockLineDto>>> ReturnableStock([FromQuery] Guid supplierId, CancellationToken ct)
    {
        if (!await _db.Suppliers.AnyAsync(s => s.Id == supplierId, ct))
            return BadRequest(new { error = "Supplier not found." });

        var receipts = await _db.StockReceipts.AsNoTracking()
            .Include(r => r.Product)
            .Where(r => r.SupplierId == supplierId)
            .ToListAsync(ct);

        var result = receipts
            .GroupBy(r => r.ProductId)
            .Select(g =>
            {
                var totalIn = g.Where(r => r.Type == StockReceiptType.ConsignmentIn).Sum(r => r.Quantity);
                var movedFromStock = g.Where(r => r.Type == StockReceiptType.StockToConsignment).Sum(r => r.Quantity);
                var totalOut = g.Where(r => r.Type is StockReceiptType.ConsignmentToStock or StockReceiptType.ConsignmentReturn).Sum(r => r.Quantity);
                var onHand = totalIn + movedFromStock - totalOut;
                var product = g.First().Product;
                return new ReturnableStockLineDto
                {
                    ProductId = g.Key,
                    Sku = product?.Sku ?? "",
                    Barcode = product?.Barcode,
                    ProductName = product?.Name ?? "",
                    OnHand = onHand
                };
            })
            .Where(x => x.OnHand > 0)
            .OrderBy(x => x.Sku)
            .ToList();

        // Also include products assigned to this supplier that have consignment qty but no receipts
        var receiptProductIds = result.Select(r => r.ProductId).ToHashSet();
        var untracked = await _db.Products.AsNoTracking()
            .Where(p => p.SupplierId == supplierId && p.QtyConsignment > 0 && !receiptProductIds.Contains(p.Id))
            .ToListAsync(ct);

        foreach (var p in untracked)
        {
            result.Add(new ReturnableStockLineDto
            {
                ProductId = p.Id,
                Sku = p.Sku,
                Barcode = p.Barcode,
                ProductName = p.Name,
                OnHand = p.QtyConsignment
            });
        }

        return result.OrderBy(x => x.Sku).ToList();
    }

    private async Task<ConsignmentBatchDto?> MapBatchAsync(Guid id, CancellationToken ct)
    {
        var batch = await _db.ConsignmentBatches.AsNoTracking()
            .Include(b => b.Supplier)
            .Include(b => b.Lines).ThenInclude(l => l.Product)
            .FirstOrDefaultAsync(b => b.Id == id, ct);
        return batch == null ? null : MapBatch(batch);
    }

    private static ConsignmentBatchDto MapBatch(ConsignmentBatch b) => new()
    {
        Id = b.Id,
        SupplierId = b.SupplierId,
        SupplierName = b.Supplier?.Name ?? "",
        Type = b.Type.ToString(),
        Status = b.Status.ToString(),
        Notes = b.Notes,
        CreatedBy = b.CreatedBy,
        CreatedAt = b.CreatedAt,
        CommittedAt = b.CommittedAt,
        Lines = b.Lines.Select(l => new ConsignmentBatchLineDto
        {
            Id = l.Id,
            ProductId = l.ProductId,
            Sku = l.Product?.Sku ?? "",
            Barcode = l.Product?.Barcode,
            ProductName = l.Product?.Name ?? "",
            ExpectedQty = l.ExpectedQty,
            CheckedQty = l.CheckedQty,
            Notes = l.Notes
        }).OrderBy(l => l.Sku).ToList()
    };
}
