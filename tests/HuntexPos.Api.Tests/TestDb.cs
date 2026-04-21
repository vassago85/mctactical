using HuntexPos.Api.Data;
using HuntexPos.Api.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HuntexPos.Api.Tests;

/// <summary>
/// Builds an in-memory SQLite database seeded with a supplier + two products. The
/// connection is kept open for the lifetime of the fixture; each test gets a fresh
/// HuntexDbContext via <see cref="NewContext"/> so EF change-tracker state doesn't
/// leak across calls (which is how the real API works: one DbContext per request).
/// Callers must Dispose.
/// </summary>
public sealed class TestDb : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<HuntexDbContext> _options;

    /// <summary>Long-lived context for direct assertions in tests (e.g. verifying post-commit state).</summary>
    public HuntexDbContext Db { get; }

    public Supplier Supplier { get; }
    public Product ProductA { get; }
    public Product ProductB { get; }

    public TestDb()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();
        _options = new DbContextOptionsBuilder<HuntexDbContext>()
            .UseSqlite(_conn)
            .Options;

        using (var setup = new HuntexDbContext(_options))
        {
            setup.Database.EnsureCreated();
        }

        Db = new HuntexDbContext(_options);

        Supplier = new Supplier
        {
            Id = Guid.NewGuid(),
            Name = "Acme Wholesalers",
            DefaultCurrency = "ZAR",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        Db.Suppliers.Add(Supplier);

        ProductA = new Product
        {
            Id = Guid.NewGuid(),
            Sku = "SKU-A",
            Name = "Widget A",
            Barcode = "BAR-A",
            SupplierId = Supplier.Id,
            Cost = 100m,
            SellPrice = 150m,
            QtyOnHand = 0,
            QtyConsignment = 0,
            Active = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        ProductB = new Product
        {
            Id = Guid.NewGuid(),
            Sku = "SKU-B",
            Name = "Widget B",
            Barcode = "BAR-B",
            SupplierId = Supplier.Id,
            Cost = 250m,
            SellPrice = 375m,
            QtyOnHand = 5,
            QtyConsignment = 3,
            Active = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        Db.Products.AddRange(ProductA, ProductB);
        Db.SaveChanges();
    }

    /// <summary>New context sharing the same in-memory DB — use this when running controller actions.</summary>
    public HuntexDbContext NewContext() => new(_options);

    public void Dispose()
    {
        Db.Dispose();
        _conn.Dispose();
    }
}
