namespace HuntexPos.Api.Options;

public class MailgunOptions
{
    public const string SectionName = "Mailgun";
    public string ApiKey { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public string From { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api.mailgun.net/v3";
    public bool AttachPdf { get; set; }
}
