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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Product>(e =>
        {
            e.HasIndex(p => p.Sku);
            e.HasIndex(p => p.Barcode);
            e.HasIndex(p => p.Name);
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
            e.Property(l => l.LineDiscount).HasPrecision(18, 2);
            e.Property(l => l.LineTotal).HasPrecision(18, 2);
        });

        modelBuilder.Entity<PricingSettings>(e =>
        {
            e.Property(p => p.DefaultMarginPercent).HasPrecision(18, 4);
            e.Property(p => p.DefaultFixedMarkup).HasPrecision(18, 2);
            e.Property(p => p.DefaultTaxRate).HasPrecision(18, 4);
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
    }
}
