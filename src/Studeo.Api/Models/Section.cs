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

    /// <summary>
    /// The «Ολόκληρο το κείμενο» choice: all pages of the document. It comes first (Order 0) when the document
    /// also has smaller sections; a document that isn't split has only this one (Order 1).
    /// </summary>
    public bool IsWholeDocument { get; set; }

    public Document? Document { get; set; }
    public List<StudySet> StudySets { get; set; } = [];
}
