using System.Reflection;
using HuntexPos.Api.Domain;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace HuntexPos.Api.Services;

/// <summary>
/// Generates product labels sized for the Brother QL-800 with DK-22205 62mm continuous tape.
/// 62mm wide × 29mm tall per label.
/// </summary>
public static class LabelPdfService
{
    private const float LabelWidthMm = 62f;
    private const float LabelHeightMm = 29f;
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
                    row.ConstantItem(26, Unit.Millimetre)
                        .AlignRight()
                        .AlignBottom()
                        .Height(11, Unit.Millimetre)
                        .Image(barcodeBytes).FitArea();
                }
            });
        });
    }

    public static byte[] BuildSingleLabel(Product product, LabelPricing pricing, int copies = 1)
    {
        var barcodeBytes = Code128Renderer.RenderToBmp(product.Barcode ?? product.Sku, barHeight: 80, moduleWidth: 2);

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
                var barcodeBytes = Code128Renderer.RenderToBmp(product.Barcode ?? product.Sku, barHeight: 80, moduleWidth: 2);
                container.Page(page => ConfigureLabelPage(page, product, barcodeBytes, pricing));
            }
        }).GeneratePdf();
    }
}

/// <summary>
/// Pure-C# Code 128B barcode encoder → BMP image. No System.Drawing or native dependencies.
/// </summary>
internal static class Code128Renderer
{
    // Code 128 bar patterns: each value encodes 6 alternating bar/space widths (11 modules total)
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
        new byte[]{2,3,3,1,1,1,2} // stop pattern (13 modules)
    };

    private const int StartCodeB = 104;
    private const int StopCode = 106;

    public static byte[]? RenderToBmp(string text, int barHeight = 80, int moduleWidth = 2)
    {
        if (string.IsNullOrEmpty(text)) return null;

        try
        {
            var values = Encode(text);
            var modules = new List<bool>();

            // Quiet zone (10 modules white)
            for (var q = 0; q < 10; q++) modules.Add(false);

            foreach (var v in values)
            {
                var pattern = Patterns[v];
                var bar = true;
                foreach (var width in pattern)
                {
                    for (var w = 0; w < width; w++)
                        modules.Add(bar);
                    bar = !bar;
                }
            }

            // Quiet zone
            for (var q = 0; q < 10; q++) modules.Add(false);

            var imgWidth = modules.Count * moduleWidth;
            return CreateBmp(modules, imgWidth, barHeight, moduleWidth);
        }
        catch
        {
            return null;
        }
    }

    private static List<int> Encode(string text)
    {
        var values = new List<int> { StartCodeB };
        foreach (var ch in text)
        {
            var v = ch - 32;
            if (v < 0 || v > 94) v = 0; // replace unprintable with space
            values.Add(v);
        }

        // Checksum
        long sum = values[0];
        for (var i = 1; i < values.Count; i++)
            sum += values[i] * i;
        values.Add((int)(sum % 103));
        values.Add(StopCode);
        return values;
    }

    /// <summary>Produce a minimal 24-bit BMP from module data. No imaging libraries needed.</summary>
    private static byte[] CreateBmp(List<bool> modules, int width, int height, int moduleWidth)
    {
        var rowBytes = width * 3;
        var rowPadding = (4 - rowBytes % 4) % 4;
        var stride = rowBytes + rowPadding;
        var pixelDataSize = stride * height;
        var fileSize = 54 + pixelDataSize;
        var bmp = new byte[fileSize];

        // BMP header
        bmp[0] = 0x42; bmp[1] = 0x4D; // "BM"
        WriteInt(bmp, 2, fileSize);
        WriteInt(bmp, 10, 54); // pixel data offset
        WriteInt(bmp, 14, 40); // DIB header size
        WriteInt(bmp, 18, width);
        WriteInt(bmp, 22, height);
        bmp[26] = 1; // planes
        bmp[28] = 24; // bits per pixel
        WriteInt(bmp, 34, pixelDataSize);

        // BMP rows are bottom-to-top; all rows are identical for a 1D barcode
        var row = new byte[stride];
        for (var x = 0; x < width; x++)
        {
            var moduleIdx = x / moduleWidth;
            var isBar = moduleIdx < modules.Count && modules[moduleIdx];
            var offset = x * 3;
            var color = isBar ? (byte)0 : (byte)255;
            row[offset] = color;     // B
            row[offset + 1] = color; // G
            row[offset + 2] = color; // R
        }

        for (var y = 0; y < height; y++)
            Buffer.BlockCopy(row, 0, bmp, 54 + y * stride, stride);

        return bmp;
    }

    private static void WriteInt(byte[] buf, int offset, int value)
    {
        buf[offset] = (byte)value;
        buf[offset + 1] = (byte)(value >> 8);
        buf[offset + 2] = (byte)(value >> 16);
        buf[offset + 3] = (byte)(value >> 24);
    }
}
