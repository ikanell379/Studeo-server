namespace Studeo.Api.Services;

public interface IPdfTextExtractor
{
    /// <summary>Extracts the text of every page (index 0 = page 1) and the lines that look like headings.</summary>
    PdfExtractionResult Extract(byte[] pdf, int maxPages);
}

/// <summary>Either Pages (success) or ErrorCode (invalid_pdf / too_many_pages).</summary>
public record PdfExtractionResult(IReadOnlyList<string> Pages, string? ErrorCode)
{
    /// <summary>
    /// Lines set apart by their style (bold, at least as large as the page's body text), in page order:
    /// what a reader sees as headings even without words like «Κεφάλαιο».
    /// </summary>
    public IReadOnlyList<StyledHeading> Headings { get; init; } = [];

    public static PdfExtractionResult Fail(string code) => new([], code);
}

/// <summary>A styled heading on a page (1-based). AtTop: no body text above it on that page (it starts the page).</summary>
public record StyledHeading(int Page, string Title, bool AtTop);
