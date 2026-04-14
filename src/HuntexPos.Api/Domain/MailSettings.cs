namespace HuntexPos.Api.Domain;

public class MailSettings
{
    public Guid Id { get; set; } = Guid.Parse("00000000-0000-0000-0000-000000000002");
    public string ApiKey { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    /// <summary>Mailgun "from" header (e.g. MC Tactical &lt;sales@domain.com&gt;).</summary>
    public string SenderFrom { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api.mailgun.net/v3";
    public bool AttachPdf { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
