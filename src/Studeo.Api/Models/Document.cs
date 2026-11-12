namespace Studeo.Api.Models;

public class Document
{
    public Guid Id { get; set; }
    public required string FileName { get; set; }
    public int PageCount { get; set; }
    public DateTimeOffset UploadedAt { get; set; }

    public List<DocumentPage> Pages { get; set; } = [];
    public List<Section> Sections { get; set; } = [];
}
