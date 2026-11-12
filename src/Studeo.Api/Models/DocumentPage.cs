namespace Studeo.Api.Models;

public class DocumentPage
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public int PageNumber { get; set; }
    public required string Text { get; set; }

    public Document? Document { get; set; }
}
