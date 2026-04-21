using HuntexPos.Api.Domain;
using HuntexPos.Api.DTOs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HuntexPos.Api.Tests;

/// <summary>
/// Protects the existing live consignment flow. If either of these tests fails during
/// the owned-stock refactor, stop — existing user data is at risk.
/// </summary>
public class ConsignmentCommitRegressionTests
{
    [Fact]
    public async Task Commit_ConsignmentReceive_IncrementsQtyConsignment_AndWritesStockReceipt()
    {
        using var tdb = new TestDb();

        var created = await ControllerFactory.MakeConsignmentBatchesController(tdb).Create(new CreateConsignmentBatchRequest
        {
            Type = "Receive",
            SupplierId = tdb.Supplier.Id
        }, CancellationToken.None);
        var batchId = ControllerFactory.Unwrap(created).Id;

        await ControllerFactory.MakeConsignmentBatchesController(tdb).AddLine(batchId, new AddBatchLineRequest
        {
            ProductId = tdb.ProductA.Id,
            ExpectedQty = 7
        }, CancellationToken.None);

        using (var ctx = tdb.NewContext())
        {
            var loaded = await ctx.ConsignmentBatches.Include(b => b.Lines).FirstAsync(b => b.Id == batchId);
            loaded.Lines.Single().CheckedQty = 7;
            await ctx.SaveChangesAsync();
        }

        var commit = await ControllerFactory.MakeConsignmentBatchesController(tdb).Commit(batchId, updateCosts: false, CancellationToken.None);
        var committed = ControllerFactory.Unwrap(commit);
        Assert.Equal("Committed", committed.Status);

        var productAfter = await tdb.Db.Products.AsNoTracking().FirstAsync(p => p.Id == tdb.ProductA.Id);
        Assert.Equal(7, productAfter.QtyConsignment);
        Assert.Equal(0, productAfter.QtyOnHand);

        var receipt = await tdb.Db.StockReceipts.AsNoTracking().SingleAsync(r => r.ProductId == tdb.ProductA.Id);
        Assert.Equal(StockReceiptType.ConsignmentIn, receipt.Type);
        Assert.Equal(7, receipt.Quantity);
        Assert.Equal(tdb.Supplier.Id, receipt.SupplierId);
    }

    [Fact]
    public async Task Commit_ConsignmentReturn_DecrementsQtyConsignment_AndWritesStockReceipt()
    {
        using var tdb = new TestDb();

        var created = await ControllerFactory.MakeConsignmentBatchesController(tdb).Create(new CreateConsignmentBatchRequest
        {
            Type = "Return",
            SupplierId = tdb.Supplier.Id
        }, CancellationToken.None);
        var batchId = ControllerFactory.Unwrap(created).Id;

        await ControllerFactory.MakeConsignmentBatchesController(tdb).AddLine(batchId, new AddBatchLineRequest
        {
            ProductId = tdb.ProductB.Id,
            ExpectedQty = 2
        }, CancellationToken.None);

        var commit = await ControllerFactory.MakeConsignmentBatchesController(tdb).Commit(batchId, updateCosts: false, CancellationToken.None);
        var committed = ControllerFactory.Unwrap(commit);
        Assert.Equal("Committed", committed.Status);

        var productAfter = await tdb.Db.Products.AsNoTracking().FirstAsync(p => p.Id == tdb.ProductB.Id);
        Assert.Equal(1, productAfter.QtyConsignment);
        Assert.Equal(5, productAfter.QtyOnHand);

        var receipt = await tdb.Db.StockReceipts.AsNoTracking().SingleAsync(r => r.ProductId == tdb.ProductB.Id);
        Assert.Equal(StockReceiptType.ConsignmentReturn, receipt.Type);
        Assert.Equal(2, receipt.Quantity);
    }
}
