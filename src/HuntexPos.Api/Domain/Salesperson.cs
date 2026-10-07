namespace HuntexPos.Api.Domain;

/// <summary>
/// A person who can be credited with a sale at checkout. Not a login — the shop till usually
/// stays signed in as the owner, and the operator picks who served the customer.
/// </summary>
public class Salesperson
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>Commission rate as a percentage (e.g. 5 = 5%).</summary>
    public decimal CommissionPercent { get; set; }

    public CommissionBasis CommissionBasis { get; set; } = CommissionBasis.SalesExVat;

    /// <summary>
    /// Soft-delete flag. Inactive salespeople are hidden from the checkout picker but stay
    /// linked to their historical invoices so monthly reports still attribute correctly.
    /// </summary>
    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>What a salesperson's <see cref="Salesperson.CommissionPercent"/> is applied to.</summary>
public enum CommissionBasis
{
    /// <summary>Net sales excluding VAT (after returns).</summary>
    SalesExVat,
    /// <summary>Gross profit excluding VAT (net sales ex VAT minus cost, after returns).</summary>
    GrossProfit
}
