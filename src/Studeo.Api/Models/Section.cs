namespace Studeo.Api.Models;

public class Section
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public int Order { get; set; }
    public required string Title { get; set; }
    public int StartPage { get; set; }
    public int EndPage { get; set; }
    public int WordCount { get; set; }

    public Document? Document { get; set; }
    public List<StudySet> StudySets { get; set; } = [];
}
