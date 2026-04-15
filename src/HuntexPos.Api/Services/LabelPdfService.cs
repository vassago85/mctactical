using System.Reflection;
using HuntexPos.Api.Domain;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace HuntexPos.Api.Services;

/// <summary>
/// Generates product labels sized for the Brother QL-800 with DK-22205 62mm continuous tape.
/// Layout: logo + name top row, then barcode centered with SKU below, then price at bottom.
/// </summary>
public static class LabelPdfService
{
    private const float LabelWidthMm = 62f;
    private const float LabelHeightMm = 40f;
    private const float PaddingMm = 1.5f;

    public record LabelPricing(decimal DisplayPrice, decimal? WasPrice, string? PromoName);

    private static byte[]? _logoCached;
    private static bool _logoLoaded;

    private static byte[]? LoadLogo()
    {
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
    /// Ensures the product has an EAN-13 barcode. If the barcode field is empty or not a valid EAN-13,
    /// generates one from the SKU digits. Returns the EAN-13 string (or original barcode/SKU if generation fails).
    /// Mutates product.Barcode if a new EAN is generated.
    /// </summary>
    public static string EnsureEan13(Product product)
    {
        if (!string.IsNullOrEmpty(product.Barcode))
        {
            var existingDigits = new string(product.Barcode.Where(char.IsDigit).ToArray());
            if (existingDigits.Length == 13) return product.Barcode;
        }
        var ean = ToEan13(product.Barcode ?? product.Sku);
        if (ean != null)
        {
            product.Barcode = ean;
            return ean;
        }
        return product.Barcode ?? product.Sku;
    }

    private static void ConfigureLabelPage(PageDescriptor page, Product product, byte[]? barcodeBytes, string barcodeText, LabelPricing pricing)
    {
        var logoBytes = LoadLogo();

        page.Size(LabelWidthMm, LabelHeightMm, Unit.Millimetre);
        page.MarginHorizontal(PaddingMm, Unit.Millimetre);
        page.MarginVertical(PaddingMm, Unit.Millimetre);
        page.DefaultTextStyle(x => x.FontSize(8).FontColor(Colors.Black));

        page.Content().Column(col =>
        {
            // Row 1: Logo centered
            if (logoBytes != null)
            {
                col.Item().AlignCenter()
                    .Height(6, Unit.Millimetre)
                    .Image(logoBytes).FitArea();
            }

            // Row 2: Barcode centered with number underneath
            col.Item().PaddingTop(0.3f, Unit.Millimetre).AlignCenter().Column(bc =>
            {
                if (barcodeBytes != null)
                {
                    bc.Item().AlignCenter()
                        .Height(12, Unit.Millimetre)
                        .Width(50, Unit.Millimetre)
                        .Image(barcodeBytes).FitArea();
                }
                bc.Item().AlignCenter().PaddingTop(0.3f, Unit.Millimetre)
                    .Text(barcodeText).FontSize(6.5f).FontColor("#444444").LetterSpacing(0.08f);
            });

            // Row 3: Price
            col.Item().PaddingTop(0.3f, Unit.Millimetre).AlignCenter().Column(priceCol =>
            {
                if (pricing.WasPrice.HasValue && pricing.WasPrice.Value != pricing.DisplayPrice)
                {
                    priceCol.Item().AlignCenter().Row(priceRow =>
                    {
                        priceRow.AutoItem()
                            .Text($"R{pricing.DisplayPrice:N2}")
                            .Bold().FontSize(14).FontColor("#CC0000");
                        priceRow.AutoItem().PaddingLeft(2f, Unit.Millimetre).AlignBottom()
                            .Text($"R{pricing.WasPrice.Value:N2}")
                            .FontSize(9).FontColor("#999999").Strikethrough();
                    });

                    if (!string.IsNullOrWhiteSpace(pricing.PromoName))
                    {
                        priceCol.Item().AlignCenter()
                            .Text(pricing.PromoName)
                            .FontSize(6).FontColor("#CC0000").Bold();
                    }
                }
                else
                {
                    priceCol.Item().AlignCenter()
                        .Text($"R{pricing.DisplayPrice:N2}")
                        .Bold().FontSize(14);
                }
            });

            // Row 4: Product name at bottom
            col.Item().PaddingTop(0.3f, Unit.Millimetre).AlignCenter()
                .Text(product.Name)
                .Bold().FontSize(6f).FontColor("#333333");
        });
    }

    /// <summary>
    /// Converts any string to an EAN-13. Extracts digits first; if none, converts letters
    /// to alphabet positions (A=01, B=02 … Z=26). Pads/truncates to 12 digits + check digit.
    /// </summary>
    public static string? ToEan13(string text)
    {
        var digits = new string(text.Where(char.IsDigit).ToArray());
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

    private static byte[]? RenderBarcode(string text)
    {
        var ean = Ean13Renderer.RenderToPng(text, barHeight: 120, moduleWidth: 2);
        if (ean != null) return ean;
        var converted = ToEan13(text);
        if (converted != null)
        {
            var eanConverted = Ean13Renderer.RenderToPng(converted, barHeight: 120, moduleWidth: 2);
            if (eanConverted != null) return eanConverted;
        }
        return Code128Renderer.RenderToPng(text, barHeight: 120, moduleWidth: 2);
    }

    public static byte[] BuildSingleLabel(Product product, LabelPricing pricing, int copies = 1)
    {
        var barcodeText = EnsureEan13(product);
        var barcodeBytes = RenderBarcode(barcodeText);

        return Document.Create(container =>
        {
            for (var i = 0; i < copies; i++)
                container.Page(page => ConfigureLabelPage(page, product, barcodeBytes, barcodeText, pricing));
        }).GeneratePdf();
    }

    public static byte[] BuildMultipleLabels(IEnumerable<(Product Product, LabelPricing Pricing)> items)
    {
        var list = items.ToList();

        return Document.Create(container =>
        {
            foreach (var (product, pricing) in list)
            {
                var barcodeText = EnsureEan13(product);
                var barcodeBytes = RenderBarcode(barcodeText);
                container.Page(page => ConfigureLabelPage(page, product, barcodeBytes, barcodeText, pricing));
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
