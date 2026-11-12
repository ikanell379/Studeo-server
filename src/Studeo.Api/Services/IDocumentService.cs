using Studeo.Api.Contracts;

namespace Studeo.Api.Services;

public interface IDocumentService
{
    /// <summary>Extracts and stores a PDF for <paramref name="ownerId"/> (the uploading user).</summary>
    Task<UploadDocumentResult> UploadAsync(string ownerId, string fileName, byte[] content, CancellationToken ct);

    /// <summary>The owner's document, or null if it doesn't exist or belongs to someone else.</summary>
    Task<DocumentResponse?> GetAsync(Guid id, string ownerId, CancellationToken ct);

    /// <summary>The owner's documents, newest first.</summary>
    Task<IReadOnlyList<DocumentSummaryResponse>> ListAsync(string ownerId, CancellationToken ct);

    /// <summary>The document's sections in order, or null if the owner has no such document.</summary>
    Task<IReadOnlyList<SectionResponse>?> GetSectionsAsync(Guid documentId, string ownerId, CancellationToken ct);
}

/// <summary>Either Document (success) or ErrorCode.</summary>
public record UploadDocumentResult(DocumentResponse? Document, string? ErrorCode);
