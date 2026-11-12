using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Studeo.Api.Contracts;
using Studeo.Api.Prompts;
using Studeo.Api.Services;
using Studeo.Api.Validators;

namespace Studeo.Tests.Unit;

public class TeachingGuideTests
{
    private readonly GeneratedTeachingGuideValidator _validator = new();
    private readonly FakeAiClient _ai = new();

    private TeachingGuideGenerator Generator() =>
        new(_ai, new GeneratedTeachingGuideValidator(), NullLogger<TeachingGuideGenerator>.Instance);

    private static AiResult Json(TeachingGuide guide) => AiResult.Ok(JsonSerializer.Serialize(guide, StudySetJson.Options));

    [Fact]
    public void A_complete_guide_passes()
    {
        Assert.True(_validator.Validate(new GeneratedTeachingGuide(TestStudySets.ValidGuide(3), 3, 4)).IsValid);
    }

    [Fact]
    public void Missing_parts_and_pages_outside_the_section_fail()
    {
        var guide = TestStudySets.ValidGuide(3) with
        {
            Overview = "",
            LessonPlan = [new LessonStep { Title = "Μόνο ένα", Minutes = 45, Description = "Όλο το μάθημα." }],
        };
        var errors = _validator.Validate(new GeneratedTeachingGuide(guide, 5, 6)).Errors.Select(e => e.PropertyName).ToList();

        Assert.Contains("overview", errors);
        Assert.Contains("lessonPlan count", errors);
        Assert.Contains("keyPoints page", errors);
    }

    [Fact]
    public async Task Generator_retries_once_with_the_reasons_then_returns_the_guide()
    {
        _ai.Guide = Sequence(Json(TestStudySets.ValidGuide(3) with { Objectives = [] }), Json(TestStudySets.ValidGuide(3)));

        var result = await Generator().GenerateAsync("Φωτοσύνθεση", "[σελ. 3]\nΚείμενο", 300, "easy", 3, 4, CancellationToken.None);

        Assert.Null(result.ErrorCode);
        Assert.Equal(3, result.Guide!.KeyPoints.Count);
        Assert.Equal(2, _ai.GuideCalls.Count);
        Assert.Contains("objectives count", _ai.GuideCalls[1]);
    }

    [Fact]
    public async Task Generator_gives_up_after_two_bad_answers_and_passes_service_errors_on()
    {
        _ai.Guide = () => AiResult.Ok("{}");
        var bad = await Generator().GenerateAsync("Τ", "κείμενο", 100, "easy", 1, 1, CancellationToken.None);
        _ai.Guide = () => AiResult.Fail(ErrorCodes.AiBusy);
        var busy = await Generator().GenerateAsync("Τ", "κείμενο", 100, "easy", 1, 1, CancellationToken.None);

        Assert.Equal(ErrorCodes.AiInvalidOutput, bad.ErrorCode);
        Assert.Equal(ErrorCodes.AiBusy, busy.ErrorCode);
    }

    [Fact]
    public async Task Key_point_pages_a_little_off_are_kept_inside_the_section()
    {
        _ai.Guide = () => Json(TestStudySets.ValidGuide(9));

        var result = await Generator().GenerateAsync("Τ", "κείμενο", 100, "easy", 3, 4, CancellationToken.None);

        Assert.All(result.Guide!.KeyPoints, p => Assert.Equal(4, p.Page));
    }

    [Fact]
    public void Prompt_has_the_text_and_schema_asks_for_every_part()
    {
        var user = TeachingGuidePrompt.User("Φωτοσύνθεση", "[σελ. 3]\nΧλωροπλάστες", "hard", 6);
        var schema = TeachingGuidePrompt.OutputSchema();

        Assert.Contains("Χλωροπλάστες", user);
        Assert.Contains("περίπου 6", user);
        Assert.Equal(7, schema["required"]!.AsArray().Count);
        Assert.InRange(TeachingGuidePrompt.KeyPointsFor(100), TeachingGuideLimits.MinKeyPoints, TeachingGuideLimits.MaxKeyPoints);
        Assert.InRange(TeachingGuidePrompt.KeyPointsFor(20_000), TeachingGuideLimits.MinKeyPoints, TeachingGuideLimits.MaxKeyPoints);
    }

    private static Func<AiResult> Sequence(params AiResult[] results)
    {
        var queue = new Queue<AiResult>(results);
        return () => queue.Dequeue();
    }
}
