using HuntexPos.Api.Data;
using HuntexPos.Api.Domain;
using HuntexPos.Api.DTOs;
using HuntexPos.Api.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HuntexPos.Api.Services;

/// <summary>
/// Business logic for customer-facing quotes / estimates. Handles creation, editing,
/// status transitions, PDF generation and conversion to invoices.
/// </summary>
public class QuoteService
{
    private readonly HuntexDbContext _db;
    private readonly QuotePdfService _pdf;
    private readonly InvoiceService _invoices;
    private readonly IEffectiveBusinessSettings _business;
    private readonly AppOptions _app;

    public QuoteService(
        HuntexDbContext db,
        QuotePdfService pdf,
        InvoiceService invoices,
        IEffectiveBusinessSettings business,
        IOptions<AppOptions> app)
    {
        _db = db;
        _pdf = pdf;
        _invoices = invoices;
        _business = business;
        _app = app.Value;
    }

    public async Task<QuoteDto> CreateAsync(CreateQuoteRequest req, string? userId, CancellationToken ct)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var taxRate = req.TaxRate ?? 15m;

        var quote = new Quote
        {
            Id = Guid.NewGuid(),
            QuoteNumber = await NextQuoteNumberAsync(ct),
            Status = QuoteStatus.Draft,
            CustomerId = req.CustomerId,
            CustomerName = req.CustomerName,
            CustomerEmail = req.CustomerEmail,
            CustomerPhone = req.CustomerPhone,
            CustomerCompany = req.CustomerCompany,
            CustomerAddress = req.CustomerAddress,
            CustomerVatNumber = req.CustomerVatNumber,
            PublicNotes = req.PublicNotes,
            InternalNotes = req.InternalNotes,
            ValidUntil = req.ValidUntil,
            DiscountTotal = req.DiscountTotal,
            TaxRate = taxRate,
            CreatedByUserId = userId,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await ApplyLinesAsync(quote, req.Lines, ct);
        RecomputeTotals(quote);

        _db.Quotes.Add(quote);
        await _db.SaveChangesAsync(ct);

        var pdfBytes = _pdf.BuildPdf(quote);
        quote.PdfStorageKey = await _pdf.SavePdfAsync(quote, pdfBytes, ct);
        await _db.SaveChangesAsync(ct);

        await tx.CommitAsync(ct);

        return MapToDto(quote);
    }

    public async Task<QuoteDto?> UpdateAsync(Guid id, UpdateQuoteRequest req, CancellationToken ct)
    {
        var quote = await _db.Quotes.Include(q => q.Lines).FirstOrDefaultAsync(q => q.Id == id, ct);
        if (quote == null) return null;

        if (quote.Status == QuoteStatus.Converted)
            throw new InvalidOperationException("A converted quote cannot be edited.");

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        quote.CustomerId = req.CustomerId;
        quote.CustomerName = req.CustomerName;
        quote.CustomerEmail = req.CustomerEmail;
        quote.CustomerPhone = req.CustomerPhone;
        quote.CustomerCompany = req.CustomerCompany;
        quote.CustomerAddress = req.CustomerAddress;
        quote.CustomerVatNumber = req.CustomerVatNumber;
        quote.PublicNotes = req.PublicNotes;
        quote.InternalNotes = req.InternalNotes;
        quote.ValidUntil = req.ValidUntil;
        quote.DiscountTotal = req.DiscountTotal;
        if (req.TaxRate.HasValue) quote.TaxRate = req.TaxRate.Value;

        _db.QuoteLines.RemoveRange(quote.Lines);
        quote.Lines.Clear();
        await ApplyLinesAsync(quote, req.Lines, ct);
        RecomputeTotals(quote);
        quote.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);

        // regenerate PDF so stored copy stays in sync
        var pdfBytes = _pdf.BuildPdf(quote);
        quote.PdfStorageKey = await _pdf.SavePdfAsync(quote, pdfBytes, ct);
        await _db.SaveChangesAsync(ct);

        await tx.CommitAsync(ct);

        return MapToDto(quote);
    }

    public async Task<QuoteDto?> GetAsync(Guid id, CancellationToken ct)
    {
        var q = await _db.Quotes.AsNoTracking()
            .Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        return q == null ? null : MapToDto(q);
    }

    public async Task<QuoteDto?> GetByPublicTokenAsync(Guid token, CancellationToken ct)
    {
        var q = await _db.Quotes.AsNoTracking()
            .Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.PublicToken == token, ct);
        if (q == null) return null;
        var dto = MapToDto(q);
        var eff = await _business.GetAsync(ct);
        dto.CompanyContact = ReceiptCompanyContact.ToDto(eff);
        return dto;
    }

    public async Task<List<QuoteListItemDto>> ListAsync(string? status, string? search, int take, CancellationToken ct)
    {
        var q = _db.Quotes.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<QuoteStatus>(status, true, out var s))
            q = q.Where(x => x.Status == s);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var needle = search.Trim();
            q = q.Where(x =>
                x.QuoteNumber.Contains(needle) ||
                (x.CustomerName != null && x.CustomerName.Contains(needle)) ||
                (x.CustomerCompany != null && x.CustomerCompany.Contains(needle)) ||
                (x.CustomerEmail != null && x.CustomerEmail.Contains(needle)));
        }
        // SQLite cannot ORDER BY DateTimeOffset, so project first then order/take client-side.
        // The working set is bounded by status/search + hard cap — safe to materialize.
        var raw = await q.Select(x => new QuoteListItemDto
            {
                Id = x.Id,
                QuoteNumber = x.QuoteNumber,
                Status = x.Status.ToString(),
                CustomerName = x.CustomerName,
                CustomerCompany = x.CustomerCompany,
                GrandTotal = x.GrandTotal,
                CreatedAt = x.CreatedAt,
                ValidUntil = x.ValidUntil,
                ConvertedInvoiceId = x.ConvertedInvoiceId
            })
            .ToListAsync(ct);

        return raw
            .OrderByDescending(x => x.CreatedAt)
            .Take(Math.Clamp(take, 1, 500))
            .ToList();
    }

    public async Task<byte[]?> GetPdfBytesAsync(Guid id, CancellationToken ct)
    {
        var q = await _db.Quotes.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (q == null) return null;
        if (q.PdfStorageKey != null)
        {
            var path = Path.Combine(Directory.GetCurrentDirectory(), _app.PdfStoragePath, q.PdfStorageKey);
            if (File.Exists(path))
                return await File.ReadAllBytesAsync(path, ct);
        }
        var bytes = _pdf.BuildPdf(q);
        q.PdfStorageKey = await _pdf.SavePdfAsync(q, bytes, ct);
        await _db.SaveChangesAsync(ct);
        return bytes;
    }

    public async Task<byte[]?> GetPdfByPublicTokenAsync(Guid token, CancellationToken ct)
    {
        var q = await _db.Quotes.AsNoTracking().FirstOrDefaultAsync(x => x.PublicToken == token, ct);
        return q == null ? null : await GetPdfBytesAsync(q.Id, ct);
    }

    public async Task<QuoteDto?> SetStatusAsync(Guid id, string status, CancellationToken ct)
    {
        var q = await _db.Quotes.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (q == null) return null;

        if (!Enum.TryParse<QuoteStatus>(status, true, out var newStatus))
            throw new InvalidOperationException($"Unknown quote status '{status}'.");

        if (q.Status == QuoteStatus.Converted)
            throw new InvalidOperationException("A converted quote cannot change status.");
        if (newStatus == QuoteStatus.Converted)
            throw new InvalidOperationException("Use the convert-to-invoice endpoint to convert a quote.");

        q.Status = newStatus;
        q.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return MapToDto(q);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var q = await _db.Quotes.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (q == null) return;
        if (q.Status == QuoteStatus.Converted)
            throw new InvalidOperationException("A converted quote cannot be deleted.");
        _db.QuoteLines.RemoveRange(q.Lines);
        _db.Quotes.Remove(q);
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Converts an accepted quote into a real <see cref="Invoice"/>. Lines backed by real
    /// products are passed through to <see cref="InvoiceService"/>, while custom ad-hoc
    /// items have to be added manually because invoices currently require products.
    /// </summary>
    public async Task<ConvertQuoteResult> ConvertToInvoiceAsync(
        Guid id, string paymentMethod, string? userId, bool managerBypass, CancellationToken ct)
    {
        var q = await _db.Quotes.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new InvalidOperationException("Quote not found.");
        if (q.Status == QuoteStatus.Converted)
            throw new InvalidOperationException("Quote has already been converted.");

        var productLines = q.Lines.Where(l => l.ProductId.HasValue).ToList();
        var customLines = q.Lines.Where(l => !l.ProductId.HasValue).ToList();
        if (productLines.Count == 0)
            throw new InvalidOperationException(
                "This quote has no stock-linked lines; convert it by creating a manual invoice.");

        var req = new CreateInvoiceRequest
        {
            CustomerName = q.CustomerName,
            CustomerEmail = q.CustomerEmail,
            CustomerCompany = q.CustomerCompany,
            CustomerAddress = q.CustomerAddress,
            CustomerVatNumber = q.CustomerVatNumber,
            PaymentMethod = paymentMethod,
            DiscountTotal = q.DiscountTotal,
            SendEmail = false,
            Lines = productLines.Select(l => new CreateInvoiceLineRequest
            {
                ProductId = l.ProductId!.Value,
                Quantity = l.Quantity,
                UnitPriceOverride = l.UnitPrice,
                OriginalUnitPrice = l.UnitPrice,
                LineDiscount = l.DiscountAmount ?? 0m
            }).ToList()
        };

        var invoice = await _invoices.CreateAsync(req, userId ?? q.CreatedByUserId ?? "system", managerBypass, ct);

        q.Status = QuoteStatus.Converted;
        q.ConvertedInvoiceId = invoice.Id;
        q.ConvertedAt = DateTimeOffset.UtcNow;
        q.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        return new ConvertQuoteResult(MapToDto(q), invoice,
            customLines.Count > 0
                ? $"{customLines.Count} custom line(s) were not added to the invoice and must be handled manually."
                : null);
    }

    private async Task ApplyLinesAsync(Quote quote, List<CreateQuoteLineRequest> lines, CancellationToken ct)
    {
        var productIds = lines.Where(l => l.ProductId.HasValue).Select(l => l.ProductId!.Value).Distinct().ToList();
        var products = productIds.Count == 0
            ? new Dictionary<Guid, Product>()
            : await _db.Products.AsNoTracking()
                .Where(p => productIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, ct);

        var sort = 0;
        foreach (var l in lines)
        {
            Product? prod = null;
            if (l.ProductId.HasValue) products.TryGetValue(l.ProductId.Value, out prod);

            var unit = l.UnitPrice;
            var qty = Math.Max(1, l.Quantity);
            var gross = unit * qty;
            var discAmt = l.DiscountAmount ??
                          (l.DiscountPercent.HasValue
                              ? PricingCalculator.Round2(gross * (l.DiscountPercent.Value / 100m))
                              : 0m);
            var lineTotal = PricingCalculator.Round2(gross - discAmt);
            if (lineTotal < 0) lineTotal = 0;

            quote.Lines.Add(new QuoteLine
            {
                Id = Guid.NewGuid(),
                QuoteId = quote.Id,
                ProductId = l.ProductId,
                Sku = l.Sku ?? prod?.Sku,
                ItemName = string.IsNullOrWhiteSpace(l.ItemName) ? (prod?.Name ?? "") : l.ItemName,
                Description = l.Description,
                Quantity = qty,
                UnitCost = l.UnitCost ?? (prod != null ? Math.Round(prod.Cost * (1 - prod.SupplierDiscountPercent / 100m), 2) : (decimal?)null),
                UnitPrice = unit,
                DiscountPercent = l.DiscountPercent,
                DiscountAmount = discAmt == 0 ? (decimal?)null : discAmt,
                TaxRate = quote.TaxRate,
                LineTotal = lineTotal,
                SortOrder = l.SortOrder == 0 ? sort : l.SortOrder
            });
            sort++;
        }
    }

    private static void RecomputeTotals(Quote quote)
    {
        var sub = quote.Lines.Sum(l => l.LineTotal);
        var afterDisc = Math.Max(0, sub - quote.DiscountTotal);
        var taxAmount = quote.TaxRate > 0
            ? PricingCalculator.Round2(afterDisc - afterDisc / (1 + quote.TaxRate / 100m))
            : 0m;
        quote.SubTotal = PricingCalculator.Round2(sub);
        quote.TaxAmount = taxAmount;
        quote.GrandTotal = PricingCalculator.Round2(afterDisc);
    }

    private async Task<string> NextQuoteNumberAsync(CancellationToken ct)
    {
        var day = DateTime.UtcNow.ToString("yyyyMMdd");
        var prefix = $"Q-{day}-";
        var last = await _db.Quotes
            .Where(q => q.QuoteNumber.StartsWith(prefix))
            .OrderByDescending(q => q.QuoteNumber)
            .Select(q => q.QuoteNumber)
            .FirstOrDefaultAsync(ct);
        var next = 1;
        if (last != null && last.Length > prefix.Length && int.TryParse(last[prefix.Length..], out var n))
            next = n + 1;
        return $"{prefix}{next:D4}";
    }

    private QuoteDto MapToDto(Quote q)
    {
        var pdfUrl = q.PdfStorageKey != null ? $"/api/quotes/{q.Id}/pdf" : null;
        var publicUrl = $"{_app.PublicBaseUrl.TrimEnd('/')}/#/quote/{q.PublicToken:N}";
        return new QuoteDto
        {
            Id = q.Id,
            QuoteNumber = q.QuoteNumber,
            Status = q.Status.ToString(),
            CustomerId = q.CustomerId,
            CustomerName = q.CustomerName,
            CustomerEmail = q.CustomerEmail,
            CustomerPhone = q.CustomerPhone,
            CustomerCompany = q.CustomerCompany,
            CustomerAddress = q.CustomerAddress,
            CustomerVatNumber = q.CustomerVatNumber,
            SubTotal = q.SubTotal,
            DiscountTotal = q.DiscountTotal,
            TaxRate = q.TaxRate,
            TaxAmount = q.TaxAmount,
            GrandTotal = q.GrandTotal,
            PublicNotes = q.PublicNotes,
            InternalNotes = q.InternalNotes,
            ValidUntil = q.ValidUntil,
            PublicToken = q.PublicToken,
            PdfUrl = pdfUrl,
            PublicUrl = publicUrl,
            ConvertedInvoiceId = q.ConvertedInvoiceId,
            ConvertedAt = q.ConvertedAt,
            CreatedByUserId = q.CreatedByUserId,
            CreatedAt = q.CreatedAt,
            UpdatedAt = q.UpdatedAt,
            Lines = q.Lines.OrderBy(l => l.SortOrder).Select(l => new QuoteLineDto
            {
                Id = l.Id,
                ProductId = l.ProductId,
                Sku = l.Sku,
                ItemName = l.ItemName,
                Description = l.Description,
                Quantity = l.Quantity,
                UnitCost = l.UnitCost,
                UnitPrice = l.UnitPrice,
                DiscountPercent = l.DiscountPercent,
                DiscountAmount = l.DiscountAmount,
                TaxRate = l.TaxRate,
                LineTotal = l.LineTotal,
                SortOrder = l.SortOrder
            }).ToList()
        };
    }
}

public record ConvertQuoteResult(QuoteDto Quote, InvoiceDto Invoice, string? Warning);
