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
/// 62mm wide × 40mm tall per label (continuous tape auto-cuts at this length).
/// </summary>
public static class LabelPdfService
{
    private const float LabelWidthMm = 62f;
    private const float LabelHeightMm = 40f;
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

        page.Size(LabelWidthMm, LabelHeightMm, Unit.Millimetre);
        page.MarginHorizontal(PaddingMm, Unit.Millimetre);
        page.MarginVertical(PaddingMm, Unit.Millimetre);
        page.DefaultTextStyle(x => x.FontSize(8).FontColor(Colors.Black));

        page.Content().Column(col =>
        {
            col.Item().Row(header =>
            {
                if (logoBytes != null)
                {
                    header.ConstantItem(14, Unit.Millimetre)
                        .AlignMiddle()
                        .Height(5, Unit.Millimetre)
                        .Image(logoBytes).FitArea();
                }

                header.RelativeItem().AlignRight().AlignMiddle().Column(right =>
                {
                    right.Item().Text(product.Name)
                        .Bold().FontSize(8).ClampLines(2).LineHeight(1.1f);
                });
            });

            col.Item().PaddingTop(0.5f, Unit.Millimetre).Row(row =>
            {
                row.RelativeItem().Column(left =>
                {
                    left.Item().Text(product.Sku)
                        .FontSize(6.5f).FontColor("#555555");

                    if (pricing.WasPrice.HasValue && pricing.WasPrice.Value != pricing.DisplayPrice)
                    {
                        left.Item().PaddingTop(0.3f, Unit.Millimetre).Row(priceRow =>
                        {
                            priceRow.AutoItem()
                                .Text($"R{pricing.DisplayPrice:N2}")
                                .Bold().FontSize(13).FontColor("#CC0000");
                            priceRow.AutoItem().PaddingLeft(1.5f, Unit.Millimetre).AlignBottom()
                                .Text($"R{pricing.WasPrice.Value:N2}")
                                .FontSize(8).FontColor("#999999").Strikethrough();
                        });

                        if (!string.IsNullOrWhiteSpace(pricing.PromoName))
                        {
                            left.Item().Text(pricing.PromoName)
                                .FontSize(5.5f).FontColor("#CC0000").Bold();
                        }
                    }
                    else
                    {
                        left.Item().PaddingTop(0.3f, Unit.Millimetre)
                            .Text($"R{pricing.DisplayPrice:N2}")
                            .Bold().FontSize(13);
                    }
                });

                if (barcodeBytes != null)
                {
                    row.ConstantItem(30, Unit.Millimetre)
                        .AlignRight()
                        .AlignBottom()
                        .Height(16, Unit.Millimetre)
                        .Image(barcodeBytes).FitArea();
                }
            });
        });
    }

    public static byte[] BuildSingleLabel(Product product, LabelPricing pricing, int copies = 1)
    {
        var barcodeBytes = Code128Renderer.RenderToPng(product.Barcode ?? product.Sku, barHeight: 120, moduleWidth: 3);

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
                var barcodeBytes = Code128Renderer.RenderToPng(product.Barcode ?? product.Sku, barHeight: 120, moduleWidth: 3);
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
