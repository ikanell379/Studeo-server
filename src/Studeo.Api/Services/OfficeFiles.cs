using DocumentFormat.OpenXml.Packaging;

namespace Studeo.Api.Services;

/// <summary>What the .docx and .pptx extractors share.</summary>
internal static class OfficeFiles
{
    /// <summary>
    /// A part (e.g. the document body) larger than this many characters is refused: a 20 MB upload is compressed
    /// and could unpack to far more (a zip bomb). Real study material is far below it.
    /// </summary>
    public const long MaxCharactersInPart = 50_000_000;

    public static OpenSettings Settings => new() { MaxCharactersInPart = MaxCharactersInPart };

    /// <summary>.docx and .pptx are zip files ("PK\x03\x04"); a password-protected one is not, so it fails early.</summary>
    public static bool IsZip(byte[] content) =>
        content.Length >= 4 && content[0] == 'P' && content[1] == 'K' && content[2] == 3 && content[3] == 4;

    /// <summary>PostgreSQL text cannot hold NUL characters; also trims each line and drops empty ones.</summary>
    public static string Clean(IEnumerable<string> lines) => Clean(lines.Select(l => new TextLine(l)));

    /// <summary>
    /// Like <see cref="Clean(IEnumerable{string})"/>, but consecutive code lines are kept with their indentation
    /// (it is part of a Python program) inside a Markdown fence («```python» … «```»), so the AI and the UI see the
    /// program as it was written. Tabs in code become four spaces.
    /// </summary>
    public static string Clean(IEnumerable<TextLine> lines)
    {
        var output = new List<string>();
        var code = new List<string>();
        string? lang = null;

        void EndCode()
        {
            while (code.Count > 0 && code[^1].Length == 0)
            {
                code.RemoveAt(code.Count - 1);
            }
            var first = code.FindIndex(l => l.Length > 0);
            if (first >= 0)
            {
                output.Add("```" + (lang ?? ""));
                output.AddRange(code.Skip(first));
                output.Add("```");
            }
            code.Clear();
            lang = null;
        }

        foreach (var line in lines)
        {
            var text = line.Text.Replace("\0", string.Empty);
            if (line.Code)
            {
                lang ??= line.Lang;
                code.AddRange(text.Split('\n').Select(l => l.Replace("\t", "    ").TrimEnd()));
                continue;
            }
            EndCode();
            text = text.Trim();
            if (text.Length > 0)
            {
                output.Add(text);
            }
        }
        EndCode();
        return string.Join('\n', output);
    }

    /// <summary>Fonts with letters of one width, used for program code (Courier, Consolas, «… Mono»).</summary>
    public static bool IsMonospaceFont(string? name) =>
        name is not null
        && !name.Contains("Monotype", StringComparison.OrdinalIgnoreCase)
        && MonospaceFonts.Any(f => name.Contains(f, StringComparison.OrdinalIgnoreCase));

    private static readonly string[] MonospaceFonts =
    [
        "Courier", "Consolas", "Menlo", "Monaco", "Lucida Console", "LucidaConsole", "Typewriter", "Source Code",
        "SourceCode", "Cascadia", "Inconsolata", "Mono",
    ];
}

/// <summary>A line of extracted text. Code: a line of a program, kept as written; Lang: its language, if known.</summary>
internal readonly record struct TextLine(string Text, bool Code = false, string? Lang = null);
