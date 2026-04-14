using System.Net.Http.Headers;
using System.Text;
namespace HuntexPos.Api.Services;

public class MailgunEmailSender : IEmailSender
{
    private readonly IEffectiveMailgunProvider _mail;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<MailgunEmailSender> _logger;

    public MailgunEmailSender(IEffectiveMailgunProvider mail, IHttpClientFactory httpClientFactory, ILogger<MailgunEmailSender> logger)
    {
        _mail = mail;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task SendInvoiceEmailAsync(string toEmail, string subject, string htmlBody, byte[]? pdfAttachment, string? attachmentFileName, CancellationToken ct = default)
    {
        var opt = await _mail.GetAsync(ct);
        if (string.IsNullOrWhiteSpace(opt.ApiKey) || string.IsNullOrWhiteSpace(opt.Domain))
        {
            _logger.LogWarning("Mailgun not configured; skipping email to {Email}", toEmail);
            return;
        }

        var client = _httpClientFactory.CreateClient();
        var url = $"{opt.BaseUrl.TrimEnd('/')}/{opt.Domain}/messages";
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(opt.From), "from");
        content.Add(new StringContent(toEmail), "to");
        content.Add(new StringContent(subject), "subject");
        content.Add(new StringContent(htmlBody, Encoding.UTF8, "text/html"), "html");

        if (pdfAttachment is { Length: > 0 } && attachmentFileName is not null)
        {
            var pdfContent = new ByteArrayContent(pdfAttachment);
            pdfContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            content.Add(pdfContent, "attachment", attachmentFileName);
        }

        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes($"api:{opt.ApiKey}")));
        req.Content = content;

        var resp = await client.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            _logger.LogError("Mailgun failed {Status}: {Body}", resp.StatusCode, body);
            throw new InvalidOperationException($"Mailgun error: {resp.StatusCode}");
        }
    }
}
