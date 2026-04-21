using HuntexPos.Api.Domain;
using HuntexPos.Api.DTOs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HuntexPos.Api.Tests;

public class OwnedReceiveCommitTests
{
    [Fact]
    public async Task OwnedReceive_Commit_IncrementsQtyOnHand_WritesOwnedInReceipt()
    {
        using var tdb = new TestDb();

        var created = await ControllerFactory.MakeConsignmentBatchesController(tdb).Create(new CreateConsignmentBatchRequest
        {
            Type = "OwnedReceive",
            SupplierId = tdb.Supplier.Id,
            SourceDocumentRef = "INV-001"
        }, CancellationToken.None);
        var batchId = ControllerFactory.Unwrap(created).Id;

        await ControllerFactory.MakeConsignmentBatchesController(tdb).AddLine(batchId, new AddBatchLineRequest
        {
            ProductId = tdb.ProductA.Id,
            ExpectedQty = 4,
            UnitCost = 110m
        }, CancellationToken.None);

        using (var ctx = tdb.NewContext())
        {
            var loaded = await ctx.ConsignmentBatches.Include(b => b.Lines).FirstAsync(b => b.Id == batchId);
            loaded.Lines.Single().CheckedQty = 4;
            await ctx.SaveChangesAsync();
        }

        var committed = ControllerFactory.Unwrap(
            await ControllerFactory.MakeConsignmentBatchesController(tdb).Commit(batchId, updateCosts: false, CancellationToken.None));
        Assert.Equal("Committed", committed.Status);

        var productAfter = await tdb.Db.Products.AsNoTracking().FirstAsync(p => p.Id == tdb.ProductA.Id);
        Assert.Equal(4, productAfter.QtyOnHand);
        Assert.Equal(0, productAfter.QtyConsignment);
        Assert.Equal(100m, productAfter.Cost);

        var receipt = await tdb.Db.StockReceipts.AsNoTracking().SingleAsync(r => r.ProductId == tdb.ProductA.Id);
        Assert.Equal(StockReceiptType.OwnedIn, receipt.Type);
        Assert.Equal(4, receipt.Quantity);
        Assert.Equal(110m, receipt.CostPrice);
        Assert.Contains("INV-001", receipt.Notes);

        var lineAfter = await tdb.Db.ConsignmentBatchLines.AsNoTracking().SingleAsync(l => l.BatchId == batchId);
        Assert.True(lineAfter.UnitCostChanged);
    }

    [Fact]
    public async Task OwnedReceive_Commit_WithUpdateCostsTrue_UpdatesProductCost()
    {
        using var tdb = new TestDb();

        var created = await ControllerFactory.MakeConsignmentBatchesController(tdb).Create(new CreateConsignmentBatchRequest
        {
            Type = "OwnedReceive",
            SupplierId = tdb.Supplier.Id
        }, CancellationToken.None);
        var batchId = ControllerFactory.Unwrap(created).Id;

        await ControllerFactory.MakeConsignmentBatchesController(tdb).AddLine(batchId, new AddBatchLineRequest
        {
            ProductId = tdb.ProductA.Id,
            ExpectedQty = 2,
            UnitCost = 125m
        }, CancellationToken.None);

        using (var ctx = tdb.NewContext())
        {
            var loaded = await ctx.ConsignmentBatches.Include(b => b.Lines).FirstAsync(b => b.Id == batchId);
            loaded.Lines.Single().CheckedQty = 2;
            await ctx.SaveChangesAsync();
        }

        await ControllerFactory.MakeConsignmentBatchesController(tdb).Commit(batchId, updateCosts: true, CancellationToken.None);

        var productAfter = await tdb.Db.Products.AsNoTracking().FirstAsync(p => p.Id == tdb.ProductA.Id);
        Assert.Equal(125m, productAfter.Cost);
        Assert.Equal(2, productAfter.QtyOnHand);
    }

    [Fact]
    public async Task OwnedReceive_Commit_WithNoUnitCost_DoesNotMarkChanged_LeavesCostUntouched()
    {
        using var tdb = new TestDb();

        var created = await ControllerFactory.MakeConsignmentBatchesController(tdb).Create(new CreateConsignmentBatchRequest
        {
            Type = "OwnedReceive",
            SupplierId = tdb.Supplier.Id
        }, CancellationToken.None);
        var batchId = ControllerFactory.Unwrap(created).Id;

        await ControllerFactory.MakeConsignmentBatchesController(tdb).AddLine(batchId, new AddBatchLineRequest
        {
            ProductId = tdb.ProductA.Id,
            ExpectedQty = 3
        }, CancellationToken.None);

        using (var ctx = tdb.NewContext())
        {
            var loaded = await ctx.ConsignmentBatches.Include(b => b.Lines).FirstAsync(b => b.Id == batchId);
            loaded.Lines.Single().CheckedQty = 3;
            await ctx.SaveChangesAsync();
        }

        await ControllerFactory.MakeConsignmentBatchesController(tdb).Commit(batchId, updateCosts: true, CancellationToken.None);

        var productAfter = await tdb.Db.Products.AsNoTracking().FirstAsync(p => p.Id == tdb.ProductA.Id);
        Assert.Equal(100m, productAfter.Cost);
        Assert.Equal(3, productAfter.QtyOnHand);

        var lineAfter = await tdb.Db.ConsignmentBatchLines.AsNoTracking().SingleAsync(l => l.BatchId == batchId);
        Assert.False(lineAfter.UnitCostChanged);
    }
}
