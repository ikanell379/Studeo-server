using System.Text.Json.Serialization;

namespace Studeo.Api.Contracts;

/// <summary>
/// Real progress of AI generation. Stage: "reading" (the AI reads the section, nothing written yet),
/// "writing" (Done of Total items finished), "saving" (checking and storing). Attempt is 2 when the first
/// answer failed validation and the AI writes it again. Percent: 0–100 for the progress bar.
/// </summary>
public record GenerationProgress(string Stage, int Attempt, int Done, int Total, int Percent)
{
    public const string Reading = "reading";
    public const string Writing = "writing";
    public const string Saving = "saving";

    public static GenerationProgress For(string stage, int attempt, int done, int total)
    {
        done = Math.Clamp(done, 0, total);
        var percent = stage switch
        {
            Reading => 3,
            Writing => 5 + (int)Math.Round(88.0 * done / Math.Max(total, 1)),
            _ => 95, // saving
        };
        return new GenerationProgress(stage, attempt, done, total, percent);
    }
}

/// <summary>
/// One line of POST /api/study-sets/stream (NDJSON): Type "progress" (Progress set), then exactly one of
/// "done" (StudySet set) or "error" (Code set, an ErrorCodes value).
/// </summary>
public record GenerationEvent(
    string Type,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] GenerationProgress? Progress = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] StudySetResponse? StudySet = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Code = null)
{
    public static GenerationEvent Of(GenerationProgress progress) => new("progress", Progress: progress);
    public static GenerationEvent Done(StudySetResponse studySet) => new("done", StudySet: studySet);
    public static GenerationEvent Error(string code) => new("error", Code: code);
}
