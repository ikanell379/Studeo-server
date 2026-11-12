using Microsoft.Extensions.Logging.Abstractions;
using Studeo.Api.Contracts;
using Studeo.Api.Services;

namespace Studeo.Tests.Unit;

public class SectionerTests
{
    private readonly Sectioner _sectioner = new();

    /// <summary>A page body of exactly n words.</summary>
    private static string Words(int n) => string.Join(' ', Enumerable.Repeat("λέξη", n));

    private static string Page(params string[] lines) => string.Join('\n', lines);

    [Fact]
    public void Splits_greek_sample_by_chapter_headings()
    {
        var pages = new PdfTextExtractor(NullLogger<PdfTextExtractor>.Instance)
            .Extract(TestPdfs.GreekSample(), DocumentLimits.MaxPages).Pages;

        var sections = _sectioner.Split(pages);

        Assert.Collection(sections,
            s => Assert.Equal(("Κεφάλαιο 1: Το κύτταρο", 1, 2), (s.Title, s.StartPage, s.EndPage)),
            s => Assert.Equal(("Κεφάλαιο 2: Η φωτοσύνθεση", 3, 4), (s.Title, s.StartPage, s.EndPage)));
        Assert.All(sections, s => Assert.True(s.WordCount > 50));
    }

    [Fact]
    public void Heading_in_the_middle_of_a_page_shares_that_page()
    {
        var pages = new[]
        {
            Page("Κεφάλαιο 1", Words(50)),
            Page(Words(30), "Κεφάλαιο 2", Words(30)),
            Page(Words(50)),
        };

        var sections = _sectioner.Split(pages);

        Assert.Equal([(1, 2), (2, 3)], sections.Select(s => (s.StartPage, s.EndPage)));
    }

    [Fact]
    public void Uppercase_and_roman_and_greek_numerals_are_headings()
    {
        Assert.True(Sectioner.IsKeywordHeading("ΚΕΦΑΛΑΙΟ 3"));
        Assert.True(Sectioner.IsKeywordHeading("ΕΝΟΤΗΤΑ Α΄ – Η αρχαία Αθήνα"));
        Assert.True(Sectioner.IsKeywordHeading("Chapter IV: Cells"));
        Assert.True(Sectioner.IsKeywordHeading("Μάθημα 12. Οι εξισώσεις"));
    }

    [Theory]
    [InlineData("Στο κεφάλαιο 2 είδαμε ότι το κύτταρο διαιρείται.")] // sentence, does not start with the keyword
    [InlineData("Κεφάλαιο 1: Το κύτταρο ........ 5")] // table of contents
    [InlineData("Κεφάλαιο 2: Η φωτοσύνθεση 12")] // table of contents without dots
    [InlineData("Κεφαλαιοποίηση της αγοράς")] // keyword is part of another word
    public void Non_headings_are_ignored(string line)
    {
        Assert.False(Sectioner.IsKeywordHeading(line));
    }

    [Fact]
    public void Table_of_contents_does_not_create_sections()
    {
        var pages = new[]
        {
            Page("Περιεχόμενα", "Κεφάλαιο 1: Αρχές", "Κεφάλαιο 2: Εφαρμογές"),
            Page("Κεφάλαιο 1: Αρχές", Words(200)),
            Page("Κεφάλαιο 2: Εφαρμογές", Words(200)),
        };

        var sections = _sectioner.Split(pages);

        Assert.Equal([("Κεφάλαιο 1: Αρχές", 2, 2), ("Κεφάλαιο 2: Εφαρμογές", 3, 3)],
            sections.Select(s => (s.Title, s.StartPage, s.EndPage)));
    }

    [Fact]
    public void Long_text_before_first_heading_becomes_introduction()
    {
        var pages = new[] { Page("Πρόλογος", Words(150)), Page("Κεφάλαιο 1", Words(100)) };

        var sections = _sectioner.Split(pages);

        Assert.Equal([("Εισαγωγή", 1, 1), ("Κεφάλαιο 1", 2, 2)], sections.Select(s => (s.Title, s.StartPage, s.EndPage)));
    }

    [Fact]
    public void Short_cover_page_before_first_heading_is_dropped()
    {
        var pages = new[] { Page("Βιολογία Β΄ Λυκείου"), Page("Κεφάλαιο 1", Words(100)) };

        var sections = _sectioner.Split(pages);

        Assert.Equal("Κεφάλαιο 1", Assert.Single(sections).Title);
    }

    [Fact]
    public void Numbered_headings_are_used_when_there_are_no_keyword_headings()
    {
        var pages = new[]
        {
            Page("1. Εισαγωγή στη χημεία", Words(100)),
            Page("2. Το περιοδικό σύστημα", Words(100)),
        };

        var sections = _sectioner.Split(pages);

        Assert.Equal(["1. Εισαγωγή στη χημεία", "2. Το περιοδικό σύστημα"], sections.Select(s => s.Title));
    }

    [Fact]
    public void Numbered_list_items_are_not_headings()
    {
        Assert.False(Sectioner.IsNumberedHeading("1. Η κυτταρική μεμβράνη ελέγχει τις ουσίες."));
        Assert.False(Sectioner.IsNumberedHeading("3 μήλα και 2 πορτοκάλια"));
    }

    [Fact]
    public void Without_headings_pages_are_chunked_by_about_3000_words()
    {
        var pages = Enumerable.Repeat(Words(1000), 9).ToArray();

        var sections = _sectioner.Split(pages);

        Assert.Equal([("Μέρος 1", 1, 3), ("Μέρος 2", 4, 6), ("Μέρος 3", 7, 9)],
            sections.Select(s => (s.Title, s.StartPage, s.EndPage)));
        Assert.All(sections, s => Assert.Equal(3000, s.WordCount));
    }

    [Fact]
    public void Small_last_chunk_is_merged_into_previous()
    {
        // 3000 + 3000 + 500 → the 500-word tail joins the second chunk
        var sections = _sectioner.Split([.. Enumerable.Repeat(Words(1000), 6), Words(500)]);

        Assert.Equal([(1, 3), (4, 7)], sections.Select(s => (s.StartPage, s.EndPage)));
    }

    [Fact]
    public void Big_enough_last_chunk_stays_separate()
    {
        // 3000 + 3000 + 1500 → three chunks
        var sections = _sectioner.Split([.. Enumerable.Repeat(Words(1000), 7), Words(500)]);

        Assert.Equal([(1, 3), (4, 6), (7, 8)], sections.Select(s => (s.StartPage, s.EndPage)));
    }

    [Fact]
    public void Short_document_without_headings_is_one_section()
    {
        var section = Assert.Single(_sectioner.Split([Words(300), Words(300)]));

        Assert.Equal(("Ολόκληρο το κείμενο", 1, 2, 600), (section.Title, section.StartPage, section.EndPage, section.WordCount));
    }

    [Fact]
    public void Very_large_chapter_is_split_into_parts()
    {
        var pages = new List<string> { Page("Κεφάλαιο 1: Ιστορία", Words(1000)) };
        pages.AddRange(Enumerable.Repeat(Words(1000), 5));

        var sections = _sectioner.Split(pages);

        Assert.Equal(["Κεφάλαιο 1: Ιστορία (μέρος 1)", "Κεφάλαιο 1: Ιστορία (μέρος 2)"], sections.Select(s => s.Title));
    }

    [Fact]
    public void Empty_pages_produce_no_empty_sections()
    {
        Assert.Empty(_sectioner.Split(["", "  "]));

        var sections = _sectioner.Split(["", "Κεφάλαιο 1\n" + Words(100)]);
        Assert.Equal(("Κεφάλαιο 1", 2, 2), Assert.Single(sections) is var s ? (s.Title, s.StartPage, s.EndPage) : default);
    }

    /// <summary>A slide: a short title line, then a few bullet points.</summary>
    private static string Slide(string title, int words = 50) => Page(title, "• " + Words(words));

    [Fact]
    public void Slides_are_split_by_their_titles_and_repeated_titles_are_joined()
    {
        string[] pages =
        [
            Page("Εισαγωγή στην", "Παιδαγωγική Επιστήμη"), // cover and contents: too few words,
            Slide("Περιεχόμενα", 20),                      // so they join the first topic
            Slide("Paulo Freire(1921 –1997)"),
            Slide("Paulo Freire(1921 –1997)"),
            Slide("Η «Τραπεζική» έννοια της εκπαίδευσης"),
            Slide("Διδακτική μεθοδολογία"),
            Slide("Διδακτική μεθοδολογία –Παραδείγματα θεμάτων συζήτησης"),
            Slide("Διδακτική μεθοδολογία"),
        ];

        var sections = _sectioner.Split(pages);

        Assert.Equal(
            [
                ("Paulo Freire(1921 –1997)", 1, 4),
                ("Η «Τραπεζική» έννοια της εκπαίδευσης", 5, 5),
                ("Διδακτική μεθοδολογία", 6, 8),
            ],
            sections.Select(s => (s.Title, s.StartPage, s.EndPage)));
    }

    [Fact]
    public void Text_heavy_pages_are_not_treated_as_slides()
    {
        var pages = Enumerable.Range(1, 6).Select(i => Slide($"Τίτλος {i}", 400)).ToArray();

        var section = Assert.Single(_sectioner.Split(pages));

        Assert.Equal("Ολόκληρο το κείμενο", section.Title);
    }

    [Fact]
    public void Few_slides_stay_one_section()
    {
        var section = Assert.Single(_sectioner.Split([Slide("Α"), Slide("Β"), Slide("Γ")]));

        Assert.Equal(("Ολόκληρο το κείμενο", 1, 3), (section.Title, section.StartPage, section.EndPage));
    }

    [Fact]
    public void CountWords_ignores_punctuation_only_tokens()
    {
        Assert.Equal(3, Sectioner.CountWords("Το κύτταρο — 42 ..."));
    }

    [Fact]
    public void Bold_headings_split_the_text_when_it_has_no_keyword_headings()
    {
        var pages = Enumerable.Range(1, 8).Select(_ => Page(Words(400))).ToArray();
        StyledHeading[] styled =
        [
            new(2, "25.2. Οι υλικές δυνάμεις", AtTop: false),
            new(5, "25.3. Κυρίαρχη τάξη", AtTop: true),
            new(5, "1. Υποενότητα στην ίδια σελίδα", AtTop: false),
            new(7, "Σημειώσεις", AtTop: false),
        ];

        var sections = _sectioner.Split(pages, styled);

        Assert.Equal(
            [("Εισαγωγή", 1, 2), ("25.2. Οι υλικές δυνάμεις", 2, 4), ("25.3. Κυρίαρχη τάξη", 5, 7), ("Σημειώσεις", 7, 8)],
            sections.Select(s => (s.Title, s.StartPage, s.EndPage)));
    }

    [Fact]
    public void Keyword_headings_come_before_bold_headings()
    {
        var pages = new[] { Page("Κεφάλαιο 1", Words(200)), Page(Words(200)), Page("Κεφάλαιο 2", Words(200)), Page(Words(200)) };
        StyledHeading[] styled = [new(2, "Ένας έντονος τίτλος", false), new(4, "Άλλος έντονος τίτλος", false)];

        var sections = _sectioner.Split(pages, styled);

        Assert.Equal(["Κεφάλαιο 1", "Κεφάλαιο 2"], sections.Select(s => s.Title));
    }

    [Fact]
    public void Bold_titles_on_almost_every_page_are_slides_not_headings()
    {
        var pages = Enumerable.Range(1, 6).Select(_ => Page(Words(300))).ToArray();
        var styled = Enumerable.Range(1, 5).Select(p => new StyledHeading(p, $"Τίτλος {p}", true)).ToArray();

        var sections = _sectioner.Split(pages, styled);

        Assert.DoesNotContain(sections, s => s.Title.StartsWith("Τίτλος"));
    }

    [Fact]
    public void A_single_bold_heading_does_not_split()
    {
        var pages = new[] { Page(Words(300)), Page(Words(300)), Page(Words(300)) };

        var sections = _sectioner.Split(pages, [new StyledHeading(2, "Μόνος τίτλος", false)]);

        Assert.Equal([Sectioner.WholeDocumentTitle], sections.Select(s => s.Title));
    }

    [Fact]
    public void Running_headers_with_page_numbers_are_not_numbered_headings()
    {
        // "25 Karl Marx 281": chapter number, author, page number at the top of every page.
        var pages = Enumerable.Range(0, 6)
            .Select(i => Page($"25 Karl Marx {281 + i}", Words(600)))
            .ToArray();

        var sections = _sectioner.Split(pages);

        Assert.DoesNotContain(sections, s => s.Title.Contains("Marx"));
    }

    [Fact]
    public void Repeated_bold_running_headers_are_dropped()
    {
        var pages = Enumerable.Range(1, 10).Select(_ => Page(Words(300))).ToArray();
        var styled = new[] { 2, 4, 6 }.Select(p => new StyledHeading(p, $"{280 + p} Γενεαλογίες της Κοινωνιολογίας", true))
            .Concat([new StyledHeading(3, "1. Πρώτη ενότητα", false), new StyledHeading(8, "2. Δεύτερη ενότητα", false)])
            .ToArray();

        var sections = _sectioner.Split(pages, styled);

        Assert.Equal(["Εισαγωγή", "1. Πρώτη ενότητα", "2. Δεύτερη ενότητα"], sections.Select(s => s.Title));
    }
}
