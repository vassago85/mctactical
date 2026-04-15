namespace HuntexPos.Api.Domain;

/// <summary>
/// Per-product special pricing. If SpecialPrice is set, it overrides the product sell price.
/// Otherwise DiscountPercent is applied to the product's base sell price.
/// A special can optionally be tied to a promotion.
/// </summary>
public class ProductSpecial
{
    public Guid Id { get; set; }

    public Guid ProductId { get; set; }
    public Product? Product { get; set; }

    public Guid? PromotionId { get; set; }
    public Promotion? Promotion { get; set; }

    public decimal? SpecialPrice { get; set; }
    public decimal? DiscountPercent { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
