using System.Text.RegularExpressions;
using Studeo.Api.Contracts;

namespace Studeo.Api.Services;

/// <summary>
/// Tells from a section's text whether it is programming (code), science (formulas, units, calculations) or general
/// material, and which programming language its code is in. Rough signals on purpose: the user sees the result in
/// the settings and can change it. A biology text with one chemical equation stays general.
/// </summary>
public static partial class SubjectDetector
{
    /// <summary>Programming: at least this many lines of code…</summary>
    public const int MinCodeLines = 6;

    /// <summary>…making up at least this share of the lines.</summary>
    public const double MinCodeShare = 0.08;

    /// <summary>Science: at least this many formula signals per 1,000 words…</summary>
    public const double MinScienceSignalsPer1000Words = 12;

    /// <summary>…and at least this many in all (a short page with two formulas is not a physics text).</summary>
    public const int MinScienceSignals = 6;

    public static MaterialSubject Detect(IEnumerable<string> pages)
    {
        var text = string.Join('\n', pages);
        var lines = text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        if (lines.Count == 0)
        {
            return MaterialSubject.General;
        }

        var codeLines = CodeLines(text);
        if (codeLines.Count >= MinCodeLines && codeLines.Count >= MinCodeShare * lines.Count)
        {
            return new MaterialSubject(Subjects.Programming, Language(text, codeLines));
        }

        var words = Math.Max(1, Sectioner.CountWords(TextKinds.ProseOnly(text)));
        var signals = ScienceSignals(text);
        return signals >= MinScienceSignals && signals * 1000.0 / words >= MinScienceSignalsPer1000Words
            ? new MaterialSubject(Subjects.Science)
            : MaterialSubject.General;
    }

    /// <summary>The language of the code in the text (even a little of it), or null when it can't be told.</summary>
    public static string? DetectLanguage(IEnumerable<string> pages)
    {
        var text = string.Join('\n', pages);
        return Language(text, CodeLines(text));
    }

    /// <summary>Lines in ``` fences, and lines outside them that look like code (keywords, «;» or «{» endings).</summary>
    internal static List<string> CodeLines(string text)
    {
        var lines = new List<string>();
        var inFence = false;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("```"))
            {
                inFence = !inFence;
                continue;
            }
            if (line.Length > 0 && (inFence || LooksLikeCode(line)))
            {
                lines.Add(line);
            }
        }
        return lines;
    }

    private static bool LooksLikeCode(string line) =>
        CodeLineRegex().IsMatch(line) || Keywords.Any(k => k.Pattern.IsMatch(line));

    /// <summary>The fences' language when they name one, else the language whose keywords the code has most.</summary>
    private static string? Language(string text, List<string> codeLines)
    {
        var fenced = FenceRegex().Matches(text)
            .Select(m => m.Groups[1].Value.ToLowerInvariant())
            .Select(Normalize)
            .Where(l => l is not null)
            .GroupBy(l => l)
            .MaxBy(g => g.Count())?.Key;
        if (fenced is not null)
        {
            return fenced;
        }

        var best = Keywords
            .GroupBy(k => k.Language)
            .Select(g => (Language: g.Key, Score: codeLines.Sum(line => g.Count(k => k.Pattern.IsMatch(line)))))
            .MaxBy(s => s.Score);
        return best.Score >= 2 ? best.Language : null;
    }

    /// <summary>«py» → python, «c++» → cpp…; null for a name we don't know.</summary>
    internal static string? Normalize(string name) => name switch
    {
        "python" or "py" or "python3" => "python",
        "c" or "h" => "c",
        "cpp" or "c++" or "cc" or "hpp" => "cpp",
        "java" => "java",
        "csharp" or "cs" or "c#" => "csharp",
        "javascript" or "js" or "typescript" or "ts" or "node" => "javascript",
        "sql" or "mysql" or "postgresql" or "sqlite" => "sql",
        "glossa" or "γλωσσα" or "γλώσσα" or "pseudocode" or "ψευδοκώδικας" => ProgrammingLanguages.Glossa,
        _ => null,
    };

    private sealed record Keyword(string Language, Regex Pattern);

    private static Keyword K(string language, string pattern) => new(language, new Regex(pattern, RegexOptions.CultureInvariant));

    private static readonly Keyword[] Keywords =
    [
        K("python", @"^\s*def \w+\(.*\)\s*:"),
        K("python", @"^\s*(import \w+|from \w+(\.\w+)* import )"),
        K("python", @"^\s*(elif .*|else|try|except.*|for \w+ in .+|while .+):\s*$"),
        K("python", @"\bprint\(|\binput\(|\brange\("),
        K("c", @"^\s*#include\s*<\w+\.h>"),
        K("c", @"\b(printf|scanf|malloc)\s*\("),
        K("c", @"\bint main\s*\("),
        K("cpp", @"#include\s*<(iostream|vector|string)>|\bstd::|\bcout\s*<<|\bcin\s*>>"),
        K("java", @"\bpublic (static )?(class|void|int)\b|System\.out\.print"),
        K("csharp", @"Console\.(Write|Read)|^\s*using System|\bnamespace \w+"),
        K("javascript", @"console\.log\(|\bfunction \w+\(|\b(const|let) \w+ =|=>\s*\{"),
        K("sql", @"^\s*(SELECT .+ FROM|INSERT INTO|UPDATE \w+ SET|DELETE FROM|CREATE TABLE)\b"),
        K(ProgrammingLanguages.Glossa, @"^\s*(ΑΛΓΟΡΙΘΜΟΣ|ΠΡΟΓΡΑΜΜΑ|ΜΕΤΑΒΛΗΤΕΣ|ΑΡΧΗ|ΤΕΛΟΣ_ΠΡΟΓΡΑΜΜΑΤΟΣ|ΤΕΛΟΣ_ΑΝ|ΤΕΛΟΣ_ΕΠΑΝΑΛΗΨΗΣ|ΜΕΧΡΙΣ_ΟΤΟΥ|ΑΚΕΡΑΙΕΣ|ΠΡΑΓΜΑΤΙΚΕΣ)\b"),
        K(ProgrammingLanguages.Glossa, @"^\s*(ΓΡΑΨΕ|ΔΙΑΒΑΣΕ|ΕΜΦΑΝΙΣΕ|ΑΝ .+ ΤΟΤΕ|ΓΙΑ \S+ ΑΠΟ .+ ΜΕΧΡΙ|ΟΣΟ .+ ΕΠΑΝΑΛΑΒΕ)\b"),
        K(ProgrammingLanguages.Glossa, @"^\s*[\p{L}_][\p{L}\d_\[\], ]*\s*<-\s*\S"),
    ];

    /// <summary>Formulas ($…$ from equations), equations with numbers, math symbols, units, chemical formulas.</summary>
    internal static int ScienceSignals(string text)
    {
        var prose = TextKinds.WithoutCode(text);
        return 3 * TextKinds.MathRegex().Count(prose)
            + EquationRegex().Count(prose)
            + MathSymbolRegex().Count(prose)
            + UnitRegex().Count(prose)
            + ChemicalFormulaRegex().Count(prose);
    }

    [GeneratedRegex(@"^```\s*([\w+#-]+)", RegexOptions.Multiline)]
    private static partial Regex FenceRegex();

    /// <summary>
    /// «x = 1;», «if (a > b) {», «}»: a statement or a brace on its own. A line ending in «;» counts only with
    /// «=» or brackets and no Greek letters: in Greek «;» is the question mark.
    /// </summary>
    [GeneratedRegex(@"\)\s*\{\s*$|^\s*[{}]\s*$|^[^\p{IsGreekandCoptic}]*[=()\[\]][^\p{IsGreekandCoptic}]*;\s*$|^\s*(if|while|for)\s*\(.*\)\s*\{?\s*$|^\s*return\b[^\p{IsGreekandCoptic}]*;\s*$")]
    private static partial Regex CodeLineRegex();

    /// <summary>«v = 20 m/s», «F = m·a», «y = 2x + 3»: a short symbol or number, «=», then an expression.</summary>
    [GeneratedRegex(@"(?<![\p{L}\d])[\p{L}\d][\p{L}\d_()²³⁻₀-₉]{0,6}\s*[=≈]\s*[-+−]?[\p{L}\d(√π][^=\n]{0,40}?[\d+\-−×·*/^²³]")]
    private static partial Regex EquationRegex();

    /// <summary>Math symbols; π and Δ only on their own («Δx», «2π»), not as letters of Greek words.</summary>
    [GeneratedRegex(@"[²³⁴⁻₀₁₂₃₄₅₆₇₈₉√∫∑∏∂∇±≤≥≠≈∞×÷→⇌∆]|(?<!\p{L})[πΔ](?!\p{IsGreekandCoptic})")]
    private static partial Regex MathSymbolRegex();

    [GeneratedRegex(@"\b\d+(?:[.,]\d+)?\s?(?:m/s²?|km/h|kg|mol|kJ|J|N|Pa|kPa|atm|W|kW|V|mA|Hz|°C|K|mL|L|cm³|m³|m²|cm|mm|km|m|g|s|Ω)(?![\p{L}\d])")]
    private static partial Regex UnitRegex();

    /// <summary>H2O, CO2, C6H12O6, H₂SO₄: element symbols with at least one count.</summary>
    [GeneratedRegex(@"(?<![\p{L}\d])(?=[A-Z][a-z]?[\d₀-₉]|(?:[A-Z][a-z]?)+[\d₀-₉])(?:[A-Z][a-z]?[\d₀-₉]*){2,}(?![\p{L}])")]
    private static partial Regex ChemicalFormulaRegex();
}

/// <summary>Parts of extracted text: ``` code blocks, `inline code` and $formulas$.</summary>
public static partial class TextKinds
{
    /// <summary>The text without code blocks, inline code and formulas: what is left is prose.</summary>
    public static string ProseOnly(string text) => MathRegex().Replace(WithoutCode(text), " ");

    public static string WithoutCode(string text) => InlineCodeRegex().Replace(CodeBlockRegex().Replace(text, " "), " ");

    [GeneratedRegex(@"^```.*?^```[^\n]*$", RegexOptions.Singleline | RegexOptions.Multiline)]
    public static partial Regex CodeBlockRegex();

    [GeneratedRegex(@"`[^`\n]+`")]
    public static partial Regex InlineCodeRegex();

    /// <summary>$$…$$ or $…$ (not «5$», «$ 10»: the opening $ is followed and the closing $ preceded by no space).</summary>
    [GeneratedRegex(@"\$\$[^$]+?\$\$|\$(?=\S)[^$\n]+?(?<=\S)\$(?!\d)")]
    public static partial Regex MathRegex();
}
