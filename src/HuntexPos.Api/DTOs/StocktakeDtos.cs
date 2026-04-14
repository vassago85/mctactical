using System.ComponentModel.DataAnnotations;

namespace HuntexPos.Api.DTOs;

public class CreateStocktakeSessionRequest
{
    [Required]
    public string Name { get; set; } = string.Empty;
}

public class AddStocktakeLineRequest
{
    [Required]
    public Guid ProductId { get; set; }
    [Range(0, 999999)]
    public int QtyCounted { get; set; }
}

public class StocktakeSessionDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public List<StocktakeLineDto> Lines { get; set; } = new();
}

public class StocktakeLineDto
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public int QtyBefore { get; set; }
    public int QtyCounted { get; set; }
}
