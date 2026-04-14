namespace HuntexPos.Api.DTOs;

public class ProductDto
{
    public Guid Id { get; set; }
    public Guid? SupplierId { get; set; }
    public string? SupplierName { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string? Barcode { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Category { get; set; }
    public decimal? Cost { get; set; }
    public decimal SellPrice { get; set; }
    public int QtyOnHand { get; set; }
    public bool Active { get; set; }
}

public class ProductSearchQuery
{
    public string? Q { get; set; }
    public Guid? SupplierId { get; set; }
    public string? Barcode { get; set; }
    public int Take { get; set; } = 50;
}

public class ProductStocklistQuery
{
    public string? Q { get; set; }
    public Guid? SupplierId { get; set; }
    public bool IncludeInactive { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; } = 500;
}

public class StocklistPageDto
{
    public int Total { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; }
    public List<ProductDto> Items { get; set; } = new();
}

public class CreateProductRequest
{
    public string Sku { get; set; } = string.Empty;
    public string? Barcode { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Category { get; set; }
    public Guid? SupplierId { get; set; }
    public decimal Cost { get; set; }
    public decimal SellPrice { get; set; }
    public int QtyOnHand { get; set; }
}

public class UpdateProductRequest
{
    public string? Sku { get; set; }
    public string? Barcode { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? Category { get; set; }
    public Guid? SupplierId { get; set; }
    public decimal? Cost { get; set; }
    public decimal? SellPrice { get; set; }
    public int? QtyOnHand { get; set; }
    public bool? Active { get; set; }
}
