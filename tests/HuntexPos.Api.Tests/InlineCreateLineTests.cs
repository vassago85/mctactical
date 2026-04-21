using HuntexPos.Api.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HuntexPos.Api.Tests;

public class InlineCreateLineTests
{
    [Fact]
    public async Task InlineCreate_CreatesProductInheritingSupplier_WithBarcodeFallingBackToSku()
    {
        using var tdb = new TestDb();

        var created = await ControllerFactory.MakeConsignmentBatchesController(tdb).Create(new CreateConsignmentBatchRequest
        {
            Type = "OwnedReceive",
            SupplierId = tdb.Supplier.Id
        }, CancellationToken.None);
        var batchId = ControllerFactory.Unwrap(created).Id;

        var result = await ControllerFactory.MakeConsignmentBatchesController(tdb).InlineCreate(batchId, new InlineCreateProductRequest
        {
            Sku = "NEW-001",
            Name = "Brand New Widget",
            UnitCost = 55m,
            Qty = 2
        }, CancellationToken.None);

        var dto = ControllerFactory.Unwrap(result);
        Assert.Single(dto.Lines);
        Assert.Equal("NEW-001", dto.Lines[0].Sku);
        Assert.Equal(2, dto.Lines[0].CheckedQty);

        var product = await tdb.Db.Products.AsNoTracking().SingleAsync(p => p.Sku == "NEW-001");
        Assert.Equal("NEW-001", product.Barcode);
        Assert.Equal(tdb.Supplier.Id, product.SupplierId);
        Assert.Equal(55m, product.Cost);
        Assert.True(product.Active);
    }

    [Fact]
    public async Task InlineCreate_UsesProvidedBarcode_WhenSupplied()
    {
        using var tdb = new TestDb();

        var created = await ControllerFactory.MakeConsignmentBatchesController(tdb).Create(new CreateConsignmentBatchRequest
        {
            Type = "OwnedReceive",
            SupplierId = tdb.Supplier.Id
        }, CancellationToken.None);
        var batchId = ControllerFactory.Unwrap(created).Id;

        await ControllerFactory.MakeConsignmentBatchesController(tdb).InlineCreate(batchId, new InlineCreateProductRequest
        {
            Sku = "NEW-002",
            Name = "Widget With Barcode",
            Barcode = "6001234567890",
            Qty = 1
        }, CancellationToken.None);

        var product = await tdb.Db.Products.AsNoTracking().SingleAsync(p => p.Sku == "NEW-002");
        Assert.Equal("6001234567890", product.Barcode);
    }

    [Fact]
    public async Task InlineCreate_DuplicateSku_ReturnsBadRequestWithExistingId()
    {
        using var tdb = new TestDb();

        var created = await ControllerFactory.MakeConsignmentBatchesController(tdb).Create(new CreateConsignmentBatchRequest
        {
            Type = "OwnedReceive",
            SupplierId = tdb.Supplier.Id
        }, CancellationToken.None);
        var batchId = ControllerFactory.Unwrap(created).Id;

        var result = await ControllerFactory.MakeConsignmentBatchesController(tdb).InlineCreate(batchId, new InlineCreateProductRequest
        {
            Sku = tdb.ProductA.Sku,
            Name = "Should Not Create",
            Qty = 1
        }, CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.NotNull(bad.Value);
        var prop = bad.Value!.GetType().GetProperty("existingProductId");
        Assert.NotNull(prop);
        Assert.Equal(tdb.ProductA.Id, (Guid?)prop!.GetValue(bad.Value));
    }
}
