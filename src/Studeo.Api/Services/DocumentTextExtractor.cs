using Studeo.Api.Contracts;

namespace Studeo.Api.Services;

public class DocumentTextExtractor(IPdfTextExtractor pdf, DocxTextExtractor docx, PptxTextExtractor pptx, PptTextExtractor ppt, TextFileExtractor text)
    : IDocumentTextExtractor
{
    public TextExtractionResult Extract(string fileName, byte[] content, int maxPages) =>
        DocumentKinds.FromFileName(fileName) switch
        {
            DocumentKind.Pdf => pdf.Extract(content, maxPages),
            DocumentKind.Docx => docx.Extract(content, maxPages),
            DocumentKind.Pptx => pptx.Extract(content, maxPages),
            DocumentKind.Ppt => ppt.Extract(content, maxPages),
            DocumentKind.Text or DocumentKind.Markdown => text.Extract(content, maxPages),
            _ => TextExtractionResult.Fail(ErrorCodes.UnsupportedFile),
        };
}
