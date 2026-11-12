namespace Studeo.Api.Services;

public interface IPdfTextExtractor
{
    /// <summary>Extracts the text of every page (index 0 = page 1).</summary>
    PdfExtractionResult Extract(byte[] pdf, int maxPages);
}

/// <summary>Either Pages (success) or ErrorCode (invalid_pdf / too_many_pages).</summary>
public record PdfExtractionResult(IReadOnlyList<string> Pages, string? ErrorCode)
{
    public static PdfExtractionResult Fail(string code) => new([], code);
}
