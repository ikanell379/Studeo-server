using System.Text;
using System.Text.Json;
using FluentValidation;
using Studeo.Api.Contracts;
using Studeo.Api.Prompts;
using Studeo.Api.Repositories;
using Studeo.Api.Validators;
using StudySetEntity = Studeo.Api.Models.StudySet;

namespace Studeo.Api.Services;

public class StudySetService(
    IDocumentRepository documents,
    IStudySetRepository studySets,
    IAiClient ai,
    IValidator<GeneratedStudySet> outputValidator,
    TimeProvider timeProvider,
    ILogger<StudySetService> logger) : IStudySetService
{
    /// <summary>One generation plus one retry if the output fails validation (SPEC flow step 7).</summary>
    internal const int MaxAttempts = 2;

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<StudySetResult> CreateAsync(CreateStudySetRequest request, CancellationToken ct)
    {
        var section = await documents.GetSectionAsync(request.SectionId, ct);
        if (section is null)
        {
            return new StudySetResult(null, ErrorCodes.NotFound);
        }

        var pages = await documents.GetPagesAsync(section.DocumentId, section.StartPage, section.EndPage, ct);
        var user = GenerateStudySetPrompt.User(section.Title, SectionText(pages.Select(p => (p.PageNumber, p.Text))),
            request.Difficulty, request.Counts);
        var schema = GenerateStudySetPrompt.OutputSchema();

        StudySet? valid = null;
        IReadOnlyList<string> errors = [];
        for (var attempt = 1; attempt <= MaxAttempts && valid is null; attempt++)
        {
            var prompt = attempt == 1 ? user : user + "\n" + GenerateStudySetPrompt.RetryNote(errors);
            var result = await ai.CompleteJsonAsync(GenerateStudySetPrompt.System, prompt, schema, ct);
            if (result.ErrorCode is not null && result.ErrorCode != ErrorCodes.AiInvalidOutput)
            {
                return new StudySetResult(null, result.ErrorCode);
            }

            (valid, errors) = result.Json is null
                ? (null, ["The response was empty or cut off."])
                : await ParseAndValidateAsync(result.Json, request.Counts, section.StartPage, section.EndPage, ct);

            if (valid is null)
            {
                logger.LogWarning("Study set attempt {Attempt} for section {SectionId} failed validation: {Errors}",
                    attempt, section.Id, string.Join("; ", errors));
            }
        }

        if (valid is null)
        {
            return new StudySetResult(null, ErrorCodes.AiInvalidOutput);
        }

        var content = WithIds(valid with { SectionTitle = section.Title, Difficulty = request.Difficulty });
        var now = timeProvider.GetUtcNow();
        var entity = new StudySetEntity
        {
            Id = Guid.NewGuid(),
            SectionId = section.Id,
            Difficulty = request.Difficulty,
            Content = JsonSerializer.Serialize(content, Json),
            CreatedAt = now,
            UpdatedAt = now,
        };
        await studySets.AddAsync(entity, ct);
        return new StudySetResult(ToResponse(entity, content), null);
    }

    public async Task<StudySetResponse?> GetAsync(Guid id, CancellationToken ct)
    {
        var entity = await studySets.GetAsync(id, ct);
        return entity is null ? null : ToResponse(entity, JsonSerializer.Deserialize<StudySet>(entity.Content, Json)!);
    }

    /// <summary>Section text with page markers, e.g. "[σελ. 12]", as the prompt expects.</summary>
    internal static string SectionText(IEnumerable<(int Page, string Text)> pages)
    {
        var sb = new StringBuilder();
        foreach (var (page, text) in pages)
        {
            sb.AppendLine($"[σελ. {page}]");
            sb.AppendLine(text.Trim());
            sb.AppendLine();
        }
        return sb.ToString().TrimEnd();
    }

    private async Task<(StudySet?, IReadOnlyList<string>)> ParseAndValidateAsync(
        string json, QuestionCounts counts, int startPage, int endPage, CancellationToken ct)
    {
        StudySet? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<StudySet>(json, Json);
        }
        catch (JsonException ex)
        {
            return (null, [$"Invalid JSON: {ex.Message}"]);
        }
        if (parsed is null)
        {
            return (null, ["Invalid JSON: null."]);
        }

        var validation = await outputValidator.ValidateAsync(new GeneratedStudySet(parsed, counts, startPage, endPage), ct);
        return validation.IsValid
            ? (parsed, [])
            : (null, validation.Errors.Select(e => $"{e.PropertyName}: {e.ErrorMessage}").ToList());
    }

    /// <summary>Ids are assigned by the server, never by the model.</summary>
    internal static StudySet WithIds(StudySet s) => s with
    {
        Theory = s.Theory.Select(x => x with { Id = NewId() }).ToList(),
        OpenQuestions = s.OpenQuestions.Select(x => x with { Id = NewId() }).ToList(),
        TrueFalse = s.TrueFalse.Select(x => x with { Id = NewId() }).ToList(),
        Matching = s.Matching.Select(x => x with { Id = NewId() }).ToList(),
        MultipleChoice = s.MultipleChoice.Select(x => x with { Id = NewId() }).ToList(),
        FillBlanks = s.FillBlanks.Select(x => x with { Id = NewId() }).ToList(),
    };

    private static string NewId() => Guid.NewGuid().ToString("N");

    private static StudySetResponse ToResponse(StudySetEntity e, StudySet content) =>
        new(e.Id, e.SectionId, e.CreatedAt, e.UpdatedAt, content);
}
