using Studeo.Api.Contracts;

namespace Studeo.Api.Services;

public interface IStudySetService
{
    /// <summary>Generates study material for a section with the AI, validates it and stores it.</summary>
    Task<StudySetResult> CreateAsync(CreateStudySetRequest request, CancellationToken ct);

    Task<StudySetResponse?> GetAsync(Guid id, CancellationToken ct);
}

/// <summary>Either StudySet (success) or ErrorCode.</summary>
public record StudySetResult(StudySetResponse? StudySet, string? ErrorCode);
