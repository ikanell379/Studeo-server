namespace Studeo.Api.Models;

/// <summary>A folder of the user's library («Μάθημα»): PDFs go into it, each with the quizzes made from it.</summary>
public class Course
{
    public Guid Id { get; set; }

    /// <summary>The user who made it (Keycloak "sub"). Only they see it.</summary>
    public required string OwnerId { get; set; }

    public required string Name { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public List<Document> Documents { get; set; } = [];
}
