namespace HuntexPos.Api.Domain;

public enum StocktakeStatus
{
    Draft = 0,
    Posted = 1
}

public class StocktakeSession
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public StocktakeStatus Status { get; set; } = StocktakeStatus.Draft;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? CreatedByUserId { get; set; }
    public DateTimeOffset? PostedAt { get; set; }
    public string? PostedByUserId { get; set; }

    public ICollection<StocktakeLine> Lines { get; set; } = new List<StocktakeLine>();
}
