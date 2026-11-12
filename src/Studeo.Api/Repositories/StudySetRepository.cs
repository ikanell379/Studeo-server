using Microsoft.EntityFrameworkCore;
using Studeo.Api.Models;

namespace Studeo.Api.Repositories;

public class StudySetRepository(StudeoDbContext db) : IStudySetRepository
{
    public async Task AddAsync(StudySet studySet, CancellationToken ct)
    {
        db.StudySets.Add(studySet);
        await db.SaveChangesAsync(ct);
    }

    public Task<StudySet?> GetAsync(Guid id, CancellationToken ct) =>
        db.StudySets.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
}
