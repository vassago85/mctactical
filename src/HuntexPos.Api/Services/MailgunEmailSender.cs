using System.Net.Http.Headers;
using System.Text;
using HuntexPos.Api.Options;
using Microsoft.Extensions.Options;

namespace HuntexPos.Api.Services;

public class MailgunEmailSender : IEmailSender
{
    private readonly MailgunOptions _opt;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<MailgunEmailSender> _logger;

    public MailgunEmailSender(IOptions<MailgunOptions> opt, IHttpClientFactory httpClientFactory, ILogger<MailgunEmailSender> logger)
    {
        _opt = opt.Value;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task SendInvoiceEmailAsync(string toEmail, string subject, string htmlBody, byte[]? pdfAttachment, string? attachmentFileName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_opt.ApiKey) || string.IsNullOrWhiteSpace(_opt.Domain))
        {
            _logger.LogWarning("Mailgun not configured; skipping email to {Email}", toEmail);
            return;
        }

        var client = _httpClientFactory.CreateClient();
        var url = $"{_opt.BaseUrl.TrimEnd('/')}/{_opt.Domain}/messages";
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(_opt.From), "from");
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
            Convert.ToBase64String(Encoding.ASCII.GetBytes($"api:{_opt.ApiKey}")));
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
