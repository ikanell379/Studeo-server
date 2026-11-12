using Microsoft.EntityFrameworkCore;
using Studeo.Api.Contracts;
using Studeo.Api.Models;

namespace Studeo.Api.Repositories;

public class DocumentRepository(StudeoDbContext db) : IDocumentRepository
{
    public async Task AddAsync(Document document, CancellationToken ct)
    {
        db.Documents.Add(document);
        await db.SaveChangesAsync(ct);
    }

    public Task<Document?> GetAsync(Guid id, string ownerId, CancellationToken ct) =>
        db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id && d.OwnerId == ownerId, ct);

    public async Task<IReadOnlyList<DocumentSummaryResponse>> ListAsync(string ownerId, CancellationToken ct) =>
        await db.Documents.AsNoTracking()
            .Where(d => d.OwnerId == ownerId)
            .OrderByDescending(d => d.UploadedAt)
            .Select(d => new DocumentSummaryResponse(
                d.Id, d.FileName, d.PageCount, d.UploadedAt, d.Sections.SelectMany(s => s.StudySets).Count(), d.CourseId))
            .ToListAsync(ct);

    public Task<Document?> FindByHashAsync(string ownerId, string contentHash, CancellationToken ct) =>
        db.Documents.AsNoTracking()
            .Where(d => d.OwnerId == ownerId && d.ContentHash == contentHash)
            .OrderBy(d => d.UploadedAt)
            .FirstOrDefaultAsync(ct);

    // The pages, sections and study sets go with it (foreign keys ON DELETE CASCADE).
    public async Task<bool> DeleteAsync(Guid id, string ownerId, CancellationToken ct) =>
        await db.Documents.Where(d => d.Id == id && d.OwnerId == ownerId).ExecuteDeleteAsync(ct) > 0;

    public Task<Document?> FindByTextHashAsync(string ownerId, string textHash, CancellationToken ct) =>
        db.Documents.AsNoTracking()
            .Where(d => d.OwnerId == ownerId && d.TextHash == textHash)
            .OrderBy(d => d.UploadedAt)
            .FirstOrDefaultAsync(ct);

    public async Task<Document?> SetCourseAsync(Guid id, string ownerId, Guid? courseId, CancellationToken ct)
    {
        var document = await db.Documents.FirstOrDefaultAsync(d => d.Id == id && d.OwnerId == ownerId, ct);
        if (document is null) return null;

        document.CourseId = courseId;
        await db.SaveChangesAsync(ct);
        return document;
    }

    public async Task<IReadOnlyList<DocumentPage>> GetPagesAsync(Guid documentId, CancellationToken ct) =>
        await db.DocumentPages.AsNoTracking()
            .Where(p => p.DocumentId == documentId)
            .OrderBy(p => p.PageNumber)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Section>> GetSectionsAsync(Guid documentId, CancellationToken ct) =>
        await db.Sections.AsNoTracking()
            .Where(s => s.DocumentId == documentId)
            .OrderBy(s => s.Order)
            .ToListAsync(ct);

    public Task<Section?> GetSectionAsync(Guid sectionId, string ownerId, CancellationToken ct) =>
        db.Sections.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sectionId && s.Document!.OwnerId == ownerId, ct);

    public async Task<IReadOnlyList<DocumentPage>> GetPagesAsync(Guid documentId, int fromPage, int toPage, CancellationToken ct) =>
        await db.DocumentPages.AsNoTracking()
            .Where(p => p.DocumentId == documentId && p.PageNumber >= fromPage && p.PageNumber <= toPage)
            .OrderBy(p => p.PageNumber)
            .ToListAsync(ct);
}
