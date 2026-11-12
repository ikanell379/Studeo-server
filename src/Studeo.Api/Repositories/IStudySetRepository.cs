using Studeo.Api.Models;

namespace Studeo.Api.Repositories;

public interface IStudySetRepository
{
    Task AddAsync(StudySet studySet, CancellationToken ct);

    Task<StudySet?> GetAsync(Guid id, CancellationToken ct);
}
