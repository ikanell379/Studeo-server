using Microsoft.EntityFrameworkCore;
using Studeo.Api.Contracts;
using Studeo.Api.Models;

namespace Studeo.Api.Repositories;

public class CourseRepository(StudeoDbContext db) : ICourseRepository
{
    public async Task AddAsync(Course course, CancellationToken ct)
    {
        db.Courses.Add(course);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<CourseResponse>> ListAsync(string ownerId, CancellationToken ct) =>
        await db.Courses.AsNoTracking()
            .Where(c => c.OwnerId == ownerId)
            .OrderBy(c => c.Name)
            .Select(c => new CourseResponse(c.Id, c.Name, c.CreatedAt, c.Documents.Count))
            .ToListAsync(ct);

    public Task<Course?> GetAsync(Guid id, string ownerId, CancellationToken ct) =>
        db.Courses.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id && c.OwnerId == ownerId, ct);

    public Task<bool> NameTakenAsync(string ownerId, string name, Guid? exceptId, CancellationToken ct)
    {
        var lower = name.ToLower();
        return db.Courses.AnyAsync(c => c.OwnerId == ownerId && c.Name.ToLower() == lower && c.Id != exceptId, ct);
    }

    public async Task<Course?> RenameAsync(Guid id, string ownerId, string name, CancellationToken ct)
    {
        var course = await db.Courses.FirstOrDefaultAsync(c => c.Id == id && c.OwnerId == ownerId, ct);
        if (course is null) return null;

        course.Name = name;
        await db.SaveChangesAsync(ct);
        return course;
    }

    // ON DELETE CASCADE: its PDFs go too, and with them their pages, sections and quizzes.
    public async Task<bool> DeleteAsync(Guid id, string ownerId, CancellationToken ct) =>
        await db.Courses.Where(c => c.Id == id && c.OwnerId == ownerId).ExecuteDeleteAsync(ct) > 0;

    public Task<int> CountDocumentsAsync(Guid id, CancellationToken ct) =>
        db.Documents.CountAsync(d => d.CourseId == id, ct);
}
