namespace HuntexPos.Api.Options;

public class AppOptions
{
    public const string SectionName = "App";
    public string PublicBaseUrl { get; set; } = "http://localhost:5173";
    public string PdfStoragePath { get; set; } = "storage/pdfs";
    public bool HideCostForSalesRole { get; set; } = true;
}
