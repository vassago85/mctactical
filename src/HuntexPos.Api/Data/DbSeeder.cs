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
        await EnsureBusinessSettingsTableAsync(db, ct);
        await EnsurePricingColumnsAsync(db, ct);
        await EnsureProductColumnsAsync(db, ct);
        await EnsureInvoiceLineColumnsAsync(db, ct);
        await EnsureStockReceiptsTableAsync(db, ct);
        await EnsureSupplierColumnsAsync(db, ct);
        await EnsureInvoiceVoidColumnsAsync(db, ct);
        await EnsurePromotionsTablesAsync(db, ct);
        await EnsureInvoiceDiscountColumnsAsync(db, ct);
        await EnsureStockReceiptCostColumnAsync(db, ct);
        await EnsureInvoiceBusinessColumnsAsync(db, ct);
        await EnsureCustomersTableAsync(db, ct);
        await EnsureConsignmentBatchesTablesAsync(db, ct);
        await EnsureConsignmentBatchColumnsAsync(db, ct);
        await EnsureSpecialOrderColumnsAsync(db, ct);
        await EnsureProductPricingColumnsAsync(db, ct);
        await EnsurePricingRulesTableAsync(db, ct);
        await EnsureImportPresetsSupplierNullableAsync(db, ct);
        await EnsureQuotesTablesAsync(db, ct);
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

        if (!await db.PricingRules.AnyAsync(r => r.Scope == PricingRuleScope.Global, ct))
        {
            var legacy = await db.PricingSettings.AsNoTracking().FirstOrDefaultAsync(ct)
                         ?? new PricingSettings();
            db.PricingRules.Add(new PricingRule
            {
                Scope = PricingRuleScope.Global,
                ScopeKey = null,
                SupplierId = null,
                DefaultMarkupPercent = legacy.UseMarginPercent ? legacy.DefaultMarginPercent : 0m,
                MaxDiscountPercent = 100m,
                RoundToNearest = legacy.RoundSellToNearest > 0 ? legacy.RoundSellToNearest : 10m,
                MinMarginPercent = null,
                IsActive = true,
                UpdatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync(ct);
        }

        var appCfg = scope.ServiceProvider.GetRequiredService<IOptions<AppOptions>>().Value;
        if (!await db.BusinessSettings.AnyAsync(ct))
        {
            db.BusinessSettings.Add(new BusinessSettings
            {
                BusinessName = (appCfg.CompanyDisplayName ?? "").Trim(),
                LegalName = (appCfg.CompanyDisplayName ?? "").Trim(),
                VatNumber = (appCfg.CompanyVatNumber ?? "").Trim(),
                Email = (appCfg.CompanyEmail ?? "").Trim(),
                Phone = (appCfg.CompanyPhone ?? "").Trim(),
                Address = (appCfg.CompanyAddress ?? "").Trim(),
                Website = (appCfg.CompanyWebsite ?? "").Trim(),
                WebsiteLabel = (appCfg.CompanyWebsiteLabel ?? "").Trim(),
                UpdatedAt = DateTimeOffset.UtcNow
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

    /// <summary>Add IsActive + UpdatedAt columns to Suppliers if missing (older DBs).</summary>
    private static async Task EnsureSupplierColumnsAsync(HuntexDbContext db, CancellationToken ct)
    {
        if (!db.Database.IsSqlite()) return;
        try { await db.Database.ExecuteSqlRawAsync(
            """ALTER TABLE "Suppliers" ADD COLUMN "IsActive" INTEGER NOT NULL DEFAULT 1;""", ct); } catch { }
        // NOTE: default is a safe mid-range UTC datetime, NOT 0001-01-01. SQLite stores
        // DateTimeOffset as TEXT without offset, and EF reads it back using the server's
        // local timezone. A '0001-01-01 00:00:00' literal on a +02:00 host becomes
        // year 0 in UTC, which overflows DateTimeOffset.UtcDateTime (the error:
        // "The UTC representation of the date '0001-01-01 00:00:00' falls outside the
        // year range 1-9999"). 1970-01-01 is safely representable in every timezone.
        try { await db.Database.ExecuteSqlRawAsync(
            """ALTER TABLE "Suppliers" ADD COLUMN "UpdatedAt" TEXT NOT NULL DEFAULT '1970-01-01 00:00:00';""", ct); } catch { }
        // Backfill any existing rows that were stamped with the old unsafe default.
        try { await db.Database.ExecuteSqlRawAsync(
            """UPDATE "Suppliers" SET "UpdatedAt" = "CreatedAt" WHERE "UpdatedAt" = '0001-01-01 00:00:00';""", ct); } catch { }
    }

    /// <summary>Add VoidedAt + VoidedByUserId to Invoices if missing (older DBs).</summary>
    private static async Task EnsureInvoiceVoidColumnsAsync(HuntexDbContext db, CancellationToken ct)
    {
        if (!db.Database.IsSqlite()) return;
        try { await db.Database.ExecuteSqlRawAsync(
            """ALTER TABLE "Invoices" ADD COLUMN "VoidedAt" TEXT NULL;""", ct); } catch { }
        try { await db.Database.ExecuteSqlRawAsync(
            """ALTER TABLE "Invoices" ADD COLUMN "VoidedByUserId" TEXT NULL;""", ct); } catch { }
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

    /// <summary>Add SourceDocumentRef/SourceDocumentPath to batches + UnitCost/UnitCostChanged to lines (older DBs).</summary>
    private static async Task EnsureConsignmentBatchColumnsAsync(HuntexDbContext db, CancellationToken ct)
    {
        if (!db.Database.IsSqlite()) return;
        try { await db.Database.ExecuteSqlRawAsync(
            """ALTER TABLE "ConsignmentBatches" ADD COLUMN "SourceDocumentRef" TEXT NULL;""", ct); } catch { }
        try { await db.Database.ExecuteSqlRawAsync(
            """ALTER TABLE "ConsignmentBatches" ADD COLUMN "SourceDocumentPath" TEXT NULL;""", ct); } catch { }
        try { await db.Database.ExecuteSqlRawAsync(
            """ALTER TABLE "ConsignmentBatchLines" ADD COLUMN "UnitCost" TEXT NULL;""", ct); } catch { }
        try { await db.Database.ExecuteSqlRawAsync(
            """ALTER TABLE "ConsignmentBatchLines" ADD COLUMN "UnitCostChanged" INTEGER NOT NULL DEFAULT 0;""", ct); } catch { }
    }

    private static async Task EnsureSpecialOrderColumnsAsync(HuntexDbContext db, CancellationToken ct)
    {
        if (!db.Database.IsSqlite()) return;
        try { await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "Invoices" ADD COLUMN "IsSpecialOrder" INTEGER NOT NULL DEFAULT 0;""", ct); } catch { }
        try { await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "Invoices" ADD COLUMN "IsDelivered" INTEGER NOT NULL DEFAULT 0;""", ct); } catch { }
        try { await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "Invoices" ADD COLUMN "DeliveredAt" TEXT;""", ct); } catch { }
        try { await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "Invoices" ADD COLUMN "DeliveryNotes" TEXT;""", ct); } catch { }
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
                var group = (await db.Products
                    .Where(p => p.Sku == sku)
                    .ToListAsync(ct))
                    .OrderByDescending(p => p.UpdatedAt ?? p.CreatedAt)
                    .ToList();

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

    /// <summary>Adds product-level pricing override columns on older DBs.</summary>
    private static async Task EnsureProductPricingColumnsAsync(HuntexDbContext db, CancellationToken ct)
    {
        if (!db.Database.IsSqlite()) return;
        try { await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "Products" ADD COLUMN "PricingMethod" TEXT NOT NULL DEFAULT 'default';""", ct); } catch { }
        try { await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "Products" ADD COLUMN "CustomMarkupPercent" TEXT;""", ct); } catch { }
        try { await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "Products" ADD COLUMN "FixedSellPrice" TEXT;""", ct); } catch { }
        try { await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "Products" ADD COLUMN "MinSellPrice" TEXT;""", ct); } catch { }
        try { await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "Products" ADD COLUMN "PriceLocked" INTEGER NOT NULL DEFAULT 0;""", ct); } catch { }
    }

    /// <summary>
    /// Older deployments had ImportPresets.SupplierId as NOT NULL. The wholesaler-agnostic
    /// wizard now allows null. SQLite does not support relaxing nullability with ALTER, so we
    /// rebuild the table only when the existing column is still NOT NULL.
    /// </summary>
    private static async Task EnsureImportPresetsSupplierNullableAsync(HuntexDbContext db, CancellationToken ct)
    {
        if (!db.Database.IsSqlite()) return;
        try
        {
            var connection = db.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync(ct);
            bool needsRebuild = false;
            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "PRAGMA table_info(\"ImportPresets\");";
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    var name = reader["name"]?.ToString();
                    if (string.Equals(name, "SupplierId", StringComparison.OrdinalIgnoreCase))
                    {
                        var notnull = Convert.ToInt32(reader["notnull"]);
                        if (notnull == 1) needsRebuild = true;
                        break;
                    }
                }
            }
            if (!needsRebuild) return;

            await db.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE IF NOT EXISTS "ImportPresets_new" (
                    "Id" TEXT NOT NULL CONSTRAINT "PK_ImportPresets_new" PRIMARY KEY,
                    "SupplierId" TEXT NULL,
                    "Name" TEXT NOT NULL,
                    "ColumnMappingJson" TEXT NOT NULL,
                    "UpdatedAt" TEXT NOT NULL
                );
                """, ct);
            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO "ImportPresets_new" ("Id","SupplierId","Name","ColumnMappingJson","UpdatedAt")
                SELECT "Id","SupplierId","Name","ColumnMappingJson","UpdatedAt" FROM "ImportPresets";
                """, ct);
            await db.Database.ExecuteSqlRawAsync("""DROP TABLE "ImportPresets";""", ct);
            await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "ImportPresets_new" RENAME TO "ImportPresets";""", ct);
        }
        catch
        {
            /* migration is best-effort; a future deploy will retry */
        }
    }

    /// <summary>Creates the PricingRules table on older DBs; global row seeded in SeedAsync.</summary>
    private static async Task EnsurePricingRulesTableAsync(HuntexDbContext db, CancellationToken ct)
    {
        if (!db.Database.IsSqlite()) return;
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "PricingRules" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_PricingRules" PRIMARY KEY,
                "Scope" TEXT NOT NULL DEFAULT 'Global',
                "ScopeKey" TEXT,
                "SupplierId" TEXT,
                "DefaultMarkupPercent" TEXT,
                "MaxDiscountPercent" TEXT,
                "RoundToNearest" TEXT,
                "MinMarginPercent" TEXT,
                "IsActive" INTEGER NOT NULL DEFAULT 1,
                "UpdatedAt" TEXT NOT NULL
            );
            """, ct);
        try { await db.Database.ExecuteSqlRawAsync(
            """CREATE UNIQUE INDEX IF NOT EXISTS "IX_PricingRules_Scope_Key_Supplier" ON "PricingRules" ("Scope", "ScopeKey", "SupplierId");""", ct); } catch { }
    }

    /// <summary>Creates Quotes and QuoteLines tables on older DBs.</summary>
    private static async Task EnsureQuotesTablesAsync(HuntexDbContext db, CancellationToken ct)
    {
        if (!db.Database.IsSqlite()) return;
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "Quotes" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_Quotes" PRIMARY KEY,
                "QuoteNumber" TEXT NOT NULL,
                "Status" TEXT NOT NULL DEFAULT 'Draft',
                "CustomerId" TEXT,
                "CustomerName" TEXT,
                "CustomerEmail" TEXT,
                "CustomerPhone" TEXT,
                "CustomerCompany" TEXT,
                "CustomerAddress" TEXT,
                "CustomerVatNumber" TEXT,
                "SubTotal" TEXT NOT NULL DEFAULT '0',
                "DiscountTotal" TEXT NOT NULL DEFAULT '0',
                "TaxRate" TEXT NOT NULL DEFAULT '0',
                "TaxAmount" TEXT NOT NULL DEFAULT '0',
                "GrandTotal" TEXT NOT NULL DEFAULT '0',
                "PublicNotes" TEXT,
                "InternalNotes" TEXT,
                "ValidUntil" TEXT,
                "PublicToken" TEXT NOT NULL,
                "PdfStorageKey" TEXT,
                "ConvertedInvoiceId" TEXT,
                "ConvertedAt" TEXT,
                "CreatedByUserId" TEXT,
                "CreatedAt" TEXT NOT NULL,
                "UpdatedAt" TEXT
            );
            """, ct);
        try { await db.Database.ExecuteSqlRawAsync(
            """CREATE UNIQUE INDEX IF NOT EXISTS "IX_Quotes_QuoteNumber" ON "Quotes" ("QuoteNumber");""", ct); } catch { }
        try { await db.Database.ExecuteSqlRawAsync(
            """CREATE UNIQUE INDEX IF NOT EXISTS "IX_Quotes_PublicToken" ON "Quotes" ("PublicToken");""", ct); } catch { }
        try { await db.Database.ExecuteSqlRawAsync(
            """CREATE INDEX IF NOT EXISTS "IX_Quotes_Status" ON "Quotes" ("Status");""", ct); } catch { }
        try { await db.Database.ExecuteSqlRawAsync(
            """CREATE INDEX IF NOT EXISTS "IX_Quotes_CreatedAt" ON "Quotes" ("CreatedAt");""", ct); } catch { }

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "QuoteLines" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_QuoteLines" PRIMARY KEY,
                "QuoteId" TEXT NOT NULL,
                "ProductId" TEXT,
                "Sku" TEXT,
                "ItemName" TEXT NOT NULL DEFAULT '',
                "Description" TEXT,
                "Quantity" INTEGER NOT NULL DEFAULT 1,
                "UnitCost" TEXT,
                "UnitPrice" TEXT NOT NULL DEFAULT '0',
                "DiscountPercent" TEXT,
                "DiscountAmount" TEXT,
                "TaxRate" TEXT NOT NULL DEFAULT '0',
                "LineTotal" TEXT NOT NULL DEFAULT '0',
                "SortOrder" INTEGER NOT NULL DEFAULT 0,
                CONSTRAINT "FK_QuoteLines_Quotes" FOREIGN KEY ("QuoteId") REFERENCES "Quotes"("Id") ON DELETE CASCADE
            );
            """, ct);
        try { await db.Database.ExecuteSqlRawAsync(
            """CREATE INDEX IF NOT EXISTS "IX_QuoteLines_QuoteId" ON "QuoteLines" ("QuoteId");""", ct); } catch { }
        try { await db.Database.ExecuteSqlRawAsync(
            """CREATE INDEX IF NOT EXISTS "IX_QuoteLines_ProductId" ON "QuoteLines" ("ProductId");""", ct); } catch { }
    }

    /// <summary>Creates BusinessSettings table on older DBs (singleton row seeded in SeedAsync).</summary>
    private static async Task EnsureBusinessSettingsTableAsync(HuntexDbContext db, CancellationToken ct)
    {
        if (!db.Database.IsSqlite()) return;
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "BusinessSettings" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_BusinessSettings" PRIMARY KEY,
                "BusinessName" TEXT NOT NULL DEFAULT '',
                "LegalName" TEXT NOT NULL DEFAULT '',
                "VatNumber" TEXT NOT NULL DEFAULT '',
                "Currency" TEXT NOT NULL DEFAULT 'ZAR',
                "TimeZone" TEXT NOT NULL DEFAULT 'Africa/Johannesburg',
                "Email" TEXT NOT NULL DEFAULT '',
                "Phone" TEXT NOT NULL DEFAULT '',
                "Address" TEXT NOT NULL DEFAULT '',
                "Website" TEXT NOT NULL DEFAULT '',
                "WebsiteLabel" TEXT NOT NULL DEFAULT '',
                "LogoStorageKey" TEXT,
                "FaviconStorageKey" TEXT,
                "PrimaryColor" TEXT NOT NULL DEFAULT '',
                "SecondaryColor" TEXT NOT NULL DEFAULT '',
                "AccentColor" TEXT NOT NULL DEFAULT '',
                "ReceiptFooter" TEXT NOT NULL DEFAULT '',
                "QuoteTerms" TEXT NOT NULL DEFAULT '',
                "InvoiceTerms" TEXT NOT NULL DEFAULT '',
                "ReturnPolicy" TEXT NOT NULL DEFAULT '',
                "QuoteLabel" TEXT NOT NULL DEFAULT 'Quote',
                "InvoiceLabel" TEXT NOT NULL DEFAULT 'Invoice',
                "CustomerLabel" TEXT NOT NULL DEFAULT 'Customer',
                "EnableQuotes" INTEGER NOT NULL DEFAULT 1,
                "EnableDiscounts" INTEGER NOT NULL DEFAULT 1,
                "EnableBrandPricingRules" INTEGER NOT NULL DEFAULT 1,
                "UpdatedAt" TEXT NOT NULL
            );
            """,
            ct);
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
