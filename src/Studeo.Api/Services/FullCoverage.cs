using Studeo.Api.Contracts;

namespace Studeo.Api.Services;

/// <summary>
/// "Full" mode: theory and exercises for the whole section. The amount of each type grows with the length of
/// the section (more text → more concepts to cover), within the usual limits, so it is still an exact count
/// the output is validated against and progress is counted towards.
/// </summary>
public static class FullCoverage
{
    public static QuestionCounts CountsFor(int wordCount)
    {
        var words = Math.Max(wordCount, 0);
        int Per(int wordsPerItem, int min, int max) =>
            Math.Clamp((int)Math.Ceiling((double)words / wordsPerItem), min, Math.Min(max, StudySetLimits.MaxPerType));

        return new QuestionCounts(
            Theory: Per(120, 4, 15),
            OpenQuestions: Per(450, 2, 10),
            TrueFalse: Per(200, 4, 15),
            Matching: Per(1500, 1, 3),
            MultipleChoice: Per(200, 4, 15),
            FillBlanks: Per(300, 3, 10));
    }
}
