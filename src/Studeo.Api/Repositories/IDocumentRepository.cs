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

    /// <summary>The owner's document with this file hash (the same PDF uploaded before), or null.</summary>
    Task<Document?> FindByHashAsync(string ownerId, string contentHash, CancellationToken ct);

    /// <summary>The owner's oldest document with this text hash (the same text uploaded before), or null.</summary>
    Task<Document?> FindByTextHashAsync(string ownerId, string textHash, CancellationToken ct);

    /// <summary>Files the owner's document under a course (null = none); null if there is no such document.</summary>
    Task<Document?> SetCourseAsync(Guid id, string ownerId, Guid? courseId, CancellationToken ct);

    /// <summary>
    /// Deletes the owner's document with everything made from it (pages, sections, quizzes: ON DELETE CASCADE).
    /// False if there was none.
    /// </summary>
    Task<bool> DeleteAsync(Guid id, string ownerId, CancellationToken ct);

    Task<IReadOnlyList<DocumentPage>> GetPagesAsync(Guid documentId, CancellationToken ct);

    Task<IReadOnlyList<Section>> GetSectionsAsync(Guid documentId, CancellationToken ct);

    /// <summary>A section of one of the owner's documents, or null.</summary>
    Task<Section?> GetSectionAsync(Guid sectionId, string ownerId, CancellationToken ct);

    /// <summary>Pages fromPage..toPage (inclusive) of a document, in order.</summary>
    Task<IReadOnlyList<DocumentPage>> GetPagesAsync(Guid documentId, int fromPage, int toPage, CancellationToken ct);
}
