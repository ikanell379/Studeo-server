using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Studeo.Api.Contracts;
using Studeo.Api.Models;

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
        var client = factory.CreateClientAs(TestUser.Student);

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

    [Fact]
    public async Task Get_returns_uploaded_document()
    {
        var client = factory.CreateClientAs(TestUser.Student);
        var created = await (await client.PostAsync("/api/documents", Form(TestPdfs.GreekSample(), "bio.pdf")))
            .Content.ReadFromJsonAsync<DocumentResponse>();

        var fetched = await client.GetFromJsonAsync<DocumentResponse>($"/api/documents/{created!.Id}");

        Assert.Equal(created.Id, fetched!.Id);
        Assert.Equal(4, fetched.PageCount);
    }

    [Fact]
    public async Task Get_sections_returns_chapters_of_uploaded_pdf()
    {
        var client = factory.CreateClientAs(TestUser.Student);
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
    }

    [Fact]
    public async Task Get_sections_of_unknown_document_returns_404()
    {
        var response = await factory.CreateClientAs(TestUser.Student).GetAsync($"/api/documents/{Guid.NewGuid()}/sections");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ErrorCodes.NotFound, await ErrorCode(response));
    }

    /// <summary>A user with no documents yet, so lists only show what the test made.</summary>
    private static TestUser NewStudent() => new(Guid.NewGuid().ToString(), [Studeo.Api.Auth.Roles.Student]);

    [Fact]
    public async Task List_returns_only_own_documents_newest_first()
    {
        var maria = factory.CreateClientAs(NewStudent());
        var other = factory.CreateClientAs(NewStudent());
        await maria.PostAsync("/api/documents", Form(TestPdfs.GreekSample(), "first.pdf"));
        await maria.PostAsync("/api/documents", Form(TestPdfs.GreekSample(), "second.pdf"));
        await other.PostAsync("/api/documents", Form(TestPdfs.GreekSample(), "not-mine.pdf"));

        var list = await maria.GetFromJsonAsync<List<DocumentSummaryResponse>>("/api/documents");

        Assert.Equal(["second.pdf", "first.pdf"], list!.Select(d => d.FileName));
        Assert.All(list!, d => Assert.Equal((4, 0), (d.PageCount, d.StudySetCount)));
    }

    [Fact]
    public async Task Someone_elses_document_returns_404()
    {
        var owner = factory.CreateClientAs(NewStudent());
        var created = await (await owner.PostAsync("/api/documents", Form(TestPdfs.GreekSample(), "bio.pdf")))
            .Content.ReadFromJsonAsync<DocumentResponse>();
        var other = factory.CreateClientAs(NewStudent());

        var document = await other.GetAsync($"/api/documents/{created!.Id}");
        var sections = await other.GetAsync($"/api/documents/{created.Id}/sections");

        Assert.Equal(HttpStatusCode.NotFound, document.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, sections.StatusCode);
    }

    [Fact]
    public async Task Get_unknown_document_returns_404()
    {
        var response = await factory.CreateClientAs(TestUser.Student).GetAsync($"/api/documents/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ErrorCodes.NotFound, await ErrorCode(response));
    }

    [Fact]
    public async Task Upload_without_file_returns_file_missing()
    {
        var response = await factory.CreateClientAs(TestUser.Student).PostAsync("/api/documents", new MultipartFormDataContent { { new StringContent("x"), "other" } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ErrorCodes.FileMissing, await ErrorCode(response));
    }

    [Fact]
    public async Task Upload_non_pdf_extension_returns_not_pdf()
    {
        var response = await factory.CreateClientAs(TestUser.Student).PostAsync("/api/documents", Form("hello"u8.ToArray(), "notes.txt", "text/plain"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ErrorCodes.NotPdf, await ErrorCode(response));
    }

    [Fact]
    public async Task Upload_fake_pdf_returns_invalid_pdf()
    {
        var response = await factory.CreateClientAs(TestUser.Student).PostAsync("/api/documents", Form("not really a pdf"u8.ToArray(), "fake.pdf"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(ErrorCodes.InvalidPdf, await ErrorCode(response));
    }

    [Fact]
    public async Task Upload_scanned_pdf_returns_no_text()
    {
        var response = await factory.CreateClientAs(TestUser.Student).PostAsync("/api/documents", Form(TestPdfs.BlankPages(3), "scan.pdf"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(ErrorCodes.NoText, await ErrorCode(response));
    }

    [Fact]
    public async Task Upload_more_than_50_pages_returns_too_many_pages()
    {
        var response = await factory.CreateClientAs(TestUser.Student).PostAsync("/api/documents", Form(TestPdfs.BlankPages(51), "big.pdf"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(ErrorCodes.TooManyPages, await ErrorCode(response));
    }
}
