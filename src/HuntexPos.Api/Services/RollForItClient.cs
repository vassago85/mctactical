using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HuntexPos.Api.Options;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace HuntexPos.Api.Services;

/// <summary>
/// Signed HTTP client to the Roll for It POS API. All methods block the operator — these calls
/// happen at the till with the customer standing there, so timeouts are deliberately short.
/// </summary>
public class RollForItClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;
    private readonly RollForItOptions _options;
    private readonly ILogger<RollForItClient> _logger;
    private readonly IMemoryCache _cache;

    public RollForItClient(HttpClient http, IOptions<RollForItOptions> options, IMemoryCache cache, ILogger<RollForItClient> logger)
    {
        _options = options.Value;
        _http = http;
        _http.Timeout = TimeSpan.FromSeconds(8);
        _cache = cache;
        _logger = logger;
    }

    public bool IsConfigured => _options.IsConfigured();

    public async Task<RollForItStatusDto> StatusAsync(CancellationToken ct)
    {
        var json = await SendAsync("status", new { }, ct);
        return JsonSerializer.Deserialize<RollForItStatusDto>(json, JsonOptions)
               ?? throw new InvalidOperationException("Roll for It returned an empty status payload.");
    }

    public async Task<RollForItRollResult> RollAsync(RollForItRollRequest request, CancellationToken ct)
    {
        var payload = new
        {
            email = request.Email,
            phone = request.Phone,
            picked_number = request.PickedNumber,
            subtotal_cents = request.SubtotalCents,
            operator_id = request.OperatorId,
            cart_token = request.CartToken,
            currency = request.Currency,
        };

        var raw = await SendAsync("roll", payload, ct);
        var result = JsonSerializer.Deserialize<RollForItRollResult>(raw, JsonOptions)
                     ?? throw new InvalidOperationException("Roll for It returned an empty roll payload.");

        // Stash wins by roll_id so InvoiceService can trust the amount at checkout without re-asking
        // Roll for It. The entry is consumed when the invoice is finalised; losses are not stored.
        if (result.Won && result.RollId > 0 && result.Payout > 0)
        {
            _cache.Set(CacheKey(result.RollId), new HeldWin(result.RollId, result.Payout, DateTimeOffset.UtcNow),
                TimeSpan.FromMinutes(_options.WinningHoldMinutes));
        }

        return result;
    }

    /// <summary>
    /// Consumes a held win and returns the server-side payout to apply. Throws when the roll
    /// is unknown, expired, or already consumed, so a tampered Vue request can't fabricate a discount.
    /// </summary>
    public long ConsumeHeldWin(long rollId, long claimedPayout)
    {
        if (!_cache.TryGetValue(CacheKey(rollId), out HeldWin? win) || win is null)
        {
            throw new InvalidOperationException("Roll for It win has expired or is unknown. Roll again.");
        }
        if (win.Payout != claimedPayout)
        {
            _logger.LogWarning("Roll for It payout mismatch for roll {RollId}: cached {Cached}, claimed {Claimed}",
                rollId, win.Payout, claimedPayout);
            throw new InvalidOperationException("Roll for It payout does not match the recorded win.");
        }

        _cache.Remove(CacheKey(rollId));
        return win.Payout;
    }

    private static string CacheKey(long rollId) => $"rfi:win:{rollId}";

    private async Task<string> SendAsync(string path, object payload, CancellationToken ct)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("Roll for It is not configured on this till.");
        }

        var body = JsonSerializer.Serialize(payload, JsonOptions);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var nonce = Guid.NewGuid().ToString("n");
        var signature = Sign(timestamp, nonce, body, _options.PosSecret);

        using var request = new HttpRequestMessage(HttpMethod.Post, _options.BaseUrl.TrimEnd('/') + "/" + path)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-RFI-Shop", _options.ShopDomain);
        request.Headers.Add("X-RFI-Timestamp", timestamp);
        request.Headers.Add("X-RFI-Nonce", nonce);
        request.Headers.Add("X-RFI-Signature", signature);
        request.Headers.Add("Accept", "application/json");

        var response = await _http.SendAsync(request, ct);
        var raw = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Roll for It {Path} returned {Status}: {Body}", path, (int) response.StatusCode, raw);
            // Return the server's error body so controllers can surface its message to the till.
            throw new RollForItRemoteException((int) response.StatusCode, raw);
        }

        return raw;
    }

    private static string Sign(string timestamp, string nonce, string body, string secret)
    {
        var payload = Encoding.UTF8.GetBytes($"{timestamp}.{nonce}.{body}");
        using var mac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(mac.ComputeHash(payload)).ToLowerInvariant();
    }

    private sealed record HeldWin(long RollId, long Payout, DateTimeOffset HeldAt);
}

public class RollForItRemoteException : Exception
{
    public int Status { get; }
    public string Body { get; }

    public RollForItRemoteException(int status, string body)
        : base($"Roll for It responded {status}: {body}")
    {
        Status = status;
        Body = body;
    }
}

public record RollForItStatusDto(
    bool Open,
    string? Reason,
    string Currency,
    string CurrencySymbol,
    long MinOrder,
    string MinOrderFormatted,
    long MaxPayout,
    string MaxPayoutFormatted,
    string Odds,
    string Disclaimer,
    string Headline,
    RollForItNextDto? NextRoll,
    RollForItAnimationDto Animation);

public record RollForItNextDto(string StartsAt, long StartsIn, string Label);

public record RollForItAnimationDto(string DieColor, string NumberColor, string AccentColor, int DurationMs);

public class RollForItRollRequest
{
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public int PickedNumber { get; set; }
    public long SubtotalCents { get; set; }
    public string? OperatorId { get; set; }
    public string? CartToken { get; set; }
    public string? Currency { get; set; }
}

public record RollForItRollResult(
    bool Repeat,
    bool Test,
    bool Won,
    int DisplayedNumber,
    int PickedNumber,
    long Payout,
    string PayoutFormatted,
    string? Code,
    bool CodeReady,
    string? EndsAt,
    string Message,
    long RollId,
    string Channel);
