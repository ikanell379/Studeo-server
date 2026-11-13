using System.Text;
using System.Text.RegularExpressions;
using Studeo.Api.Contracts;

namespace Studeo.Api.Services;

/// <summary>
/// The text of a plain-text (.txt) or Markdown (.md) file, or of text the user typed in, split into pages like a
/// Word document without page breaks: parts of about <see cref="DocxTextExtractor.WordsPerPage"/> words, at line
/// ends. A form feed starts a new page. Markdown headings («#», «##», «###» lines) are the sections, in .txt and
/// typed text too (outside ``` code blocks).
/// The file is read as UTF-8 (or UTF-16 with a BOM); a file that is not valid UTF-8 is read as Greek Windows-1253,
/// what Notepad saved before Windows 10.
/// </summary>
public partial class TextFileExtractor
{
    static TextFileExtractor() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public TextExtractionResult Extract(byte[] content, int maxPages)
    {
        if (Decode(content) is not { } text)
        {
            return TextExtractionResult.Fail(ErrorCodes.InvalidFile);
        }
        return ExtractText(text, maxPages);
    }

    public TextExtractionResult ExtractText(string text, int maxPages)
    {
        var pages = DocxTextExtractor.Paginate(Tokens(text).ToList());
        if (pages.Count > maxPages)
        {
            return TextExtractionResult.Fail(ErrorCodes.TooManyPages);
        }

        return new TextExtractionResult(pages.Select(p => OfficeFiles.Clean(p.Select(l => l.Text))).ToList(), null)
        {
            Headings = DocxTextExtractor.Headings(pages),
        };
    }

    /// <summary>The text, or null for a binary file (e.g. a PDF renamed to .txt).</summary>
    internal static string? Decode(byte[] content)
    {
        if (IsOtherDocument(content))
        {
            return null;
        }

        string text;
        if (content is [0xFF, 0xFE, ..])
        {
            text = Encoding.Unicode.GetString(content, 2, content.Length - 2);
        }
        else if (content is [0xFE, 0xFF, ..])
        {
            text = Encoding.BigEndianUnicode.GetString(content, 2, content.Length - 2);
        }
        else
        {
            var start = content is [0xEF, 0xBB, 0xBF, ..] ? 3 : 0;
            try
            {
                text = StrictUtf8.GetString(content, start, content.Length - start);
            }
            catch (DecoderFallbackException)
            {
                text = Encoding.GetEncoding(1253).GetString(content);
            }
        }

        return LooksBinary(text) ? null : text;
    }

    /// <summary>A PDF, an Office file (.docx/.pptx zip, or an old .doc/.ppt) renamed to .txt.</summary>
    private static bool IsOtherDocument(byte[] content) =>
        content is [(byte)'%', (byte)'P', (byte)'D', (byte)'F', ..]
        || OfficeFiles.IsZip(content)
        || content is [0xD0, 0xCF, 0x11, 0xE0, ..];

    /// <summary>Control characters other than tab, line ends and form feed mean it isn't text.</summary>
    private static bool LooksBinary(string text)
    {
        var control = text.Count(c => char.IsControl(c) && c is not ('\t' or '\n' or '\r' or '\f'));
        return control > 0 && control * 100 >= text.Length; // a stray one in a long text is tolerated
    }

    private static IEnumerable<DocxTextExtractor.Token> Tokens(string text)
    {
        var inCode = false;
        // ReplaceLineEndings also takes the form feed for a line end: split the pages first.
        var pages = text.Split('\f');
        for (var i = 0; i < pages.Length; i++)
        {
            if (i > 0)
            {
                yield return new DocxTextExtractor.PageBreak(Rendered: false);
            }

            foreach (var raw in pages[i].ReplaceLineEndings("\n").Split('\n'))
            {
                var line = raw;
                int? level = null;
                if (line.TrimStart().StartsWith("```"))
                {
                    inCode = !inCode; // a «# comment» in a code block is not a heading
                    continue;
                }
                if (!inCode && MarkdownHeadingRegex().Match(line) is { Success: true } m)
                {
                    level = m.Groups[1].Length;
                    line = m.Groups[2].Value.TrimEnd('#', ' ');
                }

                if (line.Trim().Length > 0)
                {
                    yield return new DocxTextExtractor.Line(line.Replace('\t', ' '), level);
                }
            }
        }
    }

    [GeneratedRegex(@"^ {0,3}(#{1,6})\s+(.+)$")]
    private static partial Regex MarkdownHeadingRegex();
}
