using HuntexPos.Api.Data;
using HuntexPos.Api.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HuntexPos.Api.Services;

/// <summary>
/// Singleton provider: loads the <c>BusinessSettings</c> row once, caches it, and merges
/// with <c>AppOptions</c> defaults. Used by PDF/email rendering and the public branding endpoint.
/// </summary>
public sealed class EffectiveBusinessSettingsProvider : IEffectiveBusinessSettings
{
    private readonly IServiceProvider _sp;
    private readonly AppOptions _app;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private EffectiveBusinessSettings? _cached;

    public EffectiveBusinessSettingsProvider(IServiceProvider sp, IOptions<AppOptions> app)
    {
        _sp = sp;
        _app = app.Value;
    }

    public async ValueTask<EffectiveBusinessSettings> GetAsync(CancellationToken ct = default)
    {
        if (_cached != null) return _cached;
        await _lock.WaitAsync(ct);
        try
        {
            if (_cached != null) return _cached;
            using var scope = _sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<HuntexDbContext>();
            var row = await db.BusinessSettings.AsNoTracking().FirstOrDefaultAsync(ct);
            _cached = Merge(row, _app);
            return _cached;
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Invalidate() => _cached = null;

    private static EffectiveBusinessSettings Merge(Domain.BusinessSettings? row, AppOptions app)
    {
        string Pick(string? dbVal, string? fallback) =>
            string.IsNullOrWhiteSpace(dbVal) ? (fallback ?? "").Trim() : dbVal.Trim();

        return new EffectiveBusinessSettings
        {
            BusinessName = Pick(row?.BusinessName, app.CompanyDisplayName),
            LegalName = Pick(row?.LegalName, app.CompanyDisplayName),
            VatNumber = Pick(row?.VatNumber, app.CompanyVatNumber),
            Currency = Pick(row?.Currency, "ZAR"),
            TimeZone = Pick(row?.TimeZone, "Africa/Johannesburg"),
            Email = Pick(row?.Email, app.CompanyEmail),
            Phone = Pick(row?.Phone, app.CompanyPhone),
            Address = Pick(row?.Address, app.CompanyAddress),
            Website = Pick(row?.Website, app.CompanyWebsite),
            WebsiteLabel = Pick(row?.WebsiteLabel, app.CompanyWebsiteLabel),
            LogoStorageKey = string.IsNullOrWhiteSpace(row?.LogoStorageKey) ? null : row!.LogoStorageKey.Trim(),
            FaviconStorageKey = string.IsNullOrWhiteSpace(row?.FaviconStorageKey) ? null : row!.FaviconStorageKey.Trim(),
            PrimaryColor = Pick(row?.PrimaryColor, ""),
            SecondaryColor = Pick(row?.SecondaryColor, ""),
            AccentColor = Pick(row?.AccentColor, ""),
            ReceiptFooter = row?.ReceiptFooter ?? "",
            QuoteTerms = row?.QuoteTerms ?? "",
            InvoiceTerms = row?.InvoiceTerms ?? "",
            ReturnPolicy = row?.ReturnPolicy ?? "",
            QuoteLabel = Pick(row?.QuoteLabel, "Quote"),
            InvoiceLabel = Pick(row?.InvoiceLabel, "Invoice"),
            CustomerLabel = Pick(row?.CustomerLabel, "Customer"),
            EnableQuotes = row?.EnableQuotes ?? true,
            EnableDiscounts = row?.EnableDiscounts ?? true,
            EnableBrandPricingRules = row?.EnableBrandPricingRules ?? true,
            AccountsEnabled = row?.AccountsEnabled ?? false,
        };
    }
}
