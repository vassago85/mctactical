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
    private const float LabelHeightMm = 45f;
    private const float PaddingMm = 2f;

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

    private static void ConfigureLabelPage(PageDescriptor page, Product product, byte[]? barcodeBytes, LabelPricing pricing)
    {
        var logoBytes = LoadLogo();
        var barcodeText = product.Barcode ?? product.Sku;

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
                    .Height(8, Unit.Millimetre)
                    .Image(logoBytes).FitArea();
            }

            // Row 2: Barcode centered with SKU underneath
            col.Item().PaddingTop(0.5f, Unit.Millimetre).AlignCenter().Column(bc =>
            {
                if (barcodeBytes != null)
                {
                    bc.Item().AlignCenter()
                        .Height(16, Unit.Millimetre)
                        .Width(54, Unit.Millimetre)
                        .Image(barcodeBytes).FitArea();
                }
                bc.Item().AlignCenter().PaddingTop(0.5f, Unit.Millimetre)
                    .Text(barcodeText).FontSize(7).FontColor("#444444").LetterSpacing(0.08f);
            });

            // Row 3: Price
            col.Item().PaddingTop(0.5f, Unit.Millimetre).AlignCenter().Column(priceCol =>
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
            col.Item().PaddingTop(0.5f, Unit.Millimetre).AlignCenter()
                .Text(product.Name)
                .Bold().FontSize(6.5f).FontColor("#333333");
        });
    }

    public static byte[] BuildSingleLabel(Product product, LabelPricing pricing, int copies = 1)
    {
        var barcodeBytes = Code128Renderer.RenderToPng(product.Barcode ?? product.Sku, barHeight: 140, moduleWidth: 3);

        return Document.Create(container =>
        {
            for (var i = 0; i < copies; i++)
                container.Page(page => ConfigureLabelPage(page, product, barcodeBytes, pricing));
        }).GeneratePdf();
    }

    public static byte[] BuildMultipleLabels(IEnumerable<(Product Product, LabelPricing Pricing)> items)
    {
        var list = items.ToList();

        return Document.Create(container =>
        {
            foreach (var (product, pricing) in list)
            {
                var barcodeBytes = Code128Renderer.RenderToPng(product.Barcode ?? product.Sku, barHeight: 140, moduleWidth: 3);
                container.Page(page => ConfigureLabelPage(page, product, barcodeBytes, pricing));
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
