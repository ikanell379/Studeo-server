using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Studeo.Api.Contracts;
using Studeo.Api.Services;

namespace Studeo.Tests.Integration;

/// <summary>The teacher's flow: guide on how to teach + test, exported as PDF or Word.</summary>
public class TeacherTestControllerTests(StudeoApiFactory factory) : IClassFixture<StudeoApiFactory>
{
    private static readonly QuestionCounts Counts = TestStudySets.SmallCounts;

    private async Task<StudySetResponse> CreateAs(HttpClient client)
    {
        var file = new ByteArrayContent(TestPdfs.GreekSample());
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        var document = await (await client.PostAsync("/api/documents", new MultipartFormDataContent { { file, "file", "bio.pdf" } }))
            .Content.ReadFromJsonAsync<DocumentResponse>();
        var sections = await client.GetFromJsonAsync<List<SectionResponse>>($"/api/documents/{document!.Id}/sections");
        var section = sections!.Single(s => s.Title == "Κεφάλαιο 2: Η φωτοσύνθεση"); // pages 3–4
        factory.Ai.Returns(AiResult.Ok(TestStudySets.ModelJson(TestStudySets.Valid(Counts, page: 3))));
        var response = await client.PostAsJsonAsync("/api/study-sets", new CreateStudySetRequest(section.Id, "medium", Counts));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<StudySetResponse>())!;
    }

    [Fact]
    public async Task Teachers_material_has_a_teaching_guide_students_does_not()
    {
        var teacher = await CreateAs(factory.CreateClientAs(TestUser.NewTeacher()));
        var student = await CreateAs(factory.CreateClientAs(TestUser.NewStudent()));

        Assert.NotNull(teacher.Content.TeachingGuide);
        Assert.NotEmpty(teacher.Content.TeachingGuide.LessonPlan);
        Assert.Null(student.Content.TeachingGuide);
    }

    [Fact]
    public async Task Export_gives_pdf_and_word_files_for_test_and_answers()
    {
        var client = factory.CreateClientAs(TestUser.NewTeacher());
        var set = await CreateAs(client);

        var pdf = await client.PostAsJsonAsync($"/api/study-sets/{set.Id}/export", new ExportTestRequest("pdf", "test", "Διαγώνισμα"));
        var docx = await client.PostAsJsonAsync($"/api/study-sets/{set.Id}/export",
            new ExportTestRequest("docx", "answers", null, true, [TestQuestionTypes.TrueFalse]));

        Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType!.MediaType);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString((await pdf.Content.ReadAsByteArrayAsync())[..4]));
        Assert.Equal("Διαγώνισμα.pdf", pdf.Content.Headers.ContentDisposition!.FileNameStar);

        Assert.Equal(HttpStatusCode.OK, docx.StatusCode);
        Assert.Equal("application/vnd.openxmlformats-officedocument.wordprocessingml.document", docx.Content.Headers.ContentType!.MediaType);
        Assert.Equal("PK", System.Text.Encoding.ASCII.GetString((await docx.Content.ReadAsByteArrayAsync())[..2]));
        Assert.Equal(Studeo.Api.Services.Export.TestSheetBuilder.FileName(set.Name, true, "docx"), docx.Content.Headers.ContentDisposition!.FileNameStar);
    }

    [Fact]
    public async Task Export_and_guide_are_for_teachers_and_only_their_own_material()
    {
        var owner = factory.CreateClientAs(TestUser.NewTeacher());
        var set = await CreateAs(owner);
        var student = factory.CreateClientAs(TestUser.NewStudent());
        var otherTeacher = factory.CreateClientAs(TestUser.NewTeacher());
        var request = new ExportTestRequest("pdf", "test");

        Assert.Equal(HttpStatusCode.Forbidden, (await student.PostAsJsonAsync($"/api/study-sets/{set.Id}/export", request)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await student.PostAsync($"/api/study-sets/{set.Id}/teaching-guide", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await otherTeacher.PostAsJsonAsync($"/api/study-sets/{set.Id}/export", request)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await otherTeacher.PostAsync($"/api/study-sets/{set.Id}/teaching-guide", null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await owner.PostAsJsonAsync($"/api/study-sets/{set.Id}/export", new ExportTestRequest("odt", "test"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await owner.PostAsJsonAsync($"/api/study-sets/{set.Id}/export", new ExportTestRequest("pdf", "test", Types: ["theory"]))).StatusCode);
    }

    [Fact]
    public async Task Teaching_guide_can_be_written_again_and_is_saved()
    {
        var client = factory.CreateClientAs(TestUser.NewTeacher());
        var set = await CreateAs(client);
        factory.Ai.Guide = () => AiResult.Ok(System.Text.Json.JsonSerializer.Serialize(
            TestStudySets.ValidGuide(4) with { Overview = "Νέα επισκόπηση." }, StudySetJson.Options));

        var response = await client.PostAsync($"/api/study-sets/{set.Id}/teaching-guide", null);
        var fetched = await client.GetFromJsonAsync<StudySetResponse>($"/api/study-sets/{set.Id}");
        factory.Ai.Guide = () => AiResult.Ok(System.Text.Json.JsonSerializer.Serialize(TestStudySets.ValidGuide(3), StudySetJson.Options));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Νέα επισκόπηση.", fetched!.Content.TeachingGuide!.Overview);
    }
}
