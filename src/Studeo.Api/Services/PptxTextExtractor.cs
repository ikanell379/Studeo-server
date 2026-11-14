using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using Studeo.Api.Contracts;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;

namespace Studeo.Api.Services;

/// <summary>
/// The text of a PowerPoint presentation, one page per slide: the slide's title on the first line (so the
/// sectioner sees it as a slide deck), then its other text and tables, then the speaker notes. Equations become
/// LaTeX between $ and lines in a monospace font are program code (kept with their indentation, in ``` fences).
/// </summary>
public class PptxTextExtractor(ILogger<PptxTextExtractor> logger)
{
    public TextExtractionResult Extract(byte[] content, int maxPages)
    {
        if (!OfficeFiles.IsZip(content))
        {
            return TextExtractionResult.Fail(ErrorCodes.InvalidFile);
        }

        try
        {
            using var stream = new MemoryStream(content, writable: false);
            using var deck = PresentationDocument.Open(stream, false, OfficeFiles.Settings);
            if (deck.PresentationPart is not { } presentation)
            {
                return TextExtractionResult.Fail(ErrorCodes.InvalidFile);
            }

            var slideIds = presentation.Presentation?.SlideIdList?.Elements<P.SlideId>().ToList() ?? [];
            if (slideIds.Count > maxPages)
            {
                return TextExtractionResult.Fail(ErrorCodes.TooManyPages);
            }

            var pages = new List<string>(slideIds.Count);
            var headings = new List<StyledHeading>();
            foreach (var slideId in slideIds)
            {
                var slide = (SlidePart)presentation.GetPartById(slideId.RelationshipId!.Value!);
                var (title, body) = SlideText(slide.Slide?.CommonSlideData?.ShapeTree);
                var notes = NotesText(slide.NotesSlidePart?.NotesSlide?.CommonSlideData?.ShapeTree);

                pages.Add(OfficeFiles.Clean([new TextLine(title ?? ""), .. body, .. notes.Select(n => new TextLine(n))]));
                if (title is not null)
                {
                    headings.Add(new StyledHeading(pages.Count, Truncate(title), AtTop: true));
                }
            }

            return new TextExtractionResult(pages, null) { Headings = headings };
        }
        catch (Exception ex)
        {
            // The SDK throws various exception types for damaged, encrypted or mislabelled files.
            logger.LogWarning(ex, "Could not read PPTX");
            return TextExtractionResult.Fail(ErrorCodes.InvalidFile);
        }
    }

    /// <summary>The title (its lines joined) and the slide's other lines; footer, date and slide number are left out.</summary>
    private static (string? Title, List<TextLine> Body) SlideText(P.ShapeTree? tree)
    {
        string? title = null;
        var body = new List<TextLine>();
        if (tree is null)
        {
            return (title, body);
        }

        foreach (var element in tree.Descendants())
        {
            if (element.Ancestors<AlternateContentFallback>().Any())
            {
                continue; // the same content again, for older PowerPoint versions
            }

            switch (element)
            {
                case P.Shape { TextBody: { } text } shape when !IsRunningPlaceholder(PlaceholderType(shape)):
                    var paragraphs = text.Elements<A.Paragraph>().Where(p => ParagraphText(p).Trim().Length > 0).ToList();
                    var lines = paragraphs.Select(ParagraphText).ToList();
                    if (title is null && IsTitle(PlaceholderType(shape)) && lines.Count > 0)
                    {
                        title = string.Join(' ', lines.Select(l => l.Replace('\n', ' ').Trim()));
                    }
                    else
                    {
                        body.AddRange(paragraphs.Select(p => new TextLine(ParagraphText(p), IsCode(p))));
                    }
                    break;
                case A.Table table:
                    foreach (var row in table.Elements<A.TableRow>())
                    {
                        var cells = row.Elements<A.TableCell>()
                            .Select(c => string.Join(' ', c.TextBody?.Elements<A.Paragraph>().Select(ParagraphText) ?? []).Trim())
                            .Where(c => c.Length > 0);
                        body.Add(new TextLine(string.Join(" | ", cells)));
                    }
                    break;
            }
        }

        return (title, body);
    }

    /// <summary>The notes the speaker wrote under the slide (the body placeholder of its notes page).</summary>
    private static IEnumerable<string> NotesText(P.ShapeTree? tree) =>
        tree?.Descendants<P.Shape>()
            .Where(s => PlaceholderType(s) == P.PlaceholderValues.Body && s.TextBody is not null)
            .SelectMany(s => s.TextBody!.Elements<A.Paragraph>().Select(ParagraphText))
        ?? [];

    private static string ParagraphText(A.Paragraph paragraph)
    {
        var text = new StringBuilder();
        foreach (var child in paragraph.ChildElements)
        {
            switch (child)
            {
                case A.Run run:
                    text.Append(run.Text?.Text);
                    break;
                case A.Field field:
                    text.Append(field.Text?.Text);
                    break;
                case A.Break:
                    text.Append('\n');
                    break;
                default:
                    // An equation: «a14:m» (in an AlternateContent for older PowerPoint versions).
                    if (child.LocalName is "m" or "AlternateContent" && OmmlLatex.FindMath(child) is { } math
                        && OmmlLatex.Convert(math) is { Length: > 0 } latex)
                    {
                        text.Append($"${latex}$");
                    }
                    break;
            }
        }
        return text.ToString();
    }

    /// <summary>A line of program code: all its text in a monospace font (Consolas, Courier…).</summary>
    private static bool IsCode(A.Paragraph paragraph)
    {
        var runs = paragraph.Elements<A.Run>().Where(r => (r.Text?.Text ?? "").Trim().Length > 0).ToList();
        return runs.Count > 0 && runs.All(r => OfficeFiles.IsMonospaceFont(r.RunProperties?.GetFirstChild<A.LatinFont>()?.Typeface?.Value));
    }

    private static P.PlaceholderValues? PlaceholderType(P.Shape shape)
    {
        var placeholder = shape.NonVisualShapeProperties?.ApplicationNonVisualDrawingProperties?.PlaceholderShape;
        return placeholder is null ? null : placeholder.Type?.Value ?? P.PlaceholderValues.Object;
    }

    private static bool IsTitle(P.PlaceholderValues? type) =>
        type == P.PlaceholderValues.Title || type == P.PlaceholderValues.CenteredTitle;

    private static bool IsRunningPlaceholder(P.PlaceholderValues? type) =>
        type == P.PlaceholderValues.Footer || type == P.PlaceholderValues.DateAndTime
        || type == P.PlaceholderValues.SlideNumber || type == P.PlaceholderValues.Header;

    internal static string Truncate(string title)
    {
        title = string.Join(' ', title.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return title.Length <= PdfTextExtractor.MaxStyledHeadingLength
            ? title
            : title[..(PdfTextExtractor.MaxStyledHeadingLength - 1)].TrimEnd() + "…";
    }
}
