using System.Globalization;
using System.Text;
using ClosedXML.Excel;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: ExportHuntexCsv <input.xlsx> <output.csv> [sheetName]");
    return 1;
}

var input = args[0];
var output = args[1];
var sheetName = args.Length > 2 ? args[2] : "huntex 2026";

if (!File.Exists(input))
{
    Console.Error.WriteLine($"Not found: {input}");
    return 1;
}

using var wb = new XLWorkbook(input);
var ws = wb.Worksheets.FirstOrDefault(w =>
        string.Equals(w.Name, sheetName, StringComparison.OrdinalIgnoreCase))
    ?? wb.Worksheets.First();

var lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;
var lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 1;

using var sw = new StreamWriter(output, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
for (var r = 1; r <= lastRow; r++)
{
    var parts = new List<string>(lastCol);
    for (var c = 1; c <= lastCol; c++)
        parts.Add(CsvEscape(CellText(ws.Cell(r, c))));
    sw.WriteLine(string.Join(',', parts));
}

Console.WriteLine($"Wrote {output} ({lastRow} rows x {lastCol} cols).");
return 0;

static string CellText(IXLCell cell)
{
    if (cell.IsEmpty()) return "";
    if (cell.DataType == XLDataType.Number)
        return cell.GetDouble().ToString(CultureInfo.InvariantCulture);
    if (cell.DataType == XLDataType.DateTime)
        return cell.GetDateTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    return cell.GetString().Trim();
}

static string CsvEscape(string s)
{
    if (string.IsNullOrEmpty(s)) return "";
    if (s.Contains('"') || s.Contains(',') || s.Contains('\n') || s.Contains('\r'))
        return "\"" + s.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    return s;
}
