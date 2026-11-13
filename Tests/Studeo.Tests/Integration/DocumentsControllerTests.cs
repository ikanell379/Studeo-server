using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Studeo.Api.Contracts;
using Studeo.Api.Models;
using Studeo.Api.Services;

namespace Studeo.Tests.Integration;

public class DocumentsControllerTests(StudeoApiFactory factory) : IClassFixture<StudeoApiFactory>
{
    private static MultipartFormDataContent Form(byte[] content, string fileName, string contentType = "application/pdf")
    {
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return new MultipartFormDataContent { { file, "file", fileName } };
    }

    private static async Task<string?> ErrorCode(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("code").GetString();
    }

    [Fact]
    public async Task Upload_greek_pdf_stores_text_per_page()
    {
        var client = factory.CreateClientAs(TestUser.NewStudent());

        var response = await client.PostAsync("/api/documents", Form(TestPdfs.GreekSample(), "biology-el.pdf"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var document = await response.Content.ReadFromJsonAsync<DocumentResponse>();
        Assert.NotNull(document);
        Assert.Equal("biology-el.pdf", document.FileName);
        Assert.Equal(4, document.PageCount);
        Assert.Equal($"/api/documents/{document.Id}", response.Headers.Location?.AbsolutePath);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StudeoDbContext>();
        var pages = await db.DocumentPages.Where(p => p.DocumentId == document.Id).OrderBy(p => p.PageNumber).ToListAsync();
        Assert.Equal([1, 2, 3, 4], pages.Select(p => p.PageNumber));
        Assert.Contains("Η φωτοσύνθεση πραγματοποιείται στους χλωροπλάστες", pages[2].Text);
    }

    private const string DocxType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    private const string PptxType = "application/vnd.openxmlformats-officedocument.presentationml.presentation";

    private static string Words(int count) => string.Join(' ', Enumerable.Repeat("φωτοσύνθεση", count));

    [Fact]
    public async Task Upload_word_document_is_split_into_pages_and_sections_by_its_headings()
    {
        var client = factory.CreateClientAs(TestUser.NewStudent());
        var docx = TestOfficeFiles.Docx(
            TestOfficeFiles.H1("Το κύτταρο"),
            TestOfficeFiles.Text(Words(400)),
            TestOfficeFiles.H1("Η φωτοσύνθεση"),
            TestOfficeFiles.Text(Words(400)));

        var response = await client.PostAsync("/api/documents", Form(docx, "biology.docx", DocxType));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var document = await response.Content.ReadFromJsonAsync<DocumentResponse>();
        Assert.Equal(("biology.docx", 2), (document!.FileName, document.PageCount));
        var sections = await client.GetFromJsonAsync<List<SectionResponse>>($"/api/documents/{document.Id}/sections");
        Assert.Equal(
            [("Ολόκληρο το κείμενο", 1, 2), ("Το κύτταρο", 1, 1), ("Η φωτοσύνθεση", 2, 2)],
            sections!.Select(s => (s.Title, s.StartPage, s.EndPage)));
    }

    [Fact]
    public async Task Upload_presentation_stores_one_page_per_slide()
    {
        var client = factory.CreateClientAs(TestUser.NewStudent());
        var pptx = TestOfficeFiles.Pptx(
            new TestOfficeFiles.Slide("Το κύτταρο", [Words(40)]),
            new TestOfficeFiles.Slide("Το κύτταρο", [Words(40)]),
            new TestOfficeFiles.Slide("Η φωτοσύνθεση", [Words(40)]),
            new TestOfficeFiles.Slide("Η φωτοσύνθεση", [Words(40)]));

        var response = await client.PostAsync("/api/documents", Form(pptx, "biology.pptx", PptxType));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var document = await response.Content.ReadFromJsonAsync<DocumentResponse>();
        Assert.Equal(4, document!.PageCount);
        var sections = await client.GetFromJsonAsync<List<SectionResponse>>($"/api/documents/{document.Id}/sections");
        Assert.Equal(["Ολόκληρο το κείμενο", "Το κύτταρο", "Η φωτοσύνθεση"], sections!.Select(s => s.Title));
    }

    [Fact]
    public async Task Upload_powerpoint_97_presentation_stores_one_page_per_slide()
    {
        var client = factory.CreateClientAs(TestUser.NewStudent());
        var ppt = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "samples", "slides-el.ppt"));

        var response = await client.PostAsync("/api/documents", Form(ppt, "slides-el.ppt", "application/vnd.ms-powerpoint"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var document = await response.Content.ReadFromJsonAsync<DocumentResponse>();
        Assert.Equal(4, document!.PageCount);
        var sections = await client.GetFromJsonAsync<List<SectionResponse>>($"/api/documents/{document.Id}/sections");
        Assert.Equal(["Ολόκληρο το κείμενο", "Το κύτταρο", "Η φωτοσύνθεση"], sections!.Select(s => s.Title));
    }

    [Fact]
    public async Task Upload_pdf_renamed_to_docx_returns_invalid_file()
    {
        var response = await factory.CreateClientAs(TestUser.NewStudent())
            .PostAsync("/api/documents", Form(TestPdfs.GreekSample(), "biology.docx", DocxType));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(ErrorCodes.InvalidFile, await ErrorCode(response));
    }

    [Fact]
    public async Task Get_returns_uploaded_document()
    {
        var client = factory.CreateClientAs(TestUser.NewStudent());
        var created = await (await client.PostAsync("/api/documents", Form(TestPdfs.GreekSample(), "bio.pdf")))
            .Content.ReadFromJsonAsync<DocumentResponse>();

        var fetched = await client.GetFromJsonAsync<DocumentResponse>($"/api/documents/{created!.Id}");

        Assert.Equal(created.Id, fetched!.Id);
        Assert.Equal(4, fetched.PageCount);
    }

    [Fact]
    public async Task Get_sections_returns_chapters_of_uploaded_pdf()
    {
        var client = factory.CreateClientAs(TestUser.NewStudent());
        var created = await (await client.PostAsync("/api/documents", Form(TestPdfs.GreekSample(), "bio.pdf")))
            .Content.ReadFromJsonAsync<DocumentResponse>();

        var sections = await client.GetFromJsonAsync<List<SectionResponse>>($"/api/documents/{created!.Id}/sections");

        Assert.NotNull(sections);
        Assert.Equal(
            [
                (0, "Ολόκληρο το κείμενο", 1, 4, true),
                (1, "Κεφάλαιο 1: Το κύτταρο", 1, 2, false),
                (2, "Κεφάλαιο 2: Η φωτοσύνθεση", 3, 4, false),
            ],
            sections.Select(s => (s.Order, s.Title, s.StartPage, s.EndPage, s.IsWholeDocument)));
        Assert.Equal(sections[1].WordCount + sections[2].WordCount, sections[0].WordCount);
        Assert.All(sections, s => Assert.True(s.WordCount > 0));
        Assert.All(sections, s => Assert.Empty(s.UnreadablePages)); // a clean PDF
    }

    [Fact]
    public async Task Get_sections_of_unknown_document_returns_404()
    {
        var response = await factory.CreateClientAs(TestUser.NewStudent()).GetAsync($"/api/documents/{Guid.NewGuid()}/sections");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ErrorCodes.NotFound, await ErrorCode(response));
    }


    [Fact]
    public async Task List_returns_only_own_documents_newest_first()
    {
        var maria = factory.CreateClientAs(TestUser.NewStudent());
        var other = factory.CreateClientAs(TestUser.NewStudent());
        await maria.PostAsync("/api/documents", Form(TestPdfs.GreekSample(), "first.pdf"));
        await maria.PostAsync("/api/documents", Form(TestPdfs.Build(TestPdfs.LongSentence, TestPdfs.LongSentence), "second.pdf"));
        await other.PostAsync("/api/documents", Form(TestPdfs.GreekSample(), "not-mine.pdf"));

        var list = await maria.GetFromJsonAsync<List<DocumentSummaryResponse>>("/api/documents");

        Assert.Equal([("second.pdf", 2), ("first.pdf", 4)], list!.Select(d => (d.FileName, d.PageCount)));
        Assert.All(list!, d => Assert.Equal((0, (Guid?)null), (d.StudySetCount, d.CourseId)));
    }

    [Fact]
    public async Task Uploading_the_same_pdf_again_returns_409_with_the_stored_one()
    {
        var client = factory.CreateClientAs(TestUser.NewStudent());
        var first = await (await client.PostAsync("/api/documents", Form(TestPdfs.GreekSample(), "bio.pdf")))
            .Content.ReadFromJsonAsync<DocumentResponse>();

        // Same bytes under another name: still the same PDF.
        var again = await client.PostAsync("/api/documents", Form(TestPdfs.GreekSample(), "bio (1).pdf"));

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        using var json = JsonDocument.Parse(await again.Content.ReadAsStringAsync());
        Assert.Equal(ErrorCodes.DuplicateDocument, json.RootElement.GetProperty("code").GetString());
        var existing = json.RootElement.GetProperty("document");
        Assert.Equal(first!.Id, existing.GetProperty("id").GetGuid());
        Assert.Equal("bio.pdf", existing.GetProperty("fileName").GetString());

        var list = await client.GetFromJsonAsync<List<DocumentSummaryResponse>>("/api/documents");
        Assert.Single(list!);
    }

    [Fact]
    public async Task Delete_removes_the_pdf_with_its_pages_sections_and_quizzes()
    {
        var client = factory.CreateClientAs(TestUser.NewStudent());
        var document = await (await client.PostAsync("/api/documents", Form(TestPdfs.GreekSample(), "bio.pdf")))
            .Content.ReadFromJsonAsync<DocumentResponse>();
        var sections = await client.GetFromJsonAsync<List<SectionResponse>>($"/api/documents/{document!.Id}/sections");
        var section = sections!.Single(s => s.Title == "Κεφάλαιο 2: Η φωτοσύνθεση");
        var counts = TestStudySets.SmallCounts;
        factory.Ai.Returns(Studeo.Api.Services.AiResult.Ok(TestStudySets.ModelJson(TestStudySets.Valid(counts, page: 3))));
        var created = await client.PostAsJsonAsync("/api/study-sets", new CreateStudySetRequest(section.Id, "easy", counts));
        var studySet = await created.Content.ReadFromJsonAsync<StudySetResponse>();

        var response = await client.DeleteAsync($"/api/documents/{document.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/documents/{document.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/study-sets/{studySet!.Id}")).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<List<StudySetSummaryResponse>>("/api/study-sets"))!);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StudeoDbContext>();
            Assert.False(await db.DocumentPages.AnyAsync(p => p.DocumentId == document.Id));
            Assert.False(await db.Sections.AnyAsync(s => s.DocumentId == document.Id));
            Assert.False(await db.StudySets.AnyAsync(s => s.Id == studySet.Id));
        }

        // Gone for good, so the same file is no longer a duplicate.
        var again = await client.PostAsync("/api/documents", Form(TestPdfs.GreekSample(), "bio.pdf"));
        Assert.Equal(HttpStatusCode.Created, again.StatusCode);
    }

    [Fact]
    public async Task Delete_someone_elses_or_unknown_pdf_returns_404()
    {
        var owner = factory.CreateClientAs(TestUser.NewStudent());
        var document = await (await owner.PostAsync("/api/documents", Form(TestPdfs.GreekSample(), "bio.pdf")))
            .Content.ReadFromJsonAsync<DocumentResponse>();
        var other = factory.CreateClientAs(TestUser.NewStudent());

        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/api/documents/{document!.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/api/documents/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/api/documents/{document.Id}")).StatusCode);
    }

    [Fact]
    public async Task A_pdf_stored_before_file_hashes_is_still_found_by_its_text()
    {
        var client = factory.CreateClientAs(TestUser.NewStudent());
        var old = await (await client.PostAsync("/api/documents", Form(TestPdfs.GreekSample(), "old.pdf")))
            .Content.ReadFromJsonAsync<DocumentResponse>();
        string expected;
        using (var scope = factory.Services.CreateScope())
        {
            // Make it look like a PDF from before: no hashes. The migration's backfill gives it its text hash.
            var db = scope.ServiceProvider.GetRequiredService<StudeoDbContext>();
            expected = DocumentHashes.TextHash(await db.DocumentPages.Where(p => p.DocumentId == old!.Id)
                .OrderBy(p => p.PageNumber).Select(p => p.Text).ToListAsync());
            await db.Documents.Where(d => d.Id == old!.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.ContentHash, (string?)null).SetProperty(d => d.TextHash, (string?)null));

            await db.Database.ExecuteSqlRawAsync(DocumentHashes.BackfillTextHashSql);

            var stored = await db.Documents.AsNoTracking().SingleAsync(d => d.Id == old!.Id);
            Assert.Equal(expected, stored.TextHash); // the SQL and the C# fingerprint agree
        }

        var again = await client.PostAsync("/api/documents", Form(TestPdfs.GreekSample(), "again.pdf"));

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal(ErrorCodes.DuplicateDocument, await ErrorCode(again));
    }

    [Fact]
    public async Task Another_user_can_upload_the_same_pdf()
    {
        await factory.CreateClientAs(TestUser.NewStudent()).PostAsync("/api/documents", Form(TestPdfs.GreekSample(), "bio.pdf"));

        var response = await factory.CreateClientAs(TestUser.NewStudent())
            .PostAsync("/api/documents", Form(TestPdfs.GreekSample(), "bio.pdf"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Upload_into_a_course_and_move_between_courses()
    {
        var client = factory.CreateClientAs(TestUser.NewStudent());
        var biology = await (await client.PostAsJsonAsync("/api/courses", new CourseNameRequest("Βιολογία")))
            .Content.ReadFromJsonAsync<CourseResponse>();
        var chemistry = await (await client.PostAsJsonAsync("/api/courses", new CourseNameRequest("Χημεία")))
            .Content.ReadFromJsonAsync<CourseResponse>();

        var form = Form(TestPdfs.GreekSample(), "bio.pdf");
        form.Add(new StringContent(biology!.Id.ToString()), "courseId");
        var uploaded = await (await client.PostAsync("/api/documents", form)).Content.ReadFromJsonAsync<DocumentResponse>();
        Assert.Equal(biology.Id, uploaded!.CourseId);

        var moved = await client.PutAsJsonAsync($"/api/documents/{uploaded.Id}/course", new MoveDocumentRequest(chemistry!.Id));
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        Assert.Equal(chemistry.Id, (await moved.Content.ReadFromJsonAsync<DocumentResponse>())!.CourseId);

        var courses = await client.GetFromJsonAsync<List<CourseResponse>>("/api/courses");
        Assert.Equal([("Βιολογία", 0), ("Χημεία", 1)], courses!.Select(c => (c.Name, c.DocumentCount)));

        var out_ = await client.PutAsJsonAsync($"/api/documents/{uploaded.Id}/course", new MoveDocumentRequest(null));
        Assert.Null((await out_.Content.ReadFromJsonAsync<DocumentResponse>())!.CourseId);
    }

    [Fact]
    public async Task Someone_elses_course_returns_404_on_upload_and_move()
    {
        var other = factory.CreateClientAs(TestUser.NewStudent());
        var theirs = await (await other.PostAsJsonAsync("/api/courses", new CourseNameRequest("Ιστορία")))
            .Content.ReadFromJsonAsync<CourseResponse>();
        var client = factory.CreateClientAs(TestUser.NewStudent());

        var form = Form(TestPdfs.GreekSample(), "bio.pdf");
        form.Add(new StringContent(theirs!.Id.ToString()), "courseId");
        var upload = await client.PostAsync("/api/documents", form);
        Assert.Equal(HttpStatusCode.NotFound, upload.StatusCode);

        var mine = await (await client.PostAsync("/api/documents", Form(TestPdfs.GreekSample(), "bio.pdf")))
            .Content.ReadFromJsonAsync<DocumentResponse>();
        var move = await client.PutAsJsonAsync($"/api/documents/{mine!.Id}/course", new MoveDocumentRequest(theirs.Id));
        Assert.Equal(HttpStatusCode.NotFound, move.StatusCode);
    }

    [Fact]
    public async Task Someone_elses_document_returns_404()
    {
        var owner = factory.CreateClientAs(TestUser.NewStudent());
        var created = await (await owner.PostAsync("/api/documents", Form(TestPdfs.GreekSample(), "bio.pdf")))
            .Content.ReadFromJsonAsync<DocumentResponse>();
        var other = factory.CreateClientAs(TestUser.NewStudent());

        var document = await other.GetAsync($"/api/documents/{created!.Id}");
        var sections = await other.GetAsync($"/api/documents/{created.Id}/sections");

        Assert.Equal(HttpStatusCode.NotFound, document.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, sections.StatusCode);
    }

    [Fact]
    public async Task Get_unknown_document_returns_404()
    {
        var response = await factory.CreateClientAs(TestUser.NewStudent()).GetAsync($"/api/documents/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ErrorCodes.NotFound, await ErrorCode(response));
    }

    [Fact]
    public async Task Upload_without_file_returns_file_missing()
    {
        var response = await factory.CreateClientAs(TestUser.NewStudent()).PostAsync("/api/documents", new MultipartFormDataContent { { new StringContent("x"), "other" } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ErrorCodes.FileMissing, await ErrorCode(response));
    }

    [Fact]
    public async Task Upload_unsupported_extension_returns_unsupported_file()
    {
        var response = await factory.CreateClientAs(TestUser.NewStudent()).PostAsync("/api/documents", Form("hello"u8.ToArray(), "notes.txt", "text/plain"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ErrorCodes.UnsupportedFile, await ErrorCode(response));
    }

    [Fact]
    public async Task Upload_fake_pdf_returns_invalid_file()
    {
        var response = await factory.CreateClientAs(TestUser.NewStudent()).PostAsync("/api/documents", Form("not really a pdf"u8.ToArray(), "fake.pdf"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(ErrorCodes.InvalidFile, await ErrorCode(response));
    }

    [Fact]
    public async Task Upload_scanned_pdf_returns_no_text()
    {
        var response = await factory.CreateClientAs(TestUser.NewStudent()).PostAsync("/api/documents", Form(TestPdfs.BlankPages(3), "scan.pdf"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(ErrorCodes.NoText, await ErrorCode(response));
    }

    [Fact]
    public async Task Upload_more_than_50_pages_returns_too_many_pages()
    {
        var response = await factory.CreateClientAs(TestUser.NewStudent()).PostAsync("/api/documents", Form(TestPdfs.BlankPages(51), "big.pdf"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(ErrorCodes.TooManyPages, await ErrorCode(response));
    }
}
