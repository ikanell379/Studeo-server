using System.Text;
using UglyToad.PdfPig.Content;

namespace Studeo.Api.Services;

/// <summary>
/// Program code in a PDF: lines set in a monospace font. The PDF's text has no spaces at the start of a line, so
/// a Python program would lose its indentation; here each code line is written again from the position of its
/// letters (one column per letter width) and put in a ``` fence in place of the same line in the page text.
/// </summary>
public static class PdfCodeBlocks
{
    /// <summary>A code block needs at least this many lines (one line in Courier is more often a file name).</summary>
    public const int MinLines = 2;

    /// <summary>Share of a line's letters that must be monospace for it to be code.</summary>
    private const double MonospaceShare = 0.8;

    public static string Restore(Page page, string text)
    {
        var blocks = Blocks(page);
        if (blocks.Count == 0)
        {
            return text;
        }

        var lines = text.Split('\n').ToList();
        foreach (var block in blocks)
        {
            var start = Find(lines, block);
            if (start < 0)
            {
                continue; // the page text has these lines in another order: leave it as it is
            }
            lines.RemoveRange(start, block.Count);
            lines.InsertRange(start, ["```", .. block, "```"]);
        }
        return string.Join('\n', lines);
    }

    /// <summary>Where the block's lines are, one after the other, in the page text (ignoring spaces); -1 if not.</summary>
    private static int Find(List<string> lines, List<string> block)
    {
        var wanted = block.Select(Squeeze).ToList();
        var have = lines.Select(Squeeze).ToList();
        for (var i = 0; i + wanted.Count <= have.Count; i++)
        {
            if (Enumerable.Range(0, wanted.Count).All(j => have[i + j] == wanted[j]))
            {
                return i;
            }
        }
        return -1;
    }

    private static string Squeeze(string line) => string.Concat(line.Where(c => !char.IsWhiteSpace(c)));

    /// <summary>The page's code blocks, top first: each line with its indentation and inner spaces.</summary>
    internal static List<List<string>> Blocks(Page page)
    {
        var letters = page.Letters.Where(l => !string.IsNullOrWhiteSpace(l.Value) && l.PointSize > 0).ToList();
        if (letters.Count == 0 || !letters.Any(l => OfficeFiles.IsMonospaceFont(l.FontName)))
        {
            return [];
        }

        // Letters into lines by baseline, top of the page first.
        var lines = new List<List<Letter>>();
        foreach (var letter in letters.OrderByDescending(l => l.StartBaseLine.Y).ThenBy(l => l.StartBaseLine.X))
        {
            var line = lines.LastOrDefault();
            if (line is not null && Math.Abs(line[0].StartBaseLine.Y - letter.StartBaseLine.Y) < 0.3 * letter.PointSize)
            {
                line.Add(letter);
            }
            else
            {
                lines.Add([letter]);
            }
        }

        var blocks = new List<List<List<Letter>>>();
        List<List<Letter>>? current = null;
        foreach (var line in lines)
        {
            var mono = line.Count(l => OfficeFiles.IsMonospaceFont(l.FontName));
            if (mono < MonospaceShare * line.Count)
            {
                current = null;
                continue;
            }
            var previous = current?[^1];
            var size = line[0].PointSize;
            if (current is null || previous is null || previous[0].StartBaseLine.Y - line[0].StartBaseLine.Y > 2.5 * size)
            {
                current = [];
                blocks.Add(current);
            }
            current.Add(line);
        }

        return blocks.Where(b => b.Count >= MinLines).Select(Write).ToList();
    }

    /// <summary>The block's lines, each letter in its column: the block's left edge is column 0.</summary>
    private static List<string> Write(List<List<Letter>> block)
    {
        var all = block.SelectMany(l => l).ToList();
        var width = LetterWidth(block);
        var left = all.Min(l => l.StartBaseLine.X);
        return block.Select(line =>
        {
            var sb = new StringBuilder();
            foreach (var letter in line.OrderBy(l => l.StartBaseLine.X))
            {
                var column = (int)Math.Round((letter.StartBaseLine.X - left) / width);
                if (column > sb.Length)
                {
                    sb.Append(' ', column - sb.Length);
                }
                sb.Append(letter.Value);
            }
            return sb.ToString().Replace("\0", string.Empty).TrimEnd();
        }).ToList();
    }

    /// <summary>The usual step from one letter to the next in the block (all letters of a monospace font are as wide).</summary>
    private static double LetterWidth(List<List<Letter>> block)
    {
        var steps = block
            .SelectMany(line =>
            {
                var ordered = line.OrderBy(l => l.StartBaseLine.X).ToList();
                return ordered.Zip(ordered.Skip(1), (a, b) => b.StartBaseLine.X - a.StartBaseLine.X);
            })
            .Where(s => s > 0.1)
            .Order()
            .ToList();
        if (steps.Count > 0)
        {
            // The smallest common step: two letters side by side (not across a space).
            var median = steps[steps.Count / 2];
            var small = steps.Where(s => s <= median * 1.05).ToList();
            return small[small.Count / 2];
        }
        var first = block[0][0];
        return first.Width > 0 ? first.Width : first.PointSize * 0.6;
    }
}
