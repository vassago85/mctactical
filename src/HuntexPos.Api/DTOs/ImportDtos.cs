using System.ComponentModel.DataAnnotations;

namespace HuntexPos.Api.DTOs;

public class ColumnMappingDto
{
    public string? Sku { get; set; }
    public string? Barcode { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? Category { get; set; }
    public string? Cost { get; set; }
    public string? SellPrice { get; set; }
    public string? QtyOnHand { get; set; }
}

public class ImportPreviewRowDto
{
    public int RowIndex { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string? Barcode { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Category { get; set; }
    public decimal Cost { get; set; }
    public decimal SellPrice { get; set; }
    public int QtyOnHand { get; set; }
    public string? Error { get; set; }
}

public class HuntexSheetImportRequest
{
    [Required]
    public IFormFile File { get; set; } = null!;
    public string SheetName { get; set; } = "huntex 2026";
    public Guid? SupplierId { get; set; }
    public bool Commit { get; set; }
}

public class WholesalerImportRequest
{
    [Required]
    public IFormFile File { get; set; } = null!;
    [Required]
    public Guid SupplierId { get; set; }
    public ColumnMappingDto Mapping { get; set; } = new();
    public bool Commit { get; set; }
}

public class SaveImportPresetRequest
{
    [Required]
    public Guid SupplierId { get; set; }
    [Required]
    public string Name { get; set; } = string.Empty;
    [Required]
    public ColumnMappingDto Mapping { get; set; } = new();
}

public class ImportPresetDto
{
    public Guid Id { get; set; }
    public Guid SupplierId { get; set; }
    public string Name { get; set; } = string.Empty;
    public ColumnMappingDto Mapping { get; set; } = new();
}
