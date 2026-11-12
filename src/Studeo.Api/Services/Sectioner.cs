using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Studeo.Api.Services;

public partial class Sectioner : ISectioner
{
    /// <summary>Target size of a chunk when there are no headings (or a section is too big).</summary>
    public const int TargetWords = 3000;

    /// <summary>A heading-based section above this size is split into chunks.</summary>
    public const int MaxSectionWords = 4500;

    /// <summary>Text before the first heading becomes an "Εισαγωγή" section only if it is at least this long.</summary>
    public const int MinIntroWords = 100;

    private const int MaxHeadingLength = 100;

    /// <summary>A deck of slides: on average at most this many words per page…</summary>
    public const int MaxSlideWordsPerPage = 200;

    /// <summary>…at least this many pages…</summary>
    public const int MinSlidePages = 4;

    /// <summary>…and most pages (this share) start with a short title line.</summary>
    public const double MinSlideTitleShare = 0.6;

    /// <summary>A slide title is at most this long.</summary>
    private const int MaxSlideTitleLength = 80;

    /// <summary>A run of slides with fewer words joins the next one (cover page, a quote, an image slide).</summary>
    public const int MinSlideSectionWords = 30;

    /// <summary>The title of the single section when nothing splits the document.</summary>
    public const string WholeDocumentTitle = "Ολόκληρο το κείμενο";

    internal record Heading(int Page, string Title, bool AtTop);

    /// <summary>Styled headings are not used when this share of pages (or more) has one: slides with bold titles.</summary>
    public const double MaxStyledHeadingPageShare = 0.6;

    /// <summary>A line found on at least this many pages is a running header/footer, not a heading.</summary>
    public const int MinRunningHeaderPages = 3;

    public IReadOnlyList<SectionDraft> Split(IReadOnlyList<string> pages) => Split(pages, []);

    /// <summary>
    /// Headings, strongest signal first: «Κεφάλαιο / Ενότητα N» in the text; then lines set in bold
    /// (<paramref name="styled"/>, from the PDF's fonts); then numbered lines ("2. Το κύτταρο"); then slide titles;
    /// otherwise chunks of about 3,000 words.
    /// </summary>
    public IReadOnlyList<SectionDraft> Split(IReadOnlyList<string> pages, IReadOnlyList<StyledHeading> styled)
    {
        if (pages.Count == 0)
        {
            return [];
        }

        var words = pages.Select(CountWords).ToArray();
        var headings = FindHeadings(pages, IsKeywordHeading, minCount: 1);
        if (headings.Count == 0)
        {
            headings = FromStyled(styled, pages.Count);
        }
        if (headings.Count == 0)
        {
            headings = WithoutRunningHeaders(FindHeadings(pages, IsNumberedHeading, minCount: 2), minCount: 2);
        }

        var sections = headings.Count > 0
            ? FromHeadings(headings, words)
            : FromSlideTitles(pages, words)
              ?? Chunk(words, 1, pages.Count, i => $"Μέρος {i}", singleTitle: WholeDocumentTitle);

        return sections.Where(s => s.WordCount > 0).ToList();
    }

    private static List<SectionDraft> FromHeadings(List<Heading> headings, int[] words)
    {
        var lastPage = words.Length;
        var result = new List<SectionDraft>();

        // Text before the first heading (e.g. a preface).
        var first = headings[0];
        var introEnd = first.AtTop ? first.Page - 1 : first.Page;
        if (introEnd >= 1 && SumWords(words, 1, introEnd) >= MinIntroWords)
        {
            result.Add(new SectionDraft("Εισαγωγή", 1, introEnd, SumWords(words, 1, introEnd)));
        }

        for (var i = 0; i < headings.Count; i++)
        {
            var start = headings[i].Page;
            var end = i + 1 < headings.Count
                ? (headings[i + 1].AtTop ? headings[i + 1].Page - 1 : headings[i + 1].Page)
                : lastPage;
            end = Math.Max(end, start);

            var title = headings[i].Title;
            if (SumWords(words, start, end) > MaxSectionWords)
            {
                result.AddRange(Chunk(words, start, end, n => $"{title} (μέρος {n})", singleTitle: title));
            }
            else
            {
                result.Add(new SectionDraft(title, start, end, SumWords(words, start, end)));
            }
        }

        return result;
    }

    /// <summary>
    /// Presentations have no "Κεφάλαιο" headings but every slide starts with its title. Consecutive slides with the
    /// same title (or a longer title starting with it, "Διδακτική μεθοδολογία – Παραδείγματα") form one section;
    /// tiny runs join the next one. Null when the document doesn't look like slides or gives a single section.
    /// </summary>
    internal static List<SectionDraft>? FromSlideTitles(IReadOnlyList<string> pages, int[] words)
    {
        if (pages.Count < MinSlidePages || words.Sum() > MaxSlideWordsPerPage * pages.Count)
        {
            return null;
        }

        var titles = pages.Select(SlideTitle).ToArray();
        if (titles.Count(t => t is not null) < MinSlideTitleShare * pages.Count)
        {
            return null;
        }

        // Runs of slides that share a title; a slide without a title continues the previous run.
        var runs = new List<(string Title, int Start, int End)>();
        for (var p = 1; p <= pages.Count; p++)
        {
            var title = titles[p - 1];
            if (runs.Count > 0 && (title is null || SameTopic(runs[^1].Title, title)))
            {
                runs[^1] = runs[^1] with { End = p };
            }
            else
            {
                runs.Add((title ?? $"Σελίδα {p}", p, p));
            }
        }

        // Tiny runs (a cover, a quote) join the next run, or the previous one at the end.
        var merged = new List<(string Title, int Start, int End)>();
        (string Title, int Start, int End)? carry = null;
        foreach (var run in runs)
        {
            var current = carry is { } c ? (run.Title, c.Start, run.End) : run;
            carry = null;
            if (SumWords(words, current.Start, current.End) < MinSlideSectionWords)
            {
                carry = current;
                continue;
            }
            merged.Add(current);
        }
        if (carry is { } tail)
        {
            if (merged.Count == 0) return null;
            merged[^1] = merged[^1] with { End = tail.End };
        }

        return merged.Count < 2
            ? null
            : merged.Select(r => new SectionDraft(r.Title, r.Start, r.End, SumWords(words, r.Start, r.End))).ToList();
    }

    /// <summary>The first line of a slide if it is short enough to be its title.</summary>
    private static string? SlideTitle(string page)
    {
        var first = page.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (first is null) return null;
        var title = CollapseSpaces(first).TrimStart('•', '●', '-', '–', ' ');
        return title.Length is > 1 and <= MaxSlideTitleLength && title.Any(char.IsLetter) ? title : null;
    }

    private static bool SameTopic(string runTitle, string title)
    {
        var a = Normalize(runTitle);
        var b = Normalize(title);
        return a == b || b.StartsWith(a, StringComparison.Ordinal);
    }

    /// <summary>Groups whole pages into chunks of about TargetWords; a small last chunk is merged into the previous one.</summary>
    internal static List<SectionDraft> Chunk(int[] words, int startPage, int endPage, Func<int, string> title, string singleTitle)
    {
        var ranges = new List<(int Start, int End, int Words)>();
        var chunkStart = startPage;
        var acc = 0;
        for (var p = startPage; p <= endPage; p++)
        {
            acc += words[p - 1];
            if (acc >= TargetWords || p == endPage)
            {
                ranges.Add((chunkStart, p, acc));
                chunkStart = p + 1;
                acc = 0;
            }
        }

        if (ranges.Count > 1 && ranges[^1].Words < TargetWords / 3)
        {
            var last = ranges[^1];
            var prev = ranges[^2];
            ranges.RemoveRange(ranges.Count - 2, 2);
            ranges.Add((prev.Start, last.End, prev.Words + last.Words));
        }

        return ranges.Count == 1
            ? [new SectionDraft(singleTitle, ranges[0].Start, ranges[0].End, ranges[0].Words)]
            : ranges.Select((r, i) => new SectionDraft(title(i + 1), r.Start, r.End, r.Words)).ToList();
    }

    /// <summary>
    /// Bold headings from the PDF: one per page (the first), without running headers. Not used for fewer than two,
    /// or when most pages have one (slides with bold titles are better split by <see cref="FromSlideTitles"/>).
    /// </summary>
    internal static List<Heading> FromStyled(IReadOnlyList<StyledHeading> styled, int pageCount)
    {
        var headings = WithoutRunningHeaders(
                styled.Where(h => h.Page >= 1 && h.Page <= pageCount)
                    .Select(h => new Heading(h.Page, CollapseSpaces(h.Title), h.AtTop))
                    .Where(h => h.Title.Length > 0)
                    .ToList(),
                minCount: 0)
            .OrderBy(h => h.Page)
            .GroupBy(h => h.Page)
            .Select(g => g.First())
            .ToList();

        var tooMany = pageCount >= MinSlidePages && headings.Count > MaxStyledHeadingPageShare * pageCount;
        return headings.Count >= 2 && !tooMany ? headings : [];
    }

    /// <summary>
    /// Drops lines that repeat on many pages with only a page number changing ("25 Karl Marx 281",
    /// "Γενεαλογίες της Κοινωνιολογίας 290"): running headers and footers. Empty if fewer than minCount remain.
    /// </summary>
    internal static List<Heading> WithoutRunningHeaders(List<Heading> headings, int minCount)
    {
        static string Key(string title) => EdgeNumbersRegex().Replace(Normalize(title), "").Trim();

        var repeating = headings.GroupBy(h => Key(h.Title))
            .Where(g => g.Select(h => h.Page).Distinct().Count() >= MinRunningHeaderPages)
            .Select(g => g.Key)
            .ToHashSet();
        var kept = headings.Where(h => !repeating.Contains(Key(h.Title))).ToList();
        return kept.Count >= minCount ? kept : [];
    }

    private static List<Heading> FindHeadings(IReadOnlyList<string> pages, Func<string, bool> isHeading, int minCount)
    {
        var found = new List<Heading>();
        for (var p = 0; p < pages.Count; p++)
        {
            var lines = pages[p].Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < lines.Length; i++)
            {
                if (isHeading(lines[i]))
                {
                    found.Add(new Heading(p + 1, CollapseSpaces(lines[i]), AtTop: i == 0));
                }
            }
        }

        // A table of contents repeats the headings early on: keep the LAST occurrence of each title,
        // then at most one heading per page (the first one).
        var headings = found
            .GroupBy(h => Normalize(h.Title))
            .Select(g => g.Last())
            .OrderBy(h => h.Page)
            .GroupBy(h => h.Page)
            .Select(g => g.First())
            .ToList();

        return headings.Count >= minCount ? headings : [];
    }

    /// <summary>"Κεφάλαιο 2: Η φωτοσύνθεση", "ΕΝΟΤΗΤΑ 3", "Chapter IV - Cells" … but not TOC lines ending in a page number.</summary>
    internal static bool IsKeywordHeading(string line)
    {
        if (line.Length > MaxHeadingLength)
        {
            return false;
        }
        var match = KeywordHeadingRegex().Match(Normalize(line));
        return match.Success && !TocPageNumberRegex().IsMatch(match.Groups["rest"].Value) && !DotLeaderRegex().IsMatch(line);
    }

    /// <summary>"1. Εισαγωγή", "2 Το κύτταρο" — short, capitalised, no sentence punctuation.</summary>
    internal static bool IsNumberedHeading(string line) =>
        line.Length <= MaxHeadingLength && NumberedHeadingRegex().IsMatch(line) && !DotLeaderRegex().IsMatch(line);

    public static int CountWords(string text) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Count(w => w.Any(char.IsLetterOrDigit));

    private static int SumWords(int[] words, int startPage, int endPage) =>
        words.Skip(startPage - 1).Take(endPage - startPage + 1).Sum();

    private static string CollapseSpaces(string s) => WhitespaceRegex().Replace(s, " ").Trim();

    /// <summary>Lower case without accents, so "ΚΕΦΑΛΑΙΟ" and "Κεφάλαιο" match the same pattern.</summary>
    private static string Normalize(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in CollapseSpaces(s).Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(char.ToLowerInvariant(c));
            }
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    [GeneratedRegex(@"^(κεφαλαιο|ενοτητα|μαθημα|μερος|chapter|section|unit|part|lesson)\s+([0-9]{1,3}|[ivxlc]{1,6}|[α-ω]{1,2}['΄ʹ’]?)(?=$|[\s:.\-–—)])(?<rest>.*)$")]
    private static partial Regex KeywordHeadingRegex();

    [GeneratedRegex(@"\s\d{1,4}\s*$")]
    private static partial Regex TocPageNumberRegex();

    [GeneratedRegex(@"(\.{3,}|…|_{3,})\s*\d*\s*$")]
    private static partial Regex DotLeaderRegex();

    [GeneratedRegex(@"^\d{1,2}\.?\s+\p{Lu}[^.!?;:]{2,90}$")]
    private static partial Regex NumberedHeadingRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    /// <summary>Page numbers at the start or end of a line.</summary>
    [GeneratedRegex(@"^[\d\s|.\-–]+|[\d\s|.\-–]+$")]
    private static partial Regex EdgeNumbersRegex();
}
