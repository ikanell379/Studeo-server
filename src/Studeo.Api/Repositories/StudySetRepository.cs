using Microsoft.EntityFrameworkCore;
using Studeo.Api.Contracts;
using Studeo.Api.Models;
using StudySet = Studeo.Api.Models.StudySet;

namespace Studeo.Api.Repositories;

public class StudySetRepository(StudeoDbContext db) : IStudySetRepository
{
    public async Task AddAsync(StudySet studySet, CancellationToken ct)
    {
        db.StudySets.Add(studySet);
        await db.SaveChangesAsync(ct);
    }

    public Task<StudySet?> GetAsync(Guid id, string ownerId, CancellationToken ct) =>
        db.StudySets.AsNoTracking().Include(s => s.Section)
            .FirstOrDefaultAsync(s => s.Id == id && s.Section!.Document!.OwnerId == ownerId, ct);

    public async Task<StudySet?> RenameAsync(Guid id, string ownerId, string name, DateTimeOffset now, CancellationToken ct)
    {
        var studySet = await db.StudySets.Include(s => s.Section)
            .FirstOrDefaultAsync(s => s.Id == id && s.Section!.Document!.OwnerId == ownerId, ct);
        if (studySet is null) return null;

        studySet.Name = name;
        studySet.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return studySet;
    }

    public async Task<IReadOnlyList<StudySetSummaryResponse>> ListAsync(string ownerId, CancellationToken ct) =>
        await db.StudySets.AsNoTracking()
            .Where(s => s.Section!.Document!.OwnerId == ownerId)
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => new StudySetSummaryResponse(
                s.Id,
                s.Name,
                s.Section!.DocumentId,
                s.Section.Document!.FileName,
                s.SectionId,
                s.Section.Title,
                s.Difficulty,
                s.CreatedAt))
            .ToListAsync(ct);
}
