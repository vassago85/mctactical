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
    private readonly RollForItClient _rollForIt;

    public InvoiceService(
        HuntexDbContext db,
        InvoicePdfService pdf,
        IEmailSender email,
        IOptions<AppOptions> app,
        IEffectiveMailgunProvider mailgun,
        IEffectiveBusinessSettings business,
        IOptions<PosRulesOptions> posRules,
        RollForItClient rollForIt)
    {
        _db = db;
        _pdf = pdf;
        _email = email;
        _app = app.Value;
        _mailgun = mailgun;
        _business = business;
        _posRules = posRules.Value;
        _rollForIt = rollForIt;
    }

    public Task<InvoiceDto> CreateAsync(CreateInvoiceRequest req, string userId, bool managerBypassPosRules, CancellationToken ct)
        => CreateAsync(req, userId, managerBypassPosRules, ct, exchangeFromInvoiceId: null, returnCreditApplied: 0m);

    /// <summary>
    /// Extended create used by both the plain POS checkout and the exchange endpoint. When
    /// <paramref name="exchangeFromInvoiceId"/> is set the new invoice is tagged as an exchange and
    /// <paramref name="returnCreditApplied"/> is subtracted from <see cref="Invoice.GrandTotal"/> to
    /// yield <see cref="Invoice.AmountPaid"/> — the sale's ledger totals stay unchanged so GP and VAT
    /// remain correct, only the reported "cash tendered" shrinks.
    /// </summary>
    public async Task<InvoiceDto> CreateAsync(
        CreateInvoiceRequest req,
        string userId,
        bool managerBypassPosRules,
        CancellationToken ct,
        Guid? exchangeFromInvoiceId,
        decimal returnCreditApplied)
    {
        const decimal taxRate = 15m;

        Salesperson? salesperson = null;
        if (req.SalespersonId.HasValue)
        {
            salesperson = await _db.Salespeople.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == req.SalespersonId.Value, ct);
            if (salesperson == null || !salesperson.IsActive)
                throw new InvalidOperationException("The selected salesperson is no longer available. Pick another.");
        }

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

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

            // The "going price" for this line is the promotion price when one is running,
            // otherwise catalog retail. Every concession is measured against this rather
            // than against whatever the operator typed.
            var goingPrice = promoEffectivePrices != null && promoEffectivePrices.TryGetValue(p.Id, out var promoPrice)
                ? promoPrice
                : p.SellPrice;

            // A price typed below the going price is a discount, not a new price. Charge the
            // going price and book the difference as LineDiscount so the sale record shows
            // what was given away. A price above it is still a genuine override.
            var requested = l.UnitPriceOverride ?? goingPrice;
            var unit = requested < goingPrice ? goingPrice : requested;
            var priceConcession = requested < goingPrice
                ? PricingCalculator.Round2((goingPrice - requested) * l.Quantity)
                : 0m;

            var lineDiscount = PricingCalculator.Round2(l.LineDiscount + priceConcession);
            var lineGross = unit * l.Quantity;

            if (!managerBypassPosRules)
            {
                var maxLineDisc = PricingCalculator.Round2(lineGross * (_posRules.MaxLineDiscountPercent / 100m));
                if (lineDiscount > maxLineDisc)
                    throw new InvalidOperationException(
                        $"Line discount for \"{p.Name}\" exceeds allowed {_posRules.MaxLineDiscountPercent}% of line total.");

                // Floor the price the customer actually pays per unit. Holding the price box
                // and the discount box to one limit stops either being used to undercut the other.
                var effectiveUnit = l.Quantity > 0
                    ? PricingCalculator.Round2((lineGross - lineDiscount) / l.Quantity)
                    : unit;
                var minUnit = PricingCalculator.Round2(goingPrice * (1 - _posRules.MaxPriceDecreasePercentFromList / 100m));
                if (effectiveUnit < minUnit)
                    throw new InvalidOperationException(
                        $"\"{p.Name}\" cannot be discounted more than {_posRules.MaxPriceDecreasePercentFromList}% below R{goingPrice:N2} by sales staff.");

                var maxUnit = PricingCalculator.Round2(goingPrice * (1 + _posRules.MaxPriceIncreasePercentFromList / 100m));
                if (unit > maxUnit)
                    throw new InvalidOperationException(
                        $"\"{p.Name}\" cannot be priced more than {_posRules.MaxPriceIncreasePercentFromList}% above R{goingPrice:N2} by sales staff.");
            }

            // Never book away more than the line is worth, so gross − discount = paid holds
            // everywhere it is displayed (receipt, invoice PDF, sales-history search).
            if (lineDiscount > lineGross) lineDiscount = PricingCalculator.Round2(lineGross);
            var lineTotal = PricingCalculator.Round2(lineGross - lineDiscount);
            subTotal += lineTotal;

            lines.Add(new InvoiceLine
            {
                Id = Guid.NewGuid(),
                ProductId = p.Id,
                Description = p.Name,
                SkuAtSale = p.Sku,
                Quantity = l.Quantity,
                UnitPrice = unit,
                OriginalUnitPrice = l.OriginalUnitPrice > 0 ? l.OriginalUnitPrice : p.SellPrice,
                LineDiscount = lineDiscount,
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

        // Roll for It wins are server-decided; the till just carries the rollId back here.
        // We consume the held win from the client's in-memory cache (which only exists if
        // Roll for It actually said "won"), then stack the payout on top of the operator discount.
        // The cart-discount cap above does not apply to this amount — the whole point of the
        // integration is to let Sales staff apply a win they couldn't otherwise authorise.
        var rollForItPayout = 0m;
        if (req.RollForItRollId.HasValue)
        {
            if (req.RollForItPayout <= 0)
                throw new InvalidOperationException("Roll for It payout must be positive when a roll id is supplied.");

            var claimedCents = (long) Math.Round(req.RollForItPayout * 100m, 0, MidpointRounding.AwayFromZero);
            var cents = _rollForIt.ConsumeHeldWin(req.RollForItRollId.Value, claimedCents);
            rollForItPayout = PricingCalculator.Round2(cents / 100m);

            // The win can never take the sale below zero. If the cart shrinks before Pay, cap it.
            var maxWinnable = Math.Max(0, subTotal - req.DiscountTotal);
            if (rollForItPayout > maxWinnable)
            {
                rollForItPayout = PricingCalculator.Round2(maxWinnable);
            }
        }

        var afterDiscount = Math.Max(0, subTotal - req.DiscountTotal - rollForItPayout);
        // Prices are VAT-inclusive; extract the VAT portion
        var taxAmount = PricingCalculator.Round2(afterDiscount - afterDiscount / (1 + taxRate / 100m));
        var grandTotal = afterDiscount;

        if (!managerBypassPosRules && _posRules.BlockZeroOrNegativeTotal && grandTotal <= 0)
            throw new InvalidOperationException("Sale total must be greater than zero.");

        // Return credit funds the customer's tender first; any remainder is what they actually pay.
        // Clamp so a customer never appears to have "paid" a negative amount when the credit is
        // larger than the new sale — the leftover is a cash refund tracked on the SaleReturn record,
        // not on this invoice.
        var creditApplied = returnCreditApplied > 0
            ? Math.Min(PricingCalculator.Round2(returnCreditApplied), grandTotal)
            : 0m;
        var amountPaid = PricingCalculator.Round2(grandTotal - creditApplied);

        var invoice = new Invoice
        {
            Id = Guid.NewGuid(),
            InvoiceNumber = await NextInvoiceNumberAsync(ct),
            Status = InvoiceStatus.Final,
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
            RollForItRollId = req.RollForItRollId,
            RollForItPayout = rollForItPayout,
            CreatedByUserId = userId,
            SalespersonId = salesperson?.Id,
            SalespersonName = salesperson?.Name,
            StockDeducted = true,
            IsSpecialOrder = isSpecialOrder,
            ExchangeFromInvoiceId = exchangeFromInvoiceId,
            ReturnCreditApplied = creditApplied,
            AmountPaid = amountPaid,
            Lines = lines
        };

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

        return dto;
    }

    private async Task<InvoiceDto> MapToDtoAsync(Invoice inv, byte[]? pdfBytes, bool includeCompanyContact, CancellationToken ct)
    {
        var dto = MapToDto(inv, pdfBytes, includeCompanyContact: false);
        if (includeCompanyContact)
        {
            var eff = await _business.GetAsync(ct);
            dto.CompanyContact = ReceiptCompanyContact.ToDto(eff);
            dto.ReceiptFooter = string.IsNullOrWhiteSpace(eff.ReceiptFooter) ? null : eff.ReceiptFooter.Trim();
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
            RollForItRollId = inv.RollForItRollId,
            RollForItPayout = inv.RollForItPayout,
            PublicToken = inv.PublicToken,
            PdfUrl = pdfUrl,
            CreatedAt = inv.CreatedAt,
            SalespersonId = inv.SalespersonId,
            SalespersonName = inv.SalespersonName,
            ReturnCreditApplied = inv.ReturnCreditApplied,
            AmountPaid = inv.AmountPaid,
            ExchangeFromInvoiceId = inv.ExchangeFromInvoiceId,
            IsSpecialOrder = inv.IsSpecialOrder,
            IsDelivered = inv.IsDelivered,
            DeliveredAt = inv.DeliveredAt,
            DeliveryNotes = inv.DeliveryNotes,
            Lines = inv.Lines.Select(l => new InvoiceLineDto
            {
                Id = l.Id,
                ProductId = l.ProductId,
                Description = l.Description,
                Sku = l.SkuAtSale ?? l.Product?.Sku,
                Quantity = l.Quantity,
                UnitPrice = l.UnitPrice,
                OriginalUnitPrice = l.OriginalUnitPrice,
                LineDiscount = l.LineDiscount,
                LineTotal = l.LineTotal,
                ReturnedQuantity = l.ReturnedQuantity
            }).ToList(),
            CompanyContact = null
        };
    }

    public async Task<InvoiceDto?> GetAsync(Guid id, CancellationToken ct)
    {
        var inv = await _db.Invoices
            .Include(i => i.Lines).ThenInclude(l => l.Product)
            .FirstOrDefaultAsync(i => i.Id == id, ct);
        return inv == null ? null : MapToDto(inv, null);
    }

    public async Task<byte[]?> GetPdfBytesAsync(Guid id, CancellationToken ct)
    {
        var inv = await _db.Invoices.Include(i => i.Lines).FirstOrDefaultAsync(i => i.Id == id, ct);
        if (inv == null) return null;

        if (inv.PdfStorageKey != null)
        {
            var path = Path.Combine(Directory.GetCurrentDirectory(), _app.PdfStoragePath, inv.PdfStorageKey);
            if (File.Exists(path))
                return await File.ReadAllBytesAsync(path, ct);
        }

        // Invoices imported from Shopify (and any whose file was lost) have no stored PDF yet.
        var bytes = _pdf.BuildPdf(inv);
        inv.PdfStorageKey = await _pdf.SavePdfAsync(inv, bytes, ct);
        await _db.SaveChangesAsync(ct);
        return bytes;
    }

    public async Task<InvoiceDto?> GetByPublicTokenAsync(Guid token, CancellationToken ct)
    {
        var inv = await _db.Invoices
            .Include(i => i.Lines).ThenInclude(l => l.Product)
            .FirstOrDefaultAsync(i => i.PublicToken == token, ct);
        return inv == null ? null : await MapToDtoAsync(inv, null, includeCompanyContact: true, ct);
    }

    public async Task<byte[]?> GetPdfByPublicTokenAsync(Guid token, CancellationToken ct)
    {
        var inv = await _db.Invoices.Include(i => i.Lines).FirstOrDefaultAsync(i => i.PublicToken == token, ct);
        if (inv == null) return null;
        return await GetPdfBytesAsync(inv.Id, ct);
    }

    /// <summary>
    /// Load a return by its public slug for the printable slip. Anonymous callers get shop contact
    /// and the customer-facing figures — no cost / GP / staff identifiers leak out.
    /// </summary>
    public async Task<PublicSaleReturnDto?> GetSaleReturnByPublicTokenAsync(Guid token, CancellationToken ct)
    {
        var sr = await _db.SaleReturns
            .Include(r => r.OriginalInvoice)
            .Include(r => r.ExchangeInvoice)
            .Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.PublicToken == token, ct);
        if (sr == null) return null;

        var eff = await _business.GetAsync(ct);
        string? cashierName = null;
        if (!string.IsNullOrWhiteSpace(sr.CreatedByUserId))
        {
            cashierName = await _db.Users.AsNoTracking()
                .Where(u => u.Id == sr.CreatedByUserId)
                .Select(u => u.DisplayName ?? u.UserName ?? u.Email)
                .FirstOrDefaultAsync(ct);
        }

        return new PublicSaleReturnDto
        {
            Id = sr.Id,
            PublicToken = sr.PublicToken,
            OriginalInvoiceId = sr.OriginalInvoiceId,
            OriginalInvoiceNumber = sr.OriginalInvoice?.InvoiceNumber ?? string.Empty,
            ExchangeInvoiceNumber = sr.ExchangeInvoice?.InvoiceNumber,
            Reason = sr.Reason,
            CustomerName = sr.OriginalInvoice?.CustomerName,
            CreditTotal = sr.CreditTotal,
            NetSettlement = sr.NetSettlement,
            SettlementMethod = sr.SettlementMethod,
            CreatedAt = sr.CreatedAt,
            CashierName = cashierName,
            Lines = sr.Lines.Select(l => new PublicSaleReturnLineDto
            {
                Description = l.Description,
                Sku = l.SkuAtReturn,
                Quantity = l.Quantity,
                UnitCredit = l.UnitCredit,
                LineCredit = l.LineCredit
            }).ToList(),
            CompanyContact = ReceiptCompanyContact.ToDto(eff),
            ReceiptFooter = string.IsNullOrWhiteSpace(eff.ReceiptFooter) ? null : eff.ReceiptFooter.Trim()
        };
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

        // Restore stock only when this sale actually deducted it. All in-store sales do; Shopify sales
        // only when imported after stock-sync was enabled (StockDeducted). Placeholder lines (unlinked
        // Shopify items and shipping) never reduced stock, so they are skipped.
        if (inv.StockDeducted)
        {
            foreach (var line in inv.Lines)
            {
                var p = await _db.Products.FirstOrDefaultAsync(x => x.Id == line.ProductId, ct);
                if (p != null && p.Sku != ShopifyOrderImportService.UnlinkedPlaceholderSku)
                {
                    p.QtyOnHand += line.Quantity;
                    p.UpdatedAt = DateTimeOffset.UtcNow;
                }
            }
        }
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    /// <summary>
    /// Return one or more lines from a prior sale and — optionally — ring up replacement items,
    /// settling only the net difference. The original invoice stays Final; only the returned qty
    /// on each affected line is booked (see <see cref="InvoiceLine.ReturnedQuantity"/>). A
    /// <see cref="SaleReturn"/> record links the return to any new sale for the audit trail.
    /// </summary>
    /// <remarks>
    /// Split across two transactions on purpose: the return + new-sale creation each need their own
    /// tx, and if the new sale fails we still want the return persisted so the customer's refund
    /// is recorded. The final "link the two together" write is small and idempotent enough that
    /// operator recovery is trivial if it ever fails.
    /// </remarks>
    public async Task<ExchangeResponse> ExchangeAsync(
        Guid originalInvoiceId,
        ExchangeRequest req,
        string userId,
        bool managerBypassPosRules,
        CancellationToken ct)
    {
        var original = await _db.Invoices
            .Include(i => i.Lines)
            .FirstOrDefaultAsync(i => i.Id == originalInvoiceId, ct)
            ?? throw new InvalidOperationException("Original invoice not found.");
        if (original.Status != InvoiceStatus.Final)
            throw new InvalidOperationException("Only finalised sales can be returned against.");

        if (req.ReturnLines == null || req.ReturnLines.Count == 0)
            throw new InvalidOperationException("At least one return line is required.");

        // Validate + snapshot each return line up front so we can reject before mutating anything.
        var linesById = original.Lines.ToDictionary(l => l.Id);
        var returnPlan = new List<(InvoiceLine line, int qty, decimal unitCredit, decimal lineCredit)>();
        var mergedByLineId = req.ReturnLines
            .GroupBy(r => r.InvoiceLineId)
            .Select(g => (Id: g.Key, Qty: g.Sum(x => x.Quantity)));

        foreach (var (lineId, qty) in mergedByLineId)
        {
            if (!linesById.TryGetValue(lineId, out var l))
                throw new InvalidOperationException($"Line {lineId} is not on invoice {original.InvoiceNumber}.");
            if (qty <= 0)
                throw new InvalidOperationException($"Return quantity for \"{l.Description}\" must be greater than zero.");
            var remaining = l.Quantity - l.ReturnedQuantity;
            if (qty > remaining)
                throw new InvalidOperationException(
                    $"Only {remaining} of \"{l.Description}\" is still returnable (already returned {l.ReturnedQuantity} of {l.Quantity}).");

            // Effective unit price is what the customer actually paid — this is the same basis Find
            // sale displays and is the fair credit amount for the return.
            var unitCredit = l.Quantity > 0
                ? PricingCalculator.Round2(l.LineTotal / l.Quantity)
                : PricingCalculator.Round2(l.UnitPrice);
            var lineCredit = PricingCalculator.Round2(unitCredit * qty);
            returnPlan.Add((l, qty, unitCredit, lineCredit));
        }

        var creditTotal = PricingCalculator.Round2(returnPlan.Sum(p => p.lineCredit));

        if (req.NewLines is { Count: > 0 } && req.SalespersonId.HasValue
            && !await _db.Salespeople.AnyAsync(s => s.Id == req.SalespersonId.Value && s.IsActive, ct))
            throw new InvalidOperationException("The selected salesperson is no longer available. Pick another.");

        // 1) Persist the return itself. Restock, bump ReturnedQuantity, insert SaleReturn(+Lines).
        //    Kept independent of new-sale creation so a refund is always recorded even if the
        //    replacement ring-up fails downstream. NetSettlement is provisionally set as if this
        //    were refund-only; step 3 revises it once the new-sale total is known.
        var saleReturn = new SaleReturn
        {
            Id = Guid.NewGuid(),
            OriginalInvoiceId = original.Id,
            CreditTotal = creditTotal,
            NetSettlement = -creditTotal,
            SettlementMethod = req.PaymentMethod,
            Reason = req.Reason.Trim(),
            CreatedByUserId = userId,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await using (var tx = await _db.Database.BeginTransactionAsync(ct))
        {
            foreach (var (line, qty, unitCredit, lineCredit) in returnPlan)
            {
                line.ReturnedQuantity += qty;

                // Restock only if this invoice actually deducted stock (mirrors VoidAsync logic).
                if (original.StockDeducted)
                {
                    var p = await _db.Products.FirstOrDefaultAsync(x => x.Id == line.ProductId, ct);
                    if (p != null && p.Sku != ShopifyOrderImportService.UnlinkedPlaceholderSku)
                    {
                        p.QtyOnHand += qty;
                        p.UpdatedAt = DateTimeOffset.UtcNow;
                    }
                }

                saleReturn.Lines.Add(new SaleReturnLine
                {
                    Id = Guid.NewGuid(),
                    SaleReturnId = saleReturn.Id,
                    OriginalInvoiceLineId = line.Id,
                    ProductId = line.ProductId,
                    SkuAtReturn = line.SkuAtSale,
                    Description = line.Description,
                    Quantity = qty,
                    UnitCredit = unitCredit,
                    LineCredit = lineCredit
                });
            }

            _db.SaleReturns.Add(saleReturn);
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        // 2) If there are replacement items, create the new sale with the credit applied. We call
        //    the public CreateAsync so all POS rules, promo pricing and PDF logic run identically.
        InvoiceDto? newSaleDto = null;
        if (req.NewLines != null && req.NewLines.Count > 0)
        {
            var createReq = new CreateInvoiceRequest
            {
                CustomerName = req.CustomerName ?? original.CustomerName,
                CustomerEmail = req.CustomerEmail ?? original.CustomerEmail,
                CustomerType = req.CustomerType ?? original.CustomerType,
                CustomerCompany = req.CustomerCompany ?? original.CustomerCompany,
                CustomerAddress = req.CustomerAddress ?? original.CustomerAddress,
                CustomerVatNumber = req.CustomerVatNumber ?? original.CustomerVatNumber,
                PaymentMethod = req.PaymentMethod,
                DiscountTotal = req.DiscountTotal,
                PromotionName = req.PromotionName,
                SendEmail = req.SendEmail,
                SalespersonId = req.SalespersonId,
                Lines = req.NewLines
            };

            newSaleDto = await CreateAsync(
                createReq,
                userId,
                managerBypassPosRules,
                ct,
                exchangeFromInvoiceId: original.Id,
                returnCreditApplied: creditTotal);
        }

        // 3) Compute net settlement and link the two records. Net > 0 = customer paid the top-up,
        //    net < 0 = customer got cash back. When there is no new sale, the whole credit is a
        //    refund (net = -creditTotal).
        var newSaleTotal = newSaleDto?.GrandTotal ?? 0m;
        var netSettlement = PricingCalculator.Round2(newSaleTotal - creditTotal);
        saleReturn.NetSettlement = netSettlement;
        saleReturn.SettlementMethod = req.PaymentMethod;
        if (newSaleDto != null)
        {
            saleReturn.ExchangeInvoiceId = newSaleDto.Id;
        }
        await _db.SaveChangesAsync(ct);

        return new ExchangeResponse
        {
            SaleReturnId = saleReturn.Id,
            SaleReturnPublicToken = saleReturn.PublicToken,
            OriginalInvoiceId = original.Id,
            OriginalInvoiceNumber = original.InvoiceNumber,
            CreditTotal = creditTotal,
            NetSettlement = netSettlement,
            ExchangeInvoice = newSaleDto,
            ReturnedLines = saleReturn.Lines.Select(l => new ExchangeReturnLineResultDto
            {
                OriginalInvoiceLineId = l.OriginalInvoiceLineId,
                ProductId = l.ProductId,
                Sku = l.SkuAtReturn,
                Description = l.Description,
                Quantity = l.Quantity,
                UnitCredit = l.UnitCredit,
                LineCredit = l.LineCredit
            }).ToList()
        };
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
