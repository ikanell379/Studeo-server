using System.Text.Json;
using FluentValidation;
using Studeo.Api.Contracts;
using Studeo.Api.Prompts;
using Studeo.Api.Validators;

namespace Studeo.Api.Services;

/// <summary>Writes the student's lesson for a section with the AI: one call plus one retry if the output fails the check.</summary>
public interface IStudentLessonGenerator
{
    /// <param name="sectionText">The section text with page markers ("[σελ. N]").</param>
    Task<StudentLessonResult> GenerateAsync(
        string sectionTitle, string sectionText, int words, string difficulty, int startPage, int endPage, MaterialSubject subject, CancellationToken ct);
}

/// <summary>Either Lesson (success) or ErrorCode.</summary>
public record StudentLessonResult(StudentLesson? Lesson, string? ErrorCode);

public class StudentLessonGenerator(
    IAiClient ai,
    IValidator<GeneratedStudentLesson> validator,
    ILogger<StudentLessonGenerator> logger) : IStudentLessonGenerator
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<StudentLessonResult> GenerateAsync(
        string sectionTitle, string sectionText, int words, string difficulty, int startPage, int endPage, MaterialSubject subject, CancellationToken ct)
    {
        var user = StudentLessonPrompt.User(sectionTitle, sectionText, difficulty,
            StudentLessonPrompt.StepsFor(words), StudentLessonPrompt.FlashcardsFor(words), subject);
        var schema = StudentLessonPrompt.OutputSchema();
        IReadOnlyList<string> errors = [];
        for (var attempt = 1; attempt <= StudySetService.MaxAttempts; attempt++)
        {
            var prompt = attempt == 1 ? user : user + "\n" + StudentLessonPrompt.RetryNote(errors);
            var result = await ai.CompleteJsonAsync(StudentLessonPrompt.System, prompt, schema, null, ct);
            if (result.ErrorCode is not null && result.ErrorCode != ErrorCodes.AiInvalidOutput)
            {
                return new StudentLessonResult(null, result.ErrorCode);
            }

            StudentLesson? lesson;
            (lesson, errors) = result.Json is null
                ? (null, ["The response was empty or cut off."])
                : await ParseAndValidateAsync(result.Json, startPage, endPage, ct);
            if (lesson is not null)
            {
                return new StudentLessonResult(lesson, null);
            }
            logger.LogWarning("Lesson attempt {Attempt} for «{Section}» failed validation: {Errors}",
                attempt, sectionTitle, string.Join("; ", errors));
        }
        return new StudentLessonResult(null, ErrorCodes.AiInvalidOutput);
    }

    private async Task<(StudentLesson?, IReadOnlyList<string>)> ParseAndValidateAsync(
        string json, int startPage, int endPage, CancellationToken ct)
    {
        StudentLesson? lesson;
        try
        {
            lesson = JsonSerializer.Deserialize<StudentLesson>(json, Json);
        }
        catch (JsonException ex)
        {
            return (null, [$"Invalid JSON: {ex.Message}"]);
        }
        if (lesson is null)
        {
            return (null, ["Invalid JSON: null."]);
        }

        // A page a little off the section is not worth a retry; extra cards are dropped rather than failing.
        lesson = lesson with
        {
            // The schema always has «worked» and «practice»; empty ones (no calculation in this step) are none.
            Steps = lesson.Steps.Select(s => s with
            {
                Page = Math.Clamp(s.Page, startPage, endPage),
                Worked = string.IsNullOrWhiteSpace(s.Worked?.Problem) ? null : s.Worked,
                Practice = string.IsNullOrWhiteSpace(s.Practice?.Problem) ? null : s.Practice,
            }).ToList(),
            Formulas = lesson.Formulas
                .Where(f => !string.IsNullOrWhiteSpace(f.Formula))
                .Select(f => f with { Page = Math.Clamp(f.Page, startPage, endPage), Formula = f.Formula.Trim().Trim('$').Trim() })
                .Take(StudentLessonLimits.MaxFormulas)
                .ToList(),
            Flashcards = lesson.Flashcards
                .Select(c => c with { Page = Math.Clamp(c.Page, startPage, endPage) })
                .Take(StudentLessonLimits.MaxFlashcards)
                .ToList(),
        };
        var validation = await validator.ValidateAsync(new GeneratedStudentLesson(lesson, startPage, endPage), ct);
        return validation.IsValid
            ? (lesson, [])
            : (null, validation.Errors.Select(e => $"{e.PropertyName}: {e.ErrorMessage}").ToList());
    }
}
