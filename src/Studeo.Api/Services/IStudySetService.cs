using Studeo.Api.Contracts;

namespace Studeo.Api.Services;

public interface IStudySetService
{
    /// <summary>
    /// Generates study material for a section with the AI, validates it and stores it.
    /// <paramref name="progress"/> (optional) hears the real progress: stage and finished items. It is called
    /// synchronously, in order, from the generating code. The section must be in one of <paramref name="ownerId"/>'s
    /// documents (otherwise not_found).
    /// </summary>
    Task<StudySetResult> CreateAsync(
        CreateStudySetRequest request, string ownerId, IProgress<GenerationProgress>? progress, CancellationToken ct);

    /// <summary>A study set made from the owner's documents, or null.</summary>
    Task<StudySetResponse?> GetAsync(Guid id, string ownerId, CancellationToken ct);

    /// <summary>Renames the owner's study set (name already validated); null if they have no such set.</summary>
    Task<StudySetResponse?> RenameAsync(Guid id, string ownerId, string name, CancellationToken ct);

    /// <summary>All study sets made from the owner's documents, newest first.</summary>
    Task<IReadOnlyList<StudySetSummaryResponse>> ListAsync(string ownerId, CancellationToken ct);
}

/// <summary>Either StudySet (success) or ErrorCode.</summary>
public record StudySetResult(StudySetResponse? StudySet, string? ErrorCode);
