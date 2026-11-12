using Studeo.Api.Contracts;

namespace Studeo.Api.Services;

public interface IDocumentService
{
    Task<UploadDocumentResult> UploadAsync(string fileName, byte[] content, CancellationToken ct);

    Task<DocumentResponse?> GetAsync(Guid id, CancellationToken ct);

    /// <summary>The document's sections in order, or null if the document does not exist.</summary>
    Task<IReadOnlyList<SectionResponse>?> GetSectionsAsync(Guid documentId, CancellationToken ct);
}

/// <summary>Either Document (success) or ErrorCode.</summary>
public record UploadDocumentResult(DocumentResponse? Document, string? ErrorCode);
