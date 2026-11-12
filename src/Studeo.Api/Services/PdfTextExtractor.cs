using Studeo.Api.Contracts;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace Studeo.Api.Services;

public class PdfTextExtractor(ILogger<PdfTextExtractor> logger) : IPdfTextExtractor
{
    public PdfExtractionResult Extract(byte[] pdf, int maxPages)
    {
        if (!HasPdfHeader(pdf))
        {
            return PdfExtractionResult.Fail(ErrorCodes.InvalidPdf);
        }

        try
        {
            using var document = PdfDocument.Open(pdf);

            if (document.NumberOfPages > maxPages)
            {
                return PdfExtractionResult.Fail(ErrorCodes.TooManyPages);
            }

            var pages = new List<string>(document.NumberOfPages);
            foreach (var page in document.GetPages())
            {
                pages.Add(Clean(ContentOrderTextExtractor.GetText(page)));
            }

            return new PdfExtractionResult(pages, null);
        }
        catch (Exception ex)
        {
            // PdfPig throws various exception types for corrupt or encrypted files.
            logger.LogWarning(ex, "Could not read PDF");
            return PdfExtractionResult.Fail(ErrorCodes.InvalidPdf);
        }
    }

    private static bool HasPdfHeader(byte[] pdf)
    {
        // "%PDF-" may be preceded by a few junk bytes; the spec allows it within the first 1024 bytes.
        var limit = Math.Min(pdf.Length, 1024) - 5;
        for (var i = 0; i <= limit; i++)
        {
            if (pdf[i] == '%' && pdf[i + 1] == 'P' && pdf[i + 2] == 'D' && pdf[i + 3] == 'F' && pdf[i + 4] == '-')
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>PostgreSQL text cannot hold NUL characters; also trims trailing whitespace.</summary>
    private static string Clean(string text) => text.Replace("\0", string.Empty).Trim();
}
