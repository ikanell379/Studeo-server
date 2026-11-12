namespace Studeo.Api.Contracts;

public static class DocumentLimits
{
    public const long MaxFileBytes = 20 * 1024 * 1024;
    public const int MaxPages = 50;

    /// <summary>Hard request limit; a bit above MaxFileBytes so the validator can return a clean error.</summary>
    public const long MaxRequestBytes = MaxFileBytes + 1024 * 1024;

    /// <summary>Below this average of letters per page the PDF is treated as scanned (no text).</summary>
    public const int MinLettersPerPage = 20;
}
