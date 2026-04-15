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

    private readonly IEmailSender _email;
    private readonly AppOptions _app;

    public SettingsController(
        HuntexDbContext db,
        IOptions<PosRulesOptions> posRules,
        IEffectiveMailgunProvider effectiveMail,
        IOptions<MailgunOptions> mailCfg,
        IEmailSender email,
        IOptions<AppOptions> app)
    {
        _db = db;
        _posRules = posRules.Value;
        _effectiveMail = effectiveMail;
        _mailCfg = mailCfg;
        _email = email;
        _app = app.Value;
    }

    [HttpGet("pos-rules")]
    [Authorize(Roles = $"{Roles.Sales},{Roles.Admin},{Roles.Owner},{Roles.Dev}")]
    public ActionResult<PosRulesDto> GetPosRules()
    {
        var isManager = User.IsInRole(Roles.Admin) || User.IsInRole(Roles.Owner) || User.IsInRole(Roles.Dev);
        return new PosRulesDto
        {
            MaxCartDiscountPercent = _posRules.MaxCartDiscountPercent,
            MaxLineDiscountPercent = _posRules.MaxLineDiscountPercent,
            MaxPriceDecreasePercentFromList = _posRules.MaxPriceDecreasePercentFromList,
            MaxPriceIncreasePercentFromList = _posRules.MaxPriceIncreasePercentFromList,
            BlockZeroOrNegativeTotal = _posRules.BlockZeroOrNegativeTotal,
            IsManager = isManager
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
            PricingMode = s.PricingMode ?? "normal",
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
        var mode = (dto.PricingMode ?? "normal").Trim().ToLowerInvariant();
        s.PricingMode = mode == "huntex" ? "huntex" : "normal";
        s.RoundSellToNearest = 10;
        s.DefaultTaxRate = 0m;
        s.HideCostForSalesRole = dto.HideCostForSalesRole;
        s.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return dto;
    }

    [HttpGet("pricing/compute-sell")]
    [Authorize(Roles = $"{Roles.Owner},{Roles.Admin},{Roles.Dev}")]
    public async Task<IActionResult> ComputeSellPrice([FromQuery] decimal cost, CancellationToken ct)
    {
        var settings = await _db.PricingSettings.AsNoTracking().FirstOrDefaultAsync(ct) ?? new PricingSettings();
        var sell = cost > 0 ? PricingCalculator.ComputeSellPrice(cost, settings) : 0m;
        return Ok(new { sellPrice = sell });
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

    [HttpPost("mail/test")]
    [Authorize(Roles = $"{Roles.Owner},{Roles.Admin},{Roles.Dev}")]
    public async Task<IActionResult> SendTestEmail([FromBody] TestEmailRequest body, CancellationToken ct)
    {
        var to = body.To?.Trim();
        if (string.IsNullOrWhiteSpace(to))
            return BadRequest(new { error = "Email address required." });

        var shopName = string.IsNullOrWhiteSpace(_app.CompanyDisplayName)
            ? "MC Tactical"
            : _app.CompanyDisplayName.Trim();
        var footer = ReceiptCompanyContact.ToEmailHtmlFooter(_app);
        var html = $"""
            <p>This is a test email from <strong>{System.Net.WebUtility.HtmlEncode(shopName)} POS</strong>.</p>
            <p>If you received this, your Mailgun settings are working correctly.</p>
            {footer}
            """;
        try
        {
            await _email.SendInvoiceEmailAsync(to, $"{shopName} — Test email", html, null, null, ct);
            return Ok(new { message = $"Test email sent to {to}" });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
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
