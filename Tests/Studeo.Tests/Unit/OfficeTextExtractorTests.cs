using Microsoft.Extensions.Logging.Abstractions;
using Studeo.Api.Contracts;
using Studeo.Api.Services;
using static Studeo.Tests.TestOfficeFiles;

namespace Studeo.Tests.Unit;

public class OfficeTextExtractorTests
{
    private readonly DocxTextExtractor _docx = new(NullLogger<DocxTextExtractor>.Instance);
    private readonly PptxTextExtractor _pptx = new(NullLogger<PptxTextExtractor>.Instance);

    private static string Words(int count, string word = "λέξη") => string.Join(' ', Enumerable.Repeat(word, count));

    // ---- PowerPoint ----

    [Fact]
    public void Pptx_gives_one_page_per_slide_with_the_title_first_and_the_notes_last()
    {
        var result = _pptx.Extract(Pptx(
            new Slide("Το κύτταρο", ["Η βασική μονάδα της ζωής.", "Έχει μεμβράνη."], Notes: "Θυμίστε τον ορισμό."),
            new Slide("Φωτοσύνθεση", ["Γίνεται στους χλωροπλάστες."])), DocumentLimits.MaxPages);

        Assert.Null(result.ErrorCode);
        Assert.Equal(
            ["Το κύτταρο\nΗ βασική μονάδα της ζωής.\nΈχει μεμβράνη.\nΘυμίστε τον ορισμό.", "Φωτοσύνθεση\nΓίνεται στους χλωροπλάστες."],
            result.Pages);
        Assert.Equal([new StyledHeading(1, "Το κύτταρο", true), new StyledHeading(2, "Φωτοσύνθεση", true)], result.Headings);
    }

    [Fact]
    public void Pptx_leaves_out_slide_numbers_and_handles_slides_without_title()
    {
        var result = _pptx.Extract(Pptx(new Slide(null, ["Μόνο κείμενο."])), DocumentLimits.MaxPages);

        Assert.Equal(["Μόνο κείμενο."], result.Pages);
        Assert.Empty(result.Headings);
    }

    [Fact]
    public void Pptx_with_too_many_slides_fails()
    {
        var slides = Enumerable.Range(1, 3).Select(i => new Slide($"Διαφάνεια {i}", ["κείμενο"])).ToArray();

        Assert.Equal(ErrorCodes.TooManyPages, _pptx.Extract(Pptx(slides), maxPages: 2).ErrorCode);
    }

    [Fact]
    public void Slide_titles_split_a_deck_into_sections()
    {
        var deck = Pptx(
            new Slide("Εισαγωγή", [Words(40)]),
            new Slide("Το κύτταρο", [Words(40)]),
            new Slide("Το κύτταρο", [Words(40)]),
            new Slide("Φωτοσύνθεση", [Words(40)]),
            new Slide("Φωτοσύνθεση", [Words(40)]));
        var result = _pptx.Extract(deck, DocumentLimits.MaxPages);

        var sections = new Sectioner().Split(result.Pages, result.Headings);

        Assert.Equal(["Εισαγωγή", "Το κύτταρο", "Φωτοσύνθεση"], sections.Select(s => s.Title));
        Assert.Equal((4, 5), (sections[2].StartPage, sections[2].EndPage));
    }

    // ---- Word ----

    [Fact]
    public void Docx_without_page_breaks_is_split_into_page_sized_parts()
    {
        var paragraphs = Enumerable.Range(0, 5).Select(_ => Text(Words(300))).ToArray();

        var result = _docx.Extract(Docx(paragraphs), DocumentLimits.MaxPages);

        Assert.Null(result.ErrorCode);
        // 300 + 300 words reach a page; 5 paragraphs → 3 pages (600, 600, 300 words).
        Assert.Equal([600, 600, 300], result.Pages.Select(Sectioner.CountWords));
    }

    [Fact]
    public void Docx_uses_words_rendered_page_breaks_and_manual_ones()
    {
        var result = _docx.Extract(Docx(
            Text("Πρώτη σελίδα."),
            Text("Δεύτερη σελίδα.", renderedBreak: true),
            Text("Τρίτη σελίδα.", pageBreak: true),
            Text(Words(900))), DocumentLimits.MaxPages);

        // With Word's own breaks the long paragraph stays on its page.
        Assert.Equal(["Πρώτη σελίδα.", "Δεύτερη σελίδα.", $"Τρίτη σελίδα.\n{Words(900)}"], result.Pages);
    }

    [Fact]
    public void Docx_headings_become_sections()
    {
        var result = _docx.Extract(Docx(
            H1("Βιολογία"),
            H2("Το κύτταρο"),
            Text(Words(400)),
            H2("Φωτοσύνθεση"),
            Text(Words(400))), DocumentLimits.MaxPages);

        // Heading 2 is the first level used twice; the one Heading 1 above it counts too.
        Assert.Equal(["Βιολογία", "Το κύτταρο", "Φωτοσύνθεση"], result.Headings.Select(h => h.Title));
        Assert.Equal(2, result.Pages.Count); // the second heading starts a new page once the first is half full
        Assert.True(result.Headings[2].AtTop);

        var sections = new Sectioner().Split(result.Pages, result.Headings);
        Assert.Contains(sections, s => s.Title == "Φωτοσύνθεση" && s.StartPage == 2);
    }

    [Fact]
    public void A_short_title_above_the_first_heading_does_not_make_an_extra_section()
    {
        var result = _docx.Extract(Docx(
            new Para("Βιολογία Α΄ Λυκείου", "Title"),
            H1("Το κύτταρο"),
            Text(Words(400)),
            H1("Φωτοσύνθεση"),
            Text(Words(400))), DocumentLimits.MaxPages);

        var sections = new Sectioner().Split(result.Pages, result.Headings);

        Assert.Equal([("Το κύτταρο", 1, 1), ("Φωτοσύνθεση", 2, 2)], sections.Select(s => (s.Title, s.StartPage, s.EndPage)));
    }

    [Fact]
    public void Docx_leaves_out_the_table_of_contents()
    {
        var result = _docx.Extract(Docx(new Para("Κεφάλαιο 1 Το κύτταρο 3", "TOC1"), Text("Κείμενο.")), DocumentLimits.MaxPages);

        Assert.Equal(["Κείμενο."], result.Pages);
    }

    [Fact]
    public void Docx_with_too_many_pages_fails()
    {
        var paragraphs = Enumerable.Range(0, 3).Select(i => Text($"Σελίδα {i}", pageBreak: i > 0)).ToArray();

        Assert.Equal(ErrorCodes.TooManyPages, _docx.Extract(Docx(paragraphs), maxPages: 2).ErrorCode);
    }

    // ---- Invalid files ----

    [Fact]
    public void Files_that_are_not_office_documents_are_invalid()
    {
        Assert.Equal(ErrorCodes.InvalidFile, _docx.Extract("not a docx"u8.ToArray(), 50).ErrorCode);
        Assert.Equal(ErrorCodes.InvalidFile, _pptx.Extract("not a pptx"u8.ToArray(), 50).ErrorCode);
        // A Word document renamed to .pptx (and the other way round).
        Assert.Equal(ErrorCodes.InvalidFile, _pptx.Extract(Docx(Text("Κείμενο.")), 50).ErrorCode);
        Assert.Equal(ErrorCodes.InvalidFile, _docx.Extract(Pptx(new Slide("Τίτλος", ["κείμενο"])), 50).ErrorCode);
    }

    [Theory]
    [InlineData("notes.PDF", null)]
    [InlineData("notes.docx", null)]
    [InlineData("slides.PPTX", null)]
    [InlineData("notes.txt", null)]
    [InlineData("notes.MD", null)]
    [InlineData("notes.rtf", ErrorCodes.UnsupportedFile)]
    public void The_extension_picks_the_extractor(string fileName, string? code)
    {
        var extractor = new DocumentTextExtractor(
            new PdfTextExtractor(NullLogger<PdfTextExtractor>.Instance), _docx, _pptx, new PptTextExtractor(NullLogger<PptTextExtractor>.Instance),
            new TextFileExtractor());
        var content = Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".pdf" => TestPdfs.Build(TestPdfs.LongSentence),
            ".docx" => Docx(Text(TestPdfs.LongSentence)),
            ".pptx" => Pptx(new Slide("Title", [TestPdfs.LongSentence])),
            ".txt" or ".md" => System.Text.Encoding.UTF8.GetBytes(TestPdfs.LongSentence),
            _ => "text"u8.ToArray(),
        };

        var result = extractor.Extract(fileName, content, DocumentLimits.MaxPages);

        Assert.Equal(code, result.ErrorCode);
        if (code is null)
        {
            Assert.Contains("Photosynthesis", result.Pages[0]);
        }
    }
}
