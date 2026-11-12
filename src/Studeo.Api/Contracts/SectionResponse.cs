namespace Studeo.Api.Contracts;

/// <summary>A part of a document to make material from. IsWholeDocument: «Ολόκληρο το κείμενο», all pages.</summary>
public record SectionResponse(Guid Id, int Order, string Title, int StartPage, int EndPage, int WordCount, bool IsWholeDocument);
