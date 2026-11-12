using Studeo.Api.Contracts;
using Studeo.Api.Models;
using Studeo.Api.Repositories;

namespace Studeo.Api.Services;

public class DocumentService(
    IPdfTextExtractor extractor,
    ISectioner sectioner,
    IDocumentRepository repository,
    TimeProvider timeProvider) : IDocumentService
{
    private const int MaxFileNameLength = 260;

    public async Task<UploadDocumentResult> UploadAsync(string fileName, byte[] content, CancellationToken ct)
    {
        var extraction = extractor.Extract(content, DocumentLimits.MaxPages);
        if (extraction.ErrorCode is not null)
        {
            return new UploadDocumentResult(null, extraction.ErrorCode);
        }

        if (!HasEnoughText(extraction.Pages))
        {
            return new UploadDocumentResult(null, ErrorCodes.NoText);
        }

        var document = new Document
        {
            Id = Guid.NewGuid(),
            FileName = SafeFileName(fileName),
            PageCount = extraction.Pages.Count,
            UploadedAt = timeProvider.GetUtcNow(),
            Pages = extraction.Pages
                .Select((text, i) => new DocumentPage { Id = Guid.NewGuid(), PageNumber = i + 1, Text = text })
                .ToList(),
            Sections = sectioner.Split(extraction.Pages)
                .Select((s, i) => new Section
                {
                    Id = Guid.NewGuid(),
                    Order = i + 1,
                    Title = s.Title,
                    StartPage = s.StartPage,
                    EndPage = s.EndPage,
                    WordCount = s.WordCount,
                })
                .ToList(),
        };

        await repository.AddAsync(document, ct);
        return new UploadDocumentResult(ToResponse(document), null);
    }

    public async Task<DocumentResponse?> GetAsync(Guid id, CancellationToken ct)
    {
        var document = await repository.GetAsync(id, ct);
        return document is null ? null : ToResponse(document);
    }

    public async Task<IReadOnlyList<SectionResponse>?> GetSectionsAsync(Guid documentId, CancellationToken ct)
    {
        if (await repository.GetAsync(documentId, ct) is null)
        {
            return null;
        }
        var sections = await repository.GetSectionsAsync(documentId, ct);
        return sections
            .Select(s => new SectionResponse(s.Id, s.Order, s.Title, s.StartPage, s.EndPage, s.WordCount))
            .ToList();
    }

    /// <summary>A scanned PDF has (almost) no extractable letters.</summary>
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

    private static DocumentResponse ToResponse(Document d) => new(d.Id, d.FileName, d.PageCount, d.UploadedAt);
}
