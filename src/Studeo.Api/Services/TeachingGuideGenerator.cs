using System.Text.Json;
using FluentValidation;
using Studeo.Api.Contracts;
using Studeo.Api.Prompts;
using Studeo.Api.Validators;

namespace Studeo.Api.Services;

/// <summary>Writes the teacher's guide for a section with the AI: one call plus one retry if the output fails the check.</summary>
public interface ITeachingGuideGenerator
{
    /// <param name="sectionText">The section text with page markers ("[σελ. N]").</param>
    Task<TeachingGuideResult> GenerateAsync(
        string sectionTitle, string sectionText, int words, string difficulty, int startPage, int endPage, MaterialSubject subject, CancellationToken ct);
}

/// <summary>Either Guide (success) or ErrorCode.</summary>
public record TeachingGuideResult(TeachingGuide? Guide, string? ErrorCode);

public class TeachingGuideGenerator(
    IAiClient ai,
    IValidator<GeneratedTeachingGuide> validator,
    ILogger<TeachingGuideGenerator> logger) : ITeachingGuideGenerator
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<TeachingGuideResult> GenerateAsync(
        string sectionTitle, string sectionText, int words, string difficulty, int startPage, int endPage, MaterialSubject subject, CancellationToken ct)
    {
        var user = TeachingGuidePrompt.User(sectionTitle, sectionText, difficulty, TeachingGuidePrompt.KeyPointsFor(words), subject);
        var schema = TeachingGuidePrompt.OutputSchema();
        IReadOnlyList<string> errors = [];
        for (var attempt = 1; attempt <= StudySetService.MaxAttempts; attempt++)
        {
            var prompt = attempt == 1 ? user : user + "\n" + TeachingGuidePrompt.RetryNote(errors);
            var result = await ai.CompleteJsonAsync(TeachingGuidePrompt.System, prompt, schema, null, ct);
            if (result.ErrorCode is not null && result.ErrorCode != ErrorCodes.AiInvalidOutput)
            {
                return new TeachingGuideResult(null, result.ErrorCode);
            }

            TeachingGuide? guide;
            (guide, errors) = result.Json is null
                ? (null, ["The response was empty or cut off."])
                : await ParseAndValidateAsync(result.Json, startPage, endPage, ct);
            if (guide is not null)
            {
                return new TeachingGuideResult(guide, null);
            }
            logger.LogWarning("Teaching guide attempt {Attempt} for «{Section}» failed validation: {Errors}",
                attempt, sectionTitle, string.Join("; ", errors));
        }
        return new TeachingGuideResult(null, ErrorCodes.AiInvalidOutput);
    }

    private async Task<(TeachingGuide?, IReadOnlyList<string>)> ParseAndValidateAsync(
        string json, int startPage, int endPage, CancellationToken ct)
    {
        TeachingGuide? guide;
        try
        {
            guide = JsonSerializer.Deserialize<TeachingGuide>(json, Json);
        }
        catch (JsonException ex)
        {
            return (null, [$"Invalid JSON: {ex.Message}"]);
        }
        if (guide is null)
        {
            return (null, ["Invalid JSON: null."]);
        }

        // A key point a page off the section is not worth a retry: keep it inside.
        guide = guide with
        {
            KeyPoints = guide.KeyPoints.Select(p => p with { Page = Math.Clamp(p.Page, startPage, endPage) }).ToList(),
        };
        var validation = await validator.ValidateAsync(new GeneratedTeachingGuide(guide, startPage, endPage), ct);
        return validation.IsValid
            ? (guide, [])
            : (null, validation.Errors.Select(e => $"{e.PropertyName}: {e.ErrorMessage}").ToList());
    }
}
