using HuntexPos.Api.Domain;
using HuntexPos.Api.Options;
using Microsoft.Extensions.Options;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
namespace HuntexPos.Api.Services;

public class InvoicePdfService
{
    private readonly AppOptions _app;

    public InvoicePdfService(IOptions<AppOptions> app) => _app = app.Value;

    public byte[] BuildPdf(Invoice invoice)
    {
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Margin(40);
                page.Header().Text("MC Tactical — Invoice").SemiBold().FontSize(20);
                page.Content().Column(col =>
                {
                    col.Spacing(8);
                    col.Item().Text($"Invoice #: {invoice.InvoiceNumber}");
                    col.Item().Text($"Date: {invoice.CreatedAt:yyyy-MM-dd HH:mm} UTC");
                    if (!string.IsNullOrEmpty(invoice.CustomerName))
                        col.Item().Text($"Customer: {invoice.CustomerName}");
                    col.Item().LineHorizontal(1).LineColor(Colors.Grey.Lighten2);

                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(3);
                            c.ConstantColumn(50);
                            c.ConstantColumn(70);
                            c.ConstantColumn(70);
                        });
                        table.Header(h =>
                        {
                            h.Cell().Text("Item").SemiBold();
                            h.Cell().AlignRight().Text("Qty").SemiBold();
                            h.Cell().AlignRight().Text("Price").SemiBold();
                            h.Cell().AlignRight().Text("Total").SemiBold();
                        });
                        foreach (var line in invoice.Lines)
                        {
                            table.Cell().Text(line.Description);
                            table.Cell().AlignRight().Text(line.Quantity.ToString());
                            table.Cell().AlignRight().Text(line.UnitPrice.ToString("F2"));
                            table.Cell().AlignRight().Text(line.LineTotal.ToString("F2"));
                        }
                    });

                    col.Item().AlignRight().Column(right =>
                    {
                        right.Item().Text($"Subtotal: {invoice.SubTotal:F2}");
                        if (invoice.DiscountTotal > 0)
                            right.Item().Text($"Discount: -{invoice.DiscountTotal:F2}");
                        if (invoice.TaxAmount > 0)
                            right.Item().Text($"Tax ({invoice.TaxRate:F2}%): {invoice.TaxAmount:F2}");
                        right.Item().Text($"Total: {invoice.GrandTotal:F2}").SemiBold();
                        right.Item().Text($"Payment: {invoice.PaymentMethod}");
                    });

                    var (footerTitle, footerLines) = ReceiptCompanyContact.ToPdfFooter(_app);
                    col.Item().PaddingTop(24).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
                    col.Item().PaddingTop(10).Text(footerTitle).SemiBold().FontSize(10);
                    foreach (var line in footerLines)
                        col.Item().Text(line).FontSize(9).FontColor(Colors.Grey.Darken1);
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
