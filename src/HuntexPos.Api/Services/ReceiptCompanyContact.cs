using System.Net;
using HuntexPos.Api.DTOs;

namespace HuntexPos.Api.Services;

/// <summary>Formats configured shop contact details for receipts/emails/PDFs. Prefers the DB-backed business settings over hardcoded defaults.</summary>
public static class ReceiptCompanyContact
{
    public static CompanyContactDto ToDto(EffectiveBusinessSettings eff)
    {
        var name = string.IsNullOrWhiteSpace(eff.BusinessName) ? "Our Shop" : eff.BusinessName.Trim();
        var site = TrimOrNull(eff.Website);
        return new CompanyContactDto
        {
            DisplayName = name,
            Phone = TrimOrNull(eff.Phone),
            Email = TrimOrNull(eff.Email),
            Address = TrimOrNull(eff.Address),
            Website = site,
            WebsiteLabel = string.IsNullOrWhiteSpace(eff.WebsiteLabel)
                ? DeriveWebsiteLabel(site)
                : eff.WebsiteLabel.Trim()
        };
    }

    public static string ToEmailHtmlFooter(EffectiveBusinessSettings eff)
    {
        var d = ToDto(eff);
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(d.Phone))
            parts.Add($"Tel: {WebUtility.HtmlEncode(d.Phone)}");
        if (!string.IsNullOrEmpty(d.Email))
        {
            var e = WebUtility.HtmlEncode(d.Email);
            parts.Add($"""Email: <a href="mailto:{e}">{e}</a>""");
        }

        if (!string.IsNullOrEmpty(d.Address))
            parts.Add(WebUtility.HtmlEncode(d.Address).Replace("\n", "<br/>", StringComparison.Ordinal));

        if (!string.IsNullOrEmpty(d.Website))
        {
            var href = WebUtility.HtmlEncode(d.Website);
            var label = WebUtility.HtmlEncode(d.WebsiteLabel ?? d.Website);
            parts.Add($"""<a href="{href}">{label}</a>""");
        }

        if (parts.Count == 0)
            return string.Empty;

        var display = WebUtility.HtmlEncode(d.DisplayName);
        return $"""
            <hr style="border:none;border-top:1px solid #ddd;margin:1.5rem 0" />
            <p style="color:#444;font-size:0.9rem;line-height:1.6">
              <strong>{display}</strong><br/>
              {string.Join("<br/>", parts)}
            </p>
            """;
    }

    public static (string Title, IReadOnlyList<string> DetailLines) ToPdfFooter(EffectiveBusinessSettings eff)
    {
        var d = ToDto(eff);
        var lines = new List<string>();
        if (!string.IsNullOrEmpty(d.Phone))
            lines.Add($"Tel: {d.Phone}");
        if (!string.IsNullOrEmpty(d.Email))
            lines.Add($"Email: {d.Email}");
        if (!string.IsNullOrEmpty(d.Address))
        {
            foreach (var chunk in d.Address.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                lines.Add(chunk);
        }

        if (!string.IsNullOrEmpty(d.Website))
            lines.Add(d.Website);
        return (d.DisplayName, lines);
    }

    private static string? TrimOrNull(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string? DeriveWebsiteLabel(string? url)
    {
        if (string.IsNullOrEmpty(url))
            return null;
        var u = url.Trim();
        if (!u.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !u.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            u = "https://" + u;
        if (!Uri.TryCreate(u, UriKind.Absolute, out var uri))
            return url;
        var host = uri.Host;
        return host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;
    }
}
