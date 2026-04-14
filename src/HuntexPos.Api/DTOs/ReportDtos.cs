namespace HuntexPos.Api.DTOs;

public class DailySummaryDto
{
    public DateOnly Date { get; set; }
    public int InvoiceCount { get; set; }
    public decimal GrandTotal { get; set; }
}

public class InvoiceListItemDto
{
    public Guid Id { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal GrandTotal { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string? CustomerName { get; set; }
    public string? CreatedByUserId { get; set; }
}
