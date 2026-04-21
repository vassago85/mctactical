using System.Reflection;
using HuntexPos.Api.Domain;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace HuntexPos.Api.Services;

/// <summary>
/// Generates product labels sized for the Brother QL-800 with DK-22205 62mm continuous tape.
/// Layout: logo + name top row, then barcode centered with SKU below, then price at bottom.
/// </summary>
public static class LabelPdfService
{
    private const float LabelWidthMm = 62f;
    private const float LabelHeightMm = 40f;
    private const float PaddingMm = 2f;

    public record LabelPricing(decimal DisplayPrice, decimal? WasPrice, string? PromoName);

    private static byte[]? _logoCached;
    private static bool _logoLoaded;

    // Monochrome cache keyed by source bytes' hash so we only re-binarize when the branded
    // logo actually changes. Thermal labels (Brother QL-800) print 1-bit, so any colour or
    // grey pixel dithers into muddy stripes unless we flatten to pure black/transparent.
    private static byte[]? _monoCached;
    private static int _monoSourceHash;

    /// <summary>
    /// Optional provider for branded logo bytes. Wired up at app startup so that
    /// labels can use the current business's uploaded logo. Falls back to the embedded logo.
    /// </summary>
    public static Func<byte[]?>? LogoProvider { get; set; }

    private static byte[]? LoadLogo()
    {
        var source = LoadSourceLogo();
        if (source == null) return null;
        return ToMonochromeBlack(source);
    }

    private static byte[]? LoadSourceLogo()
    {
        // Prefer the injected (branded) logo first so white-label deployments work.
        var branded = LogoProvider?.Invoke();
        if (branded != null) return branded;

        if (_logoLoaded) return _logoCached;
        _logoLoaded = true;
        var asm = Assembly.GetExecutingAssembly();
        var name = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("logo-dark.png", StringComparison.OrdinalIgnoreCase));
        if (name == null) return null;
        using var stream = asm.GetManifestResourceStream(name);
        if (stream == null) return null;
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        _logoCached = ms.ToArray();
        return _logoCached;
    }

    /// <summary>
    /// Flattens the source image to pure black on transparent: every visible pixel becomes
    /// fully opaque black, every faint/transparent pixel becomes fully transparent. No greys,
    /// no colours — exactly what a monochrome thermal printer needs.
    /// </summary>
    private static byte[] ToMonochromeBlack(byte[] source)
    {
        var hash = ComputeHash(source);
        if (_monoCached != null && _monoSourceHash == hash) return _monoCached;

        try
        {
            using var img = SixLabors.ImageSharp.Image.Load<Rgba32>(source);
            img.ProcessPixelRows(accessor =>
            {
                for (var y = 0; y < accessor.Height; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (var x = 0; x < row.Length; x++)
                    {
                        ref var px = ref row[x];
                        // Alpha threshold: drop faint antialias pixels so edges stay crisp.
                        // Luminance threshold: treat bright pixels as background (transparent)
                        // so a light logo on a dark background doesn't invert.
                        var luma = (px.R * 299 + px.G * 587 + px.B * 114) / 1000;
                        if (px.A < 32 || luma > 235)
                            px = new Rgba32(0, 0, 0, 0);
                        else
                            px = new Rgba32(0, 0, 0, 255);
                    }
                }
            });
            using var ms = new MemoryStream();
            img.Save(ms, new PngEncoder());
            _monoCached = ms.ToArray();
            _monoSourceHash = hash;
            return _monoCached;
        }
        catch
        {
            return source;
        }
    }

    private static int ComputeHash(byte[] data)
    {
        unchecked
        {
            var hash = 17;
            for (var i = 0; i < data.Length; i += Math.Max(1, data.Length / 64))
                hash = hash * 31 + data[i];
            return hash * 31 + data.Length;
        }
    }

    /// <summary>
    /// Ensures the product has an EAN-13 barcode. If the barcode field is empty or not a valid EAN-13,
    /// generates one from the SKU digits. Returns the EAN-13 string (or original barcode/SKU if generation fails).
    /// Mutates product.Barcode if a new EAN is generated.
    /// </summary>
    public static string EnsureEan13(Product product)
    {
        var source = product.Barcode ?? product.Sku;
        var ean = ToEan13(source);
        if (ean != null)
        {
            product.Barcode = ean;
            return ean;
        }
        return source;
    }

    private static void ConfigureLabelPage(PageDescriptor page, Product product, byte[]? barcodeBytes, string barcodeText, LabelPricing pricing)
    {
        var logoBytes = LoadLogo();

        page.Size(LabelHeightMm, LabelWidthMm, Unit.Millimetre);
        page.DefaultTextStyle(x => x.FontSize(8).FontColor(Colors.Black));

        var contentImage = RenderLabelContentImage(product, logoBytes, barcodeBytes, barcodeText, pricing);
        var rotatedImage = RotateImageCW(contentImage);

        page.Content().Image(rotatedImage).FitArea();
    }

    private static byte[] RenderLabelContentImage(Product product, byte[]? logoBytes, byte[]? barcodeBytes, string barcodeText, LabelPricing pricing)
    {
        var doc = Document.Create(container =>
        {
            container.Page(p =>
            {
                p.Size(LabelWidthMm, LabelHeightMm, Unit.Millimetre);
                p.MarginHorizontal(PaddingMm, Unit.Millimetre);
                p.MarginVertical(PaddingMm, Unit.Millimetre);
                p.DefaultTextStyle(x => x.FontSize(8).FontColor(Colors.Black));

                p.Content().Column(col =>
                {
                    if (logoBytes != null)
                    {
                        col.Item().AlignCenter()
                            .MaxHeight(5, Unit.Millimetre)
                            .Image(logoBytes).FitArea();
                    }

                    if (barcodeBytes != null)
                    {
                        col.Item().PaddingTop(1, Unit.Millimetre).AlignCenter()
                            .MaxHeight(14, Unit.Millimetre)
                            .Image(barcodeBytes).FitArea();
                    }

                    col.Item().AlignCenter()
                        .Text(barcodeText).FontSize(6);

                    var hasPromo = pricing.WasPrice.HasValue && pricing.WasPrice.Value != pricing.DisplayPrice;
                    col.Item().PaddingTop(1, Unit.Millimetre).Row(row =>
                    {
                        row.RelativeItem().AlignLeft().AlignBottom().Column(nameCol =>
                        {
                            nameCol.Item().Text(product.Name).Bold().FontSize(6);
                            nameCol.Item().Text(product.Sku).FontSize(5).FontColor(Colors.Black);
                            if (hasPromo && !string.IsNullOrWhiteSpace(pricing.PromoName))
                                nameCol.Item().Text(pricing.PromoName).FontSize(5).Bold();
                        });

                        row.AutoItem().PaddingLeft(2, Unit.Millimetre).AlignRight().Column(priceCol =>
                        {
                            priceCol.Item().AlignRight()
                                .Text($"R{pricing.DisplayPrice:N2}")
                                .Bold().FontSize(11);
                            if (hasPromo)
                            {
                                priceCol.Item().AlignRight()
                                    .Text($"was R{pricing.WasPrice!.Value:N2}")
                                    .FontSize(5.5f).Strikethrough();
                            }
                        });
                    });
                });
            });
        });

        return doc.GenerateImages(new ImageGenerationSettings
        {
            ImageFormat = ImageFormat.Png,
            ImageCompressionQuality = ImageCompressionQuality.Best,
            RasterDpi = 300
        }).First();
    }

    private static byte[] RotateImageCW(byte[] pngBytes)
    {
        using var img = SixLabors.ImageSharp.Image.Load(pngBytes);
        img.Mutate(x => x.Rotate(RotateMode.Rotate90));
        using var ms = new MemoryStream();
        img.Save(ms, new PngEncoder());
        return ms.ToArray();
    }

    /// <summary>
    /// Converts any string to an EAN-13. Extracts digits first; if none, converts letters
    /// to alphabet positions (A=01, B=02 … Z=26). Pads/truncates to 12 digits + check digit.
    /// </summary>
    public static string? ToEan13(string text)
    {
        var digits = new string(text.Where(char.IsDigit).ToArray());

        // Already a valid EAN-13
        if (digits.Length == 13) return digits;

        // If no digits, convert letters to alphabet positions
        if (digits.Length == 0)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var ch in text.ToUpperInvariant())
            {
                if (ch >= 'A' && ch <= 'Z')
                    sb.Append((ch - 'A' + 1).ToString("D2"));
                if (sb.Length >= 12) break;
            }
            digits = sb.ToString();
        }
        if (digits.Length == 0) return null;
        if (digits.Length > 12) digits = digits[..12];
        digits = digits.PadLeft(12, '0');
        return digits + Ean13Renderer.CalculateCheck(digits);
    }

    private static (byte[]? Bytes, string DisplayText) RenderBarcodeWithText(string text)
    {
        var ean13 = ToEan13(text);
        if (ean13 != null)
        {
            var png = Ean13Renderer.RenderToPng(ean13, barHeight: 120, moduleWidth: 3);
            if (png != null) return (png, ean13);
        }
        return (Code128Renderer.RenderToPng(text, barHeight: 120, moduleWidth: 3), text);
    }

    public static byte[] BuildSingleLabel(Product product, LabelPricing pricing, int copies = 1)
    {
        EnsureEan13(product);
        var (barcodeBytes, displayText) = RenderBarcodeWithText(product.Barcode ?? product.Sku);

        return Document.Create(container =>
        {
            for (var i = 0; i < copies; i++)
                container.Page(page => ConfigureLabelPage(page, product, barcodeBytes, displayText, pricing));
        }).GeneratePdf();
    }

    public static byte[] BuildMultipleLabels(IEnumerable<(Product Product, LabelPricing Pricing)> items)
    {
        var list = items.ToList();

        return Document.Create(container =>
        {
            foreach (var (product, pricing) in list)
            {
                EnsureEan13(product);
                var (barcodeBytes, displayText) = RenderBarcodeWithText(product.Barcode ?? product.Sku);
                container.Page(page => ConfigureLabelPage(page, product, barcodeBytes, displayText, pricing));
            }
        }).GeneratePdf();
    }
}

/// <summary>
/// Code 128B barcode encoder → PNG via ImageSharp. Cross-platform, no native dependencies.
/// </summary>
internal static class Code128Renderer
{
    private static readonly byte[][] Patterns =
    {
        new byte[]{2,1,2,2,2,2}, new byte[]{2,2,2,1,2,2}, new byte[]{2,2,2,2,2,1}, new byte[]{1,2,1,2,2,3},
        new byte[]{1,2,1,3,2,2}, new byte[]{1,3,1,2,2,2}, new byte[]{1,2,2,2,1,3}, new byte[]{1,2,2,3,1,2},
        new byte[]{1,3,2,2,1,2}, new byte[]{2,2,1,2,1,3}, new byte[]{2,2,1,3,1,2}, new byte[]{2,3,1,2,1,2},
        new byte[]{1,1,2,2,3,2}, new byte[]{1,2,2,1,3,2}, new byte[]{1,2,2,2,3,1}, new byte[]{1,1,3,2,2,2},
        new byte[]{1,2,3,1,2,2}, new byte[]{1,2,3,2,2,1}, new byte[]{2,2,3,2,1,1}, new byte[]{2,2,1,1,3,2},
        new byte[]{2,2,1,2,3,1}, new byte[]{2,1,3,2,1,2}, new byte[]{2,2,3,1,1,2}, new byte[]{3,1,2,1,3,1},
        new byte[]{3,1,1,2,2,2}, new byte[]{3,2,1,1,2,2}, new byte[]{3,2,1,2,2,1}, new byte[]{3,1,2,2,1,2},
        new byte[]{3,2,2,1,1,2}, new byte[]{3,2,2,2,1,1}, new byte[]{2,1,2,1,2,3}, new byte[]{2,1,2,3,2,1},
        new byte[]{2,3,2,1,2,1}, new byte[]{1,1,1,3,2,3}, new byte[]{1,3,1,1,2,3}, new byte[]{1,3,1,3,2,1},
        new byte[]{1,1,2,3,1,3}, new byte[]{1,3,2,1,1,3}, new byte[]{1,3,2,3,1,1}, new byte[]{2,1,1,3,1,3},
        new byte[]{2,3,1,1,1,3}, new byte[]{2,3,1,3,1,1}, new byte[]{1,1,2,1,3,3}, new byte[]{1,1,2,3,3,1},
        new byte[]{1,3,2,1,3,1}, new byte[]{1,1,3,1,2,3}, new byte[]{1,1,3,3,2,1}, new byte[]{1,3,3,1,2,1},
        new byte[]{3,1,3,1,2,1}, new byte[]{2,1,1,3,3,1}, new byte[]{2,3,1,1,3,1}, new byte[]{2,1,3,1,1,3},
        new byte[]{2,1,3,3,1,1}, new byte[]{2,1,3,1,3,1}, new byte[]{3,1,1,1,2,3}, new byte[]{3,1,1,3,2,1},
        new byte[]{3,3,1,1,2,1}, new byte[]{3,1,2,1,1,3}, new byte[]{3,1,2,3,1,1}, new byte[]{3,3,2,1,1,1},
        new byte[]{3,1,4,1,1,1}, new byte[]{2,2,1,4,1,1}, new byte[]{4,3,1,1,1,1}, new byte[]{1,1,1,2,2,4},
        new byte[]{1,1,1,4,2,2}, new byte[]{1,2,1,1,2,4}, new byte[]{1,2,1,4,2,1}, new byte[]{1,4,1,1,2,2},
        new byte[]{1,4,1,2,2,1}, new byte[]{1,1,2,2,1,4}, new byte[]{1,1,2,4,1,2}, new byte[]{1,2,2,1,1,4},
        new byte[]{1,2,2,4,1,1}, new byte[]{1,4,2,1,1,2}, new byte[]{1,4,2,2,1,1}, new byte[]{2,4,1,2,1,1},
        new byte[]{2,2,1,1,1,4}, new byte[]{4,1,3,1,1,1}, new byte[]{2,4,1,1,1,2}, new byte[]{1,3,4,1,1,1},
        new byte[]{1,1,1,2,4,2}, new byte[]{1,2,1,1,4,2}, new byte[]{1,2,1,2,4,1}, new byte[]{1,1,4,2,1,2},
        new byte[]{1,2,4,1,1,2}, new byte[]{1,2,4,2,1,1}, new byte[]{4,1,1,2,1,2}, new byte[]{4,2,1,1,1,2},
        new byte[]{4,2,1,2,1,1}, new byte[]{2,1,2,1,4,1}, new byte[]{2,1,4,1,2,1}, new byte[]{4,1,2,1,2,1},
        new byte[]{1,1,1,1,4,3}, new byte[]{1,1,1,3,4,1}, new byte[]{1,3,1,1,4,1}, new byte[]{1,1,4,1,1,3},
        new byte[]{1,1,4,3,1,1}, new byte[]{4,1,1,1,1,3}, new byte[]{4,1,1,3,1,1}, new byte[]{1,1,3,1,4,1},
        new byte[]{1,1,4,1,3,1}, new byte[]{3,1,1,1,4,1}, new byte[]{4,1,1,1,3,1}, new byte[]{2,1,1,4,1,2},
        new byte[]{2,1,1,2,1,4}, new byte[]{2,1,1,2,3,2},
        new byte[]{2,3,3,1,1,1,2} // stop (13 modules)
    };

    public static byte[]? RenderToPng(string text, int barHeight = 80, int moduleWidth = 2)
    {
        if (string.IsNullOrEmpty(text)) return null;
        try
        {
            var values = Encode(text);
            var modules = BuildModules(values);
            var imgWidth = modules.Count * moduleWidth;

            using var image = new Image<L8>(imgWidth, barHeight);
            for (var y = 0; y < barHeight; y++)
                for (var x = 0; x < imgWidth; x++)
                {
                    var idx = x / moduleWidth;
                    var isBar = idx < modules.Count && modules[idx];
                    image[x, y] = new L8(isBar ? (byte)0 : (byte)255);
                }

            using var ms = new MemoryStream();
            image.Save(ms, new PngEncoder());
            return ms.ToArray();
        }
        catch { return null; }
    }

    private static List<bool> BuildModules(List<int> values)
    {
        var modules = new List<bool>();
        for (var q = 0; q < 10; q++) modules.Add(false); // quiet zone
        foreach (var v in values)
        {
            var bar = true;
            foreach (var width in Patterns[v])
            {
                for (var w = 0; w < width; w++) modules.Add(bar);
                bar = !bar;
            }
        }
        for (var q = 0; q < 10; q++) modules.Add(false); // quiet zone
        return modules;
    }

    private static List<int> Encode(string text)
    {
        var values = new List<int> { 104 }; // Start Code B
        foreach (var ch in text)
        {
            var v = ch - 32;
            if (v < 0 || v > 94) v = 0;
            values.Add(v);
        }
        long sum = values[0];
        for (var i = 1; i < values.Count; i++) sum += values[i] * i;
        values.Add((int)(sum % 103));
        values.Add(106); // Stop
        return values;
    }
}

/// <summary>
/// EAN-13 barcode renderer. Accepts 12 digits (auto-calculates check) or 13 digits.
/// Returns null if the input is not a valid EAN-13 candidate.
/// </summary>
internal static class Ean13Renderer
{
    private static readonly string[] LPatterns = { "0001101","0011001","0010011","0111101","0100011","0110001","0101111","0111011","0110111","0001011" };
    private static readonly string[] GPatterns = { "0100111","0110011","0011011","0100001","0011101","0111001","0000101","0010001","0001001","0010111" };
    private static readonly string[] RPatterns = { "1110010","1100110","1101100","1000010","1011100","1001110","1010000","1000100","1001000","1110100" };
    private static readonly int[][] ParityPatterns =
    {
        new[]{0,0,0,0,0,0}, new[]{0,0,1,0,1,1}, new[]{0,0,1,1,0,1}, new[]{0,0,1,1,1,0},
        new[]{0,1,0,0,1,1}, new[]{0,1,1,0,0,1}, new[]{0,1,1,1,0,0}, new[]{0,1,0,1,0,1},
        new[]{0,1,0,1,1,0}, new[]{0,1,1,0,1,0}
    };

    public static byte[]? RenderToPng(string text, int barHeight = 100, int moduleWidth = 2)
    {
        if (string.IsNullOrEmpty(text)) return null;
        var digits = new string(text.Where(char.IsDigit).ToArray());
        if (digits.Length == 12)
            digits += CalculateCheckDigit(digits);
        else if (digits.Length != 13)
            return null;

        try
        {
            var modules = BuildModules(digits);
            var imgWidth = modules.Count * moduleWidth;

            using var image = new Image<L8>(imgWidth, barHeight);
            for (var y = 0; y < barHeight; y++)
                for (var x = 0; x < imgWidth; x++)
                {
                    var idx = x / moduleWidth;
                    var isBar = idx < modules.Count && modules[idx];
                    image[x, y] = new L8(isBar ? (byte)0 : (byte)255);
                }

            using var ms = new MemoryStream();
            image.Save(ms, new PngEncoder());
            return ms.ToArray();
        }
        catch { return null; }
    }

    private static List<bool> BuildModules(string digits)
    {
        var d = digits.Select(c => c - '0').ToArray();
        var modules = new List<bool>();

        for (var q = 0; q < 9; q++) modules.Add(false); // quiet zone
        AddPattern(modules, "101"); // start guard

        var parity = ParityPatterns[d[0]];
        for (var i = 0; i < 6; i++)
        {
            var pattern = parity[i] == 0 ? LPatterns[d[i + 1]] : GPatterns[d[i + 1]];
            AddPattern(modules, pattern);
        }

        AddPattern(modules, "01010"); // centre guard

        for (var i = 0; i < 6; i++)
            AddPattern(modules, RPatterns[d[i + 7]]);

        AddPattern(modules, "101"); // end guard
        for (var q = 0; q < 9; q++) modules.Add(false); // quiet zone

        return modules;
    }

    private static void AddPattern(List<bool> modules, string pattern)
    {
        foreach (var c in pattern) modules.Add(c == '1');
    }

    public static char CalculateCheck(string digits12) => CalculateCheckDigit(digits12);

    private static char CalculateCheckDigit(string digits12)
    {
        var sum = 0;
        for (var i = 0; i < 12; i++)
            sum += (digits12[i] - '0') * (i % 2 == 0 ? 1 : 3);
        var check = (10 - sum % 10) % 10;
        return (char)('0' + check);
    }
}
