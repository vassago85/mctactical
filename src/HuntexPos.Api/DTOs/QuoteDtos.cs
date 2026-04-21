using System.ComponentModel.DataAnnotations;

namespace HuntexPos.Api.DTOs;

public class CreateQuoteLineRequest
{
    /// <summary>Optional — omit for custom / ad-hoc items.</summary>
    public Guid? ProductId { get; set; }

    public string? Sku { get; set; }

    [Required, MaxLength(512)]
    public string ItemName { get; set; } = string.Empty;

    public string? Description { get; set; }

    [Range(1, 99999)]
    public int Quantity { get; set; } = 1;

    public decimal? UnitCost { get; set; }

    [Range(0, 9999999)]
    public decimal UnitPrice { get; set; }

    public decimal? DiscountPercent { get; set; }
    public decimal? DiscountAmount { get; set; }

    public int SortOrder { get; set; }
}

public class CreateQuoteRequest
{
    public Guid? CustomerId { get; set; }

    public string? CustomerName { get; set; }
    public string? CustomerEmail { get; set; }
    public string? CustomerPhone { get; set; }
    public string? CustomerCompany { get; set; }
    public string? CustomerAddress { get; set; }
    public string? CustomerVatNumber { get; set; }

    public string? PublicNotes { get; set; }
    public string? InternalNotes { get; set; }

    public DateTimeOffset? ValidUntil { get; set; }

    public decimal DiscountTotal { get; set; }
    public decimal? TaxRate { get; set; }

    [Required, MinLength(1)]
    public List<CreateQuoteLineRequest> Lines { get; set; } = new();
}

public class UpdateQuoteRequest : CreateQuoteRequest { }

public class UpdateQuoteStatusRequest
{
    [Required]
    public string Status { get; set; } = string.Empty;
}

public class SendQuoteEmailRequest
{
    public string? Email { get; set; }
    public string? Message { get; set; }
}

public class QuoteLineDto
{
    public Guid Id { get; set; }
    public Guid? ProductId { get; set; }
    public string? Sku { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int Quantity { get; set; }
    public decimal? UnitCost { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal? DiscountPercent { get; set; }
    public decimal? DiscountAmount { get; set; }
    public decimal TaxRate { get; set; }
    public decimal LineTotal { get; set; }
    public int SortOrder { get; set; }
}

public class QuoteDto
{
    public Guid Id { get; set; }
    public string QuoteNumber { get; set; } = string.Empty;
    public string Status { get; set; } = "Draft";

    public Guid? CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerEmail { get; set; }
    public string? CustomerPhone { get; set; }
    public string? CustomerCompany { get; set; }
    public string? CustomerAddress { get; set; }
    public string? CustomerVatNumber { get; set; }

    public decimal SubTotal { get; set; }
    public decimal DiscountTotal { get; set; }
    public decimal TaxRate { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal GrandTotal { get; set; }

    public string? PublicNotes { get; set; }
    public string? InternalNotes { get; set; }

    public DateTimeOffset? ValidUntil { get; set; }

    public Guid PublicToken { get; set; }
    public string? PdfUrl { get; set; }
    public string PublicUrl { get; set; } = string.Empty;

    public Guid? ConvertedInvoiceId { get; set; }
    public DateTimeOffset? ConvertedAt { get; set; }

    public string? CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }

    public List<QuoteLineDto> Lines { get; set; } = new();

    /// <summary>Populated on the public anonymous view so the webpage can display shop info.</summary>
    public CompanyContactDto? CompanyContact { get; set; }
}

public class QuoteListItemDto
{
    public Guid Id { get; set; }
    public string QuoteNumber { get; set; } = string.Empty;
    public string Status { get; set; } = "Draft";
    public string? CustomerName { get; set; }
    public string? CustomerCompany { get; set; }
    public decimal GrandTotal { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ValidUntil { get; set; }
    public Guid? ConvertedInvoiceId { get; set; }
}
