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
    public DbSet<MailSettings> MailSettings => Set<MailSettings>();
    public DbSet<ImportPreset> ImportPresets => Set<ImportPreset>();
    public DbSet<StockReceipt> StockReceipts => Set<StockReceipt>();
    public DbSet<Promotion> Promotions => Set<Promotion>();
    public DbSet<ProductSpecial> ProductSpecials => Set<ProductSpecial>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<ConsignmentBatch> ConsignmentBatches => Set<ConsignmentBatch>();
    public DbSet<ConsignmentBatchLine> ConsignmentBatchLines => Set<ConsignmentBatchLine>();

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
            e.Property(p => p.SellPrice).HasPrecision(18, 2);
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
    }
}
