using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using Studeo.Api.Contracts;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace Studeo.Api.Services;

/// <summary>
/// The text of a Word document, split into pages. A .docx has no fixed pages (Word lays them out when it opens
/// the file), but Word records where its pages last broke («lastRenderedPageBreak»): those are used when present.
/// Otherwise (files saved by other programs) the text is split into parts of about <see cref="WordsPerPage"/>
/// words, at paragraph ends. Manual page breaks always start a new page. Paragraphs set in a heading style
/// (Heading 1–3) are passed on as headings; headers, footers, footnotes and the table of contents are left out.
/// </summary>
public partial class DocxTextExtractor(ILogger<DocxTextExtractor> logger)
{
    /// <summary>About a printed page of text.</summary>
    public const int WordsPerPage = 500;

    /// <summary>Headings deeper than this (Heading 4…) are too small to be sections.</summary>
    private const int MaxHeadingLevel = 3;

    public TextExtractionResult Extract(byte[] content, int maxPages)
    {
        if (!OfficeFiles.IsZip(content))
        {
            return TextExtractionResult.Fail(ErrorCodes.InvalidFile);
        }

        try
        {
            using var stream = new MemoryStream(content, writable: false);
            using var document = WordprocessingDocument.Open(stream, false, OfficeFiles.Settings);
            if (document.MainDocumentPart?.Document?.Body is not { } body)
            {
                return TextExtractionResult.Fail(ErrorCodes.InvalidFile);
            }

            var styles = new StyleLevels(document.MainDocumentPart.StyleDefinitionsPart?.Styles);
            var pages = Paginate(Tokens(body, styles).ToList());
            if (pages.Count > maxPages)
            {
                return TextExtractionResult.Fail(ErrorCodes.TooManyPages);
            }

            return new TextExtractionResult(pages.Select(p => OfficeFiles.Clean(p.Select(l => l.Text))).ToList(), null)
            {
                Headings = Headings(pages),
            };
        }
        catch (Exception ex)
        {
            // The SDK throws various exception types for damaged, encrypted or mislabelled files.
            logger.LogWarning(ex, "Could not read DOCX");
            return TextExtractionResult.Fail(ErrorCodes.InvalidFile);
        }
    }

    internal abstract record Token;

    /// <summary>A paragraph (or the part of one between page breaks); Level 1–9 for headings.</summary>
    internal sealed record Line(string Text, int? Level) : Token;

    /// <summary>Rendered: where Word's own layout broke the page; otherwise a manual page break.</summary>
    internal sealed record PageBreak(bool Rendered) : Token;

    private static IEnumerable<Token> Tokens(W.Body body, StyleLevels styles)
    {
        foreach (var paragraph in body.Descendants<W.Paragraph>())
        {
            // A text box is stored twice (new and old Word format); read only the new one.
            if (paragraph.Ancestors<AlternateContentFallback>().Any())
            {
                continue;
            }

            var properties = paragraph.ParagraphProperties;
            var styleId = properties?.ParagraphStyleId?.Val?.Value;
            if (styles.IsTableOfContents(styleId))
            {
                continue; // «Κεφάλαιο 1 …… 3» lines would look like headings
            }

            var level = OutlineLevel(properties?.OutlineLevel) ?? styles.Level(styleId);
            if (properties?.PageBreakBefore is { } before && (before.Val?.Value ?? true))
            {
                yield return new PageBreak(Rendered: false);
            }

            var text = new StringBuilder();
            foreach (var element in paragraph.Descendants())
            {
                // Text of a paragraph nested in this one (a text box) is read with that paragraph.
                if (element is W.Paragraph || !ReferenceEquals(element.Ancestors<W.Paragraph>().First(), paragraph))
                {
                    continue;
                }

                switch (element)
                {
                    case W.Text t:
                        text.Append(t.Text);
                        break;
                    case W.TabChar:
                        text.Append(' ');
                        break;
                    case W.NoBreakHyphen:
                        text.Append('-');
                        break;
                    case W.CarriageReturn:
                        text.Append('\n');
                        break;
                    case W.Break br when br.Type?.Value == W.BreakValues.Page:
                    case W.LastRenderedPageBreak:
                        if (Take(text, level) is { } part)
                        {
                            yield return part;
                        }
                        yield return new PageBreak(Rendered: element is W.LastRenderedPageBreak);
                        break;
                    case W.Break br when br.Type?.Value == W.BreakValues.Column:
                        text.Append(' ');
                        break;
                    case W.Break:
                        text.Append('\n');
                        break;
                }
            }

            if (Take(text, level) is { } line)
            {
                yield return line;
            }
        }
    }

    private static Line? Take(StringBuilder text, int? level)
    {
        var value = text.ToString();
        text.Clear();
        return value.Trim().Length > 0 ? new Line(value, level) : null;
    }

    /// <summary>
    /// Pages from the page breaks. Without Word's own (rendered) breaks, a page also ends after about
    /// <see cref="WordsPerPage"/> words, and a heading starts a new page once the current one is half full.
    /// </summary>
    internal static List<List<Line>> Paginate(IReadOnlyList<Token> tokens)
    {
        var byWords = !tokens.Any(t => t is PageBreak { Rendered: true });
        var pages = new List<List<Line>>();
        var current = new List<Line>();
        var words = 0;

        void EndPage()
        {
            if (current.Count > 0)
            {
                pages.Add(current);
                current = [];
                words = 0;
            }
        }

        foreach (var token in tokens)
        {
            if (token is not Line line)
            {
                EndPage(); // an empty page (two breaks in a row) is not kept
                continue;
            }
            if (byWords && line.Level is not null && words >= WordsPerPage / 2)
            {
                EndPage();
            }
            current.Add(line);
            words += Sectioner.CountWords(line.Text);
            if (byWords && words >= WordsPerPage)
            {
                EndPage();
            }
        }
        EndPage();
        return pages;
    }

    /// <summary>
    /// The heading lines down to the first level used at least twice (one «Heading 1» title above «Heading 2»
    /// chapters gives the chapters). AtTop: no other text above it on its page.
    /// </summary>
    internal static List<StyledHeading> Headings(IReadOnlyList<List<Line>> pages)
    {
        var levels = pages.SelectMany(p => p)
            .Where(l => l.Level is >= 1 and <= MaxHeadingLevel)
            .Select(l => l.Level!.Value)
            .ToList();
        if (levels.Count == 0)
        {
            return [];
        }
        var deepest = Enumerable.Range(1, MaxHeadingLevel).FirstOrDefault(n => levels.Count(l => l == n) >= 2, MaxHeadingLevel);

        var headings = new List<StyledHeading>();
        for (var i = 0; i < pages.Count; i++)
        {
            var wordsAbove = 0;
            foreach (var line in pages[i])
            {
                if (line.Level <= deepest)
                {
                    headings.Add(new StyledHeading(i + 1, Title(line.Text), AtTop: wordsAbove < MinWordsAbove));
                }
                else
                {
                    wordsAbove += Sectioner.CountWords(line.Text);
                }
            }
        }
        return headings;
    }

    /// <summary>
    /// A heading with less text than this above it on its page still starts the page: the document's title
    /// («Βιολογία Α΄ Λυκείου») above the first chapter is not text of its own.
    /// </summary>
    private const int MinWordsAbove = 20;

    private static string Title(string text)
    {
        var title = SpacesRegex().Replace(text, " ").Trim();
        return title.Length <= PdfTextExtractor.MaxStyledHeadingLength
            ? title
            : title[..(PdfTextExtractor.MaxStyledHeadingLength - 1)].TrimEnd() + "…";
    }

    /// <summary>Word's outline level 0–8 is heading level 1–9; 9 is body text.</summary>
    private static int? OutlineLevel(W.OutlineLevel? outline) =>
        outline?.Val?.Value is { } value and >= 0 and < 9 ? value + 1 : null;

    /// <summary>"heading 2" (the built-in name, also in a Greek Word) or the style id "Heading2".</summary>
    private static int? HeadingNumber(string? name) =>
        name is not null && HeadingNameRegex().Match(name) is { Success: true } m ? int.Parse(m.Groups[1].Value) : null;

    /// <summary>Heading levels of the document's paragraph styles, following «based on» chains.</summary>
    private sealed class StyleLevels(W.Styles? styles)
    {
        private readonly Dictionary<string, W.Style> _byId = styles?.Elements<W.Style>()
            .Where(s => s.StyleId?.Value is not null)
            .GroupBy(s => s.StyleId!.Value!)
            .ToDictionary(g => g.Key, g => g.First()) ?? [];

        public int? Level(string? styleId)
        {
            if (styleId is not null && !_byId.ContainsKey(styleId))
            {
                return HeadingNumber(styleId);
            }
            for (var depth = 0; depth < 10 && styleId is not null && _byId.TryGetValue(styleId, out var style); depth++)
            {
                if ((OutlineLevel(style.StyleParagraphProperties?.OutlineLevel) ?? HeadingNumber(style.StyleName?.Val?.Value)) is { } level)
                {
                    return level;
                }
                styleId = style.BasedOn?.Val?.Value;
            }
            return null;
        }

        public bool IsTableOfContents(string? styleId)
        {
            if (styleId is null)
            {
                return false;
            }
            var name = _byId.TryGetValue(styleId, out var style) ? style.StyleName?.Val?.Value ?? styleId : styleId;
            return name.StartsWith("toc", StringComparison.OrdinalIgnoreCase) && name.Length > 3 && !char.IsLetter(name[3]);
        }
    }

    [GeneratedRegex(@"^heading\s*([1-9])$", RegexOptions.IgnoreCase)]
    private static partial Regex HeadingNameRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpacesRegex();
}
