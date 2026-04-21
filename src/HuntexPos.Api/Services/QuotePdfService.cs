using System.Reflection;
using HuntexPos.Api.Domain;
using HuntexPos.Api.Options;
using Microsoft.Extensions.Options;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace HuntexPos.Api.Services;

/// <summary>
/// Generates PDF documents for customer-facing <see cref="Quote"/>s. Shares the
/// visual language of <see cref="InvoicePdfService"/> so customers see a
/// consistent brand across quotes and invoices.
/// </summary>
public class QuotePdfService
{
    private readonly AppOptions _app;
    private readonly IEffectiveBusinessSettings _business;
    private readonly IBrandingAssetProvider _branding;

    private const string DefaultAccentHex = "#F47A20";
    private static readonly string TextDark = "#1A1A1C";
    private static readonly string TextMuted = "#5C5A56";
    private static readonly string TextLight = "#7A7874";
    private static readonly string BorderLight = "#ECEAE6";
    private static readonly string TableHeadBg = "#FAFAF8";

    public QuotePdfService(
        IOptions<AppOptions> app,
        IEffectiveBusinessSettings business,
        IBrandingAssetProvider branding)
    {
        _app = app.Value;
        _business = business;
        _branding = branding;
    }

    public byte[] BuildPdf(Quote quote)
    {
        var eff = _business.GetAsync().GetAwaiter().GetResult();
        var logoBytes = LoadLogo();
        var accent = ResolveAccent(eff);
        var title = (string.IsNullOrWhiteSpace(eff.QuoteLabel) ? "QUOTE" : eff.QuoteLabel.ToUpperInvariant());

        var validUntilText = quote.ValidUntil.HasValue
            ? quote.ValidUntil.Value.ToOffset(TimeSpan.FromHours(2)).ToString("yyyy-MM-dd")
            : "—";

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.MarginHorizontal(40);
                page.MarginVertical(35);
                page.DefaultTextStyle(x => x.FontSize(10).FontColor(TextDark));

                page.Header().Column(header =>
                {
                    header.Item().Row(row =>
                    {
                        if (logoBytes != null)
                            row.ConstantItem(120).AlignMiddle().Image(logoBytes).FitWidth();
                        else
                            row.ConstantItem(120).AlignMiddle()
                                .Text(DeriveInitials(eff.BusinessName)).Bold().FontSize(28).FontColor(accent);

                        row.RelativeItem().AlignRight().AlignMiddle().Column(right =>
                        {
                            right.Item().Text(title)
                                .Bold().FontSize(22).FontColor(TextDark).LetterSpacing(0.04f);
                            right.Item().Text(quote.QuoteNumber)
                                .FontSize(11).FontColor(TextMuted);
                            right.Item().Text($"Status: {quote.Status}")
                                .FontSize(9).FontColor(TextLight);
                        });
                    });

                    header.Item().PaddingTop(8)
                        .LineHorizontal(2).LineColor(accent);
                });

                page.Content().PaddingTop(14).Column(col =>
                {
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Column(seller =>
                        {
                            seller.Item().Text("FROM").FontSize(7).Bold()
                                .FontColor(TextLight).LetterSpacing(0.08f);
                            seller.Item().PaddingTop(2)
                                .Text(eff.BusinessName).Bold().FontSize(10);
                            if (!string.IsNullOrWhiteSpace(eff.Address))
                            {
                                foreach (var chunk in eff.Address
                                    .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                                    seller.Item().Text(chunk).FontSize(9).FontColor(TextMuted);
                            }
                            if (!string.IsNullOrWhiteSpace(eff.Phone))
                                seller.Item().Text($"Tel: {eff.Phone}").FontSize(9).FontColor(TextMuted);
                            if (!string.IsNullOrWhiteSpace(eff.Email))
                                seller.Item().Text(eff.Email).FontSize(9).FontColor(TextMuted);
                            if (!string.IsNullOrWhiteSpace(eff.VatNumber))
                                seller.Item().PaddingTop(2)
                                    .Text($"VAT No: {eff.VatNumber}").FontSize(9).Bold();
                        });

                        row.RelativeItem().Column(buyer =>
                        {
                            buyer.Item().Text("QUOTE FOR").FontSize(7).Bold()
                                .FontColor(TextLight).LetterSpacing(0.08f);

                            if (!string.IsNullOrWhiteSpace(quote.CustomerCompany))
                                buyer.Item().PaddingTop(2)
                                    .Text(quote.CustomerCompany).Bold().FontSize(10);

                            if (!string.IsNullOrWhiteSpace(quote.CustomerName))
                                buyer.Item().PaddingTop(string.IsNullOrWhiteSpace(quote.CustomerCompany) ? 2 : 0)
                                    .Text(quote.CustomerName).FontSize(10);

                            if (!string.IsNullOrWhiteSpace(quote.CustomerAddress))
                            {
                                foreach (var chunk in quote.CustomerAddress
                                    .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                                    buyer.Item().Text(chunk).FontSize(9).FontColor(TextMuted);
                            }

                            if (!string.IsNullOrWhiteSpace(quote.CustomerEmail))
                                buyer.Item().Text(quote.CustomerEmail).FontSize(9).FontColor(TextMuted);

                            if (!string.IsNullOrWhiteSpace(quote.CustomerPhone))
                                buyer.Item().Text(quote.CustomerPhone).FontSize(9).FontColor(TextMuted);

                            if (!string.IsNullOrWhiteSpace(quote.CustomerVatNumber))
                                buyer.Item().PaddingTop(2)
                                    .Text($"VAT No: {quote.CustomerVatNumber}").FontSize(9).Bold();
                        });
                    });

                    col.Item().PaddingTop(12).Row(row =>
                    {
                        void MetaCell(IContainer c, string label, string value)
                        {
                            c.Column(mc =>
                            {
                                mc.Item().Text(label).FontSize(7).Bold()
                                    .FontColor(TextLight).LetterSpacing(0.08f);
                                mc.Item().PaddingTop(2).Text(value).FontSize(10).FontColor(TextDark);
                            });
                        }

                        MetaCell(row.RelativeItem(), "DATE",
                            quote.CreatedAt.ToOffset(TimeSpan.FromHours(2)).ToString("yyyy-MM-dd"));
                        MetaCell(row.RelativeItem(), "VALID UNTIL", validUntilText);
                        MetaCell(row.RelativeItem(), "QUOTE #", quote.QuoteNumber);
                    });

                    col.Item().PaddingTop(14).Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(4);
                            c.ConstantColumn(50);
                            c.ConstantColumn(75);
                            c.ConstantColumn(80);
                        });

                        table.Header(h =>
                        {
                            void Th(IContainer c, string text, bool right = false)
                            {
                                var cell = c.BorderBottom(1).BorderColor(BorderLight)
                                    .Background(TableHeadBg)
                                    .Padding(6);
                                if (right) cell = cell.AlignRight();
                                cell.Text(text).FontSize(7).Bold()
                                    .FontColor(TextLight).LetterSpacing(0.06f);
                            }

                            Th(h.Cell(), "ITEM");
                            Th(h.Cell(), "QTY", true);
                            Th(h.Cell(), "PRICE", true);
                            Th(h.Cell(), "TOTAL", true);
                        });

                        var odd = false;
                        foreach (var line in quote.Lines.OrderBy(l => l.SortOrder))
                        {
                            var bg = odd ? "#F8F7F5" : "#FFFFFF";
                            odd = !odd;

                            table.Cell().Background(bg).BorderBottom(1).BorderColor(BorderLight)
                                .Padding(6).Column(itemCol =>
                                {
                                    itemCol.Item().Text(line.ItemName).FontSize(9.5f);
                                    if (!string.IsNullOrWhiteSpace(line.Description))
                                        itemCol.Item().PaddingTop(1)
                                            .Text(line.Description!).FontSize(8).FontColor(TextMuted);
                                    if ((line.DiscountAmount ?? 0) > 0)
                                        itemCol.Item().PaddingTop(1)
                                            .Text($"  Discount: -R{line.DiscountAmount:N2}")
                                            .FontSize(7.5f).FontColor("#CC0000");
                                });

                            table.Cell().Background(bg).BorderBottom(1).BorderColor(BorderLight)
                                .Padding(6).AlignRight()
                                .Text(line.Quantity.ToString()).FontSize(9.5f);

                            table.Cell().Background(bg).BorderBottom(1).BorderColor(BorderLight)
                                .Padding(6).AlignRight()
                                .Text($"R{line.UnitPrice:N2}").FontSize(9.5f);

                            table.Cell().Background(bg).BorderBottom(1).BorderColor(BorderLight)
                                .Padding(6).AlignRight()
                                .Text($"R{line.LineTotal:N2}").FontSize(9.5f);
                        }
                    });

                    col.Item().PaddingTop(12).AlignRight().Column(totals =>
                    {
                        totals.Spacing(3);

                        if (quote.DiscountTotal > 0)
                        {
                            totals.Item().Text($"Subtotal:  R{quote.SubTotal:N2}")
                                .FontSize(10).FontColor(TextMuted);
                            totals.Item().Text($"Discount:  -R{quote.DiscountTotal:N2}")
                                .FontSize(10).FontColor("#CC0000");
                        }
                        else
                        {
                            totals.Item().Text($"Subtotal:  R{quote.SubTotal:N2}")
                                .FontSize(10).FontColor(TextMuted);
                        }

                        if (quote.TaxAmount > 0)
                            totals.Item().Text($"VAT ({quote.TaxRate:F0}%):  R{quote.TaxAmount:N2}")
                                .FontSize(10).FontColor(TextMuted);
                    });

                    col.Item().PaddingTop(8).Row(totalRow =>
                    {
                        totalRow.RelativeItem();
                        totalRow.ConstantItem(240).Background("#FFF5EB")
                            .Border(1).BorderColor("#F9C89B")
                            .Padding(10).Row(inner =>
                            {
                                inner.RelativeItem().AlignMiddle()
                                    .Text("Quote total").FontSize(10).FontColor(TextMuted);
                                inner.AutoItem().AlignRight().AlignMiddle()
                                    .Text($"R{quote.GrandTotal:N2}")
                                    .Bold().FontSize(16).FontColor(TextDark);
                            });
                    });

                    if (!string.IsNullOrWhiteSpace(quote.PublicNotes))
                    {
                        col.Item().PaddingTop(16)
                            .Text("NOTES").FontSize(7).Bold()
                            .FontColor(TextLight).LetterSpacing(0.08f);
                        col.Item().PaddingTop(4)
                            .Text(quote.PublicNotes!).FontSize(9).FontColor(TextDark);
                    }

                    var (footerTitle, footerLines) = ReceiptCompanyContact.ToPdfFooter(eff);
                    col.Item().PaddingTop(30)
                        .LineHorizontal(1).LineColor(BorderLight);
                    col.Item().PaddingTop(10)
                        .Text(footerTitle).SemiBold().FontSize(10).FontColor(TextDark);
                    foreach (var line in footerLines)
                        col.Item().Text(line).FontSize(9).FontColor(TextMuted);
                });
            });
        }).GeneratePdf();
    }

    public async Task<string> SavePdfAsync(Quote quote, byte[] pdf, CancellationToken ct)
    {
        var dir = Path.Combine(Directory.GetCurrentDirectory(), _app.PdfStoragePath);
        Directory.CreateDirectory(dir);
        var fileName = $"quote-{quote.Id:N}.pdf";
        var path = Path.Combine(dir, fileName);
        await File.WriteAllBytesAsync(path, pdf, ct);
        return fileName;
    }

    private byte[]? LoadLogo()
    {
        var uploaded = _branding.GetLogoBytes();
        if (uploaded != null) return uploaded;

        var asm = Assembly.GetExecutingAssembly();
        var name = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("logo-dark.png", StringComparison.OrdinalIgnoreCase));
        if (name == null) return null;
        using var stream = asm.GetManifestResourceStream(name);
        if (stream == null) return null;
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    private static string DeriveInitials(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        var parts = name.Trim()
            .Split(new[] { ' ', '-', '_', '.', '/' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .Take(2)
            .ToArray();
        if (parts.Length == 0) return name.Substring(0, Math.Min(2, name.Length)).ToUpperInvariant();
        if (parts.Length == 1) return parts[0].Substring(0, Math.Min(2, parts[0].Length)).ToUpperInvariant();
        return (parts[0][0].ToString() + parts[1][0]).ToUpperInvariant();
    }

    private static string ResolveAccent(EffectiveBusinessSettings eff)
    {
        if (!string.IsNullOrWhiteSpace(eff.AccentColor) && IsHex(eff.AccentColor)) return eff.AccentColor;
        if (!string.IsNullOrWhiteSpace(eff.PrimaryColor) && IsHex(eff.PrimaryColor)) return eff.PrimaryColor;
        return DefaultAccentHex;
    }

    private static bool IsHex(string v)
        => v.StartsWith('#') && (v.Length == 4 || v.Length == 7 || v.Length == 9);
}
