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
    public const string InvalidRequest = "invalid_request";
    public const string AiNotConfigured = "ai_not_configured";
    public const string AiFailed = "ai_failed";
    public const string AiBusy = "ai_busy";
    public const string AiInvalidOutput = "ai_invalid_output";
    public const string AiRefused = "ai_refused";
}
