using HuntexPos.Api.Data;
using HuntexPos.Api.Domain;
using HuntexPos.Api.DTOs;
using HuntexPos.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HuntexPos.Api.Controllers;

[ApiController]
[Route("api/pricing-rules")]
[Authorize(Roles = $"{Roles.Admin},{Roles.Owner},{Roles.Dev}")]
public class PricingRulesController : ControllerBase
{
    private readonly HuntexDbContext _db;
    private readonly IPricingService _pricing;

    public PricingRulesController(HuntexDbContext db, IPricingService pricing)
    {
        _db = db;
        _pricing = pricing;
    }

    [HttpGet]
    public async Task<ActionResult<List<PricingRuleDto>>> List(CancellationToken ct)
    {
        var rules = await _db.PricingRules.AsNoTracking()
            .OrderBy(r => r.Scope)
            .ThenBy(r => r.ScopeKey)
            .ToListAsync(ct);

        var supplierIds = rules.Where(r => r.SupplierId.HasValue).Select(r => r.SupplierId!.Value).Distinct().ToList();
        var suppliers = supplierIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.Suppliers.AsNoTracking()
                .Where(s => supplierIds.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id, s => s.Name, ct);

        return rules.Select(r => ToDto(r, suppliers)).ToList();
    }

    [HttpPost]
    [Authorize(Roles = $"{Roles.Owner},{Roles.Dev}")]
    public async Task<ActionResult<PricingRuleDto>> Create([FromBody] UpsertPricingRuleDto dto, CancellationToken ct)
    {
        if (!TryParseScope(dto.Scope, out var scope, out var scopeErr))
            return BadRequest(new { error = scopeErr });

        var (key, supplierId, normErr) = await NormalizeScopeTargetAsync(scope, dto.ScopeKey, dto.SupplierId, ct);
        if (normErr != null) return BadRequest(new { error = normErr });

        var exists = await _db.PricingRules.AnyAsync(r =>
            r.Scope == scope && r.ScopeKey == key && r.SupplierId == supplierId, ct);
        if (exists)
            return Conflict(new { error = "A pricing rule for this target already exists." });

        var rule = new PricingRule
        {
            Scope = scope,
            ScopeKey = key,
            SupplierId = supplierId,
            DefaultMarkupPercent = dto.DefaultMarkupPercent,
            MaxDiscountPercent = dto.MaxDiscountPercent,
            RoundToNearest = dto.RoundToNearest,
            MinMarginPercent = dto.MinMarginPercent,
            IsActive = dto.IsActive,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        _db.PricingRules.Add(rule);
        await _db.SaveChangesAsync(ct);

        var suppliers = supplierId.HasValue
            ? await _db.Suppliers.AsNoTracking()
                .Where(s => s.Id == supplierId.Value)
                .ToDictionaryAsync(s => s.Id, s => s.Name, ct)
            : new Dictionary<Guid, string>();

        return CreatedAtAction(nameof(List), ToDto(rule, suppliers));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = $"{Roles.Owner},{Roles.Dev}")]
    public async Task<ActionResult<PricingRuleDto>> Update(Guid id, [FromBody] UpsertPricingRuleDto dto, CancellationToken ct)
    {
        var rule = await _db.PricingRules.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (rule == null) return NotFound();

        rule.DefaultMarkupPercent = dto.DefaultMarkupPercent;
        rule.MaxDiscountPercent = dto.MaxDiscountPercent;
        rule.RoundToNearest = dto.RoundToNearest;
        rule.MinMarginPercent = dto.MinMarginPercent;
        rule.IsActive = dto.IsActive;
        rule.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        var suppliers = rule.SupplierId.HasValue
            ? await _db.Suppliers.AsNoTracking()
                .Where(s => s.Id == rule.SupplierId.Value)
                .ToDictionaryAsync(s => s.Id, s => s.Name, ct)
            : new Dictionary<Guid, string>();

        return ToDto(rule, suppliers);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = $"{Roles.Owner},{Roles.Dev}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var rule = await _db.PricingRules.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (rule == null) return NotFound();
        if (rule.Scope == PricingRuleScope.Global)
            return BadRequest(new { error = "The Global rule cannot be deleted. Edit its values or deactivate it instead." });

        _db.PricingRules.Remove(rule);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("preview")]
    public async Task<ActionResult<PricingPreviewResponseDto>> Preview([FromBody] PricingPreviewRequestDto req, CancellationToken ct)
    {
        var res = await _pricing.PreviewAsync(
            req.Cost, req.Category, req.Manufacturer, req.SupplierId,
            req.PricingMethod, req.CustomMarkupPercent, req.FixedSellPrice, req.MinSellPrice, ct);

        return new PricingPreviewResponseDto
        {
            SellPrice = res.SellPrice,
            MinAllowedPrice = res.MinAllowedPrice,
            Source = res.Source,
            PricingMethod = res.PricingMethod,
            EffectiveMarkupPercent = res.Effective.MarkupPercent,
            EffectiveMaxDiscountPercent = res.Effective.MaxDiscountPercent,
            EffectiveRoundToNearest = res.Effective.RoundToNearest,
            EffectiveMinMarginPercent = res.Effective.MinMarginPercent
        };
    }

    private static PricingRuleDto ToDto(PricingRule r, Dictionary<Guid, string> suppliers) => new()
    {
        Id = r.Id,
        Scope = r.Scope.ToString(),
        ScopeKey = r.ScopeKey,
        SupplierId = r.SupplierId,
        SupplierName = r.SupplierId.HasValue && suppliers.TryGetValue(r.SupplierId.Value, out var n) ? n : null,
        DefaultMarkupPercent = r.DefaultMarkupPercent,
        MaxDiscountPercent = r.MaxDiscountPercent,
        RoundToNearest = r.RoundToNearest,
        MinMarginPercent = r.MinMarginPercent,
        IsActive = r.IsActive,
        UpdatedAt = r.UpdatedAt
    };

    private static bool TryParseScope(string? raw, out PricingRuleScope scope, out string? error)
    {
        scope = PricingRuleScope.Global;
        if (Enum.TryParse<PricingRuleScope>(raw, true, out var s))
        {
            scope = s;
            error = null;
            return true;
        }
        error = "Invalid scope. Use Global, Category, Manufacturer, or Supplier.";
        return false;
    }

    private async Task<(string? key, Guid? supplierId, string? error)> NormalizeScopeTargetAsync(
        PricingRuleScope scope, string? rawKey, Guid? rawSupplierId, CancellationToken ct)
    {
        switch (scope)
        {
            case PricingRuleScope.Global:
                if (await _db.PricingRules.AnyAsync(r => r.Scope == PricingRuleScope.Global, ct))
                    return (null, null, null);
                return (null, null, null);

            case PricingRuleScope.Category:
            case PricingRuleScope.Manufacturer:
                var key = rawKey?.Trim();
                if (string.IsNullOrEmpty(key))
                    return (null, null, $"A {scope} name is required.");
                return (key, null, null);

            case PricingRuleScope.Supplier:
                if (!rawSupplierId.HasValue)
                    return (null, null, "A supplier is required for a supplier-scoped rule.");
                var exists = await _db.Suppliers.AnyAsync(s => s.Id == rawSupplierId.Value, ct);
                if (!exists)
                    return (null, null, "Supplier not found.");
                return (null, rawSupplierId, null);
        }

        return (null, null, "Unsupported scope.");
    }
}
