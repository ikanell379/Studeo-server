namespace Studeo.Api.Contracts;

/// <summary>
/// POST /api/documents/text: text the user typed or pasted, stored like an uploaded .txt file.
/// Title is the name it gets in «Τα αρχεία μου»; without one, its first line is used.
/// </summary>
public record CreateTextDocumentRequest(string? Title, string? Text, Guid? CourseId);

public static class TextDocumentLimits
{
    /// <summary>About 50 pages of text (the page limit is checked again after splitting).</summary>
    public const int MaxTextLength = 300_000;

    public const int MaxTitleLength = 100;

    /// <summary>The name when there is no title and no usable first line.</summary>
    public const string DefaultTitle = "Κείμενο";
}
