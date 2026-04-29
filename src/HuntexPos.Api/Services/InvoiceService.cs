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
    private readonly IEffectiveMailgunProvider _mailgun;
    private readonly IEffectiveBusinessSettings _business;
    private readonly PosRulesOptions _posRules;

    public InvoiceService(
        HuntexDbContext db,
        InvoicePdfService pdf,
        IEmailSender email,
        IOptions<AppOptions> app,
        IEffectiveMailgunProvider mailgun,
        IEffectiveBusinessSettings business,
        IOptions<PosRulesOptions> posRules)
    {
        _db = db;
        _pdf = pdf;
        _email = email;
        _app = app.Value;
        _mailgun = mailgun;
        _business = business;
        _posRules = posRules.Value;
    }

    public async Task<InvoiceDto> CreateAsync(CreateInvoiceRequest req, string userId, bool managerBypassPosRules, CancellationToken ct)
    {
        const decimal taxRate = 15m;

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        // Phase 3B.2: validate Account-method preconditions before any stock or pricing work.
        var isAccountSale = string.Equals(req.PaymentMethod, "Account", StringComparison.OrdinalIgnoreCase);
        Customer? accountCustomer = null;
        if (isAccountSale)
        {
            var eff = await _business.GetAsync(ct);
            if (!eff.AccountsEnabled)
                throw new InvalidOperationException("Account sales are not enabled. Switch on Accounts in Business Settings first.");

            if (!req.CustomerId.HasValue)
                throw new InvalidOperationException("Select a customer before charging to account.");

            accountCustomer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == req.CustomerId.Value, ct);
            if (accountCustomer == null)
                throw new InvalidOperationException("Customer not found.");
            if (!accountCustomer.AccountEnabled)
                throw new InvalidOperationException($"{accountCustomer.Name ?? accountCustomer.Email} is not set up for account sales.");
        }

        var productIds = req.Lines.Select(l => l.ProductId).Distinct().ToList();
        var products = await _db.Products.Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);

        // Resolve active-promotion effective prices so Sales staff can ring up
        // promo-discounted items without tripping the PosRules price-drift check.
        Dictionary<Guid, decimal>? promoEffectivePrices = null;
        if (!managerBypassPosRules)
        {
            var now = DateTimeOffset.UtcNow;
            var allActivePromos = await _db.Promotions.AsNoTracking()
                .Where(p => p.IsActive).ToListAsync(ct);
            var promo = allActivePromos
                .Where(p => !p.StartsAt.HasValue || p.StartsAt <= now)
                .Where(p => !p.EndsAt.HasValue || p.EndsAt >= now)
                .FirstOrDefault();

            if (promo != null)
            {
                promoEffectivePrices = new Dictionary<Guid, decimal>();

                var specials = await _db.ProductSpecials.AsNoTracking()
                    .Where(s => s.IsActive && (s.PromotionId == null || s.PromotionId == promo.Id))
                    .ToListAsync(ct);
                var specialsByProduct = specials
                    .GroupBy(s => s.ProductId)
                    .ToDictionary(g => g.Key,
                        g => g.OrderByDescending(s => s.PromotionId.HasValue).First());

                foreach (var pid in productIds)
                {
                    if (!products.TryGetValue(pid, out var prod)) continue;
                    if (specialsByProduct.TryGetValue(pid, out var special))
                    {
                        if (special.SpecialPrice.HasValue)
                            promoEffectivePrices[pid] = special.SpecialPrice.Value;
                        else if (special.DiscountPercent.HasValue)
                            promoEffectivePrices[pid] = PricingCalculator.RoundToR10(
                                prod.SellPrice * (1 - special.DiscountPercent.Value / 100m));
                    }
                    else if (promo.DiscountPercent > 0)
                    {
                        promoEffectivePrices[pid] = PricingCalculator.RoundToR10(
                            prod.SellPrice * (1 - promo.DiscountPercent / 100m));
                    }
                }
            }
        }

        var lines = new List<InvoiceLine>();
        decimal subTotal = 0;
        bool isSpecialOrder = false;

        foreach (var l in req.Lines)
        {
            if (!products.TryGetValue(l.ProductId, out var p))
                throw new InvalidOperationException($"Unknown product {l.ProductId}");
            if (p.QtyOnHand < l.Quantity)
            {
                if (!managerBypassPosRules)
                    throw new InvalidOperationException($"Insufficient stock for {p.Name} (have {p.QtyOnHand})");
                isSpecialOrder = true;
            }

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
                    var list = promoEffectivePrices != null && promoEffectivePrices.TryGetValue(p.Id, out var promoPrice)
                        ? promoPrice
                        : p.SellPrice;
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
                OriginalUnitPrice = l.OriginalUnitPrice > 0 ? l.OriginalUnitPrice : p.SellPrice,
                LineDiscount = l.LineDiscount,
                LineTotal = lineTotal,
                CostAtSale = Math.Round(p.Cost * (1 - p.SupplierDiscountPercent / 100m), 2)
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

        var afterDiscount = Math.Max(0, subTotal - req.DiscountTotal);
        // Prices are VAT-inclusive; extract the VAT portion
        var taxAmount = PricingCalculator.Round2(afterDiscount - afterDiscount / (1 + taxRate / 100m));
        var grandTotal = afterDiscount;

        if (!managerBypassPosRules && _posRules.BlockZeroOrNegativeTotal && grandTotal <= 0)
            throw new InvalidOperationException("Sale total must be greater than zero.");

        var invoice = new Invoice
        {
            Id = Guid.NewGuid(),
            InvoiceNumber = await NextInvoiceNumberAsync(ct),
            Status = InvoiceStatus.Final,
            CustomerId = req.CustomerId,
            CustomerName = req.CustomerName,
            CustomerEmail = req.CustomerEmail,
            CustomerType = req.CustomerType,
            CustomerCompany = req.CustomerCompany,
            CustomerAddress = req.CustomerAddress,
            CustomerVatNumber = req.CustomerVatNumber,
            PaymentMethod = req.PaymentMethod,
            SubTotal = subTotal,
            TaxRate = taxRate,
            TaxAmount = taxAmount,
            DiscountTotal = req.DiscountTotal,
            GrandTotal = grandTotal,
            PromotionName = req.PromotionName,
            CreatedByUserId = userId,
            IsSpecialOrder = isSpecialOrder,
            Lines = lines
        };

        // Phase 3B.2: AR posture. Account sales are unpaid until 3B.3 receipts arrive.
        // Cash/Card/EFT invoices remain fully paid and continue to behave exactly as before.
        if (isAccountSale && accountCustomer != null)
        {
            invoice.IsAccountSale = true;
            invoice.AmountPaid = 0m;
            invoice.PaymentStatus = InvoicePaymentStatus.Unpaid;
            invoice.DueDate = DateTimeOffset.UtcNow.AddDays(Math.Max(0, accountCustomer.PaymentTermsDays));
        }
        else
        {
            invoice.IsAccountSale = false;
            invoice.AmountPaid = grandTotal;
            invoice.PaymentStatus = InvoicePaymentStatus.Paid;
            invoice.DueDate = null;
        }

        _db.Invoices.Add(invoice);
        await _db.SaveChangesAsync(ct);

        await UpsertCustomerAsync(req, ct);

        var pdfBytes = _pdf.BuildPdf(invoice);
        var key = await _pdf.SavePdfAsync(invoice, pdfBytes, ct);
        invoice.PdfStorageKey = key;
        await _db.SaveChangesAsync(ct);

        await tx.CommitAsync(ct);

        string? emailWarning = null;
        if (req.SendEmail && !string.IsNullOrWhiteSpace(req.CustomerEmail))
        {
            var mailOpt = await _mailgun.GetAsync(ct);
            if (string.IsNullOrWhiteSpace(mailOpt.ApiKey) || string.IsNullOrWhiteSpace(mailOpt.Domain))
            {
                emailWarning = "Email not sent — Mailgun is not configured. Set up email in Settings → Email.";
            }
            else
            {
                var eff = await _business.GetAsync(ct);
                var viewUrl = $"{_app.PublicBaseUrl.TrimEnd('/')}/#/invoice/{invoice.PublicToken.ToString("N")}";
                var shopName = string.IsNullOrWhiteSpace(eff.BusinessName) ? "Our Shop" : eff.BusinessName;
                var footer = ReceiptCompanyContact.ToEmailHtmlFooter(eff);
                var specialNote = isSpecialOrder
                    ? $"<p><strong>This is a special order.</strong> Items will be delivered once available. Your payment secures {System.Net.WebUtility.HtmlEncode(shopName)} pricing.</p>"
                    : "";
                var html = $"""
                            <p>Thank you for your purchase at {System.Net.WebUtility.HtmlEncode(shopName)}.</p>
                            <p>Invoice <strong>{invoice.InvoiceNumber}</strong> — Total <strong>R{invoice.GrandTotal:F2}</strong></p>
                            {specialNote}
                            <p><a href="{viewUrl}">View or print your invoice</a></p>
                            {footer}
                            """;
                var subject = isSpecialOrder
                    ? $"Order Confirmation & Invoice {invoice.InvoiceNumber}"
                    : $"Invoice {invoice.InvoiceNumber}";
                try
                {
                    await _email.SendInvoiceEmailAsync(
                        req.CustomerEmail.Trim(),
                        subject,
                        html,
                        mailOpt.AttachPdf ? pdfBytes : null,
                        mailOpt.AttachPdf ? $"{invoice.InvoiceNumber}.pdf" : null,
                        ct);
                }
                catch (Exception ex)
                {
                    emailWarning = $"Invoice saved but email failed: {ex.Message}";
                }
            }
        }

        var dto = MapToDto(invoice, pdfBytes);
        dto.EmailWarning = emailWarning;

        var belowCostNames = new List<string>();
        var totalCostInclVat = 0m;
        foreach (var line in lines)
        {
            if (!products.TryGetValue(line.ProductId, out var prod) || prod == null) continue;
            var costIncl = Math.Round(prod.Cost * 1.15m, 2);
            totalCostInclVat += costIncl * line.Quantity;
            if (line.LineTotal < costIncl * line.Quantity)
                belowCostNames.Add(prod.Name);
        }
        if (belowCostNames.Count > 0)
            dto.BelowCostWarning = $"Below cost (incl VAT): {string.Join(", ", belowCostNames)}";
        else if (grandTotal < totalCostInclVat && totalCostInclVat > 0)
            dto.BelowCostWarning = $"Sale total R{grandTotal:0.00} is below total cost incl VAT R{totalCostInclVat:0.00}";

        // Phase 3B.2: soft credit-limit warning (non-blocking).
        if (isAccountSale && accountCustomer != null && accountCustomer.CreditLimit > 0m)
        {
            // Outstanding = sum of (GrandTotal - AmountPaid) across this customer's unpaid/partial/overdue invoices.
            // The invoice we just inserted is included in this sum because we already SaveChanges'd it above.
            var outstanding = await _db.Invoices.AsNoTracking()
                .Where(i => i.CustomerId == accountCustomer.Id
                            && i.PaymentStatus != InvoicePaymentStatus.Paid
                            && i.PaymentStatus != InvoicePaymentStatus.WrittenOff
                            && i.Status != InvoiceStatus.Voided)
                .Select(i => i.GrandTotal - i.AmountPaid)
                .SumAsync(ct);

            if (outstanding > accountCustomer.CreditLimit)
            {
                dto.AccountWarning = $"Customer is now over their credit limit (balance R{outstanding:0.00} of R{accountCustomer.CreditLimit:0.00}).";
            }
        }

        return dto;
    }

    private async Task<InvoiceDto> MapToDtoAsync(Invoice inv, byte[]? pdfBytes, bool includeCompanyContact, CancellationToken ct)
    {
        var dto = MapToDto(inv, pdfBytes, includeCompanyContact: false);
        if (includeCompanyContact)
        {
            var eff = await _business.GetAsync(ct);
            dto.CompanyContact = ReceiptCompanyContact.ToDto(eff);
        }
        return dto;
    }

    private InvoiceDto MapToDto(Invoice inv, byte[]? pdfBytes, bool includeCompanyContact = false)
    {
        var pdfUrl = inv.PdfStorageKey != null
            ? $"/api/invoices/{inv.Id}/pdf"
            : null;
        return new InvoiceDto
        {
            Id = inv.Id,
            InvoiceNumber = inv.InvoiceNumber,
            Status = inv.Status.ToString(),
            CustomerId = inv.CustomerId,
            CustomerName = inv.CustomerName,
            CustomerEmail = inv.CustomerEmail,
            CustomerType = inv.CustomerType,
            CustomerCompany = inv.CustomerCompany,
            CustomerAddress = inv.CustomerAddress,
            CustomerVatNumber = inv.CustomerVatNumber,
            PaymentMethod = inv.PaymentMethod,
            SubTotal = inv.SubTotal,
            TaxRate = inv.TaxRate,
            TaxAmount = inv.TaxAmount,
            DiscountTotal = inv.DiscountTotal,
            GrandTotal = inv.GrandTotal,
            PromotionName = inv.PromotionName,
            PublicToken = inv.PublicToken,
            PdfUrl = pdfUrl,
            CreatedAt = inv.CreatedAt,
            IsSpecialOrder = inv.IsSpecialOrder,
            IsDelivered = inv.IsDelivered,
            DeliveredAt = inv.DeliveredAt,
            DeliveryNotes = inv.DeliveryNotes,
            IsAccountSale = inv.IsAccountSale,
            AmountPaid = inv.AmountPaid,
            PaymentStatus = inv.PaymentStatus.ToString(),
            DueDate = inv.DueDate,
            Lines = inv.Lines.Select(l => new InvoiceLineDto
            {
                ProductId = l.ProductId,
                Description = l.Description,
                Quantity = l.Quantity,
                UnitPrice = l.UnitPrice,
                OriginalUnitPrice = l.OriginalUnitPrice,
                LineDiscount = l.LineDiscount,
                LineTotal = l.LineTotal
            }).ToList(),
            CompanyContact = null
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
        return inv == null ? null : await MapToDtoAsync(inv, null, includeCompanyContact: true, ct);
    }

    public async Task<byte[]?> GetPdfByPublicTokenAsync(Guid token, CancellationToken ct)
    {
        var inv = await _db.Invoices.Include(i => i.Lines).FirstOrDefaultAsync(i => i.PublicToken == token, ct);
        if (inv == null) return null;
        return await GetPdfBytesAsync(inv.Id, ct);
    }

    public async Task VoidAsync(Guid id, string reason, string? userId, CancellationToken ct)
    {
        var inv = await _db.Invoices.Include(i => i.Lines).FirstOrDefaultAsync(i => i.Id == id, ct)
                  ?? throw new InvalidOperationException("Invoice not found");
        if (inv.Status == InvoiceStatus.Voided) return;

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        inv.Status = InvoiceStatus.Voided;
        inv.VoidReason = reason;
        inv.VoidedAt = DateTimeOffset.UtcNow;
        inv.VoidedByUserId = userId;

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

    private async Task UpsertCustomerAsync(CreateInvoiceRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.CustomerEmail)) return;
        var email = req.CustomerEmail.Trim().ToLower();
        try
        {
            var existing = await _db.Customers.FirstOrDefaultAsync(c => c.Email == email, ct);
            if (existing != null)
            {
                if (!string.IsNullOrWhiteSpace(req.CustomerName)) existing.Name = req.CustomerName;
                if (!string.IsNullOrWhiteSpace(req.CustomerCompany)) existing.Company = req.CustomerCompany;
                if (!string.IsNullOrWhiteSpace(req.CustomerAddress)) existing.Address = req.CustomerAddress;
                if (!string.IsNullOrWhiteSpace(req.CustomerVatNumber)) existing.VatNumber = req.CustomerVatNumber;
                if (!string.IsNullOrWhiteSpace(req.CustomerType)) existing.CustomerType = req.CustomerType;
                existing.UpdatedAt = DateTimeOffset.UtcNow;
            }
            else
            {
                _db.Customers.Add(new Customer
                {
                    Id = Guid.NewGuid(),
                    Email = email,
                    Name = req.CustomerName,
                    Company = req.CustomerCompany,
                    Address = req.CustomerAddress,
                    VatNumber = req.CustomerVatNumber,
                    CustomerType = req.CustomerType
                });
            }
            await _db.SaveChangesAsync(ct);
        }
        catch { /* non-critical — don't fail the invoice */ }
    }
}
