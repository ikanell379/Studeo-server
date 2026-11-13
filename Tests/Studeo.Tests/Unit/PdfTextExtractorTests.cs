using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Studeo.Api.Contracts;
using Studeo.Api.Services;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace Studeo.Tests.Unit;

public class PdfTextExtractorTests
{
    private readonly PdfTextExtractor _extractor = new(NullLogger<PdfTextExtractor>.Instance);

    [Fact]
    public void Extracts_greek_text_per_page()
    {
        var result = _extractor.Extract(TestPdfs.GreekSample(), DocumentLimits.MaxPages);

        Assert.Null(result.ErrorCode);
        Assert.Equal(4, result.Pages.Count);
        Assert.Contains("Κεφάλαιο 1: Το κύτταρο", result.Pages[0]);
        Assert.Contains("μιτοχόνδρια", result.Pages[1]);
        Assert.Contains("Κεφάλαιο 2: Η φωτοσύνθεση", result.Pages[2]);
        Assert.Contains("κύκλο του Calvin", result.Pages[3]);
    }

    [Fact]
    public void Keeps_blank_pages_as_empty_text()
    {
        var result = _extractor.Extract(TestPdfs.Build(TestPdfs.LongSentence, "", TestPdfs.LongSentence), 50);

        Assert.Null(result.ErrorCode);
        Assert.Equal(3, result.Pages.Count);
        Assert.Equal(string.Empty, result.Pages[1]);
    }

    [Fact]
    public void Rejects_more_pages_than_the_limit()
    {
        var result = _extractor.Extract(TestPdfs.BlankPages(3), maxPages: 2);

        Assert.Equal(ErrorCodes.TooManyPages, result.ErrorCode);
    }

    [Fact]
    public void Accepts_exactly_the_page_limit()
    {
        var result = _extractor.Extract(TestPdfs.BlankPages(2), maxPages: 2);

        Assert.Null(result.ErrorCode);
    }

    [Fact]
    public void Rejects_non_pdf_bytes()
    {
        var result = _extractor.Extract(Encoding.UTF8.GetBytes("just some text"), 50);

        Assert.Equal(ErrorCodes.InvalidFile, result.ErrorCode);
    }

    [Fact]
    public void Rejects_corrupt_pdf()
    {
        var result = _extractor.Extract(Encoding.ASCII.GetBytes("%PDF-1.7\n garbage garbage"), 50);

        Assert.Equal(ErrorCodes.InvalidFile, result.ErrorCode);
    }

    /// <summary>
    /// A page like a scanned book: body text 10.5pt, a bold 12.5pt heading over two lines with a small bold
    /// footnote number after it, then right below a new bold numbered subheading, and a bold word that is too small.
    /// </summary>
    private static byte[] StyledPdf()
    {
        var builder = new PdfDocumentBuilder();
        var regular = builder.AddStandard14Font(Standard14Font.Helvetica);
        var bold = builder.AddStandard14Font(Standard14Font.HelveticaBold);
        var page = builder.AddPage(595, 842);
        page.AddText("25 Karl Marx 281", 10.5, new PdfPoint(60, 800), regular);
        page.AddText(TestPdfs.LongSentence, 10.5, new PdfPoint(60, 770), regular);
        page.AddText(TestPdfs.LongSentence, 10.5, new PdfPoint(60, 755), regular);
        page.AddText("25.2. The material forces and the relations of", 12.5, new PdfPoint(60, 720), bold);
        page.AddText("production (Critique, 1859)", 12.5, new PdfPoint(60, 704), bold);
        page.AddText("2", 7, new PdfPoint(250, 709), bold);
        page.AddText("1. The secret of accumulation", 12.5, new PdfPoint(60, 688), bold);
        page.AddText(TestPdfs.LongSentence, 10.5, new PdfPoint(60, 660), regular);
        page.AddText(TestPdfs.LongSentence, 10.5, new PdfPoint(60, 645), regular);
        page.AddText("Note", 8, new PdfPoint(60, 620), bold);
        return builder.Build();
    }

    [Fact]
    public void Finds_bold_headings_joins_wrapped_lines_and_skips_footnote_numbers()
    {
        var result = _extractor.Extract(StyledPdf(), 50);

        Assert.Null(result.ErrorCode);
        Assert.Equal(
            [
                new StyledHeading(1, "25.2. The material forces and the relations of production (Critique, 1859)", AtTop: false),
                new StyledHeading(1, "1. The secret of accumulation", AtTop: false),
            ],
            result.Headings);
    }

    [Fact]
    public void A_pdf_without_bold_text_has_no_styled_headings()
    {
        var result = _extractor.Extract(TestPdfs.Build(TestPdfs.LongSentence, TestPdfs.LongSentence), 50);

        Assert.Empty(result.Headings);
    }
}
