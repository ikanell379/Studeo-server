namespace Studeo.Api.Contracts;

/// <summary>How many of each kind. Problems: science material only (0 for any other kind).</summary>
public record QuestionCounts(int Theory, int OpenQuestions, int TrueFalse, int Matching, int MultipleChoice, int FillBlanks, int Problems = 0);

/// <summary>
/// Generate study material for a section. Difficulty: "easy" | "medium" | "hard".
/// Mode: "custom" (default) uses Counts; "full" covers the whole section with theory and every exercise type,
/// the amounts set by the server from the section's length (Counts is then ignored and may be null).
/// Name: what the user calls this material (optional; the section title when empty).
/// Subject / Language: the kind of material, as the user set it in the settings (optional; found from the text when
/// empty). Language is used for "programming" only.
/// </summary>
public record CreateStudySetRequest(
    Guid SectionId, string Difficulty, QuestionCounts? Counts, string? Mode = null, string? Name = null,
    string? Subject = null, string? Language = null)
{
    public bool IsFull => Mode == StudySetModes.Full;
}

public static class StudySetModes
{
    public const string Custom = "custom";
    public const string Full = "full";
    public static readonly string[] All = [Custom, Full];
}

/// <summary>Study material with its name and the PDF (DocumentId) and section it was made from.</summary>
public record StudySetResponse(
    Guid Id, Guid SectionId, Guid DocumentId, string Name, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, StudySet Content);

/// <summary>PATCH /api/study-sets/{id}: give the material a new name.</summary>
public record RenameStudySetRequest(string Name);

public static class StudySetLimits
{
    public static readonly string[] Difficulties = ["easy", "medium", "hard"];
    public const int MaxPerType = 15;
    public const int OptionsPerMultipleChoice = 4;
    public const int MinMatchingPairs = 4;
    public const int MaxMatchingPairs = 6;
    public const string Blank = "____";
    public const int MaxNameLength = 120;
}
