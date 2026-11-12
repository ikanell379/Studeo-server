using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Studeo.Api.Contracts;
using Studeo.Api.Models;

namespace Studeo.Tests.Integration;

public class CoursesControllerTests(StudeoApiFactory factory) : IClassFixture<StudeoApiFactory>
{
    private static async Task<string?> ErrorCode(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("code").GetString();
    }

    private static async Task<CourseResponse> Create(HttpClient client, string name) =>
        (await (await client.PostAsJsonAsync("/api/courses", new CourseNameRequest(name))).Content.ReadFromJsonAsync<CourseResponse>())!;

    [Fact]
    public async Task Create_and_list_own_courses_by_name()
    {
        var client = factory.CreateClientAs(TestUser.NewStudent());
        await factory.CreateClientAs(TestUser.NewStudent()).PostAsJsonAsync("/api/courses", new CourseNameRequest("Όχι δικό μου"));

        var response = await client.PostAsJsonAsync("/api/courses", new CourseNameRequest("  Φυσική  "));
        await Create(client, "Βιολογία");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("Φυσική", (await response.Content.ReadFromJsonAsync<CourseResponse>())!.Name);
        var list = await client.GetFromJsonAsync<List<CourseResponse>>("/api/courses");
        Assert.Equal(["Βιολογία", "Φυσική"], list!.Select(c => c.Name));
    }

    [Fact]
    public async Task Same_name_twice_returns_409()
    {
        var client = factory.CreateClientAs(TestUser.NewStudent());
        await Create(client, "Χημεία");

        var response = await client.PostAsJsonAsync("/api/courses", new CourseNameRequest("χημεία"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(ErrorCodes.CourseExists, await ErrorCode(response));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Empty_name_returns_400(string name)
    {
        var response = await factory.CreateClientAs(TestUser.NewStudent())
            .PostAsJsonAsync("/api/courses", new CourseNameRequest(name));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Rename_course()
    {
        var client = factory.CreateClientAs(TestUser.NewStudent());
        var course = await Create(client, "Μαθηματικα");

        var response = await client.PatchAsJsonAsync($"/api/courses/{course.Id}", new CourseNameRequest("Μαθηματικά"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Μαθηματικά", (await response.Content.ReadFromJsonAsync<CourseResponse>())!.Name);
    }

    private static async Task<DocumentResponse> Upload(HttpClient client, byte[] pdf, Guid? courseId)
    {
        var file = new ByteArrayContent(pdf);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        var form = new MultipartFormDataContent { { file, "file", "notes.pdf" } };
        if (courseId is { } id) form.Add(new StringContent(id.ToString()), "courseId");
        return (await (await client.PostAsync("/api/documents", form)).Content.ReadFromJsonAsync<DocumentResponse>())!;
    }

    [Fact]
    public async Task Deleting_a_course_deletes_its_pdfs_and_their_quizzes_only()
    {
        var client = factory.CreateClientAs(TestUser.NewStudent());
        var biology = await Create(client, "Βιολογία");
        var history = await Create(client, "Ιστορία");
        var inBiology = await Upload(client, TestPdfs.GreekSample(), biology.Id);
        var inHistory = await Upload(client, TestPdfs.Build(TestPdfs.LongSentence), history.Id);
        var loose = await Upload(client, TestPdfs.Build(TestPdfs.LongSentence, TestPdfs.LongSentence), null);

        var sections = await client.GetFromJsonAsync<List<SectionResponse>>($"/api/documents/{inBiology.Id}/sections");
        var counts = TestStudySets.SmallCounts;
        factory.Ai.Returns(Studeo.Api.Services.AiResult.Ok(TestStudySets.ModelJson(TestStudySets.Valid(counts, page: 3))));
        var quiz = await (await client.PostAsJsonAsync("/api/study-sets",
                new CreateStudySetRequest(sections!.Single(s => s.Title == "Κεφάλαιο 2: Η φωτοσύνθεση").Id, "easy", counts)))
            .Content.ReadFromJsonAsync<StudySetResponse>();

        var response = await client.DeleteAsync($"/api/courses/{biology.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(["Ιστορία"], (await client.GetFromJsonAsync<List<CourseResponse>>("/api/courses"))!.Select(c => c.Name));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/documents/{inBiology.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/study-sets/{quiz!.Id}")).StatusCode);
        var left = await client.GetFromJsonAsync<List<DocumentSummaryResponse>>("/api/documents");
        Assert.Equal(new[] { inHistory.Id, loose.Id }.Order(), left!.Select(d => d.Id).Order());

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StudeoDbContext>();
        Assert.False(await db.DocumentPages.AnyAsync(p => p.DocumentId == inBiology.Id));
        Assert.False(await db.Sections.AnyAsync(s => s.DocumentId == inBiology.Id));
        Assert.False(await db.StudySets.AnyAsync(s => s.Id == quiz.Id));
    }

    [Fact]
    public async Task Someone_elses_course_returns_404()
    {
        var course = await Create(factory.CreateClientAs(TestUser.NewStudent()), "Ιστορία");
        var other = factory.CreateClientAs(TestUser.NewStudent());

        Assert.Equal(HttpStatusCode.NotFound,
            (await other.PatchAsJsonAsync($"/api/courses/{course.Id}", new CourseNameRequest("Δικό μου"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/api/courses/{course.Id}")).StatusCode);
    }
}
