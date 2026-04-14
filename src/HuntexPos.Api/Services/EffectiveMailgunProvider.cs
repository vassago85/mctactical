using HuntexPos.Api.Data;
using HuntexPos.Api.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HuntexPos.Api.Services;

public sealed class EffectiveMailgunProvider : IEffectiveMailgunProvider
{
    private readonly HuntexDbContext _db;
    private readonly MailgunOptions _cfg;

    public EffectiveMailgunProvider(HuntexDbContext db, IOptions<MailgunOptions> cfg)
    {
        _db = db;
        _cfg = cfg.Value;
    }

    public async ValueTask<EffectiveMailgunOptions> GetAsync(CancellationToken ct = default)
    {
        var row = await _db.MailSettings.AsNoTracking().FirstOrDefaultAsync(ct);
        var defaultBase = string.IsNullOrWhiteSpace(_cfg.BaseUrl) ? "https://api.mailgun.net/v3" : _cfg.BaseUrl.Trim();

        if (row == null)
        {
            return new EffectiveMailgunOptions
            {
                ApiKey = _cfg.ApiKey ?? "",
                Domain = _cfg.Domain ?? "",
                From = _cfg.From ?? "",
                BaseUrl = defaultBase,
                AttachPdf = _cfg.AttachPdf
            };
        }

        return new EffectiveMailgunOptions
        {
            ApiKey = string.IsNullOrWhiteSpace(row.ApiKey) ? (_cfg.ApiKey ?? "") : row.ApiKey.Trim(),
            Domain = string.IsNullOrWhiteSpace(row.Domain) ? (_cfg.Domain ?? "") : row.Domain.Trim(),
            From = string.IsNullOrWhiteSpace(row.SenderFrom) ? (_cfg.From ?? "") : row.SenderFrom.Trim(),
            BaseUrl = string.IsNullOrWhiteSpace(row.BaseUrl) ? defaultBase : row.BaseUrl.Trim(),
            AttachPdf = row.AttachPdf
        };
    }
}
