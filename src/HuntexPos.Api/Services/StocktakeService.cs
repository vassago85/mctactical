using HuntexPos.Api.Data;
using HuntexPos.Api.Domain;
using HuntexPos.Api.DTOs;
using Microsoft.EntityFrameworkCore;

namespace HuntexPos.Api.Services;

public class StocktakeService
{
    private readonly HuntexDbContext _db;

    public StocktakeService(HuntexDbContext db) => _db = db;

    public async Task<StocktakeSessionDto> CreateSessionAsync(string name, string userId, CancellationToken ct)
    {
        var s = new StocktakeSession
        {
            Id = Guid.NewGuid(),
            Name = name,
            Status = StocktakeStatus.Draft,
            CreatedByUserId = userId
        };
        _db.StocktakeSessions.Add(s);
        await _db.SaveChangesAsync(ct);
        return await GetSessionAsync(s.Id, ct) ?? throw new InvalidOperationException();
    }

    public async Task<StocktakeSessionDto?> GetSessionAsync(Guid id, CancellationToken ct)
    {
        var s = await _db.StocktakeSessions
            .Include(x => x.Lines)
            .ThenInclude(l => l.Product)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        return s == null ? null : Map(s);
    }

    public async Task<StocktakeLineDto> UpsertLineAsync(Guid sessionId, AddStocktakeLineRequest req, CancellationToken ct)
    {
        var session = await _db.StocktakeSessions.FirstOrDefaultAsync(x => x.Id == sessionId, ct)
                      ?? throw new InvalidOperationException("Session not found");
        if (session.Status != StocktakeStatus.Draft)
            throw new InvalidOperationException("Session is not editable");

        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == req.ProductId, ct)
                      ?? throw new InvalidOperationException("Product not found");

        var line = await _db.StocktakeLines.FirstOrDefaultAsync(
            l => l.StocktakeSessionId == sessionId && l.ProductId == req.ProductId, ct);
        if (line == null)
        {
            line = new StocktakeLine
            {
                Id = Guid.NewGuid(),
                StocktakeSessionId = sessionId,
                ProductId = product.Id,
                QtyBefore = product.QtyOnHand,
                QtyCounted = req.QtyCounted
            };
            _db.StocktakeLines.Add(line);
        }
        else
        {
            line.QtyCounted = req.QtyCounted;
        }
        await _db.SaveChangesAsync(ct);
        return MapLine(line, product);
    }

    public async Task PostSessionAsync(Guid sessionId, string userId, CancellationToken ct)
    {
        var session = await _db.StocktakeSessions
            .Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct)
                      ?? throw new InvalidOperationException("Session not found");
        if (session.Status != StocktakeStatus.Draft)
            return;

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        foreach (var line in session.Lines)
        {
            var p = await _db.Products.FirstOrDefaultAsync(x => x.Id == line.ProductId, ct);
            if (p == null) continue;
            p.QtyOnHand = line.QtyCounted;
            p.UpdatedAt = DateTimeOffset.UtcNow;
        }
        session.Status = StocktakeStatus.Posted;
        session.PostedAt = DateTimeOffset.UtcNow;
        session.PostedByUserId = userId;
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private static StocktakeSessionDto Map(StocktakeSession s) => new()
    {
        Id = s.Id,
        Name = s.Name,
        Status = s.Status.ToString(),
        CreatedAt = s.CreatedAt,
        Lines = s.Lines.Select(l => MapLine(l, l.Product!)).ToList()
    };

    private static StocktakeLineDto MapLine(StocktakeLine l, Product p) => new()
    {
        Id = l.Id,
        ProductId = l.ProductId,
        ProductName = p.Name,
        Sku = p.Sku,
        QtyBefore = l.QtyBefore,
        QtyCounted = l.QtyCounted
    };
}
