namespace Studeo.Api.Contracts;

/// <summary>
/// POST /api/study-sets/{id}/export (teachers): the test as a file to print or edit.
/// Format: "pdf" | "docx". Variant: "test" (worksheet with name/class/date fields, no answers) | "answers" (answer key).
/// Title: printed at the top (the material's name when empty). ShowPages: show the PDF page each question comes from.
/// Types: which question types go in (see <see cref="TestQuestionTypes"/>); null or empty = all.
/// </summary>
public record ExportTestRequest(
    string Format, string Variant, string? Title = null, bool ShowPages = false, IReadOnlyList<string>? Types = null);

public static class ExportFormats
{
    public const string Pdf = "pdf";
    public const string Docx = "docx";
    public static readonly string[] All = [Pdf, Docx];
}

public static class ExportVariants
{
    public const string Test = "test";
    public const string Answers = "answers";
    public static readonly string[] All = [Test, Answers];
}

/// <summary>The question types of a test, in the order they are printed.</summary>
public static class TestQuestionTypes
{
    public const string TrueFalse = "trueFalse";
    public const string MultipleChoice = "multipleChoice";
    public const string FillBlanks = "fillBlanks";
    public const string Matching = "matching";
    public const string OpenQuestions = "openQuestions";
    public static readonly string[] All = [TrueFalse, MultipleChoice, FillBlanks, Matching, OpenQuestions];
    public const int MaxTitleLength = 150;
}
