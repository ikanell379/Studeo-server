using System.Security.Cryptography;
using System.Text;

namespace Studeo.Api.Services;

/// <summary>
/// Fingerprints that tell a PDF the user already uploaded. ContentHash: the file's bytes (the very same file).
/// TextHash: its extracted text, page by page (the same PDF saved again, and PDFs stored before ContentHash
/// existed, whose text is still in DocumentPages). Both are SHA-256, lower-case hex (64 characters).
/// </summary>
public static class DocumentHashes
{
    /// <summary>Between pages in <see cref="TextHash"/>; form feed, so page breaks count.</summary>
    public const char PageSeparator = '\f';

    public static string ContentHash(byte[] content) => Convert.ToHexStringLower(SHA256.HashData(content));

    public static string TextHash(IEnumerable<string> pages) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join(PageSeparator, pages))));

    /// <summary>
    /// The same as <see cref="TextHash"/>, computed in PostgreSQL from DocumentPages, for documents that have none.
    /// Used by the AddDocumentTextHash migration; a test checks that it matches the C# version.
    /// </summary>
    public const string BackfillTextHashSql = """
        UPDATE "Documents" AS d
        SET "TextHash" = encode(sha256(convert_to(p.text, 'UTF8')), 'hex')
        FROM (
            SELECT "DocumentId", string_agg("Text", E'\f' ORDER BY "PageNumber") AS text
            FROM "DocumentPages"
            GROUP BY "DocumentId"
        ) AS p
        WHERE p."DocumentId" = d."Id" AND d."TextHash" IS NULL;
        """;
}
