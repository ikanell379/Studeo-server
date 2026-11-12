using System.Globalization;
using System.Text;

namespace Studeo.Api.Services;

/// <summary>
/// Finds pages whose text can't be read: a scan whose text layer is garbage (a page scanned upside down or
/// mirrored, then OCR'd: «Λol^noyΦUo^orj qλΦλDdDιτ»). Two signals, both needed, so that neither noisy-but-readable
/// OCR («τnν υλ1σηκn σύλλnψn») nor short pages with few common words (slides, a CV) are flagged:
/// almost none of the words are common ones («και», «του», «the») and most words mix scripts, digits or symbols.
/// </summary>
public static class PageQuality
{
    /// <summary>Pages with fewer words are not judged (a title page, a figure): too little to tell.</summary>
    public const int MinWordsToJudge = 40;

    /// <summary>Readable Greek or English prose has ~15–45% common words; garbage has ~0–4%.</summary>
    public const double MaxCommonWordShare = 0.08;

    /// <summary>Readable text has mostly clean words (letters of one script); garbage ~15–25%.</summary>
    public const double MaxCleanWordShare = 0.5;

    private static readonly HashSet<string> CommonWords = new(
        """
        και το τα της τη την του των τον τις τους ο η οι σε στο στη στην στα στον στις στους απο που με να για
        οτι δεν θα ως ειναι ενα μια αλλα η αυτο αυτη αυτα οπως κατα μετα πριν οταν ενω ετσι πιο προς χωρις επι δια
        the of and to in is that for it as with was on be by this are or an at from which not but have has had were their its
        """.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries),
        StringComparer.Ordinal);

    /// <summary>The 1-based numbers of the pages that can't be read.</summary>
    public static IReadOnlyList<int> UnreadablePages(IEnumerable<string> pages) =>
        pages.Select((text, i) => (text, page: i + 1)).Where(p => IsUnreadable(p.text)).Select(p => p.page).ToList();

    public static bool IsUnreadable(string text)
    {
        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Trim(".,;:!?«»\"'()[]-–—·…".ToCharArray()))
            .Where(w => w.Any(char.IsLetter))
            .ToList();
        if (words.Count < MinWordsToJudge)
        {
            return false;
        }

        var common = words.Count(w => CommonWords.Contains(Plain(w)));
        var clean = words.Count(IsClean);
        return common < MaxCommonWordShare * words.Count && clean < MaxCleanWordShare * words.Count;
    }

    /// <summary>Only letters (and a hyphen or apostrophe), all of them Greek or all of them Latin.</summary>
    private static bool IsClean(string word)
    {
        bool? greek = null;
        foreach (var c in word)
        {
            if (c is '-' or '\'' or '’' or '΄')
            {
                continue;
            }
            if (!char.IsLetter(c))
            {
                return false;
            }
            var isGreek = c is >= 'Ͱ' and <= 'Ͽ' or >= 'ἀ' and <= '῿';
            var isLatin = c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= 'À' and <= 'ɏ';
            if (!isGreek && !isLatin)
            {
                return false;
            }
            if (greek is { } g && g != isGreek)
            {
                return false;
            }
            greek = isGreek;
        }
        return greek is not null;
    }

    /// <summary>Lower case without accents.</summary>
    private static string Plain(string word)
    {
        var sb = new StringBuilder(word.Length);
        foreach (var c in word.ToLowerInvariant().Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }
}
