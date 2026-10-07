namespace HuntexPos.Api.DTOs;

public class MissingCostRowDto
{
    /// <summary>Sold-item key (see SoldItemKey): <c>p:&lt;productId&gt;</c> or <c>u:…</c> for unlinked Shopify items.</summary>
    public string Key { get; set; } = string.Empty;
    public bool IsUnlinkedShopify { get; set; }
    public Guid? ProductId { get; set; }
    public long? ShopifyVariantId { get; set; }
    public string? Sku { get; set; }
    public string Name { get; set; } = string.Empty;
    public int QtySold { get; set; }
    public int SaleCount { get; set; }
    public decimal Revenue { get; set; }
    public DateTimeOffset LastSoldAt { get; set; }
}

public class SaveMissingCostsRequest
{
    public List<MissingCostEntry> Items { get; set; } = new();
}

public class MissingCostEntry
{
    public string Key { get; set; } = string.Empty;
    /// <summary>Unit cost excluding VAT, before any supplier discount.</summary>
    public decimal CostExVat { get; set; }
}

public class SaveMissingCostsResponse
{
    public int ProductsUpdated { get; set; }
    public int LinesUpdated { get; set; }
}
