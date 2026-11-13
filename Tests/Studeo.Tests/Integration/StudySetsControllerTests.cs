using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Studeo.Api.Contracts;
using Studeo.Api.Services;

namespace Studeo.Tests.Integration;

public class StudySetsControllerTests(StudeoApiFactory factory) : IClassFixture<StudeoApiFactory>
{
    private static readonly QuestionCounts Counts = TestStudySets.SmallCounts;

    private async Task<SectionResponse> UploadAndGetSecondSection(HttpClient client)
    {
        var file = new ByteArrayContent(TestPdfs.GreekSample());
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        var document = await (await client.PostAsync("/api/documents", new MultipartFormDataContent { { file, "file", "bio.pdf" } }))
            .Content.ReadFromJsonAsync<DocumentResponse>();
        var sections = await client.GetFromJsonAsync<List<SectionResponse>>($"/api/documents/{document!.Id}/sections");
        return sections!.Single(s => s.Title == "Κεφάλαιο 2: Η φωτοσύνθεση"); // pages 3–4
    }

    private static async Task<string?> ErrorCode(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("code").GetString();
    }

    [Fact]
    public async Task Create_generates_stores_and_get_returns_it()
    {
        var client = factory.CreateClientAs(TestUser.NewStudent());
        var section = await UploadAndGetSecondSection(client);
        factory.Ai.Returns(AiResult.Ok(TestStudySets.ModelJson(TestStudySets.Valid(Counts, page: 3))));

        var response = await client.PostAsJsonAsync("/api/study-sets", new CreateStudySetRequest(section.Id, "easy", Counts));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<StudySetResponse>();
        Assert.Equal(section.Id, created!.SectionId);
        Assert.Equal("Κεφάλαιο 2: Η φωτοσύνθεση", created.Content.SectionTitle);
        Assert.Equal(Counts.TrueFalse, created.Content.TrueFalse.Count);
        Assert.Equal($"/api/study-sets/{created.Id}", response.Headers.Location?.AbsolutePath);
        Assert.Contains("Η φωτοσύνθεση πραγματοποιείται στους χλωροπλάστες", factory.Ai.Calls.Single().User);

        var fetched = await client.GetFromJsonAsync<StudySetResponse>($"/api/study-sets/{created.Id}");
        Assert.Equal(created.Content.Theory.Select(t => t.Id), fetched!.Content.Theory.Select(t => t.Id));
    }

    [Fact]
    public async Task Students_get_a_lesson_with_flashcards_which_they_can_write_again()
    {
        var client = factory.CreateClientAs(TestUser.NewStudent());
        var section = await UploadAndGetSecondSection(client);
        factory.Ai.Returns(AiResult.Ok(TestStudySets.ModelJson(TestStudySets.Valid(Counts, page: 3))));

        var created = await (await client.PostAsJsonAsync("/api/study-sets", new CreateStudySetRequest(section.Id, "easy", Counts)))
            .Content.ReadFromJsonAsync<StudySetResponse>();
        factory.Ai.Lesson = () => AiResult.Ok(JsonSerializer.Serialize(
            TestStudySets.ValidLesson(4) with { Intro = "Νέα εισαγωγή." }, StudySetJson.Options));
        var rewritten = await client.PostAsync($"/api/study-sets/{created!.Id}/lesson", null);
        var fetched = await client.GetFromJsonAsync<StudySetResponse>($"/api/study-sets/{created.Id}");
        var teacherCall = await factory.CreateClientAs(TestUser.NewTeacher()).PostAsync($"/api/study-sets/{created.Id}/lesson", null);
        factory.Ai.Lesson = () => AiResult.Ok(JsonSerializer.Serialize(TestStudySets.ValidLesson(3), StudySetJson.Options));

        Assert.Equal(6, created.Content.Lesson!.Flashcards.Count);
        Assert.Null(created.Content.TeachingGuide);
        Assert.Equal(HttpStatusCode.OK, rewritten.StatusCode);
        Assert.Equal("Νέα εισαγωγή.", fetched!.Content.Lesson!.Intro);
        Assert.Equal(HttpStatusCode.Forbidden, teacherCall.StatusCode);
    }

    [Fact]
    public async Task Create_with_invalid_ai_output_twice_returns_422()
    {
        var client = factory.CreateClientAs(TestUser.NewStudent());
        var section = await UploadAndGetSecondSection(client);
        factory.Ai.Returns(AiResult.Ok("{}"), AiResult.Ok("{}"));

        var response = await client.PostAsJsonAsync("/api/study-sets", new CreateStudySetRequest(section.Id, "easy", Counts));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(ErrorCodes.AiInvalidOutput, await ErrorCode(response));
    }

    [Fact]
    public async Task Create_when_ai_is_not_configured_returns_503()
    {
        var client = factory.CreateClientAs(TestUser.NewStudent());
        var section = await UploadAndGetSecondSection(client);
        factory.Ai.Returns(AiResult.Fail(ErrorCodes.AiNotConfigured));

        var response = await client.PostAsJsonAsync("/api/study-sets", new CreateStudySetRequest(section.Id, "easy", Counts));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(ErrorCodes.AiNotConfigured, await ErrorCode(response));
    }

    [Fact]
    public async Task Create_with_bad_difficulty_returns_400()
    {
        var response = await factory.CreateClientAs(TestUser.NewStudent())
            .PostAsJsonAsync("/api/study-sets", new CreateStudySetRequest(Guid.NewGuid(), "extreme", Counts));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ErrorCodes.InvalidRequest, await ErrorCode(response));
    }

    [Fact]
    public async Task Create_for_unknown_section_returns_404()
    {
        var response = await factory.CreateClientAs(TestUser.NewStudent())
            .PostAsJsonAsync("/api/study-sets", new CreateStudySetRequest(Guid.NewGuid(), "easy", Counts));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ErrorCodes.NotFound, await ErrorCode(response));
    }

    [Fact]
    public async Task List_returns_own_study_sets_with_their_pdf_and_section()
    {
        var client = factory.CreateClientAs(TestUser.NewStudent());
        var section = await UploadAndGetSecondSection(client);
        factory.Ai.Returns(AiResult.Ok(TestStudySets.ModelJson(TestStudySets.Valid(Counts, page: 3))));
        var created = await (await client.PostAsJsonAsync("/api/study-sets", new CreateStudySetRequest(section.Id, "hard", Counts)))
            .Content.ReadFromJsonAsync<StudySetResponse>();

        var mine = await client.GetFromJsonAsync<List<StudySetSummaryResponse>>("/api/study-sets");
        var others = await factory.CreateClientAs(TestUser.NewStudent()).GetFromJsonAsync<List<StudySetSummaryResponse>>("/api/study-sets");
        var documents = await client.GetFromJsonAsync<List<DocumentSummaryResponse>>("/api/documents");

        var item = Assert.Single(mine!);
        Assert.Equal((created!.Id, "Κεφάλαιο 2: Η φωτοσύνθεση", "bio.pdf", section.Id, "Κεφάλαιο 2: Η φωτοσύνθεση", "hard"),
            (item.Id, item.Name, item.FileName, item.SectionId, item.SectionTitle, item.Difficulty));
        Assert.Equal(documents!.Single().Id, item.DocumentId);
        Assert.Equal(1, documents!.Single().StudySetCount);
        Assert.Empty(others!);
    }

    [Fact]
    public async Task Someone_elses_study_set_and_section_return_404()
    {
        var owner = factory.CreateClientAs(TestUser.NewStudent());
        var section = await UploadAndGetSecondSection(owner);
        factory.Ai.Returns(AiResult.Ok(TestStudySets.ModelJson(TestStudySets.Valid(Counts, page: 3))));
        var created = await (await owner.PostAsJsonAsync("/api/study-sets", new CreateStudySetRequest(section.Id, "easy", Counts)))
            .Content.ReadFromJsonAsync<StudySetResponse>();
        var other = factory.CreateClientAs(TestUser.NewStudent());

        var get = await other.GetAsync($"/api/study-sets/{created!.Id}");
        var create = await other.PostAsJsonAsync("/api/study-sets", new CreateStudySetRequest(section.Id, "easy", Counts));

        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, create.StatusCode);
        Assert.Equal(ErrorCodes.NotFound, await ErrorCode(create));
    }

    [Fact]
    public async Task Named_material_can_be_renamed_and_keeps_its_pdf()
    {
        var client = factory.CreateClientAs(TestUser.NewStudent());
        var section = await UploadAndGetSecondSection(client);
        factory.Ai.Returns(AiResult.Ok(TestStudySets.ModelJson(TestStudySets.Valid(Counts, page: 3))));
        var created = await (await client.PostAsJsonAsync("/api/study-sets",
                new CreateStudySetRequest(section.Id, "easy", Counts, Name: "Φωτοσύνθεση για το διαγώνισμα")))
            .Content.ReadFromJsonAsync<StudySetResponse>();
        var document = (await client.GetFromJsonAsync<List<DocumentSummaryResponse>>("/api/documents"))!.Single();

        var renamed = await client.PatchAsJsonAsync($"/api/study-sets/{created!.Id}", new RenameStudySetRequest("  Τελική επανάληψη "));
        var fetched = await client.GetFromJsonAsync<StudySetResponse>($"/api/study-sets/{created.Id}");
        var listed = (await client.GetFromJsonAsync<List<StudySetSummaryResponse>>("/api/study-sets"))!.Single();

        Assert.Equal("Φωτοσύνθεση για το διαγώνισμα", created.Name);
        Assert.Equal(document.Id, created.DocumentId);
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.Equal("Τελική επανάληψη", fetched!.Name);
        Assert.Equal(document.Id, fetched.DocumentId);
        Assert.Equal("Τελική επανάληψη", listed.Name);
    }

    [Fact]
    public async Task Rename_rejects_empty_names_and_other_users()
    {
        var owner = factory.CreateClientAs(TestUser.NewStudent());
        var section = await UploadAndGetSecondSection(owner);
        factory.Ai.Returns(AiResult.Ok(TestStudySets.ModelJson(TestStudySets.Valid(Counts, page: 3))));
        var created = await (await owner.PostAsJsonAsync("/api/study-sets", new CreateStudySetRequest(section.Id, "easy", Counts)))
            .Content.ReadFromJsonAsync<StudySetResponse>();

        var empty = await owner.PatchAsJsonAsync($"/api/study-sets/{created!.Id}", new RenameStudySetRequest("   "));
        var tooLong = await owner.PatchAsJsonAsync($"/api/study-sets/{created.Id}", new RenameStudySetRequest(new string('α', 121)));
        var someoneElse = await factory.CreateClientAs(TestUser.NewStudent())
            .PatchAsJsonAsync($"/api/study-sets/{created.Id}", new RenameStudySetRequest("Δικό μου"));

        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, someoneElse.StatusCode);
        Assert.Equal("Κεφάλαιο 2: Η φωτοσύνθεση", (await owner.GetFromJsonAsync<StudySetResponse>($"/api/study-sets/{created.Id}"))!.Name);
    }

    [Fact]
    public async Task Create_with_too_long_name_returns_400()
    {
        var response = await factory.CreateClientAs(TestUser.NewStudent())
            .PostAsJsonAsync("/api/study-sets", new CreateStudySetRequest(Guid.NewGuid(), "easy", Counts, Name: new string('α', 121)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Get_unknown_returns_404()
    {
        var response = await factory.CreateClientAs(TestUser.NewStudent()).GetAsync($"/api/study-sets/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
