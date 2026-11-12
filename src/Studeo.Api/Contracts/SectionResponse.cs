namespace Studeo.Api.Contracts;

/// <summary>
/// A part of a document to make material from. IsWholeDocument: «Ολόκληρο το κείμενο», all pages.
/// UnreadablePages: its pages whose text can't be read (a bad scan); material is made from the others only.
/// </summary>
public record SectionResponse(
    Guid Id, int Order, string Title, int StartPage, int EndPage, int WordCount, bool IsWholeDocument,
    IReadOnlyList<int> UnreadablePages);
