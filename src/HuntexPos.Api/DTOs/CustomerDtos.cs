using System.ComponentModel.DataAnnotations;

namespace HuntexPos.Api.DTOs;

public class CustomerDto
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? Phone { get; set; }
    public string? Company { get; set; }
    public string? Address { get; set; }
    public string? VatNumber { get; set; }
    public string? CustomerType { get; set; }

    // Phase 3B AR fields. Default values mean every existing customer continues
    // to behave exactly as before (no account, no credit) until explicitly enabled.
    public bool TradeAccount { get; set; }
    public bool AccountEnabled { get; set; }
    public decimal CreditLimit { get; set; }
    public int PaymentTermsDays { get; set; } = 30;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public class UpdateCustomerRequest
{
    public string? Name { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Company { get; set; }
    public string? Address { get; set; }
    public string? VatNumber { get; set; }
    public string? CustomerType { get; set; }

    // AR fields are nullable so partial updates leave them untouched.
    // Only Owner/Admin/Dev may change these (enforced at the controller level).
    public bool? TradeAccount { get; set; }
    public bool? AccountEnabled { get; set; }
    public decimal? CreditLimit { get; set; }
    public int? PaymentTermsDays { get; set; }
}

public class CreateCustomerRequest
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? Phone { get; set; }
    public string? Company { get; set; }
    public string? Address { get; set; }
    public string? VatNumber { get; set; }
    public string? CustomerType { get; set; }

    public bool TradeAccount { get; set; }
    public bool AccountEnabled { get; set; }
    public decimal CreditLimit { get; set; }
    public int PaymentTermsDays { get; set; } = 30;
}
