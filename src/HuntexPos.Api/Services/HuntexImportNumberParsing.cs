using System.Globalization;
using System.Text.RegularExpressions;

namespace HuntexPos.Api.Services;

/// <summary>Parsing Huntex sheet currency cells (R prefix, spaces, thousands commas).</summary>
public static class HuntexImportNumberParsing
{
    public static decimal ParseAmount(string s, decimal fallback)
    {
        if (string.IsNullOrWhiteSpace(s)) return fallback;
        s = s.Trim();
        if (s.Length > 0 && (s[0] == 'R' || s[0] == 'r'))
            s = s[1..].Trim();
        s = s.Replace(",", "", StringComparison.Ordinal)
            .Replace("\u00a0", "", StringComparison.Ordinal);
        s = Regex.Replace(s, @"\s+", "");
        return decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : fallback;
    }
}
