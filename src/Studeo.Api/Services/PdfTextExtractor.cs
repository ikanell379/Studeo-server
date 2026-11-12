using System.Text.RegularExpressions;
using Studeo.Api.Contracts;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;

namespace Studeo.Api.Services;

public partial class PdfTextExtractor(ILogger<PdfTextExtractor> logger) : IPdfTextExtractor
{
    public PdfExtractionResult Extract(byte[] pdf, int maxPages)
    {
        if (!HasPdfHeader(pdf))
        {
            return PdfExtractionResult.Fail(ErrorCodes.InvalidPdf);
        }

        try
        {
            using var document = PdfDocument.Open(pdf);

            if (document.NumberOfPages > maxPages)
            {
                return PdfExtractionResult.Fail(ErrorCodes.TooManyPages);
            }

            var pages = new List<string>(document.NumberOfPages);
            var headings = new List<StyledHeading>();
            foreach (var page in document.GetPages())
            {
                pages.Add(Clean(ContentOrderTextExtractor.GetText(page)));
                headings.AddRange(StyledHeadings(page));
            }

            return new PdfExtractionResult(pages, null) { Headings = headings };
        }
        catch (Exception ex)
        {
            // PdfPig throws various exception types for corrupt or encrypted files.
            logger.LogWarning(ex, "Could not read PDF");
            return PdfExtractionResult.Fail(ErrorCodes.InvalidPdf);
        }
    }

    private static bool HasPdfHeader(byte[] pdf)
    {
        // "%PDF-" may be preceded by a few junk bytes; the spec allows it within the first 1024 bytes.
        var limit = Math.Min(pdf.Length, 1024) - 5;
        for (var i = 0; i <= limit; i++)
        {
            if (pdf[i] == '%' && pdf[i + 1] == 'P' && pdf[i + 2] == 'D' && pdf[i + 3] == 'F' && pdf[i + 4] == '-')
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>PostgreSQL text cannot hold NUL characters; also trims trailing whitespace.</summary>
    private static string Clean(string text) => text.Replace("\0", string.Empty).Trim();

    /// <summary>A heading needs at least this many letters (not a bold footnote number).</summary>
    private const int MinHeadingLetters = 3;

    /// <summary>A heading of several lines is cut to this length.</summary>
    internal const int MaxStyledHeadingLength = 200;

    /// <summary>
    /// The page's bold lines that are at least as large as its body text (the most common letter size), so bold
    /// footnote numbers and small bold notes don't count. Consecutive such lines of the same size are one heading
    /// that wraps ("25.2. Οι υλικές δυνάμεις… (Κριτική της" + "Πολιτικής Οικονομίας, 1859)").
    /// </summary>
    internal static IEnumerable<StyledHeading> StyledHeadings(Page page)
    {
        var letters = page.Letters.Where(l => !string.IsNullOrWhiteSpace(l.Value)).ToList();
        if (letters.Count == 0)
        {
            yield break;
        }
        var bodySize = letters.GroupBy(l => Math.Round(l.PointSize * 2) / 2).MaxBy(g => g.Count())!.Key;

        var lines = Lines(NearestNeighbourWordExtractor.Instance.GetWords(page.Letters));
        StyledLine? current = null;
        var bodyAbove = false;
        foreach (var line in lines)
        {
            var isHeading = line.Bold && line.Size >= bodySize - 0.25 && line.Text.Count(char.IsLetter) >= MinHeadingLetters;
            // The next line of the same heading: same size, right below, and not the start of a new numbered one.
            if (isHeading && current is { } open && Math.Abs(open.Size - line.Size) < 0.4
                && open.Bottom - line.Top < 2 * line.Size && !NumberedStartRegex().IsMatch(line.Text))
            {
                current = open with { Text = open.Text + " " + line.Text, Bottom = line.Bottom };
                continue;
            }
            if (current is { } done)
            {
                yield return Heading(page.Number, done);
                current = null;
            }
            if (isHeading)
            {
                current = line with { AtTop = !bodyAbove };
            }
            else if (line.Text.Count(char.IsLetter) >= 15)
            {
                bodyAbove = true; // a running header (short) doesn't count as text above
            }
        }
        if (current is { } last)
        {
            yield return Heading(page.Number, last);
        }
    }

    private static StyledHeading Heading(int page, StyledLine line)
    {
        var title = SpacesRegex().Replace(line.Text, " ").Trim();
        if (title.Length > MaxStyledHeadingLength)
        {
            title = title[..(MaxStyledHeadingLength - 1)].TrimEnd() + "…";
        }
        return new StyledHeading(page, title, line.AtTop);
    }

    private sealed record StyledLine(string Text, double Size, bool Bold, double Top, double Bottom, bool AtTop = false);

    /// <summary>Words put together into lines, top of the page first.</summary>
    private static List<StyledLine> Lines(IEnumerable<Word> words)
    {
        var lines = new List<List<Word>>();
        foreach (var word in words.Where(w => w.Letters.Count > 0).OrderByDescending(w => w.Letters[0].StartBaseLine.Y))
        {
            var baseline = word.Letters[0].StartBaseLine.Y;
            var line = lines.LastOrDefault();
            if (line is not null && Math.Abs(line[0].Letters[0].StartBaseLine.Y - baseline) < 0.5 * Math.Max(word.Letters[0].PointSize, 1))
            {
                line.Add(word);
            }
            else
            {
                lines.Add([word]);
            }
        }

        return lines.Select(line =>
        {
            // Superscripts (footnote numbers) are much smaller than the line's own text: leave them out.
            var lineSize = Median(line.SelectMany(w => w.Letters).Select(l => l.PointSize));
            var ordered = line
                .Where(w => w.Letters.Average(l => l.PointSize) >= SuperscriptRatio * lineSize)
                .OrderBy(w => w.BoundingBox.Left)
                .ToList();
            if (ordered.Count == 0)
            {
                ordered = line.OrderBy(w => w.BoundingBox.Left).ToList();
            }
            var all = ordered.SelectMany(w => w.Letters).Where(l => !string.IsNullOrWhiteSpace(l.Value)).ToList();
            var bold = all.Count(IsBold) * 2 > all.Count;
            var size = all.Count == 0 ? 0 : all.Average(l => l.PointSize);
            return new StyledLine(
                string.Join(' ', ordered.Select(w => w.Text)).Replace("\0", string.Empty),
                size,
                bold,
                ordered.Max(w => w.BoundingBox.Top),
                ordered.Min(w => w.BoundingBox.Bottom));
        }).ToList();
    }

    /// <summary>A word smaller than this share of its line's text size is a superscript.</summary>
    private const double SuperscriptRatio = 0.8;

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToList();
        return sorted.Count == 0 ? 0 : sorted[sorted.Count / 2];
    }

    /// <summary>"1. ", "2.3. ", "25.6 " at the start of a line: a new numbered heading.</summary>
    [GeneratedRegex(@"^\d{1,3}(\.\d{1,3})*\.?\s")]
    private static partial Regex NumberedStartRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpacesRegex();

    private static bool IsBold(Letter letter)
    {
        if (letter.FontDetails?.IsBold == true)
        {
            return true;
        }
        var name = letter.FontName ?? "";
        return name.Contains("Bold", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Black", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Heavy", StringComparison.OrdinalIgnoreCase);
    }
}
