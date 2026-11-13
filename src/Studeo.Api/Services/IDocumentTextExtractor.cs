namespace Studeo.Api.Services;

public interface IDocumentTextExtractor
{
    /// <summary>
    /// Extracts the text page by page from a PDF, a Word document (.docx) or a presentation (.pptx, or .ppt of PowerPoint 97–2003), chosen by the
    /// file name's extension. A slide is a page; a Word document is split at its page breaks (or into page-sized
    /// parts when it has none). ErrorCode unsupported_file for any other extension.
    /// </summary>
    TextExtractionResult Extract(string fileName, byte[] content, int maxPages);
}
