using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using A = DocumentFormat.OpenXml.Drawing;
using M = DocumentFormat.OpenXml.Math;
using P = DocumentFormat.OpenXml.Presentation;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace Studeo.Tests;

/// <summary>Builds small .docx and .pptx files in memory for tests.</summary>
public static class TestOfficeFiles
{
    /// <summary>
    /// A paragraph of a Word document: Style "Heading1"… or null; PageBreak: a manual page break before it.
    /// Font: the font of its text; Math: an equation (OMML «m:oMath» or «m:oMathPara» XML) after the text.
    /// </summary>
    public record Para(
        string Text, string? Style = null, bool PageBreak = false, bool RenderedBreak = false, string? Font = null,
        string? Math = null);

    /// <summary>The OMML namespace, for equations written as XML in tests.</summary>
    public const string MathNs = "http://schemas.openxmlformats.org/officeDocument/2006/math";

    public static Para H1(string text, bool pageBreak = false) => new(text, "Heading1", pageBreak);

    public static Para H2(string text) => new(text, "Heading2");

    public static Para Text(string text, bool pageBreak = false, bool renderedBreak = false) =>
        new(text, null, pageBreak, renderedBreak);

    public static byte[] Docx(params Para[] paragraphs)
    {
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var main = document.AddMainDocumentPart();
            var styles = main.AddNewPart<StyleDefinitionsPart>();
            styles.Styles = new W.Styles(
                Style("Heading1", "heading 1"),
                Style("Heading2", "heading 2"),
                Style("TOC1", "toc 1"));

            var body = new W.Body();
            foreach (var p in paragraphs)
            {
                var run = new W.Run();
                if (p.PageBreak)
                {
                    run.Append(new W.Break { Type = W.BreakValues.Page });
                }
                if (p.RenderedBreak)
                {
                    run.Append(new W.LastRenderedPageBreak());
                }
                if (p.Font is not null)
                {
                    run.Append(new W.RunProperties(new W.RunFonts { Ascii = p.Font, HighAnsi = p.Font }));
                }
                run.Append(new W.Text(p.Text) { Space = SpaceProcessingModeValues.Preserve });

                var paragraph = new W.Paragraph();
                if (p.Style is not null)
                {
                    paragraph.Append(new W.ParagraphProperties(new W.ParagraphStyleId { Val = p.Style }));
                }
                paragraph.Append(run);
                if (p.Math is not null)
                {
                    paragraph.Append(p.Math.StartsWith("<m:oMathPara")
                        ? new M.Paragraph(p.Math)
                        : new M.OfficeMath(p.Math));
                }
                body.Append(paragraph);
            }
            main.Document = new W.Document(body);
        }
        return stream.ToArray();
    }

    private static W.Style Style(string id, string name) =>
        new(new W.StyleName { Val = name }) { Type = W.StyleValues.Paragraph, StyleId = id };

    /// <summary>A slide: its title (null = none), body lines and speaker notes.</summary>
    public record Slide(string? Title, string[] Body, string? Notes = null);

    public static byte[] Pptx(params Slide[] slides)
    {
        using var stream = new MemoryStream();
        using (var deck = PresentationDocument.Create(stream, PresentationDocumentType.Presentation))
        {
            var presentation = deck.AddPresentationPart();
            var ids = new P.SlideIdList();
            uint id = 256;
            foreach (var s in slides)
            {
                var part = presentation.AddNewPart<SlidePart>();
                var tree = new P.ShapeTree();
                if (s.Title is not null)
                {
                    tree.Append(Shape(P.PlaceholderValues.Title, [s.Title]));
                }
                tree.Append(Shape(P.PlaceholderValues.Body, s.Body));
                tree.Append(Shape(P.PlaceholderValues.SlideNumber, [$"{id - 255}"]));
                part.Slide = new P.Slide(new P.CommonSlideData(tree));

                if (s.Notes is not null)
                {
                    var notes = part.AddNewPart<NotesSlidePart>();
                    notes.NotesSlide = new P.NotesSlide(new P.CommonSlideData(new P.ShapeTree(
                        Shape(P.PlaceholderValues.SlideImage, []),
                        Shape(P.PlaceholderValues.Body, [s.Notes]))));
                }

                ids.Append(new P.SlideId { Id = id++, RelationshipId = presentation.GetIdOfPart(part) });
            }
            presentation.Presentation = new P.Presentation(ids);
        }
        return stream.ToArray();
    }

    private static P.Shape Shape(P.PlaceholderValues type, string[] lines)
    {
        var text = new P.TextBody(new A.BodyProperties());
        foreach (var line in lines)
        {
            // A line starting with «<a:p» is a whole paragraph as XML (a code font, an equation).
            text.Append(line.StartsWith("<a:p") ? new A.Paragraph(line) : new A.Paragraph(new A.Run(new A.Text(line))));
        }
        return new P.Shape(
            new P.NonVisualShapeProperties(
                new P.NonVisualDrawingProperties { Id = 1, Name = type.ToString() },
                new P.NonVisualShapeDrawingProperties(),
                new P.ApplicationNonVisualDrawingProperties(new P.PlaceholderShape { Type = type })),
            new P.ShapeProperties(),
            text);
    }
}
