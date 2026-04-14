using HuntexPos.Api.Data;
using HuntexPos.Api.Domain;
using HuntexPos.Api.DTOs;
using HuntexPos.Api.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HuntexPos.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SettingsController : ControllerBase
{
    private readonly HuntexDbContext _db;
    private readonly PosRulesOptions _posRules;

    public SettingsController(HuntexDbContext db, IOptions<PosRulesOptions> posRules)
    {
        _db = db;
        _posRules = posRules.Value;
    }

    [HttpGet("pos-rules")]
    [Authorize(Roles = $"{Roles.Sales},{Roles.Admin},{Roles.Owner},{Roles.Dev}")]
    public ActionResult<PosRulesDto> GetPosRules()
    {
        return new PosRulesDto
        {
            MaxCartDiscountPercent = _posRules.MaxCartDiscountPercent,
            MaxLineDiscountPercent = _posRules.MaxLineDiscountPercent,
            MaxPriceDecreasePercentFromList = _posRules.MaxPriceDecreasePercentFromList,
            MaxPriceIncreasePercentFromList = _posRules.MaxPriceIncreasePercentFromList,
            BlockZeroOrNegativeTotal = _posRules.BlockZeroOrNegativeTotal
        };
    }

    [HttpGet("pricing")]
    [Authorize(Roles = $"{Roles.Owner},{Roles.Admin},{Roles.Dev}")]
    public async Task<PricingSettingsDto> GetPricing(CancellationToken ct)
    {
        var s = await _db.PricingSettings.AsNoTracking().FirstOrDefaultAsync(ct) ?? new PricingSettings();
        return new PricingSettingsDto
        {
            DefaultMarginPercent = s.DefaultMarginPercent,
            DefaultFixedMarkup = s.DefaultFixedMarkup,
            UseMarginPercent = s.UseMarginPercent,
            DefaultTaxRate = s.DefaultTaxRate,
            HideCostForSalesRole = s.HideCostForSalesRole
        };
    }

    [HttpPut("pricing")]
    [Authorize(Roles = $"{Roles.Owner},{Roles.Admin},{Roles.Dev}")]
    public async Task<PricingSettingsDto> PutPricing([FromBody] PricingSettingsDto dto, CancellationToken ct)
    {
        var s = await _db.PricingSettings.FirstOrDefaultAsync(ct);
        if (s == null)
        {
            s = new PricingSettings { Id = Guid.Parse("00000000-0000-0000-0000-000000000001") };
            _db.PricingSettings.Add(s);
        }
        s.DefaultMarginPercent = dto.DefaultMarginPercent;
        s.DefaultFixedMarkup = dto.DefaultFixedMarkup;
        s.UseMarginPercent = dto.UseMarginPercent;
        s.DefaultTaxRate = dto.DefaultTaxRate;
        s.HideCostForSalesRole = dto.HideCostForSalesRole;
        s.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return dto;
    }
}
