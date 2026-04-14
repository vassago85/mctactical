using HuntexPos.Api.Data;
using HuntexPos.Api.Domain;
using HuntexPos.Api.DTOs;
using HuntexPos.Api.Options;
using HuntexPos.Api.Services;
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
    private readonly IEffectiveMailgunProvider _effectiveMail;
    private readonly IOptions<MailgunOptions> _mailCfg;

    public SettingsController(
        HuntexDbContext db,
        IOptions<PosRulesOptions> posRules,
        IEffectiveMailgunProvider effectiveMail,
        IOptions<MailgunOptions> mailCfg)
    {
        _db = db;
        _posRules = posRules.Value;
        _effectiveMail = effectiveMail;
        _mailCfg = mailCfg;
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
        s.DefaultTaxRate = 0m;
        s.HideCostForSalesRole = dto.HideCostForSalesRole;
        s.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return dto;
    }

    [HttpGet("mail")]
    [Authorize(Roles = $"{Roles.Owner},{Roles.Admin},{Roles.Dev}")]
    public async Task<MailSettingsDto> GetMail(CancellationToken ct)
    {
        var row = await _db.MailSettings.AsNoTracking().FirstOrDefaultAsync(ct);
        var cfg = _mailCfg.Value;
        var defaultBase = string.IsNullOrWhiteSpace(cfg.BaseUrl) ? "https://api.mailgun.net/v3" : cfg.BaseUrl.Trim();

        var dto = new MailSettingsDto
        {
            Domain = row != null ? row.Domain : (cfg.Domain ?? ""),
            From = row != null ? row.SenderFrom : (cfg.From ?? ""),
            BaseUrl = row != null && !string.IsNullOrWhiteSpace(row.BaseUrl) ? row.BaseUrl.Trim() : defaultBase,
            AttachPdf = row?.AttachPdf ?? cfg.AttachPdf
        };

        var eff = await _effectiveMail.GetAsync(ct);
        dto.HasApiKey = !string.IsNullOrWhiteSpace(eff.ApiKey);
        return dto;
    }

    [HttpPut("mail")]
    [Authorize(Roles = $"{Roles.Owner},{Roles.Admin},{Roles.Dev}")]
    public async Task<MailSettingsDto> PutMail([FromBody] MailSettingsUpdateDto body, CancellationToken ct)
    {
        var row = await _db.MailSettings.FirstOrDefaultAsync(ct);
        if (row == null)
        {
            row = new MailSettings();
            _db.MailSettings.Add(row);
        }

        if (!string.IsNullOrWhiteSpace(body.ApiKey))
            row.ApiKey = body.ApiKey.Trim();

        row.Domain = body.Domain?.Trim() ?? "";
        row.SenderFrom = body.From?.Trim() ?? "";
        row.BaseUrl = string.IsNullOrWhiteSpace(body.BaseUrl)
            ? "https://api.mailgun.net/v3"
            : body.BaseUrl.Trim();
        row.AttachPdf = body.AttachPdf;
        row.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);

        var eff = await _effectiveMail.GetAsync(ct);
        return new MailSettingsDto
        {
            Domain = row.Domain,
            From = row.SenderFrom,
            BaseUrl = row.BaseUrl,
            AttachPdf = row.AttachPdf,
            HasApiKey = !string.IsNullOrWhiteSpace(eff.ApiKey)
        };
    }
}
