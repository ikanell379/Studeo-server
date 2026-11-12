using Studeo.Api.Contracts;

namespace Studeo.Api.Services;

public interface IStudySetService
{
    /// <summary>
    /// Generates study material for a section with the AI, validates it and stores it.
    /// <paramref name="progress"/> (optional) hears the real progress: stage and finished items. It is called
    /// synchronously, in order, from the generating code.
    /// </summary>
    Task<StudySetResult> CreateAsync(CreateStudySetRequest request, IProgress<GenerationProgress>? progress, CancellationToken ct);

    Task<StudySetResponse?> GetAsync(Guid id, CancellationToken ct);
}

/// <summary>Either StudySet (success) or ErrorCode.</summary>
public record StudySetResult(StudySetResponse? StudySet, string? ErrorCode);
