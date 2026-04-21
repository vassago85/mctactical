using HuntexPos.Api.Services;
using Xunit;

namespace HuntexPos.Api.Tests;

public class SupplierInvoicePdfParserTests
{
    private readonly SupplierInvoicePdfParser _parser = new();

    [Fact]
    public void ParseText_ExtractsSkuQtyAndCost_FromTypicalLine()
    {
        const string text = @"
            SUPPLIER DELIVERY NOTE
            Code          Description           Qty   Unit cost
            WID-001       Red Widget             3    49.50
            WID-002       Blue Widget - Large   10   123.00
            Subtotal                                   0.00
        ";

        var result = _parser.ParseText(text);

        Assert.Equal(2, result.Lines.Count);
        Assert.Equal("WID-001", result.Lines[0].Sku);
        Assert.Equal(3, result.Lines[0].Qty);
        Assert.Equal(49.50m, result.Lines[0].UnitCost);

        Assert.Equal("WID-002", result.Lines[1].Sku);
        Assert.Equal(10, result.Lines[1].Qty);
        Assert.Equal(123.00m, result.Lines[1].UnitCost);
    }

    [Fact]
    public void ParseText_CollectsUnknownLines_WhenNoCostColumn()
    {
        const string text = @"
            Random disclaimer text
            WID-003  Green Widget  2
            some footer line without numbers
        ";

        var result = _parser.ParseText(text);

        Assert.Single(result.Lines);
        Assert.Equal("WID-003", result.Lines[0].Sku);
        Assert.Equal(2, result.Lines[0].Qty);
        Assert.Null(result.Lines[0].UnitCost);

        Assert.Contains(result.UnparsedLines, l => l.Contains("Random disclaimer"));
        Assert.Contains(result.UnparsedLines, l => l.Contains("some footer line"));
    }

    [Fact]
    public void ParseText_ReturnsZeroLines_ForJunkText()
    {
        const string text = "blah blah\nSUBTOTAL 1234.00\nVAT 0.00\n";
        var result = _parser.ParseText(text);
        Assert.Empty(result.Lines);
    }

    [Fact]
    public void ParseText_HandlesCommaInCost()
    {
        const string text = "WID-500  Premium Widget  1  1,250.00\n";
        var result = _parser.ParseText(text);
        var line = Assert.Single(result.Lines);
        Assert.Equal("WID-500", line.Sku);
        Assert.Equal(1, line.Qty);
        Assert.Equal(1250.00m, line.UnitCost);
    }

    [Fact]
    public void Parse_OnMalformedPdfStream_ReturnsEmptyResultWithFailureText()
    {
        using var ms = new MemoryStream(new byte[] { 0x00, 0x01, 0x02, 0x03 });
        var result = _parser.Parse(ms);
        Assert.Empty(result.Lines);
        Assert.StartsWith("Failed to read PDF:", result.RawText);
    }
}
