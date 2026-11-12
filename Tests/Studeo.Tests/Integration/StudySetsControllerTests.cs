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
        return sections![1]; // "Κεφάλαιο 2: Η φωτοσύνθεση", pages 3–4
    }

    private static async Task<string?> ErrorCode(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("code").GetString();
    }

    [Fact]
    public async Task Create_generates_stores_and_get_returns_it()
    {
        var client = factory.CreateClient();
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
    public async Task Create_with_invalid_ai_output_twice_returns_422()
    {
        var client = factory.CreateClient();
        var section = await UploadAndGetSecondSection(client);
        factory.Ai.Returns(AiResult.Ok("{}"), AiResult.Ok("{}"));

        var response = await client.PostAsJsonAsync("/api/study-sets", new CreateStudySetRequest(section.Id, "easy", Counts));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(ErrorCodes.AiInvalidOutput, await ErrorCode(response));
    }

    [Fact]
    public async Task Create_when_ai_is_not_configured_returns_503()
    {
        var client = factory.CreateClient();
        var section = await UploadAndGetSecondSection(client);
        factory.Ai.Returns(AiResult.Fail(ErrorCodes.AiNotConfigured));

        var response = await client.PostAsJsonAsync("/api/study-sets", new CreateStudySetRequest(section.Id, "easy", Counts));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(ErrorCodes.AiNotConfigured, await ErrorCode(response));
    }

    [Fact]
    public async Task Create_with_bad_difficulty_returns_400()
    {
        var response = await factory.CreateClient()
            .PostAsJsonAsync("/api/study-sets", new CreateStudySetRequest(Guid.NewGuid(), "extreme", Counts));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ErrorCodes.InvalidRequest, await ErrorCode(response));
    }

    [Fact]
    public async Task Create_for_unknown_section_returns_404()
    {
        var response = await factory.CreateClient()
            .PostAsJsonAsync("/api/study-sets", new CreateStudySetRequest(Guid.NewGuid(), "easy", Counts));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ErrorCodes.NotFound, await ErrorCode(response));
    }

    [Fact]
    public async Task Get_unknown_returns_404()
    {
        var response = await factory.CreateClient().GetAsync($"/api/study-sets/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
