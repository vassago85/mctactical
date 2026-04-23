using HuntexPos.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HuntexPos.Api.Data;

/// <summary>
/// Seeds the "Venatics Gear" consignment sub-vendor and its Huntex 2026 stock list.
/// Idempotent by SKU — existing products are never overwritten and their quantities
/// are preserved. Designed to run as part of <see cref="DbSeeder"/> on container start.
/// </summary>
public static class VenaticsGearSeeder
{
    public const string SupplierName = "Venatics Gear";
    private const string SupplierNotes = "Consignment sub-vendor, Huntex 2026";

    private record Seed(
        string Sku,
        string Name,
        string Manufacturer,
        string? Category,
        string? ItemType,
        string? Description,
        decimal SellPrice,
        int QtyConsignment);

    /// <remarks>
    /// Prices are retail incl VAT (<see cref="Services.InvoiceService"/> treats sell prices
    /// as VAT-inclusive). Items without a known retail price have SellPrice = 0 so Eddie
    /// can fill them in from the Stock UI after deploy.
    /// </remarks>
    private static readonly Seed[] Items =
    {
        // MDT — price and qty to be confirmed by Eddie
        new("VG-MDT-FS-HOWA-BLK",          "MDT Field Stock Howa SA BLK",           "MDT",         "Chassis/Stocks", "Stock",      "Field Stock for Howa Short Action — Black",            0m,     1),
        new("VG-MDT-FS-HOWA-ODG",          "MDT Field Stock Howa SA ODG",           "MDT",         "Chassis/Stocks", "Stock",      "Field Stock for Howa Short Action — OD Green",         0m,     1),
        new("VG-MDT-ACC-P2-TIKKA-LA-FDE",  "MDT ACC Premier Gen 2 Tikka LA FDE",    "MDT",         "Chassis/Stocks", "Chassis",    "ACC Premier Gen 2 chassis for Tikka Long Action — FDE", 0m,    1),
        new("VG-MDT-CRBN-REM700-SA",       "MDT CRBN Elite Remington 700 SA",       "MDT",         "Chassis/Stocks", "Chassis",    "CRBN Elite chassis for Remington 700 Short Action",    0m,     1),
        new("VG-MDT-BIPOD-MTN",            "MDT Mountain Bipod",                    "MDT",         "Accessories",    "Bipod",      null,                                                    0m,    1),
        new("VG-MDT-BIPOD-BC",             "MDT BackCountry Bipod",                 "MDT",         "Accessories",    "Bipod",      null,                                                    0m,    1),
        new("VG-MDT-HC",                   "MDT Hand Cannon",                       "MDT",         "Accessories",    "Grip",       null,                                                    0m,    1),

        // Vortex — price and qty to be confirmed
        new("VG-VTX-RANGER-HD-3000",       "Vortex Ranger HD 3000 Rangefinder",     "Vortex",      "Optics",         "Rangefinder","Ranger HD 3000 laser rangefinder",                     0m,     1),
        new("VG-VTX-TALON-HD-12X50",       "Vortex Talon HD 12x50 Binocular",       "Vortex",      "Optics",         "Binocular",  "Talon HD full-size binocular",                         0m,     1),
        new("VG-VTX-ACE",                  "Vortex ACE Rangefinder",                "Vortex",      "Optics",         "Rangefinder","ACE laser rangefinder",                                0m,     1),

        // Garmin — price to be confirmed
        new("VG-GRM-XERO-C1-PRO",          "Garmin Xero C1 Pro Chronograph",        "Garmin",      "Electronics",    "Chronograph","Xero C1 Pro ballistic chronograph",                    0m,     2),

        // Apex Optics (retail incl VAT)
        new("VG-APX-SUMMIT-ED-10X42",      "Apex Summit ED 10x42 Binocular",        "Apex Optics", "Optics",         "Binocular",  "Summit ED 10x42 binocular",                        11_150m,   2),
        new("VG-APX-SUMMIT-PRO2-15X56",    "Apex Summit Pro 2 15x56 Binocular",     "Apex Optics", "Optics",         "Binocular",  "Summit Pro 2 15x56 binocular (display demo)",      35_995m,   0),
        new("VG-APX-RIVAL-X-4-32X56-FFP",  "Apex Rival X 4-32x56 FFP Riflescope",   "Apex Optics", "Optics",         "Scope",      "Rival X 4-32x56 FFP riflescope (display demo, stock ~2 weeks after Huntex)", 49_995m, 0),
        new("VG-APX-ATOM-1X28",            "Apex Atom 1x28 Red Dot",                "Apex Optics", "Optics",         "Red Dot",    "Atom 1x28 red dot sight",                           6_995m,   1),
        new("VG-APX-FUSION-1X30",          "Apex Fusion 1x30 Red Dot",              "Apex Optics", "Optics",         "Red Dot",    "Fusion 1x30 red dot sight",                        11_195m,   3),
        new("VG-APX-ONYX-34MM-MED",        "Apex Onyx 34mm Medium Rings",           "Apex Optics", "Accessories",    "Rings",      "Onyx 34mm medium-height scope rings",               2_795m,   5),

        // Fatboy (retail incl VAT)
        new("VG-FB-TRIPOD",                "Fatboy Tripod (no head)",               "Fatboy",      "Accessories",    "Tripod",     "Fatboy tripod, retail price same across all models (sold without head)", 16_995m, 0),
        new("VG-FB-INVERT-60-BALLHEAD",    "Fatboy Invert 60 Ballhead",             "Fatboy",      "Accessories",    "Tripod Head","Invert 60 ballhead",                                 6_495m,   2),
        new("VG-FB-LEVITATE-LEVELHEAD",    "Fatboy Levitate Levelhead",             "Fatboy",      "Accessories",    "Tripod Head","Levitate levelhead",                                 6_795m,   2),
        new("VG-FB-RECHARGE-BOWL",         "Fatboy Recharge Bowl",                  "Fatboy",      "Accessories",    "Tripod Head","Recharge bowl-mount head",                           5_795m,   2),
        new("VG-FB-SIDECHICK",             "Fatboy Sidechick",                      "Fatboy",      "Accessories",    "Tripod Accessory", "Sidechick accessory mount",                    1_495m,   5),
    };

    public static async Task SeedAsync(HuntexDbContext db, ILogger log, CancellationToken ct = default)
    {
        var supplier = await db.Suppliers.FirstOrDefaultAsync(s => s.Name == SupplierName, ct);
        if (supplier == null)
        {
            supplier = new Supplier
            {
                Id = Guid.NewGuid(),
                Name = SupplierName,
                Notes = SupplierNotes,
                DefaultCurrency = "ZAR",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            db.Suppliers.Add(supplier);
            await db.SaveChangesAsync(ct);
            log.LogInformation("Seeded supplier {Name} ({Id}).", supplier.Name, supplier.Id);
        }

        var existingSkus = await db.Products
            .Where(p => Items.Select(i => i.Sku).Contains(p.Sku))
            .Select(p => p.Sku)
            .ToListAsync(ct);
        var existingSet = new HashSet<string>(existingSkus, StringComparer.OrdinalIgnoreCase);

        var newProducts = new List<Product>();
        var newReceipts = new List<StockReceipt>();
        foreach (var item in Items)
        {
            if (existingSet.Contains(item.Sku)) continue;

            var pricingMethod = item.SellPrice > 0 ? "fixed_price" : "default";
            var product = new Product
            {
                Id = Guid.NewGuid(),
                SupplierId = supplier.Id,
                Sku = item.Sku,
                Name = item.Name,
                Description = item.Description,
                Category = item.Category,
                Manufacturer = item.Manufacturer,
                ItemType = item.ItemType,
                Cost = 0m,
                SellPrice = item.SellPrice,
                PricingMethod = pricingMethod,
                FixedSellPrice = item.SellPrice > 0 ? item.SellPrice : null,
                QtyOnHand = 0,
                QtyConsignment = item.QtyConsignment,
                Active = true,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            newProducts.Add(product);

            if (item.QtyConsignment > 0)
            {
                newReceipts.Add(new StockReceipt
                {
                    Id = Guid.NewGuid(),
                    ProductId = product.Id,
                    SupplierId = supplier.Id,
                    Type = StockReceiptType.ConsignmentIn,
                    Quantity = item.QtyConsignment,
                    CostPrice = null,
                    Notes = "Seed: Huntex 2026 opening consignment",
                    ProcessedBy = "system-seeder",
                    CreatedAt = DateTimeOffset.UtcNow
                });
            }
        }

        if (newProducts.Count == 0)
        {
            log.LogInformation("Venatics Gear: all {Count} seed products already present, nothing to do.", Items.Length);
            return;
        }

        db.Products.AddRange(newProducts);
        if (newReceipts.Count > 0) db.StockReceipts.AddRange(newReceipts);
        await db.SaveChangesAsync(ct);

        log.LogInformation(
            "Venatics Gear: seeded {Products} product(s) and {Receipts} consignment receipt(s).",
            newProducts.Count, newReceipts.Count);
    }
}
