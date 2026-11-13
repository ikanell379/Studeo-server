using DocumentFormat.OpenXml.Packaging;

namespace Studeo.Api.Services;

/// <summary>What the .docx and .pptx extractors share.</summary>
internal static class OfficeFiles
{
    /// <summary>
    /// A part (e.g. the document body) larger than this many characters is refused: a 20 MB upload is compressed
    /// and could unpack to far more (a zip bomb). Real study material is far below it.
    /// </summary>
    public const long MaxCharactersInPart = 50_000_000;

    public static OpenSettings Settings => new() { MaxCharactersInPart = MaxCharactersInPart };

    /// <summary>.docx and .pptx are zip files ("PK\x03\x04"); a password-protected one is not, so it fails early.</summary>
    public static bool IsZip(byte[] content) =>
        content.Length >= 4 && content[0] == 'P' && content[1] == 'K' && content[2] == 3 && content[3] == 4;

    /// <summary>PostgreSQL text cannot hold NUL characters; also trims each line and drops empty ones.</summary>
    public static string Clean(IEnumerable<string> lines) =>
        string.Join('\n', lines.Select(l => l.Replace("\0", string.Empty).Trim()).Where(l => l.Length > 0));
}
