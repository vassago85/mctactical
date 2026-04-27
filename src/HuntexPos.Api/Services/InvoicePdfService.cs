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
    private readonly IEffectiveBusinessSettings _business;
    private const string DefaultAccentHex = "#F47A20";
    private static readonly string TextDark = "#1A1A1C";
    private static readonly string TextMuted = "#3A3835";
    private static readonly string TextLight = "#4A4843";
    private static readonly string BorderLight = "#D9D6D0";
    private static readonly string TableHeadBg = "#F3F2EF";

    public InvoicePdfService(IOptions<AppOptions> app, IEffectiveBusinessSettings business)
    {
        _app = app.Value;
        _business = business;
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

    private byte[]? LoadLogoFile()
    {
        var eff = _business.GetAsync().GetAwaiter().GetResult();
        if (string.IsNullOrWhiteSpace(eff.LogoStorageKey)) return null;
        var dir = Path.IsPathRooted(_app.BrandingStoragePath)
            ? _app.BrandingStoragePath
            : Path.Combine(Directory.GetCurrentDirectory(), _app.BrandingStoragePath);
        var path = Path.Combine(dir, eff.LogoStorageKey);
        if (!System.IO.File.Exists(path)) return null;
        // SVG can't be rendered by QuestPDF image — skip and fall back to bundled default.
        if (path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)) return null;
        try { return System.IO.File.ReadAllBytes(path); } catch { return null; }
    }

    private byte[]? LoadLogo()
    {
        var uploaded = LoadLogoFile();
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

    private static string ResolveAccent(EffectiveBusinessSettings eff)
    {
        if (!string.IsNullOrWhiteSpace(eff.AccentColor) && IsHex(eff.AccentColor)) return eff.AccentColor;
        if (!string.IsNullOrWhiteSpace(eff.PrimaryColor) && IsHex(eff.PrimaryColor)) return eff.PrimaryColor;
        return DefaultAccentHex;
    }

    private static bool IsHex(string v)
        => v.StartsWith('#') && (v.Length == 4 || v.Length == 7 || v.Length == 9);

    public byte[] BuildPdf(Invoice invoice)
    {
        var eff = _business.GetAsync().GetAwaiter().GetResult();
        var logoBytes = LoadLogo();
        var accent = ResolveAccent(eff);
        var hasVat = !string.IsNullOrWhiteSpace(eff.VatNumber);
        var title = hasVat ? $"TAX {eff.InvoiceLabel.ToUpperInvariant()}" : eff.InvoiceLabel.ToUpperInvariant();

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.MarginHorizontal(40);
                page.MarginVertical(35);
                page.DefaultTextStyle(x => x.FontSize(10).FontColor(TextDark));

                // ── Header: logo left, invoice title right ──
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
                            right.Item().Text(invoice.InvoiceNumber)
                                .FontSize(11).FontColor(TextMuted);
                        });
                    });

                    header.Item().PaddingTop(8)
                        .LineHorizontal(2).LineColor(accent);
                });

                page.Content().PaddingTop(14).Column(col =>
                {
                    // ── Seller / Buyer blocks ──
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
                            if (hasVat)
                                seller.Item().PaddingTop(2)
                                    .Text($"VAT No: {eff.VatNumber}").FontSize(9).Bold();
                        });

                        // Right: buyer / customer
                        row.RelativeItem().Column(buyer =>
                        {
                            buyer.Item().Text("BILL TO").FontSize(7).Bold()
                                .FontColor(TextLight).LetterSpacing(0.08f);

                            if (!string.IsNullOrWhiteSpace(invoice.CustomerCompany))
                                buyer.Item().PaddingTop(2)
                                    .Text(invoice.CustomerCompany).Bold().FontSize(10);

                            if (!string.IsNullOrWhiteSpace(invoice.CustomerName))
                                buyer.Item().PaddingTop(string.IsNullOrWhiteSpace(invoice.CustomerCompany) ? 2 : 0)
                                    .Text(invoice.CustomerName).FontSize(10);

                            if (!string.IsNullOrWhiteSpace(invoice.CustomerAddress))
                            {
                                foreach (var chunk in invoice.CustomerAddress
                                    .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                                    buyer.Item().Text(chunk).FontSize(9).FontColor(TextMuted);
                            }

                            if (!string.IsNullOrWhiteSpace(invoice.CustomerEmail))
                                buyer.Item().Text(invoice.CustomerEmail).FontSize(9).FontColor(TextMuted);

                            if (!string.IsNullOrWhiteSpace(invoice.CustomerVatNumber))
                                buyer.Item().PaddingTop(2)
                                    .Text($"VAT No: {invoice.CustomerVatNumber}").FontSize(9).Bold();
                        });
                    });

                    // ── Meta row: date / payment ──
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
                            invoice.CreatedAt.ToOffset(TimeSpan.FromHours(2)).ToString("yyyy-MM-dd  HH:mm"));
                        MetaCell(row.RelativeItem(), "PAYMENT", invoice.PaymentMethod);
                    });

                    // ── Line items table ──
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
                            Th(h.Cell(), "PRICE (INCL.)", true);
                            Th(h.Cell(), "TOTAL", true);
                        });

                        var odd = false;
                        foreach (var line in invoice.Lines)
                        {
                            var bg = odd ? "#F8F7F5" : "#FFFFFF";
                            odd = !odd;

                            var hasPromoDisc = line.OriginalUnitPrice > 0 && line.OriginalUnitPrice != line.UnitPrice;
                            var hasLineDisc = line.LineDiscount > 0;

                            table.Cell().Background(bg).BorderBottom(1).BorderColor(BorderLight)
                                .Padding(6).Column(itemCol =>
                                {
                                    itemCol.Item().Text(line.Description).FontSize(9.5f);
                                    if (hasPromoDisc)
                                    {
                                        var promoAmt = (line.OriginalUnitPrice - line.UnitPrice) * line.Quantity;
                                        var label = !string.IsNullOrWhiteSpace(invoice.PromotionName)
                                            ? invoice.PromotionName : "Promotion";
                                        itemCol.Item().PaddingTop(1)
                                            .Text($"  {label}: -R{promoAmt:N2}")
                                            .FontSize(7.5f).FontColor("#CC0000");
                                    }
                                    if (hasLineDisc)
                                    {
                                        itemCol.Item().PaddingTop(1)
                                            .Text($"  Line discount: -R{line.LineDiscount:N2}")
                                            .FontSize(7.5f).FontColor("#CC0000");
                                    }
                                });

                            table.Cell().Background(bg).BorderBottom(1).BorderColor(BorderLight)
                                .Padding(6).AlignRight()
                                .Text(line.Quantity.ToString()).FontSize(9.5f);

                            table.Cell().Background(bg).BorderBottom(1).BorderColor(BorderLight)
                                .Padding(6).AlignRight().Column(priceCol =>
                                {
                                    if (hasPromoDisc)
                                    {
                                        priceCol.Item().AlignRight()
                                            .Text($"R{line.OriginalUnitPrice:N2}")
                                            .FontSize(8).FontColor("#999999").Strikethrough();
                                        priceCol.Item().AlignRight()
                                            .Text($"R{line.UnitPrice:N2}")
                                            .FontSize(9.5f).Bold().FontColor("#CC0000");
                                    }
                                    else
                                    {
                                        priceCol.Item().AlignRight()
                                            .Text($"R{line.UnitPrice:N2}").FontSize(9.5f);
                                    }
                                });

                            table.Cell().Background(bg).BorderBottom(1).BorderColor(BorderLight)
                                .Padding(6).AlignRight()
                                .Text($"R{line.LineTotal:N2}").FontSize(9.5f);
                        }
                    });

                    // ── Totals with VAT breakdown ──
                    col.Item().PaddingTop(12).AlignRight().Column(totals =>
                    {
                        totals.Spacing(3);

                        var afterDiscount = invoice.SubTotal - invoice.DiscountTotal;
                        if (afterDiscount < 0) afterDiscount = 0;
                        var exclVat = afterDiscount - invoice.TaxAmount;

                        if (invoice.DiscountTotal > 0)
                        {
                            totals.Item().Text($"Subtotal (incl. VAT):  R{invoice.SubTotal:N2}")
                                .FontSize(10).FontColor(TextMuted);
                            var discLabel = !string.IsNullOrWhiteSpace(invoice.PromotionName)
                                ? $"{invoice.PromotionName} discount" : "Discount";
                            totals.Item().Text($"{discLabel}:  -R{invoice.DiscountTotal:N2}")
                                .FontSize(10).FontColor("#CC0000");
                        }

                        totals.Item().Text($"Total excl. VAT:  R{exclVat:N2}")
                            .FontSize(10).FontColor(TextMuted);

                        if (invoice.TaxAmount > 0)
                            totals.Item().Text($"VAT ({invoice.TaxRate:F0}%):  R{invoice.TaxAmount:N2}")
                                .FontSize(10).FontColor(TextMuted);
                    });

                    // ── Grand total box ──
                    col.Item().PaddingTop(8).Row(totalRow =>
                    {
                        totalRow.RelativeItem();
                        totalRow.ConstantItem(240).Background("#FFF5EB")
                            .Border(1).BorderColor("#F9C89B")
                            .Padding(10).Row(inner =>
                            {
                                inner.RelativeItem().AlignMiddle()
                                    .Text("Total due (incl. VAT)").FontSize(10).FontColor(TextMuted);
                                inner.AutoItem().AlignRight().AlignMiddle()
                                    .Text($"R{invoice.GrandTotal:N2}")
                                    .Bold().FontSize(16).FontColor(TextDark);
                            });
                    });

                    var (footerTitle, footerLines) = ReceiptCompanyContact.ToPdfFooter(eff);
                    col.Item().PaddingTop(30)
                        .LineHorizontal(1).LineColor(BorderLight);
                    col.Item().PaddingTop(10)
                        .Text(footerTitle).SemiBold().FontSize(10).FontColor(TextDark);
                    foreach (var line in footerLines)
                        col.Item().Text(line).FontSize(9).FontColor(TextMuted);
                    if (hasVat)
                        col.Item().Text($"VAT No: {eff.VatNumber}").FontSize(9).FontColor(TextMuted);
                });
            });
        }).GeneratePdf();
    }

    public byte[] BuildOrderConfirmationPdf(Invoice invoice)
    {
        var eff = _business.GetAsync().GetAwaiter().GetResult();
        var logoBytes = LoadLogo();
        var accent = ResolveAccent(eff);

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
                            right.Item().Text("ORDER CONFIRMATION")
                                .Bold().FontSize(20).FontColor(TextDark).LetterSpacing(0.04f);
                            right.Item().Text(invoice.InvoiceNumber)
                                .FontSize(11).FontColor(TextMuted);
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
                        });

                        row.RelativeItem().Column(buyer =>
                        {
                            buyer.Item().Text("DELIVER TO").FontSize(7).Bold()
                                .FontColor(TextLight).LetterSpacing(0.08f);

                            if (!string.IsNullOrWhiteSpace(invoice.CustomerCompany))
                                buyer.Item().PaddingTop(2)
                                    .Text(invoice.CustomerCompany).Bold().FontSize(10);

                            if (!string.IsNullOrWhiteSpace(invoice.CustomerName))
                                buyer.Item().PaddingTop(string.IsNullOrWhiteSpace(invoice.CustomerCompany) ? 2 : 0)
                                    .Text(invoice.CustomerName).FontSize(10);

                            if (!string.IsNullOrWhiteSpace(invoice.CustomerAddress))
                            {
                                foreach (var chunk in invoice.CustomerAddress
                                    .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                                    buyer.Item().Text(chunk).FontSize(9).FontColor(TextMuted);
                            }

                            if (!string.IsNullOrWhiteSpace(invoice.CustomerEmail))
                                buyer.Item().Text(invoice.CustomerEmail).FontSize(9).FontColor(TextMuted);
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

                        MetaCell(row.RelativeItem(), "ORDER DATE",
                            invoice.CreatedAt.ToOffset(TimeSpan.FromHours(2)).ToString("yyyy-MM-dd  HH:mm"));
                        MetaCell(row.RelativeItem(), "REFERENCE", invoice.InvoiceNumber);
                    });

                    col.Item().PaddingTop(14).Background("#FFF5EB")
                        .Border(1).BorderColor("#F9C89B").Padding(10)
                        .Text($"Items will be delivered once available. Payment secures your {eff.BusinessName} pricing.")
                        .FontSize(9.5f).FontColor(TextDark);

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
                            Th(h.Cell(), "PRICE (INCL.)", true);
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

                    col.Item().PaddingTop(8).Row(totalRow =>
                    {
                        totalRow.RelativeItem();
                        totalRow.ConstantItem(240).Background("#FFF5EB")
                            .Border(1).BorderColor("#F9C89B")
                            .Padding(10).Row(inner =>
                            {
                                inner.RelativeItem().AlignMiddle()
                                    .Text("Total (incl. VAT)").FontSize(10).FontColor(TextMuted);
                                inner.AutoItem().AlignRight().AlignMiddle()
                                    .Text($"R{invoice.GrandTotal:N2}")
                                    .Bold().FontSize(16).FontColor(TextDark);
                            });
                    });

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
