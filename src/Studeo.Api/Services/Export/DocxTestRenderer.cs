using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Studeo.Api.Services.Export;

/// <summary>Writes a <see cref="TestSheet"/> as a Word document (.docx) the teacher can edit further.</summary>
public static class DocxTestRenderer
{
    private const string Font = "Calibri";
    private const string Muted = "6B7280";
    private const string LineColor = "9CA3AF";

    public static byte[] Render(TestSheet sheet)
    {
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            AddStyles(main);
            var body = new Body();
            main.Document = new Document(body);

            body.Append(Para([Run(sheet.Title, bold: true, size: 36)], after: 60));
            if (sheet.Subtitle.Length > 0)
            {
                body.Append(Para([Run(sheet.Subtitle, size: 24, color: Muted)], after: 120));
            }
            if (!sheet.IsAnswerKey)
            {
                body.Append(Para(
                    [Run("Όνομα: ____________________________   Τμήμα: __________   Ημερομηνία: __________")],
                    before: 120, after: 240));
            }

            foreach (var part in sheet.Parts)
            {
                body.Append(Heading(part.Heading));
                if (part.Instructions.Length > 0)
                {
                    body.Append(Para([Run(part.Instructions, italic: true, color: Muted)], after: 120));
                }
                foreach (var q in part.Questions)
                {
                    AppendQuestion(body, q);
                }
            }

            body.Append(new SectionProperties(
                new PageSize { Width = 11906, Height = 16838 }, // A4
                new PageMargin { Top = 1134, Bottom = 1134, Left = 1134, Right = 1134, Header = 708, Footer = 708, Gutter = 0 }));
            main.Document.Save();
        }
        return stream.ToArray();
    }

    private static void AppendQuestion(Body body, TestQuestion q)
    {
        var runs = new List<OpenXmlElement> { Run($"{q.Number}. ", bold: true), Run(q.Text) };
        if (q.Page is { } page) runs.Add(Run($"  (σελ. {page})", size: 18, color: Muted));
        if (q.Mark is { } mark)
        {
            runs.Add(new Run(new TabChar()));
            runs.Add(Run(mark, bold: true));
        }
        var question = Para(runs, before: 120, after: 60, keepNext: true);
        if (q.Mark is not null)
        {
            // The Σ / Λ mark at the right margin.
            question.ParagraphProperties!.Append(new Tabs(new TabStop { Val = TabStopValues.Right, Position = 9638 }));
        }
        body.Append(question);

        foreach (var option in q.Options)
        {
            body.Append(Para([Run(option)], indent: 567, after: 20));
        }

        if (q.Matching is { } m)
        {
            body.Append(MatchingTable(m));
        }

        for (var i = 0; i < q.AnswerLines; i++)
        {
            body.Append(WritingLine());
        }

        if (q.Answer is { } answer)
        {
            body.Append(Para([Run("Απάντηση: ", bold: true), Run(answer)], indent: 284, after: 80));
        }
    }

    private static Table MatchingTable(TestMatching m)
    {
        static TableCell Cell(string text, bool bold = false) => new(
            new TableCellProperties(new TableCellWidth { Type = TableWidthUnitValues.Pct, Width = "2500" }),
            Para([Run(text, bold: bold)], after: 0));

        var border = new Func<BorderType, BorderType>(b =>
        {
            b.Val = BorderValues.Single;
            b.Size = 4;
            b.Color = LineColor;
            return b;
        });
        var table = new Table(new TableProperties(
            new TableWidth { Type = TableWidthUnitValues.Pct, Width = "5000" },
            new TableIndentation { Width = 284, Type = TableWidthUnitValues.Dxa },
            new TableBorders(
                border(new TopBorder()), border(new BottomBorder()), border(new LeftBorder()), border(new RightBorder()),
                border(new InsideHorizontalBorder()), border(new InsideVerticalBorder())),
            new TableCellMarginDefault(
                new TopMargin { Width = "60", Type = TableWidthUnitValues.Dxa },
                new BottomMargin { Width = "60", Type = TableWidthUnitValues.Dxa },
                new TableCellLeftMargin { Width = 100, Type = TableWidthValues.Dxa },
                new TableCellRightMargin { Width = 100, Type = TableWidthValues.Dxa })));
        table.Append(new TableRow(Cell("Στήλη Α", bold: true), Cell("Στήλη Β", bold: true)));
        for (var i = 0; i < Math.Max(m.Left.Count, m.Right.Count); i++)
        {
            table.Append(new TableRow(
                Cell(i < m.Left.Count ? m.Left[i] : ""),
                Cell(i < m.Right.Count ? m.Right[i] : "")));
        }
        return table;
    }

    /// <summary>An empty paragraph with a bottom border: a line to write on.</summary>
    private static Paragraph WritingLine() => new(
        new ParagraphProperties(
            new ParagraphBorders(new BottomBorder { Val = BorderValues.Single, Size = 4, Color = LineColor }),
            new SpacingBetweenLines { Before = "0", After = "0", Line = "400", LineRule = LineSpacingRuleValues.Exact },
            new Indentation { Left = "284" }),
        new Run(new Text("")));

    private static Paragraph Heading(string text) => new(
        new ParagraphProperties(
            new KeepNext(),
            new ParagraphBorders(new BottomBorder { Val = BorderValues.Single, Size = 6, Color = LineColor }),
            new SpacingBetweenLines { Before = "360", After = "120" }),
        Run(text, bold: true, size: 26));

    private static Paragraph Para(
        IEnumerable<OpenXmlElement> runs, int before = 0, int after = 120, int indent = 0, bool keepNext = false)
    {
        var props = new ParagraphProperties();
        if (keepNext) props.Append(new KeepNext());
        props.Append(new SpacingBetweenLines { Before = before.ToString(), After = after.ToString() });
        if (indent > 0) props.Append(new Indentation { Left = indent.ToString() });
        var p = new Paragraph(props);
        p.Append(runs);
        return p;
    }

    private static Run Run(string text, bool bold = false, bool italic = false, int size = 0, string? color = null)
    {
        var props = new RunProperties();
        if (bold) props.Append(new Bold());
        if (italic) props.Append(new Italic());
        if (color is not null) props.Append(new Color { Val = color });
        if (size > 0) props.Append(new FontSize { Val = size.ToString() });
        return new Run(props, new Text(text) { Space = SpaceProcessingModeValues.Preserve });
    }

    /// <summary>Default font and size (11pt) for the whole document, with Greek as the language.</summary>
    private static void AddStyles(MainDocumentPart main)
    {
        var styles = main.AddNewPart<StyleDefinitionsPart>();
        styles.Styles = new Styles(new DocDefaults(
            new RunPropertiesDefault(new RunPropertiesBaseStyle(
                new RunFonts { Ascii = Font, HighAnsi = Font, ComplexScript = Font, EastAsia = Font },
                new FontSize { Val = "22" },
                new Languages { Val = "el-GR" })),
            new ParagraphPropertiesDefault(new ParagraphPropertiesBaseStyle(
                new SpacingBetweenLines { After = "120", Line = "276", LineRule = LineSpacingRuleValues.Auto }))));
        styles.Styles.Save();
    }
}
