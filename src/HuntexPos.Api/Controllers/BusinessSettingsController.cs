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
[Route("api/settings/business")]
public class BusinessSettingsController : ControllerBase
{
    private readonly HuntexDbContext _db;
    private readonly IEffectiveBusinessSettings _effective;
    private readonly AppOptions _app;
    private readonly IWebHostEnvironment _env;

    private static readonly string[] AllowedLogoExt = [".png", ".jpg", ".jpeg", ".webp", ".svg"];
    private static readonly string[] AllowedIconExt = [".png", ".ico", ".svg"];

    public BusinessSettingsController(
        HuntexDbContext db,
        IEffectiveBusinessSettings effective,
        IOptions<AppOptions> app,
        IWebHostEnvironment env)
    {
        _db = db;
        _effective = effective;
        _app = app.Value;
        _env = env;
    }

    [HttpGet]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Owner},{Roles.Dev}")]
    public async Task<BusinessSettingsDto> Get(CancellationToken ct)
    {
        var row = await _db.BusinessSettings.AsNoTracking().FirstOrDefaultAsync(ct) ?? new BusinessSettings();
        return ToDto(row);
    }

    [HttpPut]
    [Authorize(Roles = $"{Roles.Owner},{Roles.Dev}")]
    public async Task<BusinessSettingsDto> Put([FromBody] BusinessSettingsDto body, CancellationToken ct)
    {
        var row = await _db.BusinessSettings.FirstOrDefaultAsync(ct);
        if (row == null)
        {
            row = new BusinessSettings();
            _db.BusinessSettings.Add(row);
        }

        row.BusinessName = (body.BusinessName ?? "").Trim();
        row.LegalName = (body.LegalName ?? "").Trim();
        row.VatNumber = (body.VatNumber ?? "").Trim();
        row.Currency = string.IsNullOrWhiteSpace(body.Currency) ? "ZAR" : body.Currency.Trim();
        row.TimeZone = string.IsNullOrWhiteSpace(body.TimeZone) ? "Africa/Johannesburg" : body.TimeZone.Trim();
        row.Email = (body.Email ?? "").Trim();
        row.Phone = (body.Phone ?? "").Trim();
        row.Address = (body.Address ?? "").Trim();
        row.Website = (body.Website ?? "").Trim();
        row.WebsiteLabel = (body.WebsiteLabel ?? "").Trim();
        row.PrimaryColor = NormaliseHex(body.PrimaryColor);
        row.SecondaryColor = NormaliseHex(body.SecondaryColor);
        row.AccentColor = NormaliseHex(body.AccentColor);
        row.ReceiptFooter = body.ReceiptFooter ?? "";
        row.QuoteTerms = body.QuoteTerms ?? "";
        row.InvoiceTerms = body.InvoiceTerms ?? "";
        row.ReturnPolicy = body.ReturnPolicy ?? "";
        row.QuoteLabel = string.IsNullOrWhiteSpace(body.QuoteLabel) ? "Quote" : body.QuoteLabel.Trim();
        row.InvoiceLabel = string.IsNullOrWhiteSpace(body.InvoiceLabel) ? "Invoice" : body.InvoiceLabel.Trim();
        row.CustomerLabel = string.IsNullOrWhiteSpace(body.CustomerLabel) ? "Customer" : body.CustomerLabel.Trim();
        row.EnableQuotes = body.EnableQuotes;
        row.EnableDiscounts = body.EnableDiscounts;
        row.EnableBrandPricingRules = body.EnableBrandPricingRules;
        row.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);
        _effective.Invalidate();

        return ToDto(row);
    }

    [HttpPost("logo")]
    [Authorize(Roles = $"{Roles.Owner},{Roles.Dev}")]
    [RequestSizeLimit(4 * 1024 * 1024)]
    public async Task<IActionResult> UploadLogo(IFormFile file, CancellationToken ct)
        => await UploadImageAsync(file, isLogo: true, ct);

    [HttpPost("favicon")]
    [Authorize(Roles = $"{Roles.Owner},{Roles.Dev}")]
    [RequestSizeLimit(512 * 1024)]
    public async Task<IActionResult> UploadFavicon(IFormFile file, CancellationToken ct)
        => await UploadImageAsync(file, isLogo: false, ct);

    [HttpDelete("logo")]
    [Authorize(Roles = $"{Roles.Owner},{Roles.Dev}")]
    public async Task<IActionResult> DeleteLogo(CancellationToken ct) => await DeleteImageAsync(isLogo: true, ct);

    [HttpDelete("favicon")]
    [Authorize(Roles = $"{Roles.Owner},{Roles.Dev}")]
    public async Task<IActionResult> DeleteFavicon(CancellationToken ct) => await DeleteImageAsync(isLogo: false, ct);

    [HttpGet("/api/settings/branding/logo")]
    [AllowAnonymous]
    public async Task<IActionResult> GetLogoFile(CancellationToken ct) => await ServeImageAsync(isLogo: true, ct);

    [HttpGet("/api/settings/branding/favicon")]
    [AllowAnonymous]
    public async Task<IActionResult> GetFaviconFile(CancellationToken ct) => await ServeImageAsync(isLogo: false, ct);

    [HttpGet("/api/settings/branding/manifest.webmanifest")]
    [AllowAnonymous]
    [Produces("application/manifest+json")]
    public async Task<IActionResult> GetWebManifest(CancellationToken ct)
    {
        var eff = await _effective.GetAsync(ct);
        var name = string.IsNullOrWhiteSpace(eff.BusinessName) ? "POS" : eff.BusinessName;
        var icon = eff.LogoStorageKey != null ? "/api/settings/branding/logo" : "/favicon.svg";
        var themeColor = !string.IsNullOrWhiteSpace(eff.AccentColor)
            ? eff.AccentColor
            : (!string.IsNullOrWhiteSpace(eff.PrimaryColor) ? eff.PrimaryColor : "#0a0a0b");

        var manifest = new
        {
            name = $"{name} POS",
            short_name = name,
            description = $"{name} point of sale",
            start_url = "/",
            scope = "/",
            display = "standalone",
            background_color = "#ffffff",
            theme_color = themeColor,
            icons = new[]
            {
                new { src = icon, sizes = "192x192", type = "image/png", purpose = "any" },
                new { src = icon, sizes = "512x512", type = "image/png", purpose = "any" },
                new { src = icon, sizes = "192x192", type = "image/png", purpose = "maskable" }
            }
        };
        return new JsonResult(manifest);
    }

    [HttpGet("/api/settings/branding")]
    [AllowAnonymous]
    public async Task<PublicBrandingDto> GetBranding(CancellationToken ct)
    {
        var eff = await _effective.GetAsync(ct);
        return new PublicBrandingDto
        {
            BusinessName = eff.BusinessName,
            LogoUrl = eff.LogoStorageKey != null ? "/api/settings/branding/logo" : null,
            FaviconUrl = eff.FaviconStorageKey != null ? "/api/settings/branding/favicon" : null,
            PrimaryColor = eff.PrimaryColor,
            SecondaryColor = eff.SecondaryColor,
            AccentColor = eff.AccentColor,
            Terminology = new BrandingTerminologyDto
            {
                Quote = eff.QuoteLabel,
                Invoice = eff.InvoiceLabel,
                Customer = eff.CustomerLabel,
            },
            Features = new BrandingFeaturesDto
            {
                Quotes = eff.EnableQuotes,
                Discounts = eff.EnableDiscounts,
                BrandPricingRules = eff.EnableBrandPricingRules,
            }
        };
    }

    private async Task<IActionResult> UploadImageAsync(IFormFile file, bool isLogo, CancellationToken ct)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { error = "No file provided." });

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        var allowed = isLogo ? AllowedLogoExt : AllowedIconExt;
        if (!allowed.Contains(ext))
            return BadRequest(new { error = $"Unsupported type. Allowed: {string.Join(", ", allowed)}." });

        var dir = BrandingDir();
        Directory.CreateDirectory(dir);

        var key = (isLogo ? "logo" : "favicon") + ext;
        var path = Path.Combine(dir, key);

        foreach (var existing in Directory.EnumerateFiles(dir, (isLogo ? "logo" : "favicon") + ".*"))
        {
            try { System.IO.File.Delete(existing); } catch { }
        }

        await using (var fs = System.IO.File.Create(path))
            await file.CopyToAsync(fs, ct);

        var row = await _db.BusinessSettings.FirstOrDefaultAsync(ct) ?? _db.BusinessSettings.Add(new BusinessSettings()).Entity;
        if (isLogo) row.LogoStorageKey = key; else row.FaviconStorageKey = key;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        _effective.Invalidate();

        var url = isLogo ? "/api/settings/branding/logo" : "/api/settings/branding/favicon";
        return Ok(new { url });
    }

    private async Task<IActionResult> DeleteImageAsync(bool isLogo, CancellationToken ct)
    {
        var row = await _db.BusinessSettings.FirstOrDefaultAsync(ct);
        if (row == null) return NoContent();

        var key = isLogo ? row.LogoStorageKey : row.FaviconStorageKey;
        if (!string.IsNullOrWhiteSpace(key))
        {
            var path = Path.Combine(BrandingDir(), key);
            if (System.IO.File.Exists(path))
            {
                try { System.IO.File.Delete(path); } catch { }
            }
        }

        if (isLogo) row.LogoStorageKey = null; else row.FaviconStorageKey = null;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        _effective.Invalidate();

        return NoContent();
    }

    private async Task<IActionResult> ServeImageAsync(bool isLogo, CancellationToken ct)
    {
        var eff = await _effective.GetAsync(ct);
        var key = isLogo ? eff.LogoStorageKey : eff.FaviconStorageKey;
        if (string.IsNullOrWhiteSpace(key))
            return NotFound();

        var path = Path.Combine(BrandingDir(), key);
        if (!System.IO.File.Exists(path))
            return NotFound();

        var mime = MimeFromExtension(Path.GetExtension(path));
        var bytes = await System.IO.File.ReadAllBytesAsync(path, ct);
        return File(bytes, mime);
    }

    private string BrandingDir()
    {
        var root = _app.BrandingStoragePath;
        return Path.IsPathRooted(root)
            ? root
            : Path.Combine(Directory.GetCurrentDirectory(), root);
    }

    private static string MimeFromExtension(string ext) => ext.ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".webp" => "image/webp",
        ".svg" => "image/svg+xml",
        ".ico" => "image/x-icon",
        _ => "application/octet-stream",
    };

    private static string NormaliseHex(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var v = s.Trim();
        if (!v.StartsWith('#')) v = "#" + v;
        return v.Length is 4 or 7 or 9 ? v : "";
    }

    private static BusinessSettingsDto ToDto(BusinessSettings row) => new()
    {
        BusinessName = row.BusinessName,
        LegalName = row.LegalName,
        VatNumber = row.VatNumber,
        Currency = string.IsNullOrWhiteSpace(row.Currency) ? "ZAR" : row.Currency,
        TimeZone = string.IsNullOrWhiteSpace(row.TimeZone) ? "Africa/Johannesburg" : row.TimeZone,
        Email = row.Email,
        Phone = row.Phone,
        Address = row.Address,
        Website = row.Website,
        WebsiteLabel = row.WebsiteLabel,
        LogoUrl = row.LogoStorageKey != null ? "/api/settings/branding/logo" : null,
        FaviconUrl = row.FaviconStorageKey != null ? "/api/settings/branding/favicon" : null,
        PrimaryColor = row.PrimaryColor,
        SecondaryColor = row.SecondaryColor,
        AccentColor = row.AccentColor,
        ReceiptFooter = row.ReceiptFooter,
        QuoteTerms = row.QuoteTerms,
        InvoiceTerms = row.InvoiceTerms,
        ReturnPolicy = row.ReturnPolicy,
        QuoteLabel = string.IsNullOrWhiteSpace(row.QuoteLabel) ? "Quote" : row.QuoteLabel,
        InvoiceLabel = string.IsNullOrWhiteSpace(row.InvoiceLabel) ? "Invoice" : row.InvoiceLabel,
        CustomerLabel = string.IsNullOrWhiteSpace(row.CustomerLabel) ? "Customer" : row.CustomerLabel,
        EnableQuotes = row.EnableQuotes,
        EnableDiscounts = row.EnableDiscounts,
        EnableBrandPricingRules = row.EnableBrandPricingRules,
    };
}
