using Studeo.Api.Contracts;
using Studeo.Api.Models;

namespace Studeo.Api.Repositories;

public interface IDocumentRepository
{
    Task AddAsync(Document document, CancellationToken ct);

    /// <summary>The owner's document without its pages, or null (also when it belongs to someone else).</summary>
    Task<Document?> GetAsync(Guid id, string ownerId, CancellationToken ct);

    /// <summary>The owner's documents, newest first, with how much material was made from each.</summary>
    Task<IReadOnlyList<DocumentSummaryResponse>> ListAsync(string ownerId, CancellationToken ct);

    Task<IReadOnlyList<DocumentPage>> GetPagesAsync(Guid documentId, CancellationToken ct);

    Task<IReadOnlyList<Section>> GetSectionsAsync(Guid documentId, CancellationToken ct);

    /// <summary>A section of one of the owner's documents, or null.</summary>
    Task<Section?> GetSectionAsync(Guid sectionId, string ownerId, CancellationToken ct);

    /// <summary>Pages fromPage..toPage (inclusive) of a document, in order.</summary>
    Task<IReadOnlyList<DocumentPage>> GetPagesAsync(Guid documentId, int fromPage, int toPage, CancellationToken ct);
}
