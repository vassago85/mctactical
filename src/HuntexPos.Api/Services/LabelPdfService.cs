using System.Drawing;
using System.Reflection;
using BarcodeLib;
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
        var barcodeBytes = GenerateBarcodeImage(product.Barcode ?? product.Sku);

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
                var barcodeBytes = GenerateBarcodeImage(product.Barcode ?? product.Sku);
                container.Page(page => ConfigureLabelPage(page, product, barcodeBytes, pricing));
            }
        }).GeneratePdf();
    }

    private static byte[]? GenerateBarcodeImage(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        try
        {
            var barcode = new Barcode
            {
                IncludeLabel = true,
                LabelFont = new Font("Arial", 7, FontStyle.Regular),
                Alignment = AlignmentPositions.Center,
                Width = 280,
                Height = 90,
                BackColor = Color.White,
                ForeColor = Color.Black
            };

            var img = barcode.Encode(BarcodeLib.TYPE.CODE128, value, Color.Black, Color.White, 280, 90);

            using var ms = new MemoryStream();
            img.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            return ms.ToArray();
        }
        catch
        {
            return null;
        }
    }
}
