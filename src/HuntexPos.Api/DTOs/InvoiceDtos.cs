using System.ComponentModel.DataAnnotations;

namespace HuntexPos.Api.DTOs;

public class CreateInvoiceLineRequest
{
    [Required]
    public Guid ProductId { get; set; }
    [Range(1, 99999)]
    public int Quantity { get; set; }
    public decimal? UnitPriceOverride { get; set; }
    public decimal OriginalUnitPrice { get; set; }
    public decimal LineDiscount { get; set; }
}

public class CreateInvoiceRequest
{
    public string? CustomerName { get; set; }
    public string? CustomerEmail { get; set; }
    public string? CustomerType { get; set; }
    public string? CustomerCompany { get; set; }
    public string? CustomerAddress { get; set; }
    public string? CustomerVatNumber { get; set; }
    [Required]
    public string PaymentMethod { get; set; } = "Cash";
    public decimal DiscountTotal { get; set; }
    public string? PromotionName { get; set; }
    public bool SendEmail { get; set; }
    [Required, MinLength(1)]
    public List<CreateInvoiceLineRequest> Lines { get; set; } = new();
}

/// <summary>Shop contact block for customer-facing receipts (e.g. public invoice view).</summary>
public class CompanyContactDto
{
    public string DisplayName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? Website { get; set; }
    /// <summary>Human-friendly label for <see cref="Website"/>.</summary>
    public string? WebsiteLabel { get; set; }
    /// <summary>VAT registration number (shown on thermal receipts when present).</summary>
    public string? VatNumber { get; set; }
    /// <summary>Public URL for the configured shop logo. Null if no logo uploaded.
    /// Served on receipts so the thermal print view doesn't depend on a separate branding fetch.</summary>
    public string? LogoUrl { get; set; }
}

public class InvoiceDto
{
    public Guid Id { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? CustomerName { get; set; }
    public string? CustomerEmail { get; set; }
    public string? CustomerType { get; set; }
    public string? CustomerCompany { get; set; }
    public string? CustomerAddress { get; set; }
    public string? CustomerVatNumber { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
    public decimal SubTotal { get; set; }
    public decimal TaxRate { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal DiscountTotal { get; set; }
    public decimal GrandTotal { get; set; }
    public string? PromotionName { get; set; }
    public Guid PublicToken { get; set; }
    public string? PdfUrl { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Return credit applied at checkout when this sale was rung as part of an exchange.</summary>
    public decimal ReturnCreditApplied { get; set; }
    /// <summary>What the customer actually tendered (<c>GrandTotal - ReturnCreditApplied</c>).</summary>
    public decimal AmountPaid { get; set; }
    /// <summary>Set when this invoice was created via an exchange against a prior sale.</summary>
    public Guid? ExchangeFromInvoiceId { get; set; }

    public List<InvoiceLineDto> Lines { get; set; } = new();

    /// <summary>Set on anonymous public invoice responses so the web receipt can show shop details.</summary>
    public CompanyContactDto? CompanyContact { get; set; }

    /// <summary>Free-text footer (e.g. returns policy) shown at the bottom of the thermal receipt. Set on public responses.</summary>
    public string? ReceiptFooter { get; set; }

    public bool IsSpecialOrder { get; set; }
    public bool IsDelivered { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
    public string? DeliveryNotes { get; set; }

    /// <summary>Non-null if the sale total is below total cost (managers only).</summary>
    public string? BelowCostWarning { get; set; }
    public string? EmailWarning { get; set; }
}

public class InvoiceLineDto
{
    /// <summary>Row id, exposed so the return/exchange UI can reference it back to the server.</summary>
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public string Description { get; set; } = string.Empty;
    /// <summary>Product SKU (from catalog at render time). Empty if the product was deleted.</summary>
    public string? Sku { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal OriginalUnitPrice { get; set; }
    public decimal LineDiscount { get; set; }
    public decimal LineTotal { get; set; }
    /// <summary>Total qty already returned from this line via a prior exchange.</summary>
    public int ReturnedQuantity { get; set; }
}

/// <summary>
/// One historical sale line. Returned by the sales-history search that the counter uses
/// when a customer wants to return an item but has lost the printed receipt — it recovers
/// what they actually paid, including any discount given at the time.
/// </summary>
public class InvoiceLineSearchResultDto
{
    public Guid InvoiceId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? CustomerName { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
    /// <summary>Lets the counter reprint the thermal receipt straight from a search hit.</summary>
    public Guid PublicToken { get; set; }

    public Guid ProductId { get; set; }
    /// <summary>Original <see cref="Domain.InvoiceLine.Id"/> — used by the return/exchange flow.</summary>
    public Guid InvoiceLineId { get; set; }
    public string? Sku { get; set; }
    public string Description { get; set; } = string.Empty;
    public int Quantity { get; set; }
    /// <summary>How much of this line has already been returned via one or more prior exchanges.</summary>
    public int ReturnedQuantity { get; set; }
    /// <summary>Catalog retail price at time of sale, before any concession.</summary>
    public decimal OriginalUnitPrice { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineDiscount { get; set; }
    public decimal LineTotal { get; set; }
    /// <summary>What the customer actually paid per unit, after all line-level discounts.</summary>
    public decimal EffectiveUnitPrice { get; set; }
}

public class VoidInvoiceRequest
{
    [Required, MinLength(3)]
    public string Reason { get; set; } = string.Empty;
}

public class PendingDeliveryDto
{
    public Guid Id { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public string? CustomerName { get; set; }
    public string? CustomerEmail { get; set; }
    public decimal GrandTotal { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public bool IsDelivered { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
    public string? DeliveryNotes { get; set; }
    public string ItemsSummary { get; set; } = string.Empty;
    public Guid PublicToken { get; set; }
}

public class RecentInvoiceDto
{
    public Guid Id { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public string? CustomerName { get; set; }
    public decimal GrandTotal { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public Guid PublicToken { get; set; }
}

public class MarkDeliveredRequest
{
    public string? Notes { get; set; }
}

/// <summary>One line coming back from a prior sale, referenced by its <c>InvoiceLine.Id</c>.</summary>
public class ExchangeReturnLineRequest
{
    [Required]
    public Guid InvoiceLineId { get; set; }
    [Range(1, 99999)]
    public int Quantity { get; set; }
}

/// <summary>
/// Combined return + optional replacement sale request. When <see cref="NewLines"/> is empty this
/// is a refund only; otherwise the customer settles the net difference.
/// </summary>
public class ExchangeRequest
{
    [Required, MinLength(3)]
    public string Reason { get; set; } = string.Empty;

    /// <summary>Lines coming back from the original invoice. Must contain at least one entry.</summary>
    [Required, MinLength(1)]
    public List<ExchangeReturnLineRequest> ReturnLines { get; set; } = new();

    /// <summary>Optional replacement items. Same shape as a normal checkout line.</summary>
    public List<CreateInvoiceLineRequest> NewLines { get; set; } = new();

    /// <summary>Cart-level discount on the new sale, if any. Ignored when NewLines is empty.</summary>
    public decimal DiscountTotal { get; set; }

    /// <summary>Promotion tag for the new sale, if any. Ignored when NewLines is empty.</summary>
    public string? PromotionName { get; set; }

    /// <summary>Customer fields for the new sale. When omitted, copied from the original invoice.</summary>
    public string? CustomerName { get; set; }
    public string? CustomerEmail { get; set; }
    public string? CustomerType { get; set; }
    public string? CustomerCompany { get; set; }
    public string? CustomerAddress { get; set; }
    public string? CustomerVatNumber { get; set; }

    /// <summary>
    /// Tender used to settle the net (Cash / Card / EFT). Used for the new-sale payment method
    /// when the customer pays a positive difference, and for the refund method when the net is
    /// negative. Required in either case.
    /// </summary>
    [Required]
    public string PaymentMethod { get; set; } = "Cash";

    /// <summary>Email the new-sale receipt to the customer if a customer email is present.</summary>
    public bool SendEmail { get; set; }
}

public class ExchangeResponse
{
    public Guid SaleReturnId { get; set; }
    public Guid OriginalInvoiceId { get; set; }
    public string OriginalInvoiceNumber { get; set; } = string.Empty;
    public decimal CreditTotal { get; set; }

    /// <summary>Positive = customer paid the difference; negative = customer was refunded; zero = even swap.</summary>
    public decimal NetSettlement { get; set; }

    /// <summary>New invoice created for the replacement items. Null when this was a refund-only return.</summary>
    public InvoiceDto? ExchangeInvoice { get; set; }
    public List<ExchangeReturnLineResultDto> ReturnedLines { get; set; } = new();
}

public class ExchangeReturnLineResultDto
{
    public Guid OriginalInvoiceLineId { get; set; }
    public Guid ProductId { get; set; }
    public string? Sku { get; set; }
    public string Description { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitCredit { get; set; }
    public decimal LineCredit { get; set; }
}
