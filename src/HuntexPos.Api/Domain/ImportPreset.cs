namespace HuntexPos.Api.Domain;

public class ImportPreset
{
    public Guid Id { get; set; }
    /// <summary>
    /// Optional — legacy supplier scoping. New presets created through the
    /// wholesaler-agnostic wizard leave this null and are matched purely by
    /// <see cref="Name"/> + <see cref="ColumnMappingJson"/>.
    /// </summary>
    public Guid? SupplierId { get; set; }
    public Supplier? Supplier { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ColumnMappingJson { get; set; } = "{}";
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
