namespace HuntexPos.Api.Domain;

public class StocktakeLine
{
    public Guid Id { get; set; }
    public Guid StocktakeSessionId { get; set; }
    public StocktakeSession? Session { get; set; }

    public Guid ProductId { get; set; }
    public Product? Product { get; set; }

    public int QtyBefore { get; set; }
    public int QtyCounted { get; set; }
}
