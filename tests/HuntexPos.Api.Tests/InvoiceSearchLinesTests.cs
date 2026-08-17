using HuntexPos.Api.Domain;
using Xunit;

namespace HuntexPos.Api.Tests;

public class InvoiceSearchLinesTests
{
    [Fact]
    public async Task SearchLines_ByInvoiceNumber_ReturnsMatchingLine()
    {
        using var tdb = new TestDb();
        SeedSale(tdb, "INV-2026-0042", "Jane Doe", tdb.ProductA, "Widget A");

        var result = await ControllerFactory.MakeInvoicesController(tdb)
            .SearchLines("0042", from: null, to: null);

        var rows = ControllerFactory.Unwrap(result);
        var hit = Assert.Single(rows);
        Assert.Equal("INV-2026-0042", hit.InvoiceNumber);
        Assert.Equal("SKU-A", hit.Sku);
        Assert.Equal("Jane Doe", hit.CustomerName);
    }

    [Fact]
    public async Task SearchLines_ByCustomerSkuAndBarcode_FindsTheSameSale()
    {
        using var tdb = new TestDb();
        SeedSale(tdb, "INV-2026-0099", "Sam Buyer", tdb.ProductB, "Widget B");

        foreach (var term in new[] { "Sam", "SKU-B", "BAR-B", "Widget B" })
        {
            var result = await ControllerFactory.MakeInvoicesController(tdb)
                .SearchLines(term, from: null, to: null);
            var hit = Assert.Single(ControllerFactory.Unwrap(result));
            Assert.Equal("INV-2026-0099", hit.InvoiceNumber);
        }
    }

    [Fact]
    public async Task SearchLines_ExcludesVoided_UnlessRequested()
    {
        using var tdb = new TestDb();
        SeedSale(tdb, "INV-2026-0100", "Void Customer", tdb.ProductA, "Widget A", InvoiceStatus.Voided);

        var hidden = await ControllerFactory.MakeInvoicesController(tdb)
            .SearchLines("Void", from: null, to: null);
        Assert.Empty(ControllerFactory.Unwrap(hidden));

        var shown = await ControllerFactory.MakeInvoicesController(tdb)
            .SearchLines("Void", from: null, to: null, includeVoided: true);
        Assert.Single(ControllerFactory.Unwrap(shown));
    }

    private static void SeedSale(
        TestDb tdb,
        string invoiceNumber,
        string customerName,
        Product product,
        string description,
        InvoiceStatus status = InvoiceStatus.Final)
    {
        var invoice = new Invoice
        {
            Id = Guid.NewGuid(),
            InvoiceNumber = invoiceNumber,
            Status = status,
            CustomerName = customerName,
            PaymentMethod = "Cash",
            GrandTotal = product.SellPrice,
            CreatedAt = DateTimeOffset.UtcNow,
            PublicToken = Guid.NewGuid()
        };
        invoice.Lines.Add(new InvoiceLine
        {
            Id = Guid.NewGuid(),
            InvoiceId = invoice.Id,
            ProductId = product.Id,
            Description = description,
            SkuAtSale = product.Sku,
            Quantity = 1,
            UnitPrice = product.SellPrice,
            OriginalUnitPrice = product.SellPrice,
            LineTotal = product.SellPrice
        });
        tdb.Db.Invoices.Add(invoice);
        tdb.Db.SaveChanges();
    }
}
