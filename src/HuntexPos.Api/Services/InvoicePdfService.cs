using System.Reflection;
using HuntexPos.Api.Domain;
using HuntexPos.Api.Options;
using Microsoft.Extensions.Options;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
namespace HuntexPos.Api.Services;

public class InvoicePdfService
{
    private readonly AppOptions _app;
    private static readonly string AccentHex = "#F47A20";
    private static readonly string TextDark = "#1A1A1C";
    private static readonly string TextMuted = "#5C5A56";
    private static readonly string TextLight = "#7A7874";
    private static readonly string BorderLight = "#ECEAE6";
    private static readonly string TableHeadBg = "#FAFAF8";

    public InvoicePdfService(IOptions<AppOptions> app) => _app = app.Value;

    private static byte[]? LoadLogo()
    {
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

    public byte[] BuildPdf(Invoice invoice)
    {
        var logoBytes = LoadLogo();

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
                                .Text("MC").Bold().FontSize(28).FontColor(AccentHex);

                        row.RelativeItem().AlignRight().AlignMiddle().Column(right =>
                        {
                            right.Item().Text("INVOICE")
                                .Bold().FontSize(22).FontColor(TextDark).LetterSpacing(0.04f);
                            right.Item().Text(invoice.InvoiceNumber)
                                .FontSize(11).FontColor(TextMuted);
                        });
                    });

                    header.Item().PaddingTop(8)
                        .LineHorizontal(2).LineColor(AccentHex);
                });

                page.Content().PaddingTop(16).Column(col =>
                {
                    col.Item().Row(row =>
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
                            invoice.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd  HH:mm"));

                        if (!string.IsNullOrWhiteSpace(invoice.CustomerName))
                            MetaCell(row.RelativeItem(), "CUSTOMER", invoice.CustomerName);

                        MetaCell(row.RelativeItem(), "PAYMENT", invoice.PaymentMethod);
                    });

                    col.Item().PaddingTop(16).Table(table =>
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
                        foreach (var line in invoice.Lines)
                        {
                            var bg = odd ? "#F8F7F5" : "#FFFFFF";
                            odd = !odd;

                            table.Cell().Background(bg).BorderBottom(1).BorderColor(BorderLight)
                                .Padding(6).Text(line.Description).FontSize(9.5f);

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
                        totals.Item().Text($"Subtotal:  R{invoice.SubTotal:N2}")
                            .FontSize(10).FontColor(TextMuted);

                        if (invoice.DiscountTotal > 0)
                            totals.Item().Text($"Discount:  -R{invoice.DiscountTotal:N2}")
                                .FontSize(10).FontColor(TextMuted);

                        if (invoice.TaxAmount > 0)
                            totals.Item().Text($"Tax ({invoice.TaxRate:F2}%):  R{invoice.TaxAmount:N2}")
                                .FontSize(10).FontColor(TextMuted);
                    });

                    col.Item().PaddingTop(8).Row(totalRow =>
                    {
                        totalRow.RelativeItem();
                        totalRow.ConstantItem(220).Background("#FFF5EB")
                            .Border(1).BorderColor("#F9C89B")
                            .Padding(10).Row(inner =>
                            {
                                inner.RelativeItem().AlignMiddle()
                                    .Text("Total due").FontSize(11).FontColor(TextMuted);
                                inner.AutoItem().AlignRight().AlignMiddle()
                                    .Text($"R{invoice.GrandTotal:N2}")
                                    .Bold().FontSize(16).FontColor(TextDark);
                            });
                    });

                    var (footerTitle, footerLines) = ReceiptCompanyContact.ToPdfFooter(_app);
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

    public async Task<string> SavePdfAsync(Invoice invoice, byte[] pdf, CancellationToken ct)
    {
        var dir = Path.Combine(Directory.GetCurrentDirectory(), _app.PdfStoragePath);
        Directory.CreateDirectory(dir);
        var fileName = $"{invoice.Id:N}.pdf";
        var path = Path.Combine(dir, fileName);
        await File.WriteAllBytesAsync(path, pdf, ct);
        return fileName;
    }
}
