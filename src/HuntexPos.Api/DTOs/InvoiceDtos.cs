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
    public List<InvoiceLineDto> Lines { get; set; } = new();

    /// <summary>Set on anonymous public invoice responses so the web receipt can show shop details.</summary>
    public CompanyContactDto? CompanyContact { get; set; }

    /// <summary>Non-null if the sale total is below total cost (managers only).</summary>
    public string? BelowCostWarning { get; set; }
    public string? EmailWarning { get; set; }
}

public class InvoiceLineDto
{
    public Guid ProductId { get; set; }
    public string Description { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal OriginalUnitPrice { get; set; }
    public decimal LineDiscount { get; set; }
    public decimal LineTotal { get; set; }
}

public class VoidInvoiceRequest
{
    [Required, MinLength(3)]
    public string Reason { get; set; } = string.Empty;
}
