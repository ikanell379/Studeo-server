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

    private record Heading(int Page, string Title, bool AtTop);

    public IReadOnlyList<SectionDraft> Split(IReadOnlyList<string> pages)
    {
        if (pages.Count == 0)
        {
            return [];
        }

        var words = pages.Select(CountWords).ToArray();
        var headings = FindHeadings(pages, IsKeywordHeading, minCount: 1);
        if (headings.Count == 0)
        {
            headings = FindHeadings(pages, IsNumberedHeading, minCount: 2);
        }

        var sections = headings.Count > 0
            ? FromHeadings(headings, words)
            : Chunk(words, 1, pages.Count, i => $"Μέρος {i}", singleTitle: "Ολόκληρο το κείμενο");

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

    internal static int CountWords(string text) =>
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
}
