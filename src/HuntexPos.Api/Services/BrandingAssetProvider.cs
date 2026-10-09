using HuntexPos.Api.Options;
using Microsoft.Extensions.Options;

namespace HuntexPos.Api.Services;

public interface IBrandingAssetProvider
{
    byte[]? GetLogoBytes();
}

/// <summary>
/// Serves the current branded logo from the branding storage directory.
/// Falls back to null when no logo has been uploaded, so PDF services can use
/// their embedded defaults.
/// </summary>
public sealed class BrandingAssetProvider : IBrandingAssetProvider
{
    private readonly IEffectiveBusinessSettings _business;
    private readonly AppOptions _app;

    public BrandingAssetProvider(IEffectiveBusinessSettings business, IOptions<AppOptions> app)
    {
        _business = business;
        _app = app.Value;
    }

    public byte[]? GetLogoBytes()
    {
        var eff = _business.GetAsync().GetAwaiter().GetResult();
        var key = eff.LogoStorageKey;
        if (string.IsNullOrWhiteSpace(key)) return null;

        var root = _app.BrandingStoragePath;
        var dir = Path.IsPathRooted(root) ? root : Path.Combine(Directory.GetCurrentDirectory(), root);
        var path = Path.Combine(dir, key);
        if (!File.Exists(path)) return null;

        try { return File.ReadAllBytes(path); }
        catch { return null; }
    }
}
