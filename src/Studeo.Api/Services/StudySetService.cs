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

    public async Task<StudySetResult> CreateAsync(
        CreateStudySetRequest request, string ownerId, IProgress<GenerationProgress>? progress, CancellationToken ct)
    {
        var section = await documents.GetSectionAsync(request.SectionId, ownerId, ct);
        if (section is null)
        {
            return new StudySetResult(null, ErrorCodes.NotFound);
        }

        // Pages whose text can't be read (a bad scan) would only feed the model garbage: leave them out.
        var allPages = await documents.GetPagesAsync(section.DocumentId, section.StartPage, section.EndPage, ct);
        var unreadable = allPages.Where(p => PageQuality.IsUnreadable(p.Text)).ToList();
        var pages = allPages.Except(unreadable).ToList();
        if (pages.Count == 0)
        {
            return new StudySetResult(null, ErrorCodes.NoText);
        }

        // "full": the amounts follow the length of the readable text; otherwise what the user chose.
        var readableWords = Math.Max(0, section.WordCount - unreadable.Sum(p => Sectioner.CountWords(p.Text)));
        var counts = request.IsFull ? FullCoverage.CountsFor(readableWords) : request.Counts!;
        var total = TotalItems(counts);
        var parts = Parts(counts);

        // One bar for all parts: the items done in each part added up. A part may start over (a broken connection,
        // or a second attempt after a failed check); the bar must not jump back for that, so each part counts
        // with the furthest point it reached. Parts report from different threads.
        var partDone = new int[parts.Count];
        var attemptShown = 1;
        var gate = new object();
        void Report(int part, int attempt, int done, bool saving = false)
        {
            lock (gate)
            {
                if (part >= 0) partDone[part] = Math.Max(partDone[part], done);
                attemptShown = Math.Max(attemptShown, attempt);
                var sum = partDone.Sum();
                var stage = saving ? GenerationProgress.Saving
                    : sum == 0 ? GenerationProgress.Reading
                    : GenerationProgress.Writing;
                progress?.Report(GenerationProgress.For(stage, attemptShown, saving ? total : sum, total));
            }
        }

        var text = SectionText(pages.Select(p => (p.PageNumber, p.Text)));
        var started = timeProvider.GetTimestamp();

        // Large requests are written in parallel parts (theory / written exercises / quick exercises): the time is
        // that of the largest part instead of all of them one after the other.
        using var stopOthers = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var results = await Task.WhenAll(parts.Select((partCounts, i) => GeneratePartAsync(
            section.Id, section.Title, text, request.Difficulty, partCounts, request.IsFull, isPart: parts.Count > 1,
            section.StartPage, section.EndPage, (attempt, done) => Report(i, attempt, done), stopOthers)));

        logger.LogInformation("Study set for section {SectionId}: {Items} items in {Parts} part(s), {Seconds:0.0}s.",
            section.Id, total, parts.Count, timeProvider.GetElapsedTime(started).TotalSeconds);

        // The user left: nothing to save.
        ct.ThrowIfCancellationRequested();

        // A real error first (not a part that was stopped because another one failed), then a failed check.
        if (results.FirstOrDefault(r => r.ErrorCode is not null && r.ErrorCode != StoppedCode) is { } failed)
        {
            return new StudySetResult(null, failed.ErrorCode);
        }
        if (results.Any(r => r.StudySet is null))
        {
            return new StudySetResult(null, ErrorCodes.AiFailed);
        }

        var valid = Merge(results.Select(r => r.StudySet!));
        Report(-1, 1, total, saving: true);

        var content = WithIds(valid with { SectionTitle = section.Title, Difficulty = request.Difficulty });
        var now = timeProvider.GetUtcNow();
        var entity = new StudySetEntity
        {
            Id = Guid.NewGuid(),
            SectionId = section.Id,
            Name = NameOrDefault(request.Name, section.Title),
            Difficulty = request.Difficulty,
            Content = JsonSerializer.Serialize(content, Json),
            CreatedAt = now,
            UpdatedAt = now,
        };
        await studySets.AddAsync(entity, ct);
        return new StudySetResult(ToResponse(entity, section.DocumentId, content), null);
    }

    public async Task<StudySetResponse?> GetAsync(Guid id, string ownerId, CancellationToken ct)
    {
        var entity = await studySets.GetAsync(id, ownerId, ct);
        return entity is null ? null : ToResponse(entity);
    }

    public async Task<StudySetResponse?> RenameAsync(Guid id, string ownerId, string name, CancellationToken ct)
    {
        var entity = await studySets.RenameAsync(id, ownerId, name.Trim(), timeProvider.GetUtcNow(), ct);
        return entity is null ? null : ToResponse(entity);
    }

    /// <summary>The user's name, or the section title (cut to the limit) when they gave none.</summary>
    internal static string NameOrDefault(string? name, string sectionTitle)
    {
        var chosen = string.IsNullOrWhiteSpace(name) ? sectionTitle.Trim() : name.Trim();
        return chosen.Length <= StudySetLimits.MaxNameLength ? chosen : chosen[..StudySetLimits.MaxNameLength];
    }

    public Task<IReadOnlyList<StudySetSummaryResponse>> ListAsync(string ownerId, CancellationToken ct) =>
        studySets.ListAsync(ownerId, ct);

    /// <summary>From this many items on, the material is written in parallel parts; smaller requests are one call.</summary>
    internal const int MinItemsToSplit = 13;

    /// <summary>Marks a part that was stopped because another part failed; never returned to the caller.</summary>
    private const string StoppedCode = "stopped";

    /// <summary>
    /// What each parallel call writes: theory; open questions, true/false and matching; multiple choice and
    /// fill-in-the-blanks. Empty parts are dropped; a small request (or one with a single kind of item) is one part.
    /// </summary>
    internal static IReadOnlyList<QuestionCounts> Parts(QuestionCounts c)
    {
        if (TotalItems(c) < MinItemsToSplit)
        {
            return [c];
        }
        QuestionCounts[] parts =
        [
            new(c.Theory, 0, 0, 0, 0, 0),
            new(0, c.OpenQuestions, c.TrueFalse, c.Matching, 0, 0),
            new(0, 0, 0, 0, c.MultipleChoice, c.FillBlanks),
        ];
        var nonEmpty = parts.Where(p => TotalItems(p) > 0).ToList();
        return nonEmpty.Count > 1 ? nonEmpty : [c];
    }

    private sealed record PartResult(StudySet? StudySet, string? ErrorCode);

    /// <summary>
    /// Writes one part: one call plus one retry if its output fails the check. A real error (the service failed,
    /// refused…) stops the other parts too, since the material can't be completed without this one.
    /// </summary>
    private async Task<PartResult> GeneratePartAsync(
        Guid sectionId, string sectionTitle, string text, string difficulty, QuestionCounts counts, bool full, bool isPart,
        int startPage, int endPage, Action<int, int> onProgress, CancellationTokenSource stopOthers)
    {
        var ct = stopOthers.Token;
        var user = GenerateStudySetPrompt.User(sectionTitle, text, difficulty, counts, full, isPart);
        var schema = GenerateStudySetPrompt.OutputSchema();
        IReadOnlyList<string> errors = [];
        try
        {
            for (var attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                onProgress(attempt, 0);
                var counter = new StudySetItemCounter();
                var thisAttempt = attempt;
                counter.Changed += done => onProgress(thisAttempt, done);

                var prompt = attempt == 1 ? user : user + "\n" + GenerateStudySetPrompt.RetryNote(errors);
                var result = await ai.CompleteJsonAsync(GenerateStudySetPrompt.System, prompt, schema, counter, ct);
                if (result.ErrorCode is not null && result.ErrorCode != ErrorCodes.AiInvalidOutput)
                {
                    await stopOthers.CancelAsync();
                    return new PartResult(null, result.ErrorCode);
                }

                // Full coverage: the amounts are only an estimate from the length, so a few items short is fine.
                // Amounts the user chose: exact on the first try; the retry may be a little short rather than fail.
                var min = full || attempt == MaxAttempts ? Tolerated(counts) : counts;
                StudySet? valid;
                (valid, errors) = result.Json is null
                    ? (null, ["The response was empty or cut off."])
                    : await ParseAndValidateAsync(result.Json, counts, min, startPage, endPage, ct);
                if (valid is not null)
                {
                    return new PartResult(valid, null);
                }
                logger.LogWarning("Study set attempt {Attempt} for section {SectionId} ({Counts}) failed validation: {Errors}",
                    attempt, sectionId, counts, string.Join("; ", errors));
            }
            await stopOthers.CancelAsync();
            return new PartResult(null, ErrorCodes.AiInvalidOutput);
        }
        catch (OperationCanceledException) when (stopOthers.IsCancellationRequested)
        {
            // Another part failed (or the user left): this one's result isn't needed.
            return new PartResult(null, StoppedCode);
        }
    }

    /// <summary>The parts' items put together, each kind in the order it was written.</summary>
    internal static StudySet Merge(IEnumerable<StudySet> parts)
    {
        var all = parts.ToList();
        return new StudySet
        {
            SectionTitle = all[0].SectionTitle,
            Difficulty = all[0].Difficulty,
            Theory = all.SelectMany(p => p.Theory).ToList(),
            OpenQuestions = all.SelectMany(p => p.OpenQuestions).ToList(),
            TrueFalse = all.SelectMany(p => p.TrueFalse).ToList(),
            Matching = all.SelectMany(p => p.Matching).ToList(),
            MultipleChoice = all.SelectMany(p => p.MultipleChoice).ToList(),
            FillBlanks = all.SelectMany(p => p.FillBlanks).ToList(),
        };
    }

    internal static int TotalItems(QuestionCounts c) =>
        c.Theory + c.OpenQuestions + c.TrueFalse + c.Matching + c.MultipleChoice + c.FillBlanks;

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

    /// <summary>The least accepted when a few items short is fine: 70% of each kind asked for (at least 1).</summary>
    internal const double MinShareOfAsked = 0.7;

    internal static QuestionCounts Tolerated(QuestionCounts c)
    {
        static int Min(int asked) => asked == 0 ? 0 : Math.Max(1, (int)Math.Ceiling(asked * MinShareOfAsked));
        return new QuestionCounts(Min(c.Theory), Min(c.OpenQuestions), Min(c.TrueFalse), Min(c.Matching),
            Min(c.MultipleChoice), Min(c.FillBlanks));
    }

    /// <summary>Extra items (more than asked for) are dropped instead of failing the check.</summary>
    internal static StudySet AtMost(StudySet s, QuestionCounts c) => s with
    {
        Theory = s.Theory.Take(c.Theory).ToList(),
        OpenQuestions = s.OpenQuestions.Take(c.OpenQuestions).ToList(),
        TrueFalse = s.TrueFalse.Take(c.TrueFalse).ToList(),
        Matching = s.Matching.Take(c.Matching).ToList(),
        MultipleChoice = s.MultipleChoice.Take(c.MultipleChoice).ToList(),
        FillBlanks = s.FillBlanks.Take(c.FillBlanks).ToList(),
    };

    private async Task<(StudySet?, IReadOnlyList<string>)> ParseAndValidateAsync(
        string json, QuestionCounts counts, QuestionCounts min, int startPage, int endPage, CancellationToken ct)
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

        parsed = AtMost(parsed, counts);
        var validation = await outputValidator.ValidateAsync(new GeneratedStudySet(parsed, counts, startPage, endPage, min), ct);
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

    private static StudySetResponse ToResponse(StudySetEntity e, Guid documentId, StudySet content) =>
        new(e.Id, e.SectionId, documentId, e.Name, e.CreatedAt, e.UpdatedAt, content);

    /// <summary>For a stored set loaded with its Section.</summary>
    private static StudySetResponse ToResponse(StudySetEntity e) =>
        ToResponse(e, e.Section!.DocumentId, JsonSerializer.Deserialize<StudySet>(e.Content, Json)!);
}
