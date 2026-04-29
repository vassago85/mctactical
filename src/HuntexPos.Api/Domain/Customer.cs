namespace HuntexPos.Api.Domain;

public class Customer
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? Phone { get; set; }
    public string? Company { get; set; }
    public string? Address { get; set; }
    public string? VatNumber { get; set; }
    public string? CustomerType { get; set; }

    /// <summary>True if this customer is flagged as a trade customer (independent of <see cref="AccountEnabled"/>).</summary>
    public bool TradeAccount { get; set; }

    /// <summary>True if this customer is allowed to charge sales to an account (Accounts Receivable).</summary>
    public bool AccountEnabled { get; set; }

    /// <summary>Soft credit ceiling (warn but do not block when exceeded). 0 = no limit set.</summary>
    public decimal CreditLimit { get; set; }

    /// <summary>Default net payment days when invoicing on account (e.g. 30 = Net 30).</summary>
    public int PaymentTermsDays { get; set; } = 30;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
