using Studeo.Api.Contracts;
using Studeo.Api.Models;
using Studeo.Api.Repositories;

namespace Studeo.Api.Services;

public class DocumentService(
    IDocumentTextExtractor extractor,
    ISectioner sectioner,
    IDocumentRepository repository,
    ICourseRepository courses,
    TimeProvider timeProvider) : IDocumentService
{
    private const int MaxFileNameLength = 260;

    public async Task<UploadDocumentResult> UploadAsync(
        string ownerId, string fileName, byte[] content, Guid? courseId, CancellationToken ct)
    {
        if (courseId is { } id && await courses.GetAsync(id, ownerId, ct) is null)
        {
            return new UploadDocumentResult(null, ErrorCodes.NotFound);
        }

        // A file the user already has: tell them instead of storing it twice.
        // First the very same file (cheap, before extracting), then the same text (re-saved PDF, or one stored
        // before file hashes existed).
        var hash = ContentHash(content);
        if (await repository.FindByHashAsync(ownerId, hash, ct) is { } sameFile)
        {
            return new UploadDocumentResult(null, ErrorCodes.DuplicateDocument, ToResponse(sameFile));
        }

        var extraction = extractor.Extract(fileName, content, DocumentLimits.MaxPages);
        if (extraction.ErrorCode is not null)
        {
            return new UploadDocumentResult(null, extraction.ErrorCode);
        }

        if (!HasEnoughText(extraction.Pages))
        {
            return new UploadDocumentResult(null, ErrorCodes.NoText);
        }

        var textHash = DocumentHashes.TextHash(extraction.Pages);
        if (await repository.FindByTextHashAsync(ownerId, textHash, ct) is { } sameText)
        {
            return new UploadDocumentResult(null, ErrorCodes.DuplicateDocument, ToResponse(sameText));
        }

        var document = new Document
        {
            Id = Guid.NewGuid(),
            OwnerId = ownerId,
            CourseId = courseId,
            ContentHash = hash,
            TextHash = textHash,
            FileName = SafeFileName(fileName),
            PageCount = extraction.Pages.Count,
            UploadedAt = timeProvider.GetUtcNow(),
            Pages = extraction.Pages
                .Select((text, i) => new DocumentPage { Id = Guid.NewGuid(), PageNumber = i + 1, Text = text })
                .ToList(),
            Sections = WithWholeDocument(sectioner.Split(extraction.Pages, extraction.Headings), extraction.Pages),
        };

        await repository.AddAsync(document, ct);
        return new UploadDocumentResult(ToResponse(document), null);
    }

    public async Task<DocumentResponse?> GetAsync(Guid id, string ownerId, CancellationToken ct)
    {
        var document = await repository.GetAsync(id, ownerId, ct);
        return document is null ? null : ToResponse(document);
    }

    public Task<IReadOnlyList<DocumentSummaryResponse>> ListAsync(string ownerId, CancellationToken ct) =>
        repository.ListAsync(ownerId, ct);

    public async Task<IReadOnlyList<SectionResponse>?> GetSectionsAsync(Guid documentId, string ownerId, CancellationToken ct)
    {
        if (await repository.GetAsync(documentId, ownerId, ct) is null)
        {
            return null;
        }
        var sections = await repository.GetSectionsAsync(documentId, ct);
        var pages = await repository.GetPagesAsync(documentId, ct);
        var unreadable = PageQuality.UnreadablePages(pages.OrderBy(p => p.PageNumber).Select(p => p.Text));
        return sections
            .Select(s => new SectionResponse(s.Id, s.Order, s.Title, s.StartPage, s.EndPage, s.WordCount, s.IsWholeDocument,
                unreadable.Where(p => p >= s.StartPage && p <= s.EndPage).ToList()))
            .ToList();
    }

    /// <summary>
    /// The sections in order, plus «Ολόκληρο το κείμενο» first (Order 0) so the user can also study everything.
    /// When the only section already spans every page, it is the whole document itself (no duplicate).
    /// </summary>
    internal static List<Section> WithWholeDocument(IReadOnlyList<SectionDraft> drafts, IReadOnlyList<string> pages)
    {
        var sections = drafts
            .Select((s, i) => new Section
            {
                Id = Guid.NewGuid(),
                Order = i + 1,
                Title = s.Title,
                StartPage = s.StartPage,
                EndPage = s.EndPage,
                WordCount = s.WordCount,
            })
            .ToList();

        if (sections is [var only] && only.StartPage == 1 && only.EndPage == pages.Count)
        {
            only.IsWholeDocument = true;
            only.Title = Sectioner.WholeDocumentTitle;
            return sections;
        }

        sections.Insert(0, new Section
        {
            Id = Guid.NewGuid(),
            Order = 0,
            Title = Sectioner.WholeDocumentTitle,
            StartPage = 1,
            EndPage = pages.Count,
            WordCount = pages.Sum(Sectioner.CountWords),
            IsWholeDocument = true,
        });
        return sections;
    }

    /// <summary>A scanned PDF (or slides of pictures) has (almost) no extractable letters.</summary>
    internal static bool HasEnoughText(IReadOnlyList<string> pages)
    {
        if (pages.Count == 0)
        {
            return false;
        }
        var letters = pages.Sum(p => p.Count(char.IsLetter));
        return letters >= pages.Count * DocumentLimits.MinLettersPerPage;
    }

    internal static string SafeFileName(string fileName)
    {
        var name = Path.GetFileName(fileName.Replace('\\', '/')).Trim();
        if (name.Length == 0)
        {
            name = "document.pdf";
        }
        return name.Length <= MaxFileNameLength ? name : name[^MaxFileNameLength..];
    }

    public async Task<UploadDocumentResult> MoveAsync(Guid id, string ownerId, Guid? courseId, CancellationToken ct)
    {
        if (courseId is { } cid && await courses.GetAsync(cid, ownerId, ct) is null)
        {
            return new UploadDocumentResult(null, ErrorCodes.NotFound);
        }
        var document = await repository.SetCourseAsync(id, ownerId, courseId, ct);
        return document is null
            ? new UploadDocumentResult(null, ErrorCodes.NotFound)
            : new UploadDocumentResult(ToResponse(document), null);
    }

    public Task<bool> DeleteAsync(Guid id, string ownerId, CancellationToken ct) => repository.DeleteAsync(id, ownerId, ct);

    /// <summary>SHA-256 of the file, lower-case hex (64 characters).</summary>
    internal static string ContentHash(byte[] content) => DocumentHashes.ContentHash(content);

    private static DocumentResponse ToResponse(Document d) => new(d.Id, d.FileName, d.PageCount, d.UploadedAt, d.CourseId);
}
