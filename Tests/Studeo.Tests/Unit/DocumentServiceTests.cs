using Studeo.Api.Contracts;
using Studeo.Api.Models;
using Studeo.Api.Repositories;
using Studeo.Api.Services;

namespace Studeo.Tests.Unit;

public class DocumentServiceTests
{
    private sealed class FakeExtractor(TextExtractionResult result) : IDocumentTextExtractor
    {
        public TextExtractionResult Extract(string fileName, byte[] content, int maxPages) => result;
    }

    private sealed class FakeRepository : IDocumentRepository
    {
        public List<Document> Added { get; } = [];

        public Task AddAsync(Document document, CancellationToken ct)
        {
            Added.Add(document);
            return Task.CompletedTask;
        }

        public Task<Document?> GetAsync(Guid id, string ownerId, CancellationToken ct) =>
            Task.FromResult(Added.FirstOrDefault(d => d.Id == id && d.OwnerId == ownerId));

        public Task<IReadOnlyList<DocumentSummaryResponse>> ListAsync(string ownerId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<Document?> FindByHashAsync(string ownerId, string contentHash, CancellationToken ct) =>
            Task.FromResult(Added.FirstOrDefault(d => d.OwnerId == ownerId && d.ContentHash == contentHash));

        public Task<Document?> FindByTextHashAsync(string ownerId, string textHash, CancellationToken ct) =>
            Task.FromResult(Added.FirstOrDefault(d => d.OwnerId == ownerId && d.TextHash == textHash));

        public Task<bool> DeleteAsync(Guid id, string ownerId, CancellationToken ct) =>
            Task.FromResult(Added.RemoveAll(d => d.Id == id && d.OwnerId == ownerId) > 0);

        public Task<Document?> SetCourseAsync(Guid id, string ownerId, Guid? courseId, CancellationToken ct)
        {
            var document = Added.FirstOrDefault(d => d.Id == id && d.OwnerId == ownerId);
            if (document is not null) document.CourseId = courseId;
            return Task.FromResult(document);
        }

        public Task<IReadOnlyList<DocumentPage>> GetPagesAsync(Guid documentId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<DocumentPage>>([]);

        public Task<IReadOnlyList<Section>> GetSectionsAsync(Guid documentId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Section>>(Added.Single(d => d.Id == documentId).Sections);

        public Task<Section?> GetSectionAsync(Guid sectionId, string ownerId, CancellationToken ct) => throw new NotSupportedException();

        public Task<IReadOnlyList<DocumentPage>> GetPagesAsync(Guid documentId, int fromPage, int toPage, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    /// <summary>One course, <see cref="OwnersCourse"/>, that belongs to <see cref="Owner"/>.</summary>
    private sealed class FakeCourseRepository : ICourseRepository
    {
        public Task<Course?> GetAsync(Guid id, string ownerId, CancellationToken ct) =>
            Task.FromResult(id == OwnersCourse && ownerId == Owner
                ? new Course { Id = id, OwnerId = ownerId, Name = "Βιολογία" }
                : null);

        public Task AddAsync(Course course, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<CourseResponse>> ListAsync(string ownerId, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> NameTakenAsync(string ownerId, string name, Guid? exceptId, CancellationToken ct) => throw new NotSupportedException();
        public Task<Course?> RenameAsync(Guid id, string ownerId, string name, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(Guid id, string ownerId, CancellationToken ct) => throw new NotSupportedException();
        public Task<int> CountDocumentsAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
    }

    private const string Owner = "owner-1";
    private static readonly Guid OwnersCourse = Guid.NewGuid();

    private static readonly string TextPage = new('α', DocumentLimits.MinLettersPerPage);

    private static (DocumentService Service, FakeRepository Repo) Create(TextExtractionResult extraction)
    {
        var repo = new FakeRepository();
        return (new DocumentService(new FakeExtractor(extraction), new Sectioner(), repo, new FakeCourseRepository(), TimeProvider.System), repo);
    }

    [Fact]
    public async Task Same_file_twice_is_a_duplicate_and_saved_once()
    {
        var (service, repo) = Create(new TextExtractionResult([TextPage], null));
        var first = await service.UploadAsync(Owner, "a.pdf", [1, 2, 3], null, CancellationToken.None);

        var again = await service.UploadAsync(Owner, "b.pdf", [1, 2, 3], OwnersCourse, CancellationToken.None);

        Assert.Equal(ErrorCodes.DuplicateDocument, again.ErrorCode);
        Assert.Equal(first.Document!.Id, again.Existing!.Id);
        Assert.Equal("a.pdf", again.Existing.FileName);
        Assert.Single(repo.Added);
    }

    /// <summary>Text that depends on the file's last byte, so different files have different text.</summary>
    private sealed class ByContentExtractor : IDocumentTextExtractor
    {
        public TextExtractionResult Extract(string fileName, byte[] content, int maxPages) => new([TextPage + content[^1], TextPage], null);
    }

    [Fact]
    public async Task A_different_pdf_or_another_owner_is_not_a_duplicate()
    {
        var repo = new FakeRepository();
        var service = new DocumentService(new ByContentExtractor(), new Sectioner(), repo, new FakeCourseRepository(), TimeProvider.System);
        await service.UploadAsync(Owner, "a.pdf", [1, 2, 3], null, CancellationToken.None);

        var other = await service.UploadAsync(Owner, "a.pdf", [1, 2, 4], null, CancellationToken.None);
        var otherOwner = await service.UploadAsync("owner-2", "a.pdf", [1, 2, 3], null, CancellationToken.None);

        Assert.Null(other.ErrorCode);
        Assert.Null(otherOwner.ErrorCode);
        Assert.Equal(3, repo.Added.Count);
        Assert.Equal(DocumentService.ContentHash([1, 2, 3]), repo.Added[0].ContentHash);
        Assert.Equal(DocumentHashes.TextHash([TextPage + 3, TextPage]), repo.Added[0].TextHash);
        Assert.All(repo.Added, d => Assert.Equal((64, 64), (d.ContentHash!.Length, d.TextHash!.Length)));
    }

    [Fact]
    public async Task Same_text_in_another_file_is_a_duplicate()
    {
        // e.g. the PDF saved again, or one stored before file hashes existed (only its text hash is known).
        var (service, repo) = Create(new TextExtractionResult([TextPage, TextPage], null));
        var first = await service.UploadAsync(Owner, "notes.pdf", [1, 2, 3], null, CancellationToken.None);
        repo.Added[0].ContentHash = null;

        var again = await service.UploadAsync(Owner, "notes-v2.pdf", [9, 9, 9], null, CancellationToken.None);

        Assert.Equal(ErrorCodes.DuplicateDocument, again.ErrorCode);
        Assert.Equal(first.Document!.Id, again.Existing!.Id);
        Assert.Single(repo.Added);
    }

    [Fact]
    public void Text_hash_depends_on_page_breaks()
    {
        Assert.NotEqual(DocumentHashes.TextHash(["ab", "c"]), DocumentHashes.TextHash(["a", "bc"]));
        Assert.Equal(DocumentHashes.TextHash(["α", "β"]), DocumentHashes.TextHash(["α", "β"]));
    }

    [Fact]
    public async Task Upload_into_own_course_only()
    {
        var (service, repo) = Create(new TextExtractionResult([TextPage], null));

        var inCourse = await service.UploadAsync(Owner, "a.pdf", [1], OwnersCourse, CancellationToken.None);
        var notMine = await service.UploadAsync(Owner, "b.pdf", [2], Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(OwnersCourse, inCourse.Document!.CourseId);
        Assert.Equal(ErrorCodes.NotFound, notMine.ErrorCode);
        Assert.Single(repo.Added);
    }

    [Fact]
    public async Task Move_files_the_pdf_under_a_course_and_back_out()
    {
        var (service, _) = Create(new TextExtractionResult([TextPage], null));
        var uploaded = await service.UploadAsync(Owner, "a.pdf", [1], null, CancellationToken.None);

        var moved = await service.MoveAsync(uploaded.Document!.Id, Owner, OwnersCourse, CancellationToken.None);
        var outOfCourse = await service.MoveAsync(uploaded.Document.Id, Owner, null, CancellationToken.None);

        Assert.Equal(OwnersCourse, moved.Document!.CourseId);
        Assert.Null(outOfCourse.Document!.CourseId);
        Assert.Equal(ErrorCodes.NotFound,
            (await service.MoveAsync(uploaded.Document.Id, "someone-else", null, CancellationToken.None)).ErrorCode);
    }

    [Fact]
    public async Task Saves_document_with_numbered_pages()
    {
        var (service, repo) = Create(new TextExtractionResult([TextPage, TextPage], null));

        var result = await service.UploadAsync(Owner, @"C:\fakepath\notes.pdf", [], null, CancellationToken.None);

        Assert.Null(result.ErrorCode);
        Assert.Equal("notes.pdf", result.Document!.FileName);
        Assert.Equal(2, result.Document.PageCount);
        var saved = Assert.Single(repo.Added);
        Assert.Equal(Owner, saved.OwnerId);
        Assert.Equal([1, 2], saved.Pages.Select(p => p.PageNumber));
    }

    [Fact]
    public async Task Someone_elses_document_is_not_found()
    {
        var (service, _) = Create(new TextExtractionResult([TextPage], null));
        var uploaded = await service.UploadAsync(Owner, "a.pdf", [], null, CancellationToken.None);

        Assert.Null(await service.GetAsync(uploaded.Document!.Id, "someone-else", CancellationToken.None));
        Assert.Null(await service.GetSectionsAsync(uploaded.Document.Id, "someone-else", CancellationToken.None));
    }

    [Fact]
    public async Task Saves_sections_in_order_and_returns_them()
    {
        var page1 = "Κεφάλαιο 1: Αρχές\n" + TextPage;
        var page2 = "Κεφάλαιο 2: Εφαρμογές\n" + TextPage;
        var (service, _) = Create(new TextExtractionResult([page1, page2], null));
        var uploaded = await service.UploadAsync(Owner, "a.pdf", [], null, CancellationToken.None);

        var sections = await service.GetSectionsAsync(uploaded.Document!.Id, Owner, CancellationToken.None);

        Assert.Equal([(0, "Ολόκληρο το κείμενο", 1, 2), (1, "Κεφάλαιο 1: Αρχές", 1, 1), (2, "Κεφάλαιο 2: Εφαρμογές", 2, 2)],
            sections!.Select(s => (s.Order, s.Title, s.StartPage, s.EndPage)));
        Assert.Equal([true, false, false], sections!.Select(s => s.IsWholeDocument));
    }

    [Fact]
    public void A_document_that_is_not_split_is_only_the_whole_document()
    {
        var sections = DocumentService.WithWholeDocument(
            [new SectionDraft("Ολόκληρο το κείμενο", 1, 3, 600)], ["α", "β", "γ"]);

        var only = Assert.Single(sections);
        Assert.True(only.IsWholeDocument);
        Assert.Equal((1, 1, 3), (only.Order, only.StartPage, only.EndPage));
    }

    [Fact]
    public void One_section_that_skips_pages_still_gets_the_whole_document_first()
    {
        var sections = DocumentService.WithWholeDocument(
            [new SectionDraft("Κεφάλαιο 1", 2, 3, 500)], ["εξώφυλλο", "λέξη λέξη", "λέξη"]);

        Assert.Equal([(0, true, 1, 3, 4), (1, false, 2, 3, 500)],
            sections.Select(s => (s.Order, s.IsWholeDocument, s.StartPage, s.EndPage, s.WordCount)));
    }

    [Fact]
    public async Task GetSections_of_unknown_document_returns_null()
    {
        var (service, _) = Create(new TextExtractionResult([], null));

        Assert.Null(await service.GetSectionsAsync(Guid.NewGuid(), Owner, CancellationToken.None));
    }

    [Fact]
    public async Task Returns_extractor_error_and_saves_nothing()
    {
        var (service, repo) = Create(TextExtractionResult.Fail(ErrorCodes.TooManyPages));

        var result = await service.UploadAsync(Owner, "a.pdf", [], null, CancellationToken.None);

        Assert.Equal(ErrorCodes.TooManyPages, result.ErrorCode);
        Assert.Empty(repo.Added);
    }

    [Fact]
    public async Task Scanned_pdf_without_text_returns_no_text()
    {
        var (service, repo) = Create(new TextExtractionResult(["", " 12 ", ""], null));

        var result = await service.UploadAsync(Owner, "scan.pdf", [], null, CancellationToken.None);

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
