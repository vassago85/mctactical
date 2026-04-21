using HuntexPos.Api.Data;
using HuntexPos.Api.Domain;
using HuntexPos.Api.DTOs;
using HuntexPos.Api.Options;
using HuntexPos.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HuntexPos.Api.Controllers;

[ApiController]
[Route("api/consignment-batches")]
[Authorize(Roles = $"{Roles.Admin},{Roles.Owner},{Roles.Dev}")]
public class ConsignmentBatchesController : ControllerBase
{
    private readonly HuntexDbContext _db;
    private readonly ConsignmentPdfService _pdf;
    private readonly SupplierInvoicePdfParser _invoicePdfParser;
    private readonly AppOptions _app;

    public ConsignmentBatchesController(
        HuntexDbContext db,
        ConsignmentPdfService pdf,
        SupplierInvoicePdfParser invoicePdfParser,
        IOptions<AppOptions> app)
    {
        _db = db;
        _pdf = pdf;
        _invoicePdfParser = invoicePdfParser;
        _app = app.Value;
    }

    [HttpPost]
    public async Task<ActionResult<ConsignmentBatchDto>> Create([FromBody] CreateConsignmentBatchRequest req, CancellationToken ct)
    {
        if (!Enum.TryParse<ConsignmentBatchType>(req.Type, true, out var type))
            return BadRequest(new { error = "Type must be 'Receive', 'Return', or 'OwnedReceive'." });

        if (!await _db.Suppliers.AnyAsync(s => s.Id == req.SupplierId, ct))
            return BadRequest(new { error = "Supplier not found." });

        var batch = new ConsignmentBatch
        {
            Id = Guid.NewGuid(),
            SupplierId = req.SupplierId,
            Type = type,
            Status = ConsignmentBatchStatus.Draft,
            Notes = req.Notes?.Trim(),
            SourceDocumentRef = string.IsNullOrWhiteSpace(req.SourceDocumentRef) ? null : req.SourceDocumentRef.Trim(),
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
            if (req.UnitCost.HasValue) existing.UnitCost = req.UnitCost;
        }
        else
        {
            _db.ConsignmentBatchLines.Add(new ConsignmentBatchLine
            {
                Id = Guid.NewGuid(),
                BatchId = id,
                ProductId = req.ProductId,
                ExpectedQty = req.ExpectedQty,
                Notes = req.Notes?.Trim(),
                UnitCost = req.UnitCost
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
        if (req.UnitCost.HasValue) line.UnitCost = req.UnitCost;

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
            return NotFound(new { error = $"No product found for barcode '{barcode}'.", barcode });

        var line = batch.Lines.FirstOrDefault(l => l.ProductId == product.Id);
        if (line != null)
        {
            line.CheckedQty += req.Qty;
            if (req.UnitCost.HasValue) line.UnitCost = req.UnitCost;
        }
        else
        {
            _db.ConsignmentBatchLines.Add(new ConsignmentBatchLine
            {
                Id = Guid.NewGuid(),
                BatchId = id,
                ProductId = product.Id,
                ExpectedQty = 0,
                CheckedQty = req.Qty,
                UnitCost = req.UnitCost
            });
        }

        await _db.SaveChangesAsync(ct);
        return Ok(await MapBatchAsync(id, ct));
    }

    [HttpPost("{id:guid}/lines-inline-create")]
    public async Task<ActionResult<ConsignmentBatchDto>> InlineCreate(Guid id, [FromBody] InlineCreateProductRequest req, CancellationToken ct)
    {
        var batch = await _db.ConsignmentBatches.Include(b => b.Lines).FirstOrDefaultAsync(b => b.Id == id, ct);
        if (batch == null) return NotFound(new { error = "Batch not found." });
        if (batch.Status == ConsignmentBatchStatus.Committed)
            return BadRequest(new { error = "Batch is already committed." });

        var sku = req.Sku.Trim();
        var name = req.Name.Trim();
        var barcode = string.IsNullOrWhiteSpace(req.Barcode) ? sku : req.Barcode.Trim();

        var existing = await _db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Sku == sku, ct);
        if (existing != null)
            return BadRequest(new { error = $"A product with SKU '{sku}' already exists.", existingProductId = existing.Id });

        var product = new Product
        {
            Id = Guid.NewGuid(),
            Sku = sku,
            Name = name,
            Barcode = barcode,
            SupplierId = batch.SupplierId,
            Cost = req.UnitCost ?? 0m,
            SellPrice = 0m,
            QtyOnHand = 0,
            QtyConsignment = 0,
            Active = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _db.Products.Add(product);

        _db.ConsignmentBatchLines.Add(new ConsignmentBatchLine
        {
            Id = Guid.NewGuid(),
            BatchId = id,
            ProductId = product.Id,
            ExpectedQty = req.Qty,
            CheckedQty = batch.Type == ConsignmentBatchType.OwnedReceive || batch.Type == ConsignmentBatchType.Receive ? req.Qty : 0,
            UnitCost = req.UnitCost
        });

        await _db.SaveChangesAsync(ct);
        return Ok(await MapBatchAsync(id, ct));
    }

    [HttpPost("{id:guid}/import")]
    [RequestSizeLimit(50_000_000)]
    public async Task<ActionResult<object>> Import(Guid id, [FromForm] IFormFile file, CancellationToken ct)
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
        var costIdx = cols.FindIndex(c => c is "COST" or "UNIT COST" or "PRICE" or "UNIT PRICE");

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

            decimal? unitCost = null;
            if (costIdx >= 0 && costIdx < parts.Length)
            {
                var raw = parts[costIdx].Trim().Trim('"').Replace(" ", "").Replace(",", "");
                if (decimal.TryParse(raw, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var c) && c >= 0)
                    unitCost = c;
            }

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
                if (unitCost.HasValue) existing.UnitCost = unitCost;
            }
            else
            {
                _db.ConsignmentBatchLines.Add(new ConsignmentBatchLine
                {
                    Id = Guid.NewGuid(),
                    BatchId = id,
                    ProductId = product.Id,
                    ExpectedQty = qty,
                    UnitCost = unitCost
                });
                added++;
            }
        }

        await _db.SaveChangesAsync(ct);
        var dto = await MapBatchAsync(id, ct);
        return Ok(new { batch = dto, added, notFound });
    }

    [HttpPost("{id:guid}/import-pdf")]
    [RequestSizeLimit(50_000_000)]
    public async Task<ActionResult<PdfImportResultDto>> ImportPdf(Guid id, [FromForm] IFormFile file, CancellationToken ct)
    {
        var batch = await _db.ConsignmentBatches.Include(b => b.Lines).FirstOrDefaultAsync(b => b.Id == id, ct);
        if (batch == null) return NotFound(new { error = "Batch not found." });
        if (batch.Status == ConsignmentBatchStatus.Committed)
            return BadRequest(new { error = "Batch is already committed." });
        if (file == null || file.Length == 0)
            return BadRequest(new { error = "File required." });

        SupplierInvoicePdfParser.ParseResult parseResult;
        var pdfBytes = new byte[file.Length];
        await using (var ms = new MemoryStream())
        {
            await file.CopyToAsync(ms, ct);
            pdfBytes = ms.ToArray();
        }

        using (var parseStream = new MemoryStream(pdfBytes))
        {
            parseResult = _invoicePdfParser.Parse(parseStream);
        }

        var dir = Path.Combine(Directory.GetCurrentDirectory(), _app.PdfStoragePath);
        Directory.CreateDirectory(dir);
        var relative = $"batch-{batch.Id:N}.pdf";
        var absolute = Path.Combine(dir, relative);
        await System.IO.File.WriteAllBytesAsync(absolute, pdfBytes, ct);
        batch.SourceDocumentPath = relative;

        var notFound = new List<string>();
        var matched = 0;

        foreach (var parsed in parseResult.Lines)
        {
            var product = await _db.Products.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Sku == parsed.Sku || p.Barcode == parsed.Sku, ct);
            if (product == null)
            {
                notFound.Add(parsed.Sku);
                continue;
            }

            var existing = batch.Lines.FirstOrDefault(l => l.ProductId == product.Id);
            if (existing != null)
            {
                existing.ExpectedQty += parsed.Qty;
                if (parsed.UnitCost.HasValue) existing.UnitCost = parsed.UnitCost;
            }
            else
            {
                _db.ConsignmentBatchLines.Add(new ConsignmentBatchLine
                {
                    Id = Guid.NewGuid(),
                    BatchId = id,
                    ProductId = product.Id,
                    ExpectedQty = parsed.Qty,
                    UnitCost = parsed.UnitCost
                });
            }
            matched++;
        }

        await _db.SaveChangesAsync(ct);
        var dto = await MapBatchAsync(id, ct);
        return Ok(new PdfImportResultDto
        {
            Batch = dto!,
            Parsed = matched,
            NotFound = notFound,
            UnparsedLines = parseResult.UnparsedLines,
            RawText = parseResult.Lines.Count == 0 ? parseResult.RawText : null
        });
    }

    [HttpPost("{id:guid}/commit")]
    public async Task<ActionResult<ConsignmentBatchDto>> Commit(
        Guid id,
        [FromQuery] bool updateCosts,
        CancellationToken ct)
    {
        var batch = await _db.ConsignmentBatches
            .Include(b => b.Lines)
            .FirstOrDefaultAsync(b => b.Id == id, ct);
        if (batch == null) return NotFound(new { error = "Batch not found." });
        if (batch.Status == ConsignmentBatchStatus.Committed)
            return BadRequest(new { error = "Batch is already committed." });
        if (!batch.Lines.Any())
            return BadRequest(new { error = "Batch has no lines." });

        var receiptType = batch.Type switch
        {
            ConsignmentBatchType.Receive => StockReceiptType.ConsignmentIn,
            ConsignmentBatchType.Return => StockReceiptType.ConsignmentReturn,
            ConsignmentBatchType.OwnedReceive => StockReceiptType.OwnedIn,
            _ => throw new InvalidOperationException($"Unsupported batch type '{batch.Type}'.")
        };

        foreach (var line in batch.Lines)
        {
            var qty = batch.Type == ConsignmentBatchType.Return ? line.ExpectedQty : line.CheckedQty;
            if (batch.Type == ConsignmentBatchType.Return && qty == 0)
                qty = line.ExpectedQty;
            if (qty < 1) continue;

            var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == line.ProductId, ct);
            if (product == null) continue;

            switch (receiptType)
            {
                case StockReceiptType.ConsignmentIn:
                    product.QtyConsignment += qty;
                    break;
                case StockReceiptType.ConsignmentReturn:
                    product.QtyConsignment = Math.Max(0, product.QtyConsignment - qty);
                    break;
                case StockReceiptType.OwnedIn:
                    product.QtyOnHand += qty;
                    break;
            }

            if (line.UnitCost.HasValue && line.UnitCost.Value > 0 && line.UnitCost.Value != product.Cost)
            {
                line.UnitCostChanged = true;
                if (updateCosts)
                {
                    product.Cost = line.UnitCost.Value;
                }
            }

            product.UpdatedAt = DateTimeOffset.UtcNow;

            _db.StockReceipts.Add(new StockReceipt
            {
                Id = Guid.NewGuid(),
                ProductId = line.ProductId,
                SupplierId = batch.SupplierId,
                Type = receiptType,
                Quantity = qty,
                CostPrice = line.UnitCost,
                Notes = string.IsNullOrEmpty(batch.SourceDocumentRef)
                    ? $"Batch {batch.Id:N}"
                    : $"Batch {batch.Id:N} · {batch.SourceDocumentRef}",
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
        var label = batch.Type == ConsignmentBatchType.Receive ? "receive-check"
                  : batch.Type == ConsignmentBatchType.Return ? "return-packing"
                  : "owned-receive";
        return File(pdf, "application/pdf", $"batch-{label}-{batch.CreatedAt:yyyyMMdd}.pdf");
    }

    [HttpGet("{id:guid}/source-document")]
    public async Task<IActionResult> SourceDocument(Guid id, CancellationToken ct)
    {
        var batch = await _db.ConsignmentBatches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, ct);
        if (batch == null) return NotFound();
        if (string.IsNullOrEmpty(batch.SourceDocumentPath)) return NotFound(new { error = "No source document." });

        var path = Path.Combine(Directory.GetCurrentDirectory(), _app.PdfStoragePath, batch.SourceDocumentPath);
        if (!System.IO.File.Exists(path)) return NotFound();
        var bytes = await System.IO.File.ReadAllBytesAsync(path, ct);
        return File(bytes, "application/pdf", batch.SourceDocumentPath);
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
        SourceDocumentRef = b.SourceDocumentRef,
        HasSourceDocument = !string.IsNullOrEmpty(b.SourceDocumentPath),
        Lines = b.Lines.Select(l => new ConsignmentBatchLineDto
        {
            Id = l.Id,
            ProductId = l.ProductId,
            Sku = l.Product?.Sku ?? "",
            Barcode = l.Product?.Barcode,
            ProductName = l.Product?.Name ?? "",
            ExpectedQty = l.ExpectedQty,
            CheckedQty = l.CheckedQty,
            Notes = l.Notes,
            UnitCost = l.UnitCost,
            CurrentProductCost = l.Product?.Cost ?? 0m,
            UnitCostChanged = l.UnitCostChanged
        }).OrderBy(l => l.Sku).ToList()
    };
}
