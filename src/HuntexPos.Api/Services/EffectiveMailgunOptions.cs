namespace HuntexPos.Api.Services;

public sealed class EffectiveMailgunOptions
{
    public string ApiKey { get; init; } = string.Empty;
    public string Domain { get; init; } = string.Empty;
    public string From { get; init; } = string.Empty;
    public string BaseUrl { get; init; } = "https://api.mailgun.net/v3";
    public bool AttachPdf { get; init; }
}
