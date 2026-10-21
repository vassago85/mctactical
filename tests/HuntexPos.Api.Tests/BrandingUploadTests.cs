using System.Text;
using HuntexPos.Api.Controllers;
using HuntexPos.Api.Options;
using HuntexPos.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace HuntexPos.Api.Tests;

/// <summary>
/// Anonymous branding URLs serve the stored file on this origin. SVG would run as a script
/// if opened directly, so upload and serve accept raster types only.
/// </summary>
public class BrandingUploadTests
{
    [Fact]
    public async Task UploadLogo_Svg_IsRejected_AndNotStored()
    {
        using var fx = new Fixture();
        var svg = Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"></svg>");

        var result = await fx.Controller.UploadLogo(Form("logo.svg", svg), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.False(File.Exists(Path.Combine(fx.Dir, "logo.svg")));
    }

    [Fact]
    public async Task UploadLogo_Png_IsStored_AndServedAsPng()
    {
        using var fx = new Fixture();
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47 };

        var uploaded = await fx.Controller.UploadLogo(Form("logo.png", png), CancellationToken.None);
        Assert.IsType<OkObjectResult>(uploaded);
        Assert.True(File.Exists(Path.Combine(fx.Dir, "logo.png")));

        fx.Settings.Current = new EffectiveBusinessSettings { LogoStorageKey = "logo.png" };
        var served = await fx.Controller.GetLogoFile(CancellationToken.None);
        var file = Assert.IsType<FileContentResult>(served);
        Assert.Equal("image/png", file.ContentType);
    }

    [Fact]
    public async Task GetLogoFile_StoredSvg_IsNotServed()
    {
        using var fx = new Fixture();
        await File.WriteAllTextAsync(Path.Combine(fx.Dir, "logo.svg"), "<svg xmlns=\"http://www.w3.org/2000/svg\"></svg>");
        fx.Settings.Current = new EffectiveBusinessSettings { LogoStorageKey = "logo.svg" };

        var served = await fx.Controller.GetLogoFile(CancellationToken.None);
        Assert.IsType<NotFoundResult>(served);

        var branding = await fx.Controller.GetBranding(CancellationToken.None);
        Assert.Null(branding.LogoUrl);
    }

    private static FormFile Form(string name, byte[] bytes)
    {
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, bytes.Length, "file", name);
    }

    private sealed class Fixture : IDisposable
    {
        public string Dir { get; } = Path.Combine(Path.GetTempPath(), "huntex-branding-" + Guid.NewGuid().ToString("N"));
        public StubSettings Settings { get; } = new();
        public BusinessSettingsController Controller { get; }
        private readonly TestDb _db = new();

        public Fixture()
        {
            Directory.CreateDirectory(Dir);
            Controller = new BusinessSettingsController(
                _db.NewContext(),
                Settings,
                Microsoft.Extensions.Options.Options.Create(new AppOptions { BrandingStoragePath = Dir }),
                env: null!);
        }

        public void Dispose()
        {
            _db.Dispose();
            try { Directory.Delete(Dir, recursive: true); } catch { }
        }
    }

    private sealed class StubSettings : IEffectiveBusinessSettings
    {
        public EffectiveBusinessSettings Current { get; set; } = new();
        public void Invalidate() { }
        public ValueTask<EffectiveBusinessSettings> GetAsync(CancellationToken ct = default) => new(Current);
    }
}
