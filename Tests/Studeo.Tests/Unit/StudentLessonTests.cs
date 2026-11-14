using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Studeo.Api.Contracts;
using Studeo.Api.Prompts;
using Studeo.Api.Services;
using Studeo.Api.Validators;

namespace Studeo.Tests.Unit;

public class StudentLessonTests
{
    private readonly GeneratedStudentLessonValidator _validator = new();
    private readonly FakeAiClient _ai = new();

    private StudentLessonGenerator Generator() =>
        new(_ai, new GeneratedStudentLessonValidator(), NullLogger<StudentLessonGenerator>.Instance);

    private static AiResult Json(StudentLesson lesson) => AiResult.Ok(JsonSerializer.Serialize(lesson, StudySetJson.Options));

    [Fact]
    public void A_complete_lesson_passes()
    {
        Assert.True(_validator.Validate(new GeneratedStudentLesson(TestStudySets.ValidLesson(3), 3, 4)).IsValid);
    }

    [Fact]
    public void Missing_parts_too_few_cards_and_pages_outside_the_section_fail()
    {
        var lesson = TestStudySets.ValidLesson(3) with
        {
            Intro = "",
            Flashcards = [new Flashcard { Front = "Όρος", Back = "Ορισμός", Page = 3 }],
            Steps =
            [
                .. TestStudySets.ValidLesson(3).Steps,
                new LessonPart { Title = "Χωρίς έλεγχο", Explanation = "Κάτι.", Page = 3 },
            ],
        };
        var errors = _validator.Validate(new GeneratedStudentLesson(lesson, 5, 6)).Errors.Select(e => e.PropertyName).ToList();

        Assert.Contains("intro", errors);
        Assert.Contains("flashcards count", errors);
        Assert.Contains("page", errors);
        Assert.Contains(errors, e => e.StartsWith("steps[3]"));
    }

    [Fact]
    public async Task Generator_retries_once_with_the_reasons_then_returns_the_lesson()
    {
        _ai.Lesson = Sequence(Json(TestStudySets.ValidLesson(3) with { Summary = [] }), Json(TestStudySets.ValidLesson(3)));

        var result = await Generator().GenerateAsync("Φωτοσύνθεση", "[σελ. 3]\nΚείμενο", 300, "easy", 3, 4, MaterialSubject.General, CancellationToken.None);

        Assert.Null(result.ErrorCode);
        Assert.Equal(6, result.Lesson!.Flashcards.Count);
        Assert.Equal(2, _ai.LessonCalls.Count);
        Assert.Contains("summary count", _ai.LessonCalls[1]);
    }

    [Fact]
    public async Task Generator_gives_up_after_two_bad_answers_and_passes_service_errors_on()
    {
        _ai.Lesson = () => AiResult.Ok("{}");
        var bad = await Generator().GenerateAsync("Τ", "κείμενο", 100, "easy", 1, 1, MaterialSubject.General, CancellationToken.None);
        _ai.Lesson = () => AiResult.Fail(ErrorCodes.AiBusy);
        var busy = await Generator().GenerateAsync("Τ", "κείμενο", 100, "easy", 1, 1, MaterialSubject.General, CancellationToken.None);

        Assert.Equal(ErrorCodes.AiInvalidOutput, bad.ErrorCode);
        Assert.Equal(ErrorCodes.AiBusy, busy.ErrorCode);
    }

    [Fact]
    public async Task Pages_a_little_off_are_kept_inside_and_extra_cards_dropped()
    {
        var tooMany = Enumerable.Range(1, StudentLessonLimits.MaxFlashcards + 5)
            .Select(i => new Flashcard { Front = $"Όρος {i}", Back = "Ορισμός.", Page = 9 }).ToList();
        _ai.Lesson = () => Json(TestStudySets.ValidLesson(9) with { Flashcards = tooMany });

        var result = await Generator().GenerateAsync("Τ", "κείμενο", 100, "easy", 3, 4, MaterialSubject.General, CancellationToken.None);

        Assert.All(result.Lesson!.Steps, s => Assert.Equal(4, s.Page));
        Assert.Equal(StudentLessonLimits.MaxFlashcards, result.Lesson.Flashcards.Count);
    }

    [Fact]
    public void Prompt_has_the_text_and_amounts_and_schema_asks_for_every_part()
    {
        var user = StudentLessonPrompt.User("Φωτοσύνθεση", "[σελ. 3]\nΧλωροπλάστες", "easy", 4, 12);
        var schema = StudentLessonPrompt.OutputSchema();

        Assert.Contains("Χλωροπλάστες", user);
        Assert.Contains("περίπου 4", user);
        Assert.Contains("περίπου 12", user);
        Assert.Equal(8, schema["required"]!.AsArray().Count);
        foreach (var words in new[] { 50, 1000, 30_000 })
        {
            Assert.InRange(StudentLessonPrompt.StepsFor(words), StudentLessonLimits.MinSteps, StudentLessonLimits.MaxSteps);
            Assert.InRange(StudentLessonPrompt.FlashcardsFor(words), StudentLessonLimits.MinFlashcards, StudentLessonLimits.MaxFlashcards);
        }
    }

    private static Func<AiResult> Sequence(params AiResult[] results)
    {
        var queue = new Queue<AiResult>(results);
        return () => queue.Dequeue();
    }
}
