using HuntexPos.Api.Data;
using HuntexPos.Api.Domain;
using HuntexPos.Api.DTOs;
using HuntexPos.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HuntexPos.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = $"{Roles.Admin},{Roles.Owner},{Roles.Dev}")]
public class PromotionsController : ControllerBase
{
    private readonly HuntexDbContext _db;

    public PromotionsController(HuntexDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<List<PromotionDto>>> List(CancellationToken ct)
    {
        try
        {
            var promos = await _db.Promotions.AsNoTracking().ToListAsync(ct);
            promos = promos.OrderByDescending(p => p.CreatedAt).ToList();

            var specials = await _db.ProductSpecials.AsNoTracking()
                .Where(s => s.PromotionId != null)
                .Select(s => s.PromotionId!.Value)
                .ToListAsync(ct);
            var countMap = specials.GroupBy(id => id).ToDictionary(g => g.Key, g => g.Count());

            return promos.Select(p => new PromotionDto
            {
                Id = p.Id,
                Name = p.Name,
                DiscountPercent = p.DiscountPercent,
                IsActive = p.IsActive,
                StartsAt = p.StartsAt,
                EndsAt = p.EndsAt,
                CreatedAt = p.CreatedAt,
                SpecialsCount = countMap.GetValueOrDefault(p.Id)
            }).ToList();
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message, detail = ex.InnerException?.Message });
        }
    }

    [HttpPost]
    public async Task<ActionResult<PromotionDto>> Create([FromBody] CreatePromotionRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
            return BadRequest(new { error = "Name is required." });

        var promo = new Promotion
        {
            Id = Guid.NewGuid(),
            Name = req.Name.Trim(),
            DiscountPercent = req.DiscountPercent,
            IsActive = req.IsActive,
            StartsAt = req.StartsAt,
            EndsAt = req.EndsAt
        };
        _db.Promotions.Add(promo);

        if (req.IsActive)
            await DeactivateOtherPromotions(promo.Id, ct);

        await _db.SaveChangesAsync(ct);
        return Ok(MapPromo(promo, 0));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<PromotionDto>> Update(Guid id, [FromBody] UpdatePromotionRequest req, CancellationToken ct)
    {
        var promo = await _db.Promotions.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (promo == null) return NotFound();

        if (req.Name != null) promo.Name = req.Name.Trim();
        if (req.DiscountPercent.HasValue) promo.DiscountPercent = req.DiscountPercent.Value;
        if (req.StartsAt.HasValue) promo.StartsAt = req.StartsAt;
        if (req.EndsAt.HasValue) promo.EndsAt = req.EndsAt;
        if (req.IsActive.HasValue)
        {
            promo.IsActive = req.IsActive.Value;
            if (promo.IsActive)
                await DeactivateOtherPromotions(promo.Id, ct);
        }

        await _db.SaveChangesAsync(ct);
        var count = await _db.ProductSpecials.CountAsync(s => s.PromotionId == id, ct);
        return Ok(MapPromo(promo, count));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var promo = await _db.Promotions.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (promo == null) return NotFound();

        var specials = await _db.ProductSpecials.Where(s => s.PromotionId == id).ToListAsync(ct);
        _db.ProductSpecials.RemoveRange(specials);
        _db.Promotions.Remove(promo);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    /* ── Product specials ── */

    [HttpGet("{promoId:guid}/specials")]
    public async Task<List<ProductSpecialDto>> ListSpecials(Guid promoId, CancellationToken ct)
    {
        var specials = await _db.ProductSpecials.AsNoTracking()
            .Include(s => s.Product)
            .Include(s => s.Promotion)
            .Where(s => s.PromotionId == promoId)
            .OrderBy(s => s.Product!.Name)
            .ToListAsync(ct);
        return specials.Select(MapSpecial).ToList();
    }

    [HttpPost("specials")]
    public async Task<ActionResult<ProductSpecialDto>> CreateSpecial([FromBody] CreateProductSpecialRequest req, CancellationToken ct)
    {
        var product = await _db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == req.ProductId, ct);
        if (product == null) return BadRequest(new { error = "Product not found." });

        if (req.PromotionId.HasValue && !await _db.Promotions.AnyAsync(p => p.Id == req.PromotionId.Value, ct))
            return BadRequest(new { error = "Promotion not found." });

        if (!req.SpecialPrice.HasValue && !req.DiscountPercent.HasValue)
            return BadRequest(new { error = "Either specialPrice or discountPercent is required." });

        var existing = await _db.ProductSpecials.FirstOrDefaultAsync(
            s => s.ProductId == req.ProductId && s.PromotionId == req.PromotionId, ct);
        if (existing != null)
            return BadRequest(new { error = "A special already exists for this product on this promotion." });

        var special = new ProductSpecial
        {
            Id = Guid.NewGuid(),
            ProductId = req.ProductId,
            PromotionId = req.PromotionId,
            SpecialPrice = req.SpecialPrice,
            DiscountPercent = req.DiscountPercent,
            IsActive = req.IsActive
        };
        _db.ProductSpecials.Add(special);
        await _db.SaveChangesAsync(ct);

        special.Product = product;
        if (req.PromotionId.HasValue)
            special.Promotion = await _db.Promotions.AsNoTracking().FirstOrDefaultAsync(p => p.Id == req.PromotionId.Value, ct);
        return Ok(MapSpecial(special));
    }

    [HttpPut("specials/{id:guid}")]
    public async Task<ActionResult<ProductSpecialDto>> UpdateSpecial(Guid id, [FromBody] UpdateProductSpecialRequest req, CancellationToken ct)
    {
        var special = await _db.ProductSpecials.Include(s => s.Product).Include(s => s.Promotion)
            .FirstOrDefaultAsync(s => s.Id == id, ct);
        if (special == null) return NotFound();

        if (req.SpecialPrice.HasValue) special.SpecialPrice = req.SpecialPrice;
        if (req.DiscountPercent.HasValue) special.DiscountPercent = req.DiscountPercent;
        if (req.IsActive.HasValue) special.IsActive = req.IsActive.Value;
        await _db.SaveChangesAsync(ct);
        return Ok(MapSpecial(special));
    }

    [HttpDelete("specials/{id:guid}")]
    public async Task<IActionResult> DeleteSpecial(Guid id, CancellationToken ct)
    {
        var special = await _db.ProductSpecials.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (special == null) return NotFound();
        _db.ProductSpecials.Remove(special);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    /* ── Active promotion (for POS) ── */

    [HttpGet("active")]
    [Authorize(Roles = $"{Roles.Sales},{Roles.Admin},{Roles.Owner},{Roles.Dev}")]
    public async Task<ActivePromotionDto> GetActive(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var allActive = await _db.Promotions.AsNoTracking()
            .Where(p => p.IsActive)
            .ToListAsync(ct);
        var promo = allActive
            .Where(p => !p.StartsAt.HasValue || p.StartsAt <= now)
            .Where(p => !p.EndsAt.HasValue || p.EndsAt >= now)
            .FirstOrDefault();

        var specialsQuery = _db.ProductSpecials.AsNoTracking().Where(s => s.IsActive);
        if (promo != null)
            specialsQuery = specialsQuery.Where(s => s.PromotionId == null || s.PromotionId == promo.Id);
        else
            specialsQuery = specialsQuery.Where(s => s.PromotionId == null);

        var rawSpecials = await specialsQuery.ToListAsync(ct);

        // Deduplicate per product: promotion-linked specials take priority over standalone ones
        var specials = rawSpecials
            .GroupBy(s => s.ProductId)
            .Select(g => g.OrderByDescending(s => s.PromotionId.HasValue).First())
            .Select(s => new ActiveSpecialDto
            {
                ProductId = s.ProductId,
                SpecialPrice = s.SpecialPrice,
                DiscountPercent = s.DiscountPercent
            })
            .ToList();

        return new ActivePromotionDto
        {
            PromotionId = promo?.Id,
            PromotionName = promo?.Name,
            SiteDiscountPercent = promo?.DiscountPercent ?? 0,
            Specials = specials
        };
    }

    private async Task DeactivateOtherPromotions(Guid keepId, CancellationToken ct)
    {
        var others = await _db.Promotions.Where(p => p.IsActive && p.Id != keepId).ToListAsync(ct);
        foreach (var o in others) o.IsActive = false;
    }

    private static PromotionDto MapPromo(Promotion p, int specialsCount) => new()
    {
        Id = p.Id,
        Name = p.Name,
        DiscountPercent = p.DiscountPercent,
        IsActive = p.IsActive,
        StartsAt = p.StartsAt,
        EndsAt = p.EndsAt,
        CreatedAt = p.CreatedAt,
        SpecialsCount = specialsCount
    };

    private static ProductSpecialDto MapSpecial(ProductSpecial s)
    {
        var basePrice = s.Product?.SellPrice ?? 0;
        decimal effective;
        if (s.SpecialPrice.HasValue)
            effective = s.SpecialPrice.Value;
        else if (s.DiscountPercent.HasValue)
            effective = Math.Ceiling(basePrice * (1 - s.DiscountPercent.Value / 100m) / 10m) * 10m;
        else
            effective = basePrice;

        return new ProductSpecialDto
        {
            Id = s.Id,
            ProductId = s.ProductId,
            ProductSku = s.Product?.Sku ?? "",
            ProductName = s.Product?.Name ?? "",
            BaseSellPrice = basePrice,
            PromotionId = s.PromotionId,
            PromotionName = s.Promotion?.Name,
            SpecialPrice = s.SpecialPrice,
            DiscountPercent = s.DiscountPercent,
            EffectivePrice = effective,
            IsActive = s.IsActive
        };
    }
}
