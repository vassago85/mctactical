using System.Text.Json;
using HuntexPos.Api.Options;
using Microsoft.Extensions.Options;

namespace HuntexPos.Api.Services;

/// <summary>
/// Supplies a valid Shopify Admin API access token. Modern Dev Dashboard apps issue short-lived
/// (24h) tokens via the client-credentials grant rather than a permanent token, so this provider
/// exchanges the app's Client ID/Secret for a token, caches it, and refreshes shortly before it
/// expires. If a legacy static <see cref="ShopifyOptions.AdminAccessToken"/> is configured, that is
/// returned directly. Registered as a singleton so the cache is shared across requests.
/// </summary>
public class ShopifyTokenProvider
{
    private static readonly TimeSpan RefreshSkew = TimeSpan.FromMinutes(2);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ShopifyOptions _opt;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private string? _cachedToken;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    public ShopifyTokenProvider(IHttpClientFactory httpClientFactory, IOptions<ShopifyOptions> opt)
    {
        _httpClientFactory = httpClientFactory;
        _opt = opt.Value;
    }

    /// <summary>True when either a static token or a client id/secret pair is configured.</summary>
    public bool HasCredentials =>
        !string.IsNullOrWhiteSpace(_opt.AdminAccessToken)
        || (!string.IsNullOrWhiteSpace(_opt.ClientId) && !string.IsNullOrWhiteSpace(_opt.ClientSecret));

    public async Task<string> GetTokenAsync(CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(_opt.AdminAccessToken))
            return _opt.AdminAccessToken.Trim();

        if (string.IsNullOrWhiteSpace(_opt.ClientId) || string.IsNullOrWhiteSpace(_opt.ClientSecret))
            throw new ShopifyNotConfiguredException(
                "Shopify auth is not configured. Set Shopify:ClientId and Shopify:ClientSecret (Dev Dashboard app), or a legacy Shopify:AdminAccessToken.");

        if (_cachedToken != null && DateTimeOffset.UtcNow < _expiresAt - RefreshSkew)
            return _cachedToken;

        await _gate.WaitAsync(ct);
        try
        {
            if (_cachedToken != null && DateTimeOffset.UtcNow < _expiresAt - RefreshSkew)
                return _cachedToken;

            var (token, expiresIn) = await RequestTokenAsync(ct);
            _cachedToken = token;
            _expiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn);
            return token;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<(string Token, int ExpiresIn)> RequestTokenAsync(CancellationToken ct)
    {
        var domain = _opt.ShopDomain.Trim().TrimEnd('/');
        var url = $"https://{domain}/admin/oauth/access_token";

        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = _opt.ClientId.Trim(),
            ["client_secret"] = _opt.ClientSecret.Trim()
        });

        var http = _httpClientFactory.CreateClient();
        using var res = await http.PostAsync(url, content, ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
            throw new ShopifyApiException((int)res.StatusCode,
                $"Token request failed: {body}. If this is 'shop_not_permitted', the app and store must be in the same Dev Dashboard organization.");

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var token = root.TryGetProperty("access_token", out var t) ? t.GetString() : null;
        if (string.IsNullOrWhiteSpace(token))
            throw new ShopifyApiException((int)res.StatusCode, $"Token response missing access_token: {body}");

        var expiresIn = root.TryGetProperty("expires_in", out var e) && e.ValueKind == JsonValueKind.Number
            ? e.GetInt32()
            : 86399;

        return (token, expiresIn);
    }
}
