using System.Text;
using Studeo.Api.Contracts;
using Studeo.Api.Services;
using Studeo.Api.Validators;

namespace Studeo.Tests.Unit;

public class TextFileExtractorTests
{
    private readonly TextFileExtractor _extractor = new();

    private static string Words(int count, string word = "λέξη") => string.Join(' ', Enumerable.Repeat(word, count));

    [Fact]
    public void Short_text_is_one_page_with_trimmed_lines_and_no_empty_ones()
    {
        var result = _extractor.Extract(Encoding.UTF8.GetBytes("  Το κύτταρο\r\n\r\n\tΗ βασική μονάδα της ζωής.  \n"), 50);

        Assert.Null(result.ErrorCode);
        Assert.Equal(["Το κύτταρο\nΗ βασική μονάδα της ζωής."], result.Pages);
        Assert.Empty(result.Headings);
    }

    [Fact]
    public void Long_text_is_split_into_pages_of_about_500_words_at_line_ends()
    {
        var text = string.Join('\n', Enumerable.Repeat(Words(100), 12));

        var result = _extractor.ExtractText(text, 50);

        Assert.Equal([500, 500, 200], result.Pages.Select(Sectioner.CountWords));
    }

    [Fact]
    public void Form_feed_starts_a_new_page()
    {
        var result = _extractor.ExtractText("Σελίδα ένα\fΣελίδα δύο", 50);

        Assert.Equal(["Σελίδα ένα", "Σελίδα δύο"], result.Pages);
    }

    [Fact]
    public void Too_many_pages_fail()
    {
        var text = string.Join('\f', Enumerable.Range(1, 51).Select(i => $"Σελίδα {i}"));

        Assert.Equal(ErrorCodes.TooManyPages, _extractor.ExtractText(text, 50).ErrorCode);
    }

    [Fact]
    public void Markdown_headings_are_sections_without_their_hashes_also_in_plain_text()
    {
        var text = $"# Βιολογία\n## Το κύτταρο ##\n{Words(300)}\n```\n# όχι τίτλος\n```\n## Η φωτοσύνθεση\n{Words(300)}";

        var result = _extractor.ExtractText(text, 50);

        Assert.Equal(2, result.Pages.Count);
        Assert.StartsWith("Βιολογία\nΤο κύτταρο\n", result.Pages[0]);
        Assert.Contains("# όχι τίτλος", result.Pages[0]);
        Assert.Equal(
            [new StyledHeading(1, "Βιολογία", true), new StyledHeading(1, "Το κύτταρο", true), new StyledHeading(2, "Η φωτοσύνθεση", true)],
            result.Headings);
    }

    [Fact]
    public void Utf16_and_greek_windows_1253_files_are_read()
    {
        const string text = "Η φωτοσύνθεση";
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        Assert.Equal(text, TextFileExtractor.Decode([.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(text)]));
        Assert.Equal(text, TextFileExtractor.Decode([.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(text)]));
        Assert.Equal(text, TextFileExtractor.Decode(Encoding.GetEncoding(1253).GetBytes(text)));
    }

    [Fact]
    public void Binary_file_is_invalid()
    {
        Assert.Equal(ErrorCodes.InvalidFile, _extractor.Extract(TestPdfs.BlankPages(1), 50).ErrorCode);
        Assert.Equal(ErrorCodes.InvalidFile, _extractor.Extract([0, 1, 2, 3, 0x50, 0x4B, 0, 7], 50).ErrorCode);
    }

    // ---- Typed text ----

    [Theory]
    [InlineData("Σημειώσεις", "κείμενο", "Σημειώσεις.txt")]
    [InlineData("  ", "\n\n## Το κύτταρο\nκείμενο", "Το κύτταρο.txt")]
    [InlineData(null, "Κεφ. 1: Τα κύτταρα / ιστοί?", "Κεφ. 1 Τα κύτταρα ιστοί.txt")]
    [InlineData("notes.TXT", "κείμενο", "notes.TXT")]
    [InlineData(null, "???", "Κείμενο.txt")]
    public void Typed_text_is_named_after_its_title_or_first_line(string? title, string text, string expected)
    {
        Assert.Equal(expected, DocumentService.TextFileName(title, text));
    }

    [Fact]
    public void Long_first_line_is_shortened()
    {
        var name = DocumentService.TextFileName(null, Words(100));

        Assert.Equal(TextDocumentLimits.MaxTitleLength + ".txt".Length, name.Length);
        Assert.EndsWith("….txt", name);
    }

    [Theory]
    [InlineData(null, ErrorCodes.TextMissing)]
    [InlineData(" \n ", ErrorCodes.TextMissing)]
    public void Typed_text_must_not_be_empty(string? text, string code)
    {
        var result = new CreateTextDocumentRequestValidator().Validate(new CreateTextDocumentRequest(null, text, null));

        Assert.Equal(code, result.Errors.Single().ErrorCode);
    }

    [Fact]
    public void Typed_text_and_title_have_limits()
    {
        var validator = new CreateTextDocumentRequestValidator();

        Assert.True(validator.Validate(new CreateTextDocumentRequest("Τίτλος", "κείμενο", null)).IsValid);
        Assert.Equal(ErrorCodes.TextTooLong, validator.Validate(
            new CreateTextDocumentRequest(null, new string('α', TextDocumentLimits.MaxTextLength + 1), null)).Errors.Single().ErrorCode);
        Assert.Equal(ErrorCodes.InvalidRequest, validator.Validate(
            new CreateTextDocumentRequest(new string('α', TextDocumentLimits.MaxTitleLength + 1), "κείμενο", null)).Errors.Single().ErrorCode);
    }
}
