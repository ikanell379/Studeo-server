namespace Studeo.Api.Contracts;

/// <summary>The kinds of file a user can upload, told apart by the file name's extension.</summary>
public enum DocumentKind
{
    Pdf,
    Docx,
    Pptx,

    /// <summary>PowerPoint 97–2003.</summary>
    Ppt,

    /// <summary>Plain text (.txt), or text typed in by the user.</summary>
    Text,

    /// <summary>Markdown (.md); read like a .txt file.</summary>
    Markdown,
}

public static class DocumentKinds
{
    /// <summary>The accepted extensions, lower case with the dot.</summary>
    public static readonly IReadOnlyList<string> Extensions = [".pdf", ".docx", ".pptx", ".ppt", ".txt", ".md", ".markdown"];

    /// <summary>The kind for the file name's extension (any case), or null if it isn't accepted.</summary>
    public static DocumentKind? FromFileName(string? fileName) =>
        Path.GetExtension(fileName ?? "").ToLowerInvariant() switch
        {
            ".pdf" => DocumentKind.Pdf,
            ".docx" => DocumentKind.Docx,
            ".pptx" => DocumentKind.Pptx,
            ".ppt" => DocumentKind.Ppt,
            ".txt" => DocumentKind.Text,
            ".md" or ".markdown" => DocumentKind.Markdown,
            _ => null,
        };
}
