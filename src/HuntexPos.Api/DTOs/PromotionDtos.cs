using System.ComponentModel.DataAnnotations;

namespace HuntexPos.Api.DTOs;

public class PromotionDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal DiscountPercent { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset? StartsAt { get; set; }
    public DateTimeOffset? EndsAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public int SpecialsCount { get; set; }
}

public class CreatePromotionRequest
{
    [Required, MinLength(1)]
    public string Name { get; set; } = string.Empty;
    [Range(0, 100)]
    public decimal DiscountPercent { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset? StartsAt { get; set; }
    public DateTimeOffset? EndsAt { get; set; }
}

public class UpdatePromotionRequest
{
    public string? Name { get; set; }
    public decimal? DiscountPercent { get; set; }
    public bool? IsActive { get; set; }
    public DateTimeOffset? StartsAt { get; set; }
    public DateTimeOffset? EndsAt { get; set; }
}

public class ProductSpecialDto
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public string ProductSku { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public decimal BaseSellPrice { get; set; }
    public Guid? PromotionId { get; set; }
    public string? PromotionName { get; set; }
    public decimal? SpecialPrice { get; set; }
    public decimal? DiscountPercent { get; set; }
    public decimal EffectivePrice { get; set; }
    public bool IsActive { get; set; }
}

public class CreateProductSpecialRequest
{
    [Required]
    public Guid ProductId { get; set; }
    public Guid? PromotionId { get; set; }
    public decimal? SpecialPrice { get; set; }
    [Range(0, 100)]
    public decimal? DiscountPercent { get; set; }
    public bool IsActive { get; set; } = true;
}

public class UpdateProductSpecialRequest
{
    public decimal? SpecialPrice { get; set; }
    public decimal? DiscountPercent { get; set; }
    public bool? IsActive { get; set; }
}

/// <summary>Sent to POS so it knows the current active promotion and any product specials.</summary>
public class ActivePromotionDto
{
    public Guid? PromotionId { get; set; }
    public string? PromotionName { get; set; }
    public decimal SiteDiscountPercent { get; set; }
    public List<ActiveSpecialDto> Specials { get; set; } = new();
}

public class ActiveSpecialDto
{
    public Guid ProductId { get; set; }
    public decimal? SpecialPrice { get; set; }
    public decimal? DiscountPercent { get; set; }
}
