using Studeo.Api.Models;

namespace Studeo.Api.Repositories;

public interface IDocumentRepository
{
    Task AddAsync(Document document, CancellationToken ct);

    /// <summary>Returns the document without its pages, or null.</summary>
    Task<Document?> GetAsync(Guid id, CancellationToken ct);

    Task<IReadOnlyList<DocumentPage>> GetPagesAsync(Guid documentId, CancellationToken ct);

    Task<IReadOnlyList<Section>> GetSectionsAsync(Guid documentId, CancellationToken ct);
}
