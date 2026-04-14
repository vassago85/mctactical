using System.Globalization;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using CsvHelper;
using CsvHelper.Configuration;
using HuntexPos.Api.Data;
using HuntexPos.Api.Domain;
using HuntexPos.Api.DTOs;
using Microsoft.EntityFrameworkCore;

namespace HuntexPos.Api.Services;

public class ImportService
{
    private readonly HuntexDbContext _db;

    public ImportService(HuntexDbContext db) => _db = db;

    public async Task<(List<ImportPreviewRowDto> Rows, List<string> Warnings)> PreviewHuntexSheetAsync(
        Stream stream, string sheetName, Guid? supplierId, CancellationToken ct)
    {
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheets.FirstOrDefault(w =>
            string.Equals(w.Name, sheetName, StringComparison.OrdinalIgnoreCase))
            ?? workbook.Worksheets.First();

        var headers = ReadHeaderRow(sheet);
        var map = AutoMapHeaders(headers, sheet);
        var rows = new List<ImportPreviewRowDto>();
        var warnings = new List<string>();

        var settings = await _db.PricingSettings.AsNoTracking().FirstOrDefaultAsync(ct)
                       ?? new PricingSettings();

        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 1;
        for (var r = 2; r <= lastRow; r++)
        {
            var row = sheet.Row(r);
            if (IsRowEmpty(row, map)) continue;
            if (map.Sku != null && string.IsNullOrWhiteSpace(row.Cell(map.Sku.Value).GetString()))
                continue;

            var preview = ParseRow(row, map, r, settings, out var err);
            if (err != null)
                preview.Error = err;
            rows.Add(preview);
        }

        if (map.UnmappedHeaders.Count > 0)
            warnings.Add("Unmapped columns: " + string.Join(", ", map.UnmappedHeaders));

        return (rows, warnings);
    }

    /// <summary>Huntex import from CSV: same auto column mapping as Excel; first row must be headers.</summary>
    public async Task<(List<ImportPreviewRowDto> Rows, List<string> Warnings)> PreviewHuntexCsvAsync(
        Stream stream, Guid? supplierId, CancellationToken ct)
    {
        var rows = new List<ImportPreviewRowDto>();
        var warnings = new List<string>();
        var settings = await _db.PricingSettings.AsNoTracking().FirstOrDefaultAsync(ct)
                       ?? new PricingSettings();

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            MissingFieldFound = null,
            BadDataFound = null
        };
        using var csv = new CsvReader(reader, config);
        await csv.ReadAsync();
        csv.ReadHeader();
        var headerRecord = csv.HeaderRecord;
        if (headerRecord == null || headerRecord.Length == 0)
        {
            warnings.Add("CSV has no header row.");
            return (rows, warnings);
        }

        var headersDict = new Dictionary<int, string>();
        for (var i = 0; i < headerRecord.Length; i++)
            headersDict[i + 1] = headerRecord[i]?.Trim() ?? "";

        var map = AutoMapHeaders(headersDict, sheetForMerge: null);
        var colCount = headerRecord.Length;
        var rowIndex = 1;
        while (await csv.ReadAsync())
        {
            rowIndex++;
            var cells = new string?[colCount];
            for (var i = 0; i < colCount; i++)
            {
                try
                {
                    cells[i] = csv.GetField(i)?.Trim();
                }
                catch
                {
                    cells[i] = null;
                }
            }

            if (IsRowEmptyFromCells(cells, map)) continue;
            if (map.Sku != null)
            {
                var si = map.Sku.Value - 1;
                if (si < 0 || si >= cells.Length || string.IsNullOrWhiteSpace(cells[si]))
                    continue;
            }

            var preview = ParseRowFromGetter(col => GetCellFromArray(cells, col), map, rowIndex, settings, out var err);
            if (err != null)
                preview.Error = err;
            rows.Add(preview);
        }

        if (map.UnmappedHeaders.Count > 0)
            warnings.Add("Unmapped columns: " + string.Join(", ", map.UnmappedHeaders));

        return (rows, warnings);
    }

    public async Task<int> CommitHuntexPreviewAsync(List<ImportPreviewRowDto> validRows, Guid? supplierId, CancellationToken ct)
    {
        var count = 0;
        foreach (var p in validRows.Where(x => x.Error == null))
        {
            var sku = p.Sku.Trim();
            var existing = await _db.Products.FirstOrDefaultAsync(x => x.Sku == sku, ct);
            if (existing != null)
            {
                existing.Name = p.Name;
                existing.Barcode = string.IsNullOrWhiteSpace(p.Barcode) ? existing.Barcode : p.Barcode.Trim();
                existing.Cost = p.Cost;
                existing.SellPrice = p.SellPrice;
                existing.QtyOnHand = p.QtyOnHand;
                existing.Category = p.Category ?? existing.Category;
                existing.Manufacturer = p.Manufacturer ?? existing.Manufacturer;
                existing.ItemType = p.ItemType ?? existing.ItemType;
                existing.SupplierId = supplierId ?? existing.SupplierId;
                existing.UpdatedAt = DateTimeOffset.UtcNow;
            }
            else
            {
                _db.Products.Add(new Product
                {
                    Id = Guid.NewGuid(),
                    Sku = sku,
                    Barcode = string.IsNullOrWhiteSpace(p.Barcode) ? null : p.Barcode.Trim(),
                    Name = p.Name,
                    Category = p.Category,
                    Manufacturer = p.Manufacturer,
                    ItemType = p.ItemType,
                    Cost = p.Cost,
                    SellPrice = p.SellPrice,
                    QtyOnHand = p.QtyOnHand,
                    SupplierId = supplierId,
                    Active = true
                });
            }
            count++;
        }
        await _db.SaveChangesAsync(ct);
        return count;
    }

    public async Task<(List<ImportPreviewRowDto> Rows, List<string> Warnings)> PreviewWholesalerAsync(
        Stream stream, string fileName, Guid supplierId, ColumnMappingDto mapping, CancellationToken ct)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        if (ext == ".xlsx" || ext == ".xlsm")
            return await PreviewWholesalerXlsxAsync(stream, supplierId, mapping, ct);
        return await PreviewWholesalerCsvAsync(stream, supplierId, mapping, ct);
    }

    private async Task<(List<ImportPreviewRowDto>, List<string>)> PreviewWholesalerCsvAsync(
        Stream stream, Guid supplierId, ColumnMappingDto mapping, CancellationToken ct)
    {
        var settings = await _db.PricingSettings.AsNoTracking().FirstOrDefaultAsync(ct) ?? new PricingSettings();
        using var reader = new StreamReader(stream);
        using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            MissingFieldFound = null,
            BadDataFound = null
        });
        await csv.ReadAsync();
        csv.ReadHeader();
        var headers = csv.HeaderRecord?.ToList() ?? new List<string>();
        var idx = BuildIndex(headers, mapping);

        var rows = new List<ImportPreviewRowDto>();
        var rowIndex = 1;
        while (await csv.ReadAsync())
        {
            rowIndex++;
            var preview = ParseCsvRow(csv, idx, rowIndex, settings, out var err);
            if (err != null) preview.Error = err;
            rows.Add(preview);
        }
        return (rows, new List<string>());
    }

    private async Task<(List<ImportPreviewRowDto>, List<string>)> PreviewWholesalerXlsxAsync(
        Stream stream, Guid supplierId, ColumnMappingDto mapping, CancellationToken ct)
    {
        var settings = await _db.PricingSettings.AsNoTracking().FirstOrDefaultAsync(ct) ?? new PricingSettings();
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheets.First();
        var headers = ReadHeaderRow(sheet);
        var letterMap = MappingToLetterMap(mapping, headers);
        var rows = new List<ImportPreviewRowDto>();
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 1;
        for (var r = 2; r <= lastRow; r++)
        {
            var row = sheet.Row(r);
            var preview = ParseRowByLetters(row, letterMap, r, settings, out var err);
            if (err != null) preview.Error = err;
            rows.Add(preview);
        }
        return (rows, new List<string>());
    }

    public async Task<int> CommitWholesalerAsync(List<ImportPreviewRowDto> validRows, Guid supplierId, CancellationToken ct)
    {
        var count = 0;
        foreach (var p in validRows.Where(x => x.Error == null))
        {
            var sku = p.Sku.Trim();
            var existing = await _db.Products.FirstOrDefaultAsync(x => x.Sku == sku, ct);
            if (existing != null)
            {
                existing.Name = p.Name;
                existing.Barcode = string.IsNullOrWhiteSpace(p.Barcode) ? existing.Barcode : p.Barcode!.Trim();
                existing.Cost = p.Cost;
                existing.SellPrice = p.SellPrice;
                existing.QtyOnHand = p.QtyOnHand;
                existing.Category = p.Category ?? existing.Category;
                existing.Manufacturer = p.Manufacturer ?? existing.Manufacturer;
                existing.ItemType = p.ItemType ?? existing.ItemType;
                existing.SupplierId = supplierId;
                existing.UpdatedAt = DateTimeOffset.UtcNow;
            }
            else
            {
                _db.Products.Add(new Product
                {
                    Id = Guid.NewGuid(),
                    Sku = sku,
                    Barcode = string.IsNullOrWhiteSpace(p.Barcode) ? null : p.Barcode.Trim(),
                    Name = p.Name,
                    Category = p.Category,
                    Manufacturer = p.Manufacturer,
                    ItemType = p.ItemType,
                    Cost = p.Cost,
                    SellPrice = p.SellPrice,
                    QtyOnHand = p.QtyOnHand,
                    SupplierId = supplierId,
                    Active = true
                });
            }
            count++;
        }
        await _db.SaveChangesAsync(ct);
        return count;
    }

    private static Dictionary<string, int?> BuildIndex(List<string> headers, ColumnMappingDto m)
    {
        var h = headers.Select((x, i) => (Name: x?.Trim() ?? "", Index: i)).ToList();
        int? Find(string? key)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;
            if (key.Length == 1 && char.IsLetter(key[0]))
                return ColumnLetterToIndex(key);
            var idx = h.FindIndex(x => string.Equals(x.Name, key, StringComparison.OrdinalIgnoreCase));
            return idx >= 0 ? idx : null;
        }
        return new Dictionary<string, int?>
        {
            ["sku"] = Find(m.Sku),
            ["barcode"] = Find(m.Barcode),
            ["name"] = Find(m.Name),
            ["desc"] = Find(m.Description),
            ["category"] = Find(m.Category),
            ["cost"] = Find(m.Cost),
            ["sell"] = Find(m.SellPrice),
            ["qty"] = Find(m.QtyOnHand)
        };
    }

    private static int ColumnLetterToIndex(string letters)
    {
        letters = letters.Trim().ToUpperInvariant();
        var n = 0;
        foreach (var c in letters)
        {
            if (c < 'A' || c > 'Z') return 0;
            n = n * 26 + (c - 'A' + 1);
        }
        return n - 1;
    }

    private static ImportPreviewRowDto ParseCsvRow(CsvReader csv, Dictionary<string, int?> idx, int rowIndex, PricingSettings settings, out string? error)
    {
        error = null;
        string Get(string key)
        {
            var i = idx[key];
            if (i == null) return "";
            try { return csv.GetField(i.Value)?.Trim() ?? ""; }
            catch { return ""; }
        }
        return BuildPreviewFromStrings(Get, rowIndex, settings, out error, Get("category"));
    }

    private static LetterMap MappingToLetterMap(ColumnMappingDto m, Dictionary<int, string> headers)
    {
        int? Col(string? key)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;
            if (key.Length <= 3 && key.All(char.IsLetter))
                return ColumnLetterToIndex(key) + 1;
            var kv = headers.FirstOrDefault(h => string.Equals(h.Value, key, StringComparison.OrdinalIgnoreCase));
            return kv.Key == 0 ? null : kv.Key;
        }
        return new LetterMap(
            Col(m.Sku), Col(m.Barcode), Col(m.Name), Col(m.Description), Col(m.Category),
            Col(m.Cost), Col(m.SellPrice), Col(m.QtyOnHand));
    }

    private record LetterMap(int? Sku, int? Barcode, int? Name, int? Desc, int? Category, int? Cost, int? Sell, int? Qty);

    private static ImportPreviewRowDto ParseRowByLetters(IXLRow row, LetterMap map, int rowIndex, PricingSettings settings, out string? error)
    {
        error = null;
        string GetCol(int? col)
        {
            if (col == null) return "";
            return row.Cell(col.Value).GetString().Trim();
        }
        string Get(string key) => key switch
        {
            "sku" => GetCol(map.Sku),
            "barcode" => GetCol(map.Barcode),
            "name" => GetCol(map.Name),
            "desc" => GetCol(map.Desc),
            "category" => GetCol(map.Category),
            "cost" => GetCol(map.Cost),
            "sell" => GetCol(map.Sell),
            "qty" => GetCol(map.Qty),
            _ => ""
        };
        return BuildPreviewFromStrings(Get, rowIndex, settings, out error, Get("category"));
    }

    private static ImportPreviewRowDto BuildPreviewFromStrings(Func<string, string> get, int rowIndex, PricingSettings settings, out string? error, string? category = null)
    {
        error = null;
        var sku = get("sku");
        var name = get("name");
        if (string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(get("desc")))
            name = get("desc");
        if (string.IsNullOrWhiteSpace(sku))
        {
            error = "Missing SKU";
            return new ImportPreviewRowDto { RowIndex = rowIndex, Name = name ?? "" };
        }
        if (string.IsNullOrWhiteSpace(name))
        {
            error = "Missing name";
            return new ImportPreviewRowDto { RowIndex = rowIndex, Sku = sku };
        }

        var cost = ParseDecimal(get("cost"), 0);
        var sellRaw = get("sell");
        decimal sellPrice;
        if (!string.IsNullOrWhiteSpace(sellRaw))
            sellPrice = PricingCalculator.ApplyRounding(ParseDecimal(sellRaw, 0), settings);
        else
            sellPrice = PricingCalculator.ComputeSellPrice(cost, settings);

        var qty = (int)ParseDecimal(get("qty"), 0);

        return new ImportPreviewRowDto
        {
            RowIndex = rowIndex,
            Sku = sku,
            Barcode = string.IsNullOrWhiteSpace(get("barcode")) ? null : get("barcode"),
            Name = name.Trim(),
            Category = string.IsNullOrWhiteSpace(category) ? null : category.Trim(),
            Cost = cost,
            SellPrice = sellPrice,
            QtyOnHand = qty
        };
    }

    private sealed class HeaderMap
    {
        public int? Sku { get; init; }
        public int? Barcode { get; init; }
        public int? Name { get; init; }
        public int? Description { get; init; }
        public int? Category { get; init; }
        public int? Manufacturer { get; init; }
        public int? ItemType { get; init; }
        public int? Cost { get; init; }
        public int? Sell { get; init; }
        public int? Qty { get; init; }
        public List<string> UnmappedHeaders { get; init; } = new();
    }

    private static Dictionary<int, string> ReadHeaderRow(IXLWorksheet sheet)
    {
        var row = sheet.Row(1);
        var last = row.LastCellUsed()?.Address.ColumnNumber ?? 1;
        var d = new Dictionary<int, string>();
        for (var c = 1; c <= last; c++)
            d[c] = row.Cell(c).GetString().Trim();
        return d;
    }

    private static HeaderMap AutoMapHeaders(Dictionary<int, string> headers, IXLWorksheet? sheetForMerge)
    {
        int? Match(params string[] keys)
        {
            foreach (var kv in headers)
            {
                var h = kv.Value.ToLowerInvariant();
                if (string.IsNullOrEmpty(h)) continue;
                if (keys.Any(k => h.Contains(k, StringComparison.OrdinalIgnoreCase)))
                    return kv.Key;
            }
            return null;
        }

        int? MatchExact(string header)
        {
            foreach (var kv in headers)
            {
                if (string.Equals(kv.Value.Trim(), header, StringComparison.OrdinalIgnoreCase))
                    return kv.Key;
            }
            return null;
        }

        // Huntex sheets: cost = wholesale ex VAT ("Price Excl."). List retail = ex VAT × 1.5; POS sell = Huntex rounded (typo "Huntes" in some files).
        var sku = Match("product code", "sku", "item code", "code", "article", "style");
        var barcode = Match("barcode", "ean", "upc", "gtin");
        var name = Match("product description", "description", "product name", "item", "name") ?? Match("title");
        var desc = Match("long desc", "details");
        // Short "Product" column (not "Product Code") → category / line
        var category = MatchExact("Product")
                       ?? Match("category", "department", "group", "product line");
        // Ex-VAT wholesale only — never Huntex columns (avoid "Huntex sale p"). "Price Excl." before "Price Excluding x 1.5" in column order.
        var cost = Match("price excl", "excl.", "ex vat", "exclusive")
                   ?? Match("cost", "buy", "wholesale", "unit cost", "net");
        // Sell: prefer rounded Huntex column; then list retail inc VAT; never unrounded Huntex / raw ×1.5 / line wholesale inc.
        int? sell = Match(
            "huntes rounded",
            "huntex rounded",
            "rounded to next 10",
            "rounded to nearest");
        if (sell == null)
        {
            sell = Match(
                "retail in vat",
                "retail inc vat",
                "rrp",
                "retail price",
                "selling price",
                "sell price",
                "retail inc",
                "inc gst");
        }
        if (sell == null)
        {
            foreach (var kv in headers.OrderBy(kv => kv.Key))
            {
                var h = kv.Value.ToLowerInvariant();
                if (string.IsNullOrEmpty(h)) continue;
                if (h.Contains("total", StringComparison.OrdinalIgnoreCase)) continue;
                if (h.Contains("huntex sale", StringComparison.OrdinalIgnoreCase)) continue;
                if (h.Contains("huntes rounded", StringComparison.OrdinalIgnoreCase)) continue;
                if (h.Contains("huntex rounded", StringComparison.OrdinalIgnoreCase)) continue;
                if (h.Contains("x 1.5", StringComparison.OrdinalIgnoreCase)) continue;
                if (h.Contains("excluding x", StringComparison.OrdinalIgnoreCase)) continue;
                if (h.Contains("cost", StringComparison.OrdinalIgnoreCase)) continue;
                if (h.Contains("huntex", StringComparison.OrdinalIgnoreCase)) continue;
                if (h.Contains("excl", StringComparison.OrdinalIgnoreCase)) continue;
                if (h.Contains("price inc", StringComparison.OrdinalIgnoreCase)
                    || h.Contains("price incl", StringComparison.OrdinalIgnoreCase))
                {
                    sell = kv.Key;
                    break;
                }
            }
        }
        if (sell == null)
        {
            foreach (var kv in headers.OrderBy(kv => kv.Key))
            {
                var h = kv.Value.ToLowerInvariant();
                if (string.IsNullOrEmpty(h)) continue;
                if (h.Contains("total", StringComparison.OrdinalIgnoreCase)) continue;
                if (h.Contains("huntex sale", StringComparison.OrdinalIgnoreCase)) continue;
                if (h.Contains("x 1.5", StringComparison.OrdinalIgnoreCase)) continue;
                if (h.Contains("excluding x", StringComparison.OrdinalIgnoreCase)) continue;
                if (h.Contains("cost", StringComparison.OrdinalIgnoreCase)) continue;
                if (h.Contains("huntex", StringComparison.OrdinalIgnoreCase)) continue;
                if (h.Contains("price", StringComparison.OrdinalIgnoreCase) || h.Contains("retail", StringComparison.OrdinalIgnoreCase) || h == "sell")
                {
                    sell = kv.Key;
                    break;
                }
            }
        }
        var qty = Match("qty", "quantity", "stock", "on hand", "soh");
        var manufacturer = Match("manufacturer", "brand", "make");
        var itemType = Match("item type", "type", "product type");

        if (sheetForMerge != null && !IsExplicitHuntexRoundedColumn(headers, sell))
            sell = TryResolveHuntexRoundedSellFromMerge(sheetForMerge, headers) ?? sell;

        var mappedCols = new HashSet<int>();
        foreach (var n in new int?[] { sku, barcode, name, desc, category, manufacturer, itemType, cost, sell, qty })
            if (n.HasValue) mappedCols.Add(n.Value);

        var unmapped = headers.Where(kv => !string.IsNullOrWhiteSpace(kv.Value) && !mappedCols.Contains(kv.Key))
            .Select(kv => kv.Value).ToList();

        return new HeaderMap
        {
            Sku = sku,
            Barcode = barcode,
            Name = name,
            Description = desc,
            Category = category,
            Manufacturer = manufacturer,
            ItemType = itemType,
            Cost = cost,
            Sell = sell,
            Qty = qty,
            UnmappedHeaders = unmapped
        };
    }

    /// <summary>Header already names the Huntex rounded column (e.g. "Huntes Rounded to next 10").</summary>
    private static bool IsExplicitHuntexRoundedColumn(Dictionary<int, string> headers, int? sellCol)
    {
        if (sellCol == null) return false;
        var h = headers.GetValueOrDefault(sellCol.Value, "").ToLowerInvariant();
        return h.Contains("rounded", StringComparison.OrdinalIgnoreCase)
               && (h.Contains("huntex", StringComparison.OrdinalIgnoreCase)
                   || h.Contains("huntes", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Second data column under a row-1 "Huntex" group merge (rounded price), or column after a lone Huntex label.
    /// Skips headers that are Huntex sale / unrounded.
    /// </summary>
    private static int? TryResolveHuntexRoundedSellFromMerge(IXLWorksheet sheet, Dictionary<int, string> headers)
    {
        var headerLast = headers.Keys.DefaultIfEmpty(0).Max();
        var sheetLast = sheet.LastColumnUsed()?.ColumnNumber() ?? headerLast;
        var maxCol = Math.Max(headerLast, sheetLast);
        var seenMerge = new HashSet<string>(StringComparer.Ordinal);

        for (var c = 1; c <= maxCol; c++)
        {
            var cell = sheet.Cell(1, c);
            if (!cell.IsMerged()) continue;
            IXLRange merge;
            try
            {
                merge = cell.MergedRange();
            }
            catch
            {
                continue;
            }

            var ra = merge.RangeAddress;
            if (ra.FirstAddress.RowNumber != 1 || ra.LastAddress.RowNumber != 1)
                continue;
            var mergeKey = $"{ra.FirstAddress.RowNumber}:{ra.FirstAddress.ColumnNumber}:{ra.LastAddress.RowNumber}:{ra.LastAddress.ColumnNumber}";
            if (!seenMerge.Add(mergeKey)) continue;

            var label = merge.FirstCell().GetString().Trim();
            var low = label.ToLowerInvariant();
            if (string.IsNullOrEmpty(low) || !low.Contains("huntex", StringComparison.OrdinalIgnoreCase))
                continue;
            if (low.Contains("rounded", StringComparison.OrdinalIgnoreCase)
                || low.Contains("sale", StringComparison.OrdinalIgnoreCase))
                continue;

            var fc = ra.FirstAddress.ColumnNumber;
            var lc = ra.LastAddress.ColumnNumber;
            if (lc > fc)
                return fc + 1;
        }

        for (var k = 1; k <= maxCol; k++)
        {
            var h = headers.GetValueOrDefault(k, "").Trim();
            var low = h.ToLowerInvariant();
            if (string.IsNullOrEmpty(low) || !low.Contains("huntex", StringComparison.OrdinalIgnoreCase))
                continue;
            if (low.Contains("rounded", StringComparison.OrdinalIgnoreCase)
                || low.Contains("sale", StringComparison.OrdinalIgnoreCase))
                continue;

            var cell = sheet.Cell(1, k);
            if (cell.IsMerged())
            {
                try
                {
                    var merge = cell.MergedRange();
                    var rng = merge.RangeAddress;
                    if (rng.LastAddress.ColumnNumber > rng.FirstAddress.ColumnNumber)
                        return rng.FirstAddress.ColumnNumber + 1;
                }
                catch
                {
                    return k + 1 <= maxCol ? k + 1 : null;
                }
            }
            else
                return k + 1 <= maxCol ? k + 1 : null;
        }

        return null;
    }

    private static bool IsRowEmpty(IXLRow row, HeaderMap map)
    {
        foreach (var col in new[] { map.Sku, map.Name, map.Barcode, map.Cost, map.Sell, map.Qty, map.Manufacturer, map.ItemType })
        {
            if (col == null) continue;
            if (!string.IsNullOrWhiteSpace(row.Cell(col.Value).GetString()))
                return false;
        }
        return true;
    }

    private static bool IsRowEmptyFromCells(string?[] cells, HeaderMap map)
    {
        foreach (var col in new[] { map.Sku, map.Name, map.Barcode, map.Cost, map.Sell, map.Qty })
        {
            if (col == null) continue;
            var i = col.Value - 1;
            if (i >= 0 && i < cells.Length && !string.IsNullOrWhiteSpace(cells[i]))
                return false;
        }
        return true;
    }

    private static string GetCellFromArray(string?[] cells, int? col)
    {
        if (col == null) return "";
        var i = col.Value - 1;
        if (i < 0 || i >= cells.Length) return "";
        return cells[i]?.Trim() ?? "";
    }

    private static ImportPreviewRowDto ParseRow(IXLRow row, HeaderMap map, int rowIndex, PricingSettings settings, out string? error)
    {
        string Get(int? col) => col == null ? "" : row.Cell(col.Value).GetString().Trim();
        return ParseRowFromGetter(Get, map, rowIndex, settings, out error);
    }

    private static ImportPreviewRowDto ParseRowFromGetter(
        Func<int?, string> get,
        HeaderMap map,
        int rowIndex,
        PricingSettings settings,
        out string? error)
    {
        error = null;
        var sku = get(map.Sku);
        var name = get(map.Name);
        if (string.IsNullOrWhiteSpace(name))
            name = get(map.Description);
        if (string.IsNullOrWhiteSpace(sku))
        {
            error = "Missing SKU";
            return new ImportPreviewRowDto { RowIndex = rowIndex, Name = name };
        }
        if (string.IsNullOrWhiteSpace(name))
        {
            error = "Missing name";
            return new ImportPreviewRowDto { RowIndex = rowIndex, Sku = sku };
        }

        var cost = ParseDecimal(get(map.Cost), 0);
        var sellStr = get(map.Sell);
        decimal sellPrice = string.IsNullOrWhiteSpace(sellStr)
            ? PricingCalculator.ComputeSellPrice(cost, settings)
            : PricingCalculator.ApplyRounding(ParseDecimal(sellStr, 0), settings);
        var qty = (int)ParseDecimal(get(map.Qty), 0);
        var barcode = get(map.Barcode);
        var category = get(map.Category);
        var manufacturer = get(map.Manufacturer);
        var itemTypeVal = get(map.ItemType);

        return new ImportPreviewRowDto
        {
            RowIndex = rowIndex,
            Sku = sku,
            Barcode = string.IsNullOrWhiteSpace(barcode) ? null : barcode,
            Name = name,
            Category = string.IsNullOrWhiteSpace(category) ? null : category,
            Manufacturer = string.IsNullOrWhiteSpace(manufacturer) ? null : manufacturer,
            ItemType = string.IsNullOrWhiteSpace(itemTypeVal) ? null : itemTypeVal,
            Cost = cost,
            SellPrice = sellPrice,
            QtyOnHand = qty
        };
    }

    private static decimal ParseDecimal(string s, decimal fallback) =>
        HuntexImportNumberParsing.ParseAmount(s, fallback);

    public static string SerializeMapping(ColumnMappingDto m) => JsonSerializer.Serialize(m);

    public static ColumnMappingDto DeserializeMapping(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<ColumnMappingDto>(json) ?? new ColumnMappingDto();
        }
        catch
        {
            return new ColumnMappingDto();
        }
    }
}
