namespace Studeo.Api.Models;

public class Document
{
    public Guid Id { get; set; }

    /// <summary>The user who uploaded it (Keycloak "sub"). Only they see it and the material made from it.</summary>
    public required string OwnerId { get; set; }

    /// <summary>The course it is filed under; null = not in a course. Deleting the course deletes the PDF too.</summary>
    public Guid? CourseId { get; set; }

    public required string FileName { get; set; }
    public int PageCount { get; set; }
    public DateTimeOffset UploadedAt { get; set; }

    /// <summary>
    /// SHA-256 of the uploaded file (lower-case hex), to tell the user when they upload the same PDF again.
    /// Null for PDFs uploaded before it was recorded.
    /// </summary>
    public string? ContentHash { get; set; }

    /// <summary>
    /// SHA-256 of the extracted text, pages joined by a form feed (Services/DocumentHashes): finds the same PDF
    /// saved again, and PDFs from before ContentHash (filled in from their pages by a migration).
    /// </summary>
    public string? TextHash { get; set; }

    public Course? Course { get; set; }
    public List<DocumentPage> Pages { get; set; } = [];
    public List<Section> Sections { get; set; } = [];
}
