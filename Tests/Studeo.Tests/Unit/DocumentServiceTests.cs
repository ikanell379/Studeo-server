using Studeo.Api.Contracts;
using Studeo.Api.Models;
using Studeo.Api.Repositories;
using Studeo.Api.Services;

namespace Studeo.Tests.Unit;

public class DocumentServiceTests
{
    private sealed class FakeExtractor(PdfExtractionResult result) : IPdfTextExtractor
    {
        public PdfExtractionResult Extract(byte[] pdf, int maxPages) => result;
    }

    private sealed class FakeRepository : IDocumentRepository
    {
        public List<Document> Added { get; } = [];

        public Task AddAsync(Document document, CancellationToken ct)
        {
            Added.Add(document);
            return Task.CompletedTask;
        }

        public Task<Document?> GetAsync(Guid id, CancellationToken ct) =>
            Task.FromResult(Added.FirstOrDefault(d => d.Id == id));

        public Task<IReadOnlyList<DocumentPage>> GetPagesAsync(Guid documentId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<DocumentPage>>([]);

        public Task<IReadOnlyList<Section>> GetSectionsAsync(Guid documentId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Section>>(Added.Single(d => d.Id == documentId).Sections);

        public Task<Section?> GetSectionAsync(Guid sectionId, CancellationToken ct) => throw new NotSupportedException();

        public Task<IReadOnlyList<DocumentPage>> GetPagesAsync(Guid documentId, int fromPage, int toPage, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    private static readonly string TextPage = new('α', DocumentLimits.MinLettersPerPage);

    private static (DocumentService Service, FakeRepository Repo) Create(PdfExtractionResult extraction)
    {
        var repo = new FakeRepository();
        return (new DocumentService(new FakeExtractor(extraction), new Sectioner(), repo, TimeProvider.System), repo);
    }

    [Fact]
    public async Task Saves_document_with_numbered_pages()
    {
        var (service, repo) = Create(new PdfExtractionResult([TextPage, TextPage], null));

        var result = await service.UploadAsync(@"C:\fakepath\notes.pdf", [], CancellationToken.None);

        Assert.Null(result.ErrorCode);
        Assert.Equal("notes.pdf", result.Document!.FileName);
        Assert.Equal(2, result.Document.PageCount);
        var saved = Assert.Single(repo.Added);
        Assert.Equal([1, 2], saved.Pages.Select(p => p.PageNumber));
    }

    [Fact]
    public async Task Saves_sections_in_order_and_returns_them()
    {
        var page1 = "Κεφάλαιο 1: Αρχές\n" + TextPage;
        var page2 = "Κεφάλαιο 2: Εφαρμογές\n" + TextPage;
        var (service, _) = Create(new PdfExtractionResult([page1, page2], null));
        var uploaded = await service.UploadAsync("a.pdf", [], CancellationToken.None);

        var sections = await service.GetSectionsAsync(uploaded.Document!.Id, CancellationToken.None);

        Assert.Equal([(1, "Κεφάλαιο 1: Αρχές", 1, 1), (2, "Κεφάλαιο 2: Εφαρμογές", 2, 2)],
            sections!.Select(s => (s.Order, s.Title, s.StartPage, s.EndPage)));
    }

    [Fact]
    public async Task GetSections_of_unknown_document_returns_null()
    {
        var (service, _) = Create(new PdfExtractionResult([], null));

        Assert.Null(await service.GetSectionsAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task Returns_extractor_error_and_saves_nothing()
    {
        var (service, repo) = Create(PdfExtractionResult.Fail(ErrorCodes.TooManyPages));

        var result = await service.UploadAsync("a.pdf", [], CancellationToken.None);

        Assert.Equal(ErrorCodes.TooManyPages, result.ErrorCode);
        Assert.Empty(repo.Added);
    }

    [Fact]
    public async Task Scanned_pdf_without_text_returns_no_text()
    {
        var (service, repo) = Create(new PdfExtractionResult(["", " 12 ", ""], null));

        var result = await service.UploadAsync("scan.pdf", [], CancellationToken.None);

        Assert.Equal(ErrorCodes.NoText, result.ErrorCode);
        Assert.Empty(repo.Added);
    }

    [Theory]
    [InlineData(new[] { "" }, false)]
    [InlineData(new string[0], false)]
    [InlineData(new[] { "Σελίδα 1" }, false)]
    [InlineData(new[] { "Το κύτταρο είναι η μικρότερη μονάδα της ζωής" }, true)]
    public void HasEnoughText_uses_average_letters_per_page(string[] pages, bool expected)
    {
        Assert.Equal(expected, DocumentService.HasEnoughText(pages));
    }

    [Theory]
    [InlineData("notes.pdf", "notes.pdf")]
    [InlineData(@"C:\Users\me\notes.pdf", "notes.pdf")]
    [InlineData("../../etc/notes.pdf", "notes.pdf")]
    [InlineData("   ", "document.pdf")]
    public void SafeFileName_strips_paths(string input, string expected)
    {
        Assert.Equal(expected, DocumentService.SafeFileName(input));
    }

    [Fact]
    public void SafeFileName_keeps_the_end_of_very_long_names()
    {
        var name = new string('x', 300) + ".pdf";

        var safe = DocumentService.SafeFileName(name);

        Assert.Equal(260, safe.Length);
        Assert.EndsWith(".pdf", safe);
    }
}
