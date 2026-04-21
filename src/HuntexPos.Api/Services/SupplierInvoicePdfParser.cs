using System.Globalization;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace HuntexPos.Api.Services;

/// <summary>
/// Simple text-layer PDF parser for supplier delivery notes / invoices.
/// Extracts (sku, qty, unitCost) tuples from lines that look like
/// "SKU  description…  qty  cost". Returns raw text + unparsed rows when the
/// heuristic fails so the caller can fall back to manual triage.
/// </summary>
public class SupplierInvoicePdfParser
{
    public class ParsedLine
    {
        public string Sku { get; set; } = string.Empty;
        public int Qty { get; set; }
        public decimal? UnitCost { get; set; }
        public string? Description { get; set; }
    }

    public class ParseResult
    {
        public List<ParsedLine> Lines { get; set; } = new();
        public List<string> UnparsedLines { get; set; } = new();
        public string RawText { get; set; } = string.Empty;
    }

    // "SKU  …description…  QTY  COST" at end of line.
    // SKU = letters/digits/dash/dot/slash, 3+ chars.
    // QTY = 1-4 digits.
    // COST = "1234.56" or "1,234.56" (must have decimal).
    private static readonly Regex LineRegex = new(
        @"^\s*(?<sku>[A-Za-z0-9][A-Za-z0-9\-\./]{2,})\s+(?<desc>.+?)\s+(?<qty>\d{1,4})\s+(?<cost>\d{1,3}(?:[\s,]\d{3})*\.\d{2}|\d+\.\d{2})\s*$",
        RegexOptions.Compiled);

    // Fallback: lines that start with a SKU-like token and have a numeric qty but no cost.
    private static readonly Regex SkuQtyRegex = new(
        @"^\s*(?<sku>[A-Za-z0-9][A-Za-z0-9\-\./]{2,})\s+(?<desc>.+?)\s+(?<qty>\d{1,4})\s*$",
        RegexOptions.Compiled);

    public virtual ParseResult Parse(Stream pdfStream)
    {
        var result = new ParseResult();
        string rawText;
        try
        {
            using var doc = PdfDocument.Open(pdfStream);
            var sb = new System.Text.StringBuilder();
            foreach (Page page in doc.GetPages())
            {
                sb.AppendLine(page.Text);
            }
            rawText = sb.ToString();
        }
        catch (Exception ex)
        {
            result.RawText = $"Failed to read PDF: {ex.Message}";
            return result;
        }

        result.RawText = rawText;
        return ParseText(rawText, result);
    }

    public virtual ParseResult ParseText(string text)
    {
        return ParseText(text, new ParseResult { RawText = text });
    }

    private static ParseResult ParseText(string text, ParseResult result)
    {
        var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var seenSkus = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length < 5) continue;
            if (LooksLikeHeader(line)) continue;

            var m = LineRegex.Match(line);
            if (m.Success && int.TryParse(m.Groups["qty"].Value, out var qty) && qty > 0)
            {
                var sku = m.Groups["sku"].Value.Trim();
                var costRaw = m.Groups["cost"].Value.Replace(" ", "").Replace(",", "");
                decimal? cost = null;
                if (decimal.TryParse(costRaw, NumberStyles.Number, CultureInfo.InvariantCulture, out var c) && c >= 0)
                    cost = c;

                if (seenSkus.Add(sku))
                {
                    result.Lines.Add(new ParsedLine
                    {
                        Sku = sku,
                        Qty = qty,
                        UnitCost = cost,
                        Description = m.Groups["desc"].Value.Trim()
                    });
                }
                continue;
            }

            var m2 = SkuQtyRegex.Match(line);
            if (m2.Success && int.TryParse(m2.Groups["qty"].Value, out var qty2) && qty2 > 0)
            {
                var sku = m2.Groups["sku"].Value.Trim();
                if (seenSkus.Add(sku))
                {
                    result.Lines.Add(new ParsedLine
                    {
                        Sku = sku,
                        Qty = qty2,
                        UnitCost = null,
                        Description = m2.Groups["desc"].Value.Trim()
                    });
                }
                continue;
            }

            result.UnparsedLines.Add(line);
        }

        return result;
    }

    private static bool LooksLikeHeader(string line)
    {
        var upper = line.ToUpperInvariant();
        if (upper.Contains("INVOICE") && !char.IsDigit(line[0])) return true;
        if (upper.Contains("DELIVERY NOTE")) return true;
        if (upper.Contains("TOTAL") && !char.IsLetterOrDigit(line[0])) return true;
        if (upper.Contains("SUBTOTAL")) return true;
        if (upper.Contains("VAT")) return true;
        if (upper.StartsWith("PAGE ")) return true;
        return false;
    }
}
