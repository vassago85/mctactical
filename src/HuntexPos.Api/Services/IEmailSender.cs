namespace HuntexPos.Api.Services;

public interface IEmailSender
{
    Task SendInvoiceEmailAsync(string toEmail, string subject, string htmlBody, byte[]? pdfAttachment, string? attachmentFileName, CancellationToken ct = default);
}
