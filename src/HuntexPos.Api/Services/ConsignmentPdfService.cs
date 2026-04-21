using System.Reflection;
using HuntexPos.Api.Domain;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace HuntexPos.Api.Services;

public class ConsignmentPdfService
{
    private readonly IEffectiveBusinessSettings _business;
    private readonly IBrandingAssetProvider _brandingAssets;

    private const string DefaultAccentHex = "#F47A20";
    private static readonly string TextDark = "#1A1A1C";
    private static readonly string TextMuted = "#5C5A56";
    private static readonly string TextLight = "#7A7874";
    private static readonly string BorderLight = "#ECEAE6";
    private static readonly string TableHeadBg = "#FAFAF8";

    public ConsignmentPdfService(IEffectiveBusinessSettings business, IBrandingAssetProvider brandingAssets)
    {
        _business = business;
        _brandingAssets = brandingAssets;
    }

    private byte[]? LoadLogo()
    {
        var dbLogo = _brandingAssets.GetLogoBytes();
        if (dbLogo != null) return dbLogo;
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
        static bool IsHex(string v) => v.StartsWith('#') && (v.Length == 4 || v.Length == 7 || v.Length == 9);
        if (!string.IsNullOrWhiteSpace(eff.AccentColor) && IsHex(eff.AccentColor)) return eff.AccentColor;
        if (!string.IsNullOrWhiteSpace(eff.PrimaryColor) && IsHex(eff.PrimaryColor)) return eff.PrimaryColor;
        return DefaultAccentHex;
    }

    private static string DeriveInitials(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "•";
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1) return parts[0].Substring(0, Math.Min(2, parts[0].Length)).ToUpperInvariant();
        return (parts[0][0].ToString() + parts[1][0].ToString()).ToUpperInvariant();
    }

    public byte[] BuildPdf(ConsignmentBatch batch)
    {
        var eff = _business.GetAsync().GetAwaiter().GetResult();
        return batch.Type == ConsignmentBatchType.Receive
            ? BuildReceiveCheckSheet(batch, eff)
            : BuildReturnPackingList(batch, eff);
    }

    private byte[] BuildReceiveCheckSheet(ConsignmentBatch batch, EffectiveBusinessSettings eff)
    {
        var logoBytes = LoadLogo();
        var accent = ResolveAccent(eff);
        var title = "CONSIGNMENT RECEIVE";
        var sast = batch.CreatedAt.ToOffset(TimeSpan.FromHours(2));

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
                            row.ConstantItem(120).AlignMiddle().Text(DeriveInitials(eff.BusinessName)).Bold().FontSize(28).FontColor(accent);

                        row.RelativeItem().AlignRight().AlignMiddle().Column(right =>
                        {
                            right.Item().Text(title).Bold().FontSize(22).FontColor(TextDark).LetterSpacing(0.04f);
                            right.Item().Text($"Ref: {batch.Id:N}"[..20]).FontSize(9).FontColor(TextMuted);
                        });
                    });
                    header.Item().PaddingTop(8).LineHorizontal(2).LineColor(accent);
                });

                page.Content().PaddingTop(14).Column(col =>
                {
                    col.Item().Row(row =>
                    {
                        Label(row.RelativeItem(), "SUPPLIER", batch.Supplier?.Name ?? "—");
                        Label(row.RelativeItem(), "DATE", sast.ToString("yyyy-MM-dd  HH:mm"));
                        Label(row.RelativeItem(), "STATUS", batch.Status.ToString());
                    });

                    if (!string.IsNullOrWhiteSpace(batch.Notes))
                    {
                        col.Item().PaddingTop(6).Text($"Notes: {batch.Notes}").FontSize(9).FontColor(TextMuted);
                    }

                    col.Item().PaddingTop(14).Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.ConstantColumn(25);
                            c.ConstantColumn(80);
                            c.RelativeColumn(4);
                            c.ConstantColumn(60);
                            c.ConstantColumn(60);
                            c.ConstantColumn(50);
                        });

                        table.Header(h =>
                        {
                            Th(h.Cell(), "#");
                            Th(h.Cell(), "SKU");
                            Th(h.Cell(), "PRODUCT");
                            Th(h.Cell(), "EXPECTED", true);
                            Th(h.Cell(), "RECEIVED", true);
                            Th(h.Cell(), "STATUS", true);
                        });

                        var idx = 0;
                        foreach (var line in batch.Lines.OrderBy(l => l.Product?.Sku))
                        {
                            idx++;
                            var odd = idx % 2 == 1;
                            var bg = odd ? "#F8F7F5" : "#FFFFFF";
                            var status = line.CheckedQty >= line.ExpectedQty && line.ExpectedQty > 0 ? "OK"
                                : line.CheckedQty > 0 ? "PARTIAL"
                                : line.ExpectedQty == 0 && line.CheckedQty > 0 ? "EXTRA"
                                : "—";
                            var statusColor = status == "OK" ? "#16a34a" : status == "PARTIAL" ? "#d97706" : TextMuted;

                            Td(table.Cell(), bg, idx.ToString());
                            Td(table.Cell(), bg, line.Product?.Sku ?? "");
                            Td(table.Cell(), bg, line.Product?.Name ?? "");
                            TdRight(table.Cell(), bg, line.ExpectedQty.ToString());
                            TdRight(table.Cell(), bg, line.CheckedQty.ToString());
                            TdRight(table.Cell(), bg, status, statusColor);
                        }
                    });

                    var totalExpected = batch.Lines.Sum(l => l.ExpectedQty);
                    var totalChecked = batch.Lines.Sum(l => l.CheckedQty);
                    col.Item().PaddingTop(10).AlignRight().Column(totals =>
                    {
                        totals.Item().Text($"Total expected: {totalExpected} items").FontSize(10).FontColor(TextMuted);
                        totals.Item().Text($"Total received: {totalChecked} items").FontSize(10).Bold();
                    });
                });
            });
        }).GeneratePdf();
    }

    private byte[] BuildReturnPackingList(ConsignmentBatch batch, EffectiveBusinessSettings eff)
    {
        var logoBytes = LoadLogo();
        var accent = ResolveAccent(eff);
        var title = "CONSIGNMENT RETURN";
        var sast = batch.CommittedAt?.ToOffset(TimeSpan.FromHours(2))
                   ?? batch.CreatedAt.ToOffset(TimeSpan.FromHours(2));

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
                            row.ConstantItem(120).AlignMiddle().Text(DeriveInitials(eff.BusinessName)).Bold().FontSize(28).FontColor(accent);

                        row.RelativeItem().AlignRight().AlignMiddle().Column(right =>
                        {
                            right.Item().Text(title).Bold().FontSize(22).FontColor(TextDark).LetterSpacing(0.04f);
                            right.Item().Text($"Ref: {batch.Id:N}"[..20]).FontSize(9).FontColor(TextMuted);
                        });
                    });
                    header.Item().PaddingTop(8).LineHorizontal(2).LineColor(accent);
                });

                page.Content().PaddingTop(14).Column(col =>
                {
                    col.Item().Row(row =>
                    {
                        Label(row.RelativeItem(), "RETURN TO", batch.Supplier?.Name ?? "—");
                        Label(row.RelativeItem(), "DATE", sast.ToString("yyyy-MM-dd  HH:mm"));
                    });

                    if (!string.IsNullOrWhiteSpace(batch.Notes))
                    {
                        col.Item().PaddingTop(6).Text($"Notes: {batch.Notes}").FontSize(9).FontColor(TextMuted);
                    }

                    col.Item().PaddingTop(14).Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.ConstantColumn(25);
                            c.ConstantColumn(80);
                            c.RelativeColumn(4);
                            c.ConstantColumn(60);
                        });

                        table.Header(h =>
                        {
                            Th(h.Cell(), "#");
                            Th(h.Cell(), "SKU");
                            Th(h.Cell(), "PRODUCT");
                            Th(h.Cell(), "QTY", true);
                        });

                        var idx = 0;
                        foreach (var line in batch.Lines.Where(l => l.ExpectedQty > 0).OrderBy(l => l.Product?.Sku))
                        {
                            idx++;
                            var bg = idx % 2 == 1 ? "#F8F7F5" : "#FFFFFF";
                            Td(table.Cell(), bg, idx.ToString());
                            Td(table.Cell(), bg, line.Product?.Sku ?? "");
                            Td(table.Cell(), bg, line.Product?.Name ?? "");
                            TdRight(table.Cell(), bg, line.ExpectedQty.ToString());
                        }
                    });

                    var totalItems = batch.Lines.Where(l => l.ExpectedQty > 0).Sum(l => l.ExpectedQty);
                    var totalLines = batch.Lines.Count(l => l.ExpectedQty > 0);
                    col.Item().PaddingTop(10).AlignRight().Column(totals =>
                    {
                        totals.Item().Text($"{totalLines} product lines,  {totalItems} total items").FontSize(10).Bold();
                    });

                    col.Item().PaddingTop(40).Column(sig =>
                    {
                        sig.Item().Row(row =>
                        {
                            row.RelativeItem().Column(left =>
                            {
                                left.Item().LineHorizontal(1).LineColor(BorderLight);
                                left.Item().PaddingTop(4).Text($"Packed by ({eff.BusinessName})").FontSize(8).FontColor(TextLight);
                            });
                            row.ConstantItem(30);
                            row.RelativeItem().Column(right =>
                            {
                                right.Item().LineHorizontal(1).LineColor(BorderLight);
                                right.Item().PaddingTop(4).Text("Received by (Supplier)").FontSize(8).FontColor(TextLight);
                            });
                        });
                        sig.Item().PaddingTop(16).Row(row =>
                        {
                            row.RelativeItem().Column(left =>
                            {
                                left.Item().LineHorizontal(1).LineColor(BorderLight);
                                left.Item().PaddingTop(4).Text("Date").FontSize(8).FontColor(TextLight);
                            });
                            row.ConstantItem(30);
                            row.RelativeItem().Column(right =>
                            {
                                right.Item().LineHorizontal(1).LineColor(BorderLight);
                                right.Item().PaddingTop(4).Text("Date").FontSize(8).FontColor(TextLight);
                            });
                        });
                    });
                });
            });
        }).GeneratePdf();
    }

    private static void Label(IContainer container, string label, string value)
    {
        container.Column(c =>
        {
            c.Item().Text(label).FontSize(7).Bold().FontColor(TextLight).LetterSpacing(0.08f);
            c.Item().PaddingTop(2).Text(value).FontSize(10).FontColor(TextDark);
        });
    }

    private static void Th(IContainer cell, string text, bool right = false)
    {
        var c = cell.BorderBottom(1).BorderColor(BorderLight).Background(TableHeadBg).Padding(6);
        if (right) c = c.AlignRight();
        c.Text(text).FontSize(7).Bold().FontColor(TextLight).LetterSpacing(0.06f);
    }

    private static void Td(IContainer cell, string bg, string text)
    {
        cell.Background(bg).BorderBottom(1).BorderColor(BorderLight).Padding(6)
            .Text(text).FontSize(9.5f);
    }

    private static void TdRight(IContainer cell, string bg, string text, string? color = null)
    {
        cell.Background(bg).BorderBottom(1).BorderColor(BorderLight).Padding(6).AlignRight()
            .Text(text).FontSize(9.5f).FontColor(color ?? TextDark);
    }
}
