namespace Studeo.Api.Contracts;

/// <summary>
/// Machine-readable error codes returned in ProblemDetails as "code".
/// The UI maps them to Greek messages (Studeo-UI/src/lib/strings.ts).
/// </summary>
public static class ErrorCodes
{
    public const string FileMissing = "file_missing";
    public const string FileEmpty = "file_empty";
    public const string FileTooLarge = "file_too_large";
    public const string NotPdf = "not_pdf";
    public const string InvalidPdf = "invalid_pdf";
    public const string TooManyPages = "too_many_pages";
    public const string NoText = "no_text";
    public const string NotFound = "not_found";
}
