namespace Studeo.Api.Contracts;

/// <summary>
/// A part of a document to make material from. IsWholeDocument: «Ολόκληρο το κείμενο», all pages.
/// UnreadablePages: its pages whose text can't be read (a bad scan); material is made from the others only.
/// Subject: what kind of material its text is (general / science / programming, with the language), found from the
/// text; the settings start from it and the user can change it.
/// </summary>
public record SectionResponse(
    Guid Id, int Order, string Title, int StartPage, int EndPage, int WordCount, bool IsWholeDocument,
    IReadOnlyList<int> UnreadablePages, MaterialSubject Subject);
