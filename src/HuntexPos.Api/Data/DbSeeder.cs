using System.Linq;
using HuntexPos.Api.Domain;
using HuntexPos.Api.Options;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HuntexPos.Api.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(IServiceProvider sp, CancellationToken ct = default)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HuntexDbContext>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var seedOpt = scope.ServiceProvider.GetRequiredService<IOptions<SeedOptions>>().Value;
        var log = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DbSeeder");

        await db.Database.EnsureCreatedAsync(ct);
        await EnsureMailSettingsTableAsync(db, ct);
        await EnsurePricingColumnsAsync(db, ct);
        await EnsureProductColumnsAsync(db, ct);
        await EnsureInvoiceLineColumnsAsync(db, ct);
        await EnsureStockReceiptsTableAsync(db, ct);
        await EnsurePromotionsTablesAsync(db, ct);
        await EnsureInvoiceDiscountColumnsAsync(db, ct);
        await EnsureStockReceiptCostColumnAsync(db, ct);
        await EnsureInvoiceBusinessColumnsAsync(db, ct);
        await EnsureCustomersTableAsync(db, ct);
        await EnsureConsignmentBatchesTablesAsync(db, ct);
        await MergeDuplicateSkusAsync(db, log, ct);

        foreach (var r in Roles.All)
        {
            if (!await roleManager.RoleExistsAsync(r))
                await roleManager.CreateAsync(new IdentityRole(r));
        }

        if (!await db.PricingSettings.AnyAsync(ct))
        {
            db.PricingSettings.Add(new PricingSettings
            {
                DefaultMarginPercent = 50,
                DefaultFixedMarkup = 0,
                UseMarginPercent = true,
                PricingMode = "normal",
                RoundSellToNearest = 10,
                DefaultTaxRate = 0,
                HideCostForSalesRole = true
            });
            await db.SaveChangesAsync(ct);
        }

        var mailCfg = scope.ServiceProvider.GetRequiredService<IOptions<MailgunOptions>>().Value;
        if (!await db.MailSettings.AnyAsync(ct))
        {
            db.MailSettings.Add(new MailSettings
            {
                ApiKey = mailCfg.ApiKey ?? "",
                Domain = mailCfg.Domain ?? "",
                SenderFrom = mailCfg.From ?? "",
                BaseUrl = string.IsNullOrWhiteSpace(mailCfg.BaseUrl) ? "https://api.mailgun.net/v3" : mailCfg.BaseUrl.Trim(),
                AttachPdf = mailCfg.AttachPdf,
                UpdatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync(ct);
        }

        var ownerEmail = seedOpt.OwnerEmail?.Trim() ?? string.Empty;
        var ownerPassword = seedOpt.OwnerPassword ?? string.Empty;
        if (string.IsNullOrWhiteSpace(ownerEmail) || string.IsNullOrWhiteSpace(ownerPassword))
        {
            log.LogInformation(
                "Seed owner skipped: set Seed:OwnerEmail and Seed:OwnerPassword (e.g. env Seed__OwnerEmail / Seed__OwnerPassword) to create the first owner on startup.");
        }
        else if (await userManager.FindByEmailAsync(ownerEmail) == null)
        {
            var user = new ApplicationUser
            {
                UserName = ownerEmail,
                Email = ownerEmail,
                EmailConfirmed = true,
                DisplayName = "Owner"
            };
            var result = await userManager.CreateAsync(user, ownerPassword);
            if (result.Succeeded)
            {
                await userManager.AddToRolesAsync(user, new[] { Roles.Owner, Roles.Admin, Roles.Dev, Roles.Sales });
                log.LogInformation("Seeded owner account for {Email}.", ownerEmail);
            }
            else
            {
                log.LogWarning("Could not seed owner {Email}: {Errors}", ownerEmail,
                    string.Join("; ", result.Errors.Select(e => e.Description)));
            }
        }
    }

    /// <summary>Add RoundSellToNearest and PricingMode columns to PricingSettings if missing (older DBs).</summary>
    private static async Task EnsurePricingColumnsAsync(HuntexDbContext db, CancellationToken ct)
    {
        if (!db.Database.IsSqlite()) return;
        try { await db.Database.ExecuteSqlRawAsync(
            """ALTER TABLE "PricingSettings" ADD COLUMN "RoundSellToNearest" TEXT NOT NULL DEFAULT '10';""", ct); } catch { }
        try { await db.Database.ExecuteSqlRawAsync(
            """ALTER TABLE "PricingSettings" ADD COLUMN "PricingMode" TEXT NOT NULL DEFAULT 'normal';""", ct); } catch { }
    }

    /// <summary>Add Manufacturer, ItemType, QtyConsignment columns to Products if missing (older DBs).</summary>
    private static async Task EnsureProductColumnsAsync(HuntexDbContext db, CancellationToken ct)
    {
        if (!db.Database.IsSqlite()) return;
        try { await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "Products" ADD COLUMN "Manufacturer" TEXT;""", ct); } catch { }
        try { await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "Products" ADD COLUMN "ItemType" TEXT;""", ct); } catch { }
        try { await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "Products" ADD COLUMN "QtyConsignment" INTEGER NOT NULL DEFAULT 0;""", ct); } catch { }
    }

    /// <summary>Add CostAtSale to InvoiceLines for GP reporting (older DBs).</summary>
    private static async Task EnsureInvoiceLineColumnsAsync(HuntexDbContext db, CancellationToken ct)
    {
        if (!db.Database.IsSqlite()) return;
        try { await db.Database.ExecuteSqlRawAsync(
            """ALTER TABLE "InvoiceLines" ADD COLUMN "CostAtSale" TEXT NOT NULL DEFAULT '0';""", ct); } catch { }
    }

    private static async Task EnsureStockReceiptsTableAsync(HuntexDbContext db, CancellationToken ct)
    {
        if (!db.Database.IsSqlite()) return;
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "StockReceipts" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_StockReceipts" PRIMARY KEY,
                "ProductId" TEXT NOT NULL,
                "SupplierId" TEXT,
                "Type" TEXT NOT NULL,
                "Quantity" INTEGER NOT NULL,
                "Notes" TEXT,
                "ProcessedBy" TEXT,
                "CreatedAt" TEXT NOT NULL,
                CONSTRAINT "FK_StockReceipts_Products_ProductId" FOREIGN KEY ("ProductId") REFERENCES "Products" ("Id") ON DELETE CASCADE,
                CONSTRAINT "FK_StockReceipts_Suppliers_SupplierId" FOREIGN KEY ("SupplierId") REFERENCES "Suppliers" ("Id")
            );
            """, ct);
        try { await db.Database.ExecuteSqlRawAsync("""CREATE INDEX IF NOT EXISTS "IX_StockReceipts_ProductId" ON "StockReceipts" ("ProductId");""", ct); } catch { }
        try { await db.Database.ExecuteSqlRawAsync("""CREATE INDEX IF NOT EXISTS "IX_StockReceipts_SupplierId" ON "StockReceipts" ("SupplierId");""", ct); } catch { }
    }

    private static async Task EnsurePromotionsTablesAsync(HuntexDbContext db, CancellationToken ct)
    {
        if (!db.Database.IsSqlite()) return;
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "Promotions" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_Promotions" PRIMARY KEY,
                "Name" TEXT NOT NULL,
                "DiscountPercent" TEXT NOT NULL DEFAULT '0',
                "IsActive" INTEGER NOT NULL DEFAULT 0,
                "StartsAt" TEXT,
                "EndsAt" TEXT,
                "CreatedAt" TEXT NOT NULL
            );
            """, ct);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "ProductSpecials" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_ProductSpecials" PRIMARY KEY,
                "ProductId" TEXT NOT NULL,
                "PromotionId" TEXT,
                "SpecialPrice" TEXT,
                "DiscountPercent" TEXT,
                "IsActive" INTEGER NOT NULL DEFAULT 1,
                "CreatedAt" TEXT NOT NULL,
                CONSTRAINT "FK_ProductSpecials_Products_ProductId" FOREIGN KEY ("ProductId") REFERENCES "Products" ("Id") ON DELETE CASCADE,
                CONSTRAINT "FK_ProductSpecials_Promotions_PromotionId" FOREIGN KEY ("PromotionId") REFERENCES "Promotions" ("Id")
            );
            """, ct);
        try { await db.Database.ExecuteSqlRawAsync("""CREATE INDEX IF NOT EXISTS "IX_ProductSpecials_ProductId" ON "ProductSpecials" ("ProductId");""", ct); } catch { }
        try { await db.Database.ExecuteSqlRawAsync("""CREATE INDEX IF NOT EXISTS "IX_ProductSpecials_PromotionId" ON "ProductSpecials" ("PromotionId");""", ct); } catch { }
    }

    private static async Task EnsureInvoiceDiscountColumnsAsync(HuntexDbContext db, CancellationToken ct)
    {
        if (!db.Database.IsSqlite()) return;
        try { await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "InvoiceLines" ADD COLUMN "OriginalUnitPrice" TEXT NOT NULL DEFAULT '0';""", ct); } catch { }
        try { await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "Invoices" ADD COLUMN "PromotionName" TEXT;""", ct); } catch { }
    }

    private static async Task EnsureStockReceiptCostColumnAsync(HuntexDbContext db, CancellationToken ct)
    {
        if (!db.Database.IsSqlite()) return;
        try { await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "StockReceipts" ADD COLUMN "CostPrice" TEXT;""", ct); } catch { }
    }

    private static async Task EnsureCustomersTableAsync(HuntexDbContext db, CancellationToken ct)
    {
        if (!db.Database.IsSqlite()) return;
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "Customers" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_Customers" PRIMARY KEY,
                "Email" TEXT NOT NULL,
                "Name" TEXT,
                "Phone" TEXT,
                "Company" TEXT,
                "Address" TEXT,
                "VatNumber" TEXT,
                "CustomerType" TEXT,
                "CreatedAt" TEXT NOT NULL,
                "UpdatedAt" TEXT NOT NULL
            );
            """, ct);
        try { await db.Database.ExecuteSqlRawAsync("""CREATE UNIQUE INDEX IF NOT EXISTS "IX_Customers_Email" ON "Customers" ("Email");""", ct); } catch { }
    }

    private static async Task EnsureInvoiceBusinessColumnsAsync(HuntexDbContext db, CancellationToken ct)
    {
        if (!db.Database.IsSqlite()) return;
        try { await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "Invoices" ADD COLUMN "CustomerCompany" TEXT;""", ct); } catch { }
        try { await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "Invoices" ADD COLUMN "CustomerAddress" TEXT;""", ct); } catch { }
        try { await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "Invoices" ADD COLUMN "CustomerVatNumber" TEXT;""", ct); } catch { }
    }

    private static async Task EnsureConsignmentBatchesTablesAsync(HuntexDbContext db, CancellationToken ct)
    {
        if (!db.Database.IsSqlite()) return;
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "ConsignmentBatches" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_ConsignmentBatches" PRIMARY KEY,
                "SupplierId" TEXT NOT NULL,
                "Type" TEXT NOT NULL,
                "Status" TEXT NOT NULL,
                "Notes" TEXT,
                "CreatedBy" TEXT,
                "CreatedAt" TEXT NOT NULL,
                "CommittedAt" TEXT,
                CONSTRAINT "FK_ConsignmentBatches_Suppliers_SupplierId" FOREIGN KEY ("SupplierId") REFERENCES "Suppliers" ("Id") ON DELETE CASCADE
            );
            """, ct);
        try { await db.Database.ExecuteSqlRawAsync("""CREATE INDEX IF NOT EXISTS "IX_ConsignmentBatches_SupplierId" ON "ConsignmentBatches" ("SupplierId");""", ct); } catch { }
        try { await db.Database.ExecuteSqlRawAsync("""CREATE INDEX IF NOT EXISTS "IX_ConsignmentBatches_Status" ON "ConsignmentBatches" ("Status");""", ct); } catch { }

        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "ConsignmentBatchLines" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_ConsignmentBatchLines" PRIMARY KEY,
                "BatchId" TEXT NOT NULL,
                "ProductId" TEXT NOT NULL,
                "ExpectedQty" INTEGER NOT NULL,
                "CheckedQty" INTEGER NOT NULL,
                "Notes" TEXT,
                CONSTRAINT "FK_ConsignmentBatchLines_ConsignmentBatches_BatchId" FOREIGN KEY ("BatchId") REFERENCES "ConsignmentBatches" ("Id") ON DELETE CASCADE,
                CONSTRAINT "FK_ConsignmentBatchLines_Products_ProductId" FOREIGN KEY ("ProductId") REFERENCES "Products" ("Id") ON DELETE CASCADE
            );
            """, ct);
        try { await db.Database.ExecuteSqlRawAsync("""CREATE INDEX IF NOT EXISTS "IX_ConsignmentBatchLines_BatchId" ON "ConsignmentBatchLines" ("BatchId");""", ct); } catch { }
        try { await db.Database.ExecuteSqlRawAsync("""CREATE INDEX IF NOT EXISTS "IX_ConsignmentBatchLines_ProductId" ON "ConsignmentBatchLines" ("ProductId");""", ct); } catch { }
    }

    /// <summary>Auto-merge products with duplicate SKUs, then enforce a unique index.</summary>
    private static async Task MergeDuplicateSkusAsync(HuntexDbContext db, ILogger log, CancellationToken ct)
    {
        if (!db.Database.IsSqlite()) return;

        var dupes = await db.Products
            .GroupBy(p => p.Sku)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToListAsync(ct);

        if (dupes.Count > 0)
        {
            foreach (var sku in dupes)
            {
                var group = await db.Products
                    .Where(p => p.Sku == sku)
                    .OrderByDescending(p => p.UpdatedAt ?? p.CreatedAt)
                    .ToListAsync(ct);

                var survivor = group[0];
                var victims = group.Skip(1).ToList();
                var victimIds = victims.Select(v => v.Id).ToList();

                survivor.QtyOnHand += victims.Sum(v => v.QtyOnHand);
                survivor.QtyConsignment += victims.Sum(v => v.QtyConsignment);
                survivor.UpdatedAt = DateTimeOffset.UtcNow;

                await db.InvoiceLines
                    .Where(il => victimIds.Contains(il.ProductId))
                    .ExecuteUpdateAsync(s => s.SetProperty(il => il.ProductId, survivor.Id), ct);
                await db.StockReceipts
                    .Where(sr => victimIds.Contains(sr.ProductId))
                    .ExecuteUpdateAsync(s => s.SetProperty(sr => sr.ProductId, survivor.Id), ct);
                await db.StocktakeLines
                    .Where(sl => victimIds.Contains(sl.ProductId))
                    .ExecuteUpdateAsync(s => s.SetProperty(sl => sl.ProductId, survivor.Id), ct);
                await db.ConsignmentBatchLines
                    .Where(cl => victimIds.Contains(cl.ProductId))
                    .ExecuteUpdateAsync(s => s.SetProperty(cl => cl.ProductId, survivor.Id), ct);
                await db.ProductSpecials
                    .Where(ps => victimIds.Contains(ps.ProductId))
                    .ExecuteUpdateAsync(s => s.SetProperty(ps => ps.ProductId, survivor.Id), ct);

                db.Products.RemoveRange(victims);
            }

            await db.SaveChangesAsync(ct);
            log.LogInformation("Merged {Count} duplicate SKUs: {Skus}", dupes.Count, string.Join(", ", dupes));
        }

        try
        {
            await db.Database.ExecuteSqlRawAsync(
                """DROP INDEX IF EXISTS "IX_Products_Sku";""", ct);
            await db.Database.ExecuteSqlRawAsync(
                """CREATE UNIQUE INDEX IF NOT EXISTS "IX_Products_Sku" ON "Products" ("Sku");""", ct);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Could not create unique SKU index — duplicates may still exist.");
        }
    }

    /// <summary>Upgrades SQLite DBs created before MailSettings existed (EnsureCreated does not alter schema).</summary>
    private static async Task EnsureMailSettingsTableAsync(HuntexDbContext db, CancellationToken ct)
    {
        if (!db.Database.IsSqlite())
            return;

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "MailSettings" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_MailSettings" PRIMARY KEY,
                "ApiKey" TEXT NOT NULL,
                "Domain" TEXT NOT NULL,
                "SenderFrom" TEXT NOT NULL,
                "BaseUrl" TEXT NOT NULL,
                "AttachPdf" INTEGER NOT NULL,
                "UpdatedAt" TEXT NOT NULL
            );
            """,
            ct);
    }
}
