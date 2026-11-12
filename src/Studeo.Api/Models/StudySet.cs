namespace Studeo.Api.Models;

public class StudySet
{
    public Guid Id { get; set; }
    public Guid SectionId { get; set; }
    public required string Difficulty { get; set; }

    /// <summary>The generated/edited material as JSON (StudySet schema), stored as jsonb.</summary>
    public required string Content { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Section? Section { get; set; }
}
