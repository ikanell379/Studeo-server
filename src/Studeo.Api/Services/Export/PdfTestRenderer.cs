using QuestPDF.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Studeo.Api.Services.Export;

/// <summary>Draws a <see cref="TestSheet"/> as an A4 PDF with the embedded Noto Sans (Greek).</summary>
public static class PdfTestRenderer
{
    public const string FontFamily = "Noto Sans";
    private const string Muted = "#6B7280";
    private const string Line = "#9CA3AF";

    private static readonly Lazy<bool> Setup = new(() =>
    {
        QuestPDF.Settings.License = LicenseType.Community;
        // Only the embedded font: the same output on any server, whatever fonts it has.
        QuestPDF.Settings.UseSystemFonts = false;
        var assembly = typeof(PdfTestRenderer).Assembly;
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)))
        {
            using var font = assembly.GetManifestResourceStream(name)!;
            FontManager.RegisterFontFromStream(font);
        }
        return true;
    });

    public static byte[] Render(TestSheet sheet)
    {
        _ = Setup.Value;
        return Document.Create(doc => doc.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(2, Unit.Centimetre);
            page.DefaultTextStyle(t => t.FontFamily(FontFamily).FontSize(11).LineHeight(1.3f));

            page.Header().ShowOnce().PaddingBottom(12).Column(col =>
            {
                col.Item().Text(sheet.Title).FontSize(18).Bold();
                if (sheet.Subtitle.Length > 0)
                {
                    col.Item().Text(sheet.Subtitle).FontSize(12).FontColor(Muted);
                }
            });

            page.Content().Column(col =>
            {
                col.Spacing(14);
                if (!sheet.IsAnswerKey)
                {
                    col.Item().Row(row =>
                    {
                        row.Spacing(16);
                        row.RelativeItem(3).Text("Όνομα: ______________________________");
                        row.RelativeItem(2).Text("Τμήμα: ____________");
                        row.RelativeItem(2).Text("Ημερομηνία: ___________");
                    });
                }

                foreach (var part in sheet.Parts)
                {
                    col.Item().PaddingTop(6).Column(p =>
                    {
                        p.Spacing(8);
                        // The heading never stays alone at the bottom of a page: it goes with the first question.
                        p.Item().ShowEntire().Column(head =>
                        {
                            head.Spacing(8);
                            head.Item().BorderBottom(1).BorderColor(Line).PaddingBottom(3).Text(part.Heading).FontSize(13).Bold();
                            if (part.Instructions.Length > 0)
                            {
                                head.Item().Text(part.Instructions).Italic().FontColor(Muted);
                            }
                            head.Item().Element(e => Question(e, part.Questions[0]));
                        });
                        foreach (var q in part.Questions.Skip(1))
                        {
                            p.Item().ShowEntire().Element(e => Question(e, q));
                        }
                    });
                }
            });

            page.Footer().AlignCenter().Text(t =>
            {
                t.DefaultTextStyle(s => s.FontSize(9).FontColor(Muted));
                t.CurrentPageNumber();
                t.Span(" / ");
                t.TotalPages();
            });
        })).GeneratePdf();
    }

    private static void Question(IContainer container, TestQuestion q)
    {
        container.Column(col =>
        {
            col.Spacing(4);
            col.Item().Row(row =>
            {
                row.ConstantItem(22).Text($"{q.Number}.").Bold();
                row.RelativeItem().Text(t =>
                {
                    t.Span(q.Text);
                    if (q.Page is { } page) t.Span($"  (σελ. {page})").FontSize(9).FontColor(Muted);
                });
                if (q.Mark is { } mark) row.ConstantItem(60).AlignRight().Text(mark).Bold();
            });

            if (q.Options.Count > 0)
            {
                col.Item().PaddingLeft(34).Column(o =>
                {
                    o.Spacing(2);
                    foreach (var option in q.Options) o.Item().Text(option);
                });
            }

            if (q.Matching is { } m)
            {
                col.Item().PaddingLeft(22).PaddingTop(2).Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.RelativeColumn();
                        c.RelativeColumn();
                    });
                    table.Header(h =>
                    {
                        h.Cell().Element(Cell).Text("Στήλη Α").Bold();
                        h.Cell().Element(Cell).Text("Στήλη Β").Bold();
                    });
                    for (var i = 0; i < Math.Max(m.Left.Count, m.Right.Count); i++)
                    {
                        table.Cell().Element(Cell).Text(i < m.Left.Count ? m.Left[i] : "");
                        table.Cell().Element(Cell).Text(i < m.Right.Count ? m.Right[i] : "");
                    }
                });
            }

            for (var i = 0; i < q.AnswerLines; i++)
            {
                col.Item().PaddingLeft(22).Height(20).BorderBottom(0.5f).BorderColor(Line);
            }

            if (q.Answer is { } answer)
            {
                col.Item().PaddingLeft(22).Text(t =>
                {
                    t.Span("Απάντηση: ").Bold();
                    t.Span(answer);
                });
            }
        });
    }

    private static IContainer Cell(IContainer c) => c.Border(0.5f).BorderColor(Line).Padding(5);
}
