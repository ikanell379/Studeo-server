using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Studeo.Api.Contracts;
using Studeo.Api.Services;

namespace Studeo.Tests.Integration;

/// <summary>POST /api/study-sets/stream: NDJSON with real progress, then the study set or an error.</summary>
public class StudySetStreamTests(StudeoApiFactory factory) : IClassFixture<StudeoApiFactory>
{
    private static readonly QuestionCounts Counts = TestStudySets.SmallCounts;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private async Task<SectionResponse> UploadAndGetSecondSection(HttpClient client)
    {
        var file = new ByteArrayContent(TestPdfs.GreekSample());
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        var document = await (await client.PostAsync("/api/documents", new MultipartFormDataContent { { file, "file", "bio.pdf" } }))
            .Content.ReadFromJsonAsync<DocumentResponse>();
        var sections = await client.GetFromJsonAsync<List<SectionResponse>>($"/api/documents/{document!.Id}/sections");
        return sections!.Single(s => s.Title == "Κεφάλαιο 2: Η φωτοσύνθεση");
    }

    private static async Task<List<GenerationEvent>> Events(HttpResponseMessage response) =>
        (await response.Content.ReadAsStringAsync())
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonSerializer.Deserialize<GenerationEvent>(line, Json)!)
            .ToList();

    [Fact]
    public async Task Streams_progress_then_the_saved_study_set()
    {
        var client = factory.CreateClientAs(TestUser.NewTeacher());
        var section = await UploadAndGetSecondSection(client);
        factory.Ai.Returns(AiResult.Ok(TestStudySets.ModelJson(TestStudySets.Valid(Counts, page: 3))));

        var response = await client.PostAsJsonAsync("/api/study-sets/stream", new CreateStudySetRequest(section.Id, "easy", Counts));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/x-ndjson", response.Content.Headers.ContentType?.MediaType);
        var events = await Events(response);
        var progress = events.Where(e => e.Type == "progress").Select(e => e.Progress!).ToList();
        var total = Counts.Theory + Counts.OpenQuestions + Counts.TrueFalse + Counts.Matching + Counts.MultipleChoice + Counts.FillBlanks;

        Assert.Equal(GenerationProgress.Reading, progress[0].Stage);
        Assert.Equal(Enumerable.Range(1, total), progress.Where(p => p.Stage == GenerationProgress.Writing).Select(p => p.Done));
        Assert.Equal(GenerationProgress.Saving, progress[^1].Stage);

        var done = events[^1];
        Assert.Equal("done", done.Type);
        Assert.Equal(section.Id, done.StudySet!.SectionId);
        var stored = await client.GetFromJsonAsync<StudySetResponse>($"/api/study-sets/{done.StudySet.Id}");
        Assert.Equal(done.StudySet.Content.Theory.Select(t => t.Id), stored!.Content.Theory.Select(t => t.Id));
    }

    [Fact]
    public async Task Ai_failure_ends_the_stream_with_an_error_event()
    {
        var client = factory.CreateClientAs(TestUser.NewStudent());
        var section = await UploadAndGetSecondSection(client);
        factory.Ai.Returns(AiResult.Fail(ErrorCodes.AiBusy));

        var response = await client.PostAsJsonAsync("/api/study-sets/stream", new CreateStudySetRequest(section.Id, "easy", Counts));

        var last = (await Events(response))[^1];
        Assert.Equal("error", last.Type);
        Assert.Equal(ErrorCodes.AiBusy, last.Code);
    }

    [Fact]
    public async Task Unknown_section_ends_with_not_found()
    {
        var response = await factory.CreateClientAs(TestUser.NewStudent())
            .PostAsJsonAsync("/api/study-sets/stream", new CreateStudySetRequest(Guid.NewGuid(), "easy", Counts));

        var events = await Events(response);
        Assert.Equal(new GenerationEvent("error", Code: ErrorCodes.NotFound), Assert.Single(events));
    }

    [Fact]
    public async Task Invalid_request_gets_a_plain_400()
    {
        var response = await factory.CreateClientAs(TestUser.NewStudent())
            .PostAsJsonAsync("/api/study-sets/stream", new CreateStudySetRequest(Guid.NewGuid(), "impossible", Counts));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(ErrorCodes.InvalidRequest, json.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Anonymous_gets_401()
    {
        var response = await factory.CreateClient()
            .PostAsJsonAsync("/api/study-sets/stream", new CreateStudySetRequest(Guid.NewGuid(), "easy", Counts));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
