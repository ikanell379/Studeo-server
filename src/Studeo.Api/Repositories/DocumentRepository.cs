using Microsoft.EntityFrameworkCore;
using Studeo.Api.Models;

namespace Studeo.Api.Repositories;

public class DocumentRepository(StudeoDbContext db) : IDocumentRepository
{
    public async Task AddAsync(Document document, CancellationToken ct)
    {
        db.Documents.Add(document);
        await db.SaveChangesAsync(ct);
    }

    public Task<Document?> GetAsync(Guid id, CancellationToken ct) =>
        db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct);

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

    public Task<Section?> GetSectionAsync(Guid sectionId, CancellationToken ct) =>
        db.Sections.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sectionId, ct);

    public async Task<IReadOnlyList<DocumentPage>> GetPagesAsync(Guid documentId, int fromPage, int toPage, CancellationToken ct) =>
        await db.DocumentPages.AsNoTracking()
            .Where(p => p.DocumentId == documentId && p.PageNumber >= fromPage && p.PageNumber <= toPage)
            .OrderBy(p => p.PageNumber)
            .ToListAsync(ct);
}
