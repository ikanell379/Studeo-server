namespace Studeo.Api.Contracts;

public record QuestionCounts(int Theory, int OpenQuestions, int TrueFalse, int Matching, int MultipleChoice, int FillBlanks);

/// <summary>Generate study material for a section. Difficulty: "easy" | "medium" | "hard".</summary>
public record CreateStudySetRequest(Guid SectionId, string Difficulty, QuestionCounts Counts);

public record StudySetResponse(Guid Id, Guid SectionId, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, StudySet Content);

public static class StudySetLimits
{
    public static readonly string[] Difficulties = ["easy", "medium", "hard"];
    public const int MaxPerType = 15;
    public const int OptionsPerMultipleChoice = 4;
    public const int MinMatchingPairs = 4;
    public const int MaxMatchingPairs = 6;
    public const string Blank = "____";
}
