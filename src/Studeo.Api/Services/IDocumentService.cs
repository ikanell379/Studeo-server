using Studeo.Api.Contracts;

namespace Studeo.Api.Services;

public interface IDocumentService
{
    /// <summary>
    /// Extracts and stores a PDF, Word document or presentation for <paramref name="ownerId"/> (the uploading user), optionally in one of their courses.
    /// If they uploaded the same file before, nothing is stored: ErrorCode duplicate_document with the stored one in Existing.
    /// </summary>
    Task<UploadDocumentResult> UploadAsync(string ownerId, string fileName, byte[] content, Guid? courseId, CancellationToken ct);

    /// <summary>
    /// Stores text the user typed or pasted like an uploaded .txt file named after <paramref name="title"/>
    /// (or the text's first line). Same results as <see cref="UploadAsync"/>.
    /// </summary>
    Task<UploadDocumentResult> CreateFromTextAsync(string ownerId, string? title, string text, Guid? courseId, CancellationToken ct);

    /// <summary>The owner's document, or null if it doesn't exist or belongs to someone else.</summary>
    Task<DocumentResponse?> GetAsync(Guid id, string ownerId, CancellationToken ct);

    /// <summary>The owner's documents, newest first.</summary>
    Task<IReadOnlyList<DocumentSummaryResponse>> ListAsync(string ownerId, CancellationToken ct);

    /// <summary>The document's sections in order, or null if the owner has no such document.</summary>
    Task<IReadOnlyList<SectionResponse>?> GetSectionsAsync(Guid documentId, string ownerId, CancellationToken ct);

    /// <summary>
    /// Files the owner's PDF (with its quizzes) under one of their courses, or out of any course with null.
    /// ErrorCode not_found when the document or the course isn't theirs.
    /// </summary>
    Task<UploadDocumentResult> MoveAsync(Guid id, string ownerId, Guid? courseId, CancellationToken ct);

    /// <summary>
    /// Deletes the owner's PDF for good, with its pages, sections and all quizzes made from it.
    /// False if the owner has no such document.
    /// </summary>
    Task<bool> DeleteAsync(Guid id, string ownerId, CancellationToken ct);
}

/// <summary>Either Document (success) or ErrorCode; for duplicate_document, Existing is the PDF uploaded before.</summary>
public record UploadDocumentResult(DocumentResponse? Document, string? ErrorCode, DocumentResponse? Existing = null);
