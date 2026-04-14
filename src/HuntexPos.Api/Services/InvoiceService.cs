using HuntexPos.Api.Data;
using HuntexPos.Api.Domain;
using HuntexPos.Api.DTOs;
using HuntexPos.Api.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HuntexPos.Api.Services;

public class InvoiceService
{
    private readonly HuntexDbContext _db;
    private readonly InvoicePdfService _pdf;
    private readonly IEmailSender _email;
    private readonly AppOptions _app;
    private readonly MailgunOptions _mailgun;
    private readonly PosRulesOptions _posRules;

    public InvoiceService(
        HuntexDbContext db,
        InvoicePdfService pdf,
        IEmailSender email,
        IOptions<AppOptions> app,
        IOptions<MailgunOptions> mailgun,
        IOptions<PosRulesOptions> posRules)
    {
        _db = db;
        _pdf = pdf;
        _email = email;
        _app = app.Value;
        _mailgun = mailgun.Value;
        _posRules = posRules.Value;
    }

    public async Task<InvoiceDto> CreateAsync(CreateInvoiceRequest req, string userId, bool managerBypassPosRules, CancellationToken ct)
    {
        var settings = await _db.PricingSettings.AsNoTracking().FirstOrDefaultAsync(ct) ?? new PricingSettings();
        var taxRate = settings.DefaultTaxRate;

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var productIds = req.Lines.Select(l => l.ProductId).Distinct().ToList();
        var products = await _db.Products.Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);

        var lines = new List<InvoiceLine>();
        decimal subTotal = 0;

        foreach (var l in req.Lines)
        {
            if (!products.TryGetValue(l.ProductId, out var p))
                throw new InvalidOperationException($"Unknown product {l.ProductId}");
            if (p.QtyOnHand < l.Quantity)
                throw new InvalidOperationException($"Insufficient stock for {p.Name} (have {p.QtyOnHand})");

            var unit = l.UnitPriceOverride ?? p.SellPrice;
            var lineGross = unit * l.Quantity;

            if (!managerBypassPosRules)
            {
                var maxLineDisc = PricingCalculator.Round2(lineGross * (_posRules.MaxLineDiscountPercent / 100m));
                if (l.LineDiscount > maxLineDisc)
                    throw new InvalidOperationException(
                        $"Line discount for \"{p.Name}\" exceeds allowed {_posRules.MaxLineDiscountPercent}% of line total.");

                if (l.UnitPriceOverride.HasValue)
                {
                    var list = p.SellPrice;
                    var minUnit = PricingCalculator.Round2(list * (1 - _posRules.MaxPriceDecreasePercentFromList / 100m));
                    var maxUnit = PricingCalculator.Round2(list * (1 + _posRules.MaxPriceIncreasePercentFromList / 100m));
                    if (unit < minUnit || unit > maxUnit)
                        throw new InvalidOperationException(
                            $"Price for \"{p.Name}\" must stay within {_posRules.MaxPriceDecreasePercentFromList}% below and {_posRules.MaxPriceIncreasePercentFromList}% above list price for sales staff.");
                }
            }

            var lineTotal = PricingCalculator.Round2(unit * l.Quantity - l.LineDiscount);
            if (lineTotal < 0) lineTotal = 0;
            subTotal += lineTotal;

            lines.Add(new InvoiceLine
            {
                Id = Guid.NewGuid(),
                ProductId = p.Id,
                Description = p.Name,
                Quantity = l.Quantity,
                UnitPrice = unit,
                LineDiscount = l.LineDiscount,
                LineTotal = lineTotal
            });

            p.QtyOnHand -= l.Quantity;
            p.UpdatedAt = DateTimeOffset.UtcNow;
        }

        if (!managerBypassPosRules && req.DiscountTotal > 0)
        {
            var maxCart = PricingCalculator.Round2(subTotal * (_posRules.MaxCartDiscountPercent / 100m));
            if (req.DiscountTotal > maxCart)
                throw new InvalidOperationException(
                    $"Cart discount exceeds allowed {_posRules.MaxCartDiscountPercent}% of the sale subtotal for sales staff.");
        }

        subTotal -= req.DiscountTotal;
        if (subTotal < 0) subTotal = 0;
        var taxAmount = PricingCalculator.Round2(subTotal * taxRate / 100m);
        var grandTotal = subTotal + taxAmount;

        if (!managerBypassPosRules && _posRules.BlockZeroOrNegativeTotal && grandTotal <= 0)
            throw new InvalidOperationException("Sale total must be greater than zero.");

        var invoice = new Invoice
        {
            Id = Guid.NewGuid(),
            InvoiceNumber = await NextInvoiceNumberAsync(ct),
            Status = InvoiceStatus.Final,
            CustomerName = req.CustomerName,
            CustomerEmail = req.CustomerEmail,
            CustomerType = req.CustomerType,
            PaymentMethod = req.PaymentMethod,
            SubTotal = subTotal,
            TaxRate = taxRate,
            TaxAmount = taxAmount,
            DiscountTotal = req.DiscountTotal,
            GrandTotal = grandTotal,
            CreatedByUserId = userId,
            Lines = lines
        };

        _db.Invoices.Add(invoice);
        await _db.SaveChangesAsync(ct);

        var pdfBytes = _pdf.BuildPdf(invoice);
        var key = await _pdf.SavePdfAsync(invoice, pdfBytes, ct);
        invoice.PdfStorageKey = key;
        await _db.SaveChangesAsync(ct);

        await tx.CommitAsync(ct);

        if (req.SendEmail && !string.IsNullOrWhiteSpace(req.CustomerEmail))
        {
            var viewUrl = $"{_app.PublicBaseUrl.TrimEnd('/')}/#/invoice/{invoice.PublicToken.ToString("N")}";
            var html = $"""
                        <p>Thank you for your purchase at MC Tactical.</p>
                        <p>Invoice <strong>{invoice.InvoiceNumber}</strong> — Total <strong>{invoice.GrandTotal:F2}</strong></p>
                        <p><a href="{viewUrl}">View or print your invoice</a></p>
                        """;
            try
            {
                await _email.SendInvoiceEmailAsync(
                    req.CustomerEmail.Trim(),
                    $"Invoice {invoice.InvoiceNumber}",
                    html,
                    _mailgun.AttachPdf ? pdfBytes : null,
                    _mailgun.AttachPdf ? $"{invoice.InvoiceNumber}.pdf" : null,
                    ct);
            }
            catch
            {
                // logged in sender; invoice still valid
            }
        }

        return MapToDto(invoice, pdfBytes);
    }

    private InvoiceDto MapToDto(Invoice inv, byte[]? pdfBytes)
    {
        var pdfUrl = inv.PdfStorageKey != null
            ? $"/api/invoices/{inv.Id}/pdf"
            : null;
        return new InvoiceDto
        {
            Id = inv.Id,
            InvoiceNumber = inv.InvoiceNumber,
            Status = inv.Status.ToString(),
            CustomerName = inv.CustomerName,
            CustomerEmail = inv.CustomerEmail,
            CustomerType = inv.CustomerType,
            PaymentMethod = inv.PaymentMethod,
            SubTotal = inv.SubTotal,
            TaxRate = inv.TaxRate,
            TaxAmount = inv.TaxAmount,
            DiscountTotal = inv.DiscountTotal,
            GrandTotal = inv.GrandTotal,
            PublicToken = inv.PublicToken,
            PdfUrl = pdfUrl,
            CreatedAt = inv.CreatedAt,
            Lines = inv.Lines.Select(l => new InvoiceLineDto
            {
                ProductId = l.ProductId,
                Description = l.Description,
                Quantity = l.Quantity,
                UnitPrice = l.UnitPrice,
                LineDiscount = l.LineDiscount,
                LineTotal = l.LineTotal
            }).ToList()
        };
    }

    public async Task<InvoiceDto?> GetAsync(Guid id, CancellationToken ct)
    {
        var inv = await _db.Invoices.Include(i => i.Lines).FirstOrDefaultAsync(i => i.Id == id, ct);
        return inv == null ? null : MapToDto(inv, null);
    }

    public async Task<byte[]?> GetPdfBytesAsync(Guid id, CancellationToken ct)
    {
        var inv = await _db.Invoices.Include(i => i.Lines).FirstOrDefaultAsync(i => i.Id == id, ct);
        if (inv?.PdfStorageKey == null) return null;
        var path = Path.Combine(Directory.GetCurrentDirectory(), _app.PdfStoragePath, inv.PdfStorageKey);
        if (!File.Exists(path))
        {
            var bytes = _pdf.BuildPdf(inv);
            await _pdf.SavePdfAsync(inv, bytes, ct);
            return bytes;
        }
        return await File.ReadAllBytesAsync(path, ct);
    }

    public async Task<InvoiceDto?> GetByPublicTokenAsync(Guid token, CancellationToken ct)
    {
        var inv = await _db.Invoices.Include(i => i.Lines).FirstOrDefaultAsync(i => i.PublicToken == token, ct);
        return inv == null ? null : MapToDto(inv, null);
    }

    public async Task<byte[]?> GetPdfByPublicTokenAsync(Guid token, CancellationToken ct)
    {
        var inv = await _db.Invoices.Include(i => i.Lines).FirstOrDefaultAsync(i => i.PublicToken == token, ct);
        if (inv == null) return null;
        return await GetPdfBytesAsync(inv.Id, ct);
    }

    public async Task VoidAsync(Guid id, string reason, CancellationToken ct)
    {
        var inv = await _db.Invoices.Include(i => i.Lines).FirstOrDefaultAsync(i => i.Id == id, ct)
                  ?? throw new InvalidOperationException("Invoice not found");
        if (inv.Status == InvoiceStatus.Voided) return;

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        inv.Status = InvoiceStatus.Voided;
        inv.VoidReason = reason;

        foreach (var line in inv.Lines)
        {
            var p = await _db.Products.FirstOrDefaultAsync(x => x.Id == line.ProductId, ct);
            if (p != null)
            {
                p.QtyOnHand += line.Quantity;
                p.UpdatedAt = DateTimeOffset.UtcNow;
            }
        }
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private async Task<string> NextInvoiceNumberAsync(CancellationToken ct)
    {
        var day = DateTime.UtcNow.ToString("yyyyMMdd");
        var prefix = $"INV-{day}-";
        var last = await _db.Invoices
            .Where(i => i.InvoiceNumber.StartsWith(prefix))
            .OrderByDescending(i => i.InvoiceNumber)
            .Select(i => i.InvoiceNumber)
            .FirstOrDefaultAsync(ct);
        var next = 1;
        if (last != null && last.Length > prefix.Length && int.TryParse(last[prefix.Length..], out var n))
            next = n + 1;
        return $"{prefix}{next:D4}";
    }
}
