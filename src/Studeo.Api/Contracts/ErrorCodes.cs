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
    public const string UnsupportedFile = "unsupported_file";
    /// <summary>The file is damaged, password-protected, or not what its extension says.</summary>
    public const string InvalidFile = "invalid_file";
    public const string TooManyPages = "too_many_pages";
    public const string NoText = "no_text";
    public const string NotFound = "not_found";

    /// <summary>
    /// The user already uploaded this exact PDF. The 409 response has the stored one as "document"
    /// (a DocumentResponse), so the UI can tell them where it is and open it.
    /// </summary>
    public const string DuplicateDocument = "duplicate_document";

    /// <summary>The user already has a course with this name.</summary>
    public const string CourseExists = "course_exists";
    public const string InvalidRequest = "invalid_request";
    public const string AiNotConfigured = "ai_not_configured";
    public const string AiFailed = "ai_failed";
    public const string AiBusy = "ai_busy";

    /// <summary>The AI service's daily limit is used up (e.g. Gemini free tier); it resets the next day.</summary>
    public const string AiQuota = "ai_quota";
    public const string AiInvalidOutput = "ai_invalid_output";
    public const string AiRefused = "ai_refused";

    /// <summary>Signed in, but the account has no Studeo role (teacher / student).</summary>
    public const string NoRole = "no_role";

    /// <summary>The auth provider (Keycloak) could not be reached.</summary>
    public const string AuthFailed = "auth_failed";

    public const string InvalidEmail = "invalid_email";
    public const string WeakPassword = "weak_password";
    public const string EmailTaken = "email_taken";

    /// <summary>Wrong email or password (also: account locked after too many tries).</summary>
    public const string InvalidCredentials = "invalid_credentials";

    /// <summary>No session, or it has ended: sign in again.</summary>
    public const string SessionExpired = "session_expired";
}
