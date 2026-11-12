using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Studeo.Api.Contracts;
using Studeo.Api.Services;

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

        Assert.Equal(ErrorCodes.InvalidPdf, result.ErrorCode);
    }

    [Fact]
    public void Rejects_corrupt_pdf()
    {
        var result = _extractor.Extract(Encoding.ASCII.GetBytes("%PDF-1.7\n garbage garbage"), 50);

        Assert.Equal(ErrorCodes.InvalidPdf, result.ErrorCode);
    }
}
