using Studeo.Api.Contracts;
using Studeo.Api.Models;
using StudySet = Studeo.Api.Models.StudySet;

namespace Studeo.Api.Repositories;

public interface IStudySetRepository
{
    Task AddAsync(StudySet studySet, CancellationToken ct);

    /// <summary>A study set made from one of the owner's documents (with its Section), or null.</summary>
    Task<StudySet?> GetAsync(Guid id, string ownerId, CancellationToken ct);

    /// <summary>Renames the owner's study set; returns it (with its Section), or null if they have no such set.</summary>
    Task<StudySet?> RenameAsync(Guid id, string ownerId, string name, DateTimeOffset now, CancellationToken ct);

    /// <summary>All study sets made from the owner's documents, newest first.</summary>
    Task<IReadOnlyList<StudySetSummaryResponse>> ListAsync(string ownerId, CancellationToken ct);
}
