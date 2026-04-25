using HuntexPos.Api.Domain;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace HuntexPos.Api.Data;

public class HuntexDbContext : IdentityDbContext<ApplicationUser>
{
    public HuntexDbContext(DbContextOptions<HuntexDbContext> options) : base(options) { }

    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceLine> InvoiceLines => Set<InvoiceLine>();
    public DbSet<StocktakeSession> StocktakeSessions => Set<StocktakeSession>();
    public DbSet<StocktakeLine> StocktakeLines => Set<StocktakeLine>();
    public DbSet<PricingSettings> PricingSettings => Set<PricingSettings>();
    public DbSet<PricingRule> PricingRules => Set<PricingRule>();
    public DbSet<MailSettings> MailSettings => Set<MailSettings>();
    public DbSet<BusinessSettings> BusinessSettings => Set<BusinessSettings>();
    public DbSet<ImportPreset> ImportPresets => Set<ImportPreset>();
    public DbSet<StockReceipt> StockReceipts => Set<StockReceipt>();
    public DbSet<Promotion> Promotions => Set<Promotion>();
    public DbSet<ProductSpecial> ProductSpecials => Set<ProductSpecial>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<ConsignmentBatch> ConsignmentBatches => Set<ConsignmentBatch>();
    public DbSet<ConsignmentBatchLine> ConsignmentBatchLines => Set<ConsignmentBatchLine>();
    public DbSet<Quote> Quotes => Set<Quote>();
    public DbSet<QuoteLine> QuoteLines => Set<QuoteLine>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Product>(e =>
        {
            e.HasIndex(p => p.Sku).IsUnique();
            e.HasIndex(p => p.Barcode);
            e.HasIndex(p => p.Name);
            e.HasIndex(p => p.Manufacturer);
            e.HasIndex(p => p.ItemType);
            e.Property(p => p.Cost).HasPrecision(18, 2);
            e.Property(p => p.SupplierDiscountPercent).HasPrecision(18, 4);
            e.Property(p => p.SellPrice).HasPrecision(18, 2);
            e.Property(p => p.PricingMethod).HasMaxLength(32).HasDefaultValue("default");
            e.Property(p => p.CustomMarkupPercent).HasPrecision(18, 4);
            e.Property(p => p.FixedSellPrice).HasPrecision(18, 2);
            e.Property(p => p.MinSellPrice).HasPrecision(18, 2);
        });

        modelBuilder.Entity<PricingRule>(e =>
        {
            e.HasIndex(p => new { p.Scope, p.ScopeKey, p.SupplierId }).IsUnique();
            e.Property(p => p.Scope).HasConversion<string>().HasMaxLength(32);
            e.Property(p => p.ScopeKey).HasMaxLength(256);
            e.Property(p => p.DefaultMarkupPercent).HasPrecision(18, 4);
            e.Property(p => p.MaxDiscountPercent).HasPrecision(18, 4);
            e.Property(p => p.RoundToNearest).HasPrecision(18, 2);
            e.Property(p => p.MinMarginPercent).HasPrecision(18, 4);
        });

        modelBuilder.Entity<Invoice>(e =>
        {
            e.HasIndex(i => i.InvoiceNumber).IsUnique();
            e.HasIndex(i => i.PublicToken).IsUnique();
            e.Property(i => i.SubTotal).HasPrecision(18, 2);
            e.Property(i => i.TaxRate).HasPrecision(18, 2);
            e.Property(i => i.TaxAmount).HasPrecision(18, 2);
            e.Property(i => i.DiscountTotal).HasPrecision(18, 2);
            e.Property(i => i.GrandTotal).HasPrecision(18, 2);
        });

        modelBuilder.Entity<InvoiceLine>(e =>
        {
            e.Property(l => l.UnitPrice).HasPrecision(18, 2);
            e.Property(l => l.OriginalUnitPrice).HasPrecision(18, 2);
            e.Property(l => l.LineDiscount).HasPrecision(18, 2);
            e.Property(l => l.LineTotal).HasPrecision(18, 2);
            e.Property(l => l.CostAtSale).HasPrecision(18, 2);
        });

        modelBuilder.Entity<PricingSettings>(e =>
        {
            e.Property(p => p.DefaultMarginPercent).HasPrecision(18, 4);
            e.Property(p => p.DefaultFixedMarkup).HasPrecision(18, 2);
            e.Property(p => p.DefaultTaxRate).HasPrecision(18, 4);
            e.Property(p => p.RoundSellToNearest).HasPrecision(18, 2);
            e.Property(p => p.PricingMode).HasMaxLength(20).HasDefaultValue("normal");
        });

        modelBuilder.Entity<MailSettings>(e =>
        {
            e.Property(m => m.SenderFrom).HasMaxLength(512);
            e.Property(m => m.Domain).HasMaxLength(256);
            e.Property(m => m.BaseUrl).HasMaxLength(512);
        });

        modelBuilder.Entity<BusinessSettings>(e =>
        {
            e.Property(b => b.BusinessName).HasMaxLength(256);
            e.Property(b => b.LegalName).HasMaxLength(256);
            e.Property(b => b.VatNumber).HasMaxLength(64);
            e.Property(b => b.Currency).HasMaxLength(8);
            e.Property(b => b.TimeZone).HasMaxLength(64);
            e.Property(b => b.Email).HasMaxLength(256);
            e.Property(b => b.Phone).HasMaxLength(64);
            e.Property(b => b.Website).HasMaxLength(512);
            e.Property(b => b.WebsiteLabel).HasMaxLength(256);
            e.Property(b => b.LogoStorageKey).HasMaxLength(256);
            e.Property(b => b.FaviconStorageKey).HasMaxLength(256);
            e.Property(b => b.PrimaryColor).HasMaxLength(16);
            e.Property(b => b.SecondaryColor).HasMaxLength(16);
            e.Property(b => b.AccentColor).HasMaxLength(16);
            e.Property(b => b.QuoteLabel).HasMaxLength(32);
            e.Property(b => b.InvoiceLabel).HasMaxLength(32);
            e.Property(b => b.CustomerLabel).HasMaxLength(32);
        });

        modelBuilder.Entity<Invoice>()
            .HasMany(i => i.Lines)
            .WithOne(l => l.Invoice)
            .HasForeignKey(l => l.InvoiceId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<StocktakeSession>()
            .HasMany(s => s.Lines)
            .WithOne(l => l.Session)
            .HasForeignKey(l => l.StocktakeSessionId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<StockReceipt>(e =>
        {
            e.HasIndex(r => r.ProductId);
            e.HasIndex(r => r.SupplierId);
            e.Property(r => r.Type).HasConversion<string>().HasMaxLength(30);
            e.Property(r => r.CostPrice).HasPrecision(18, 2);
        });

        modelBuilder.Entity<Promotion>(e =>
        {
            e.Property(p => p.DiscountPercent).HasPrecision(18, 4);
        });

        modelBuilder.Entity<ProductSpecial>(e =>
        {
            e.HasIndex(s => s.ProductId);
            e.HasIndex(s => s.PromotionId);
            e.Property(s => s.SpecialPrice).HasPrecision(18, 2);
            e.Property(s => s.DiscountPercent).HasPrecision(18, 4);
        });

        modelBuilder.Entity<Customer>(e =>
        {
            e.HasIndex(c => c.Email).IsUnique();
        });

        modelBuilder.Entity<ConsignmentBatch>(e =>
        {
            e.HasIndex(b => b.SupplierId);
            e.HasIndex(b => b.Status);
            e.Property(b => b.Type).HasConversion<string>().HasMaxLength(20);
            e.Property(b => b.Status).HasConversion<string>().HasMaxLength(20);
            e.HasMany(b => b.Lines)
                .WithOne(l => l.Batch)
                .HasForeignKey(l => l.BatchId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Quote>(e =>
        {
            e.HasIndex(q => q.QuoteNumber).IsUnique();
            e.HasIndex(q => q.PublicToken).IsUnique();
            e.HasIndex(q => q.Status);
            e.HasIndex(q => q.CreatedAt);
            e.Property(q => q.QuoteNumber).HasMaxLength(40);
            e.Property(q => q.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(q => q.SubTotal).HasPrecision(18, 2);
            e.Property(q => q.DiscountTotal).HasPrecision(18, 2);
            e.Property(q => q.TaxRate).HasPrecision(18, 4);
            e.Property(q => q.TaxAmount).HasPrecision(18, 2);
            e.Property(q => q.GrandTotal).HasPrecision(18, 2);
            e.HasMany(q => q.Lines)
                .WithOne(l => l.Quote)
                .HasForeignKey(l => l.QuoteId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<QuoteLine>(e =>
        {
            e.HasIndex(l => l.QuoteId);
            e.HasIndex(l => l.ProductId);
            e.Property(l => l.UnitCost).HasPrecision(18, 2);
            e.Property(l => l.UnitPrice).HasPrecision(18, 2);
            e.Property(l => l.DiscountPercent).HasPrecision(18, 4);
            e.Property(l => l.DiscountAmount).HasPrecision(18, 2);
            e.Property(l => l.TaxRate).HasPrecision(18, 4);
            e.Property(l => l.LineTotal).HasPrecision(18, 2);
            e.Property(l => l.Sku).HasMaxLength(64);
            e.Property(l => l.ItemName).HasMaxLength(512);
        });
    }
}
