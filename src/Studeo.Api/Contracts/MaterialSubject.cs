namespace Studeo.Api.Contracts;

/// <summary>
/// What kind of material a section is: it decides how formulas and code are written, and (later) which exercises
/// fit. Found from the text (Services/SubjectDetector) and shown in the settings, where the user can change it.
/// Language: the programming language, for "programming" (null when it can't be told).
/// </summary>
public record MaterialSubject(string Subject, string? Language = null)
{
    public static readonly MaterialSubject General = new(Subjects.General);
}

public static class Subjects
{
    /// <summary>Text: history, literature, biology…</summary>
    public const string General = "general";

    /// <summary>Mathematics, physics, chemistry: formulas, units, calculations.</summary>
    public const string Science = "science";

    /// <summary>Programming and algorithms: code.</summary>
    public const string Programming = "programming";

    public static readonly string[] All = [General, Science, Programming];
}

public static class ProgrammingLanguages
{
    /// <summary>ΓΛΩΣΣΑ: the pseudo-language of Greek upper secondary computer science (ΑΕΠΠ).</summary>
    public const string Glossa = "glossa";

    /// <summary>The fence names used in the text (```python), and what the UI highlights.</summary>
    public static readonly string[] All = ["python", "c", "cpp", "java", "csharp", "javascript", "sql", Glossa];
}
