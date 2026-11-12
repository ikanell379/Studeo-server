using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Studeo.Api.Contracts;
using Studeo.Api.Models;
using Studeo.Api.Prompts;
using Studeo.Api.Repositories;
using Studeo.Api.Services;
using Studeo.Api.Validators;
using StudySetEntity = Studeo.Api.Models.StudySet;

namespace Studeo.Tests.Unit;

public class StudySetServiceTests
{
    private static readonly QuestionCounts Counts = TestStudySets.SmallCounts;

    private readonly Section _section = new()
    {
        Id = Guid.NewGuid(), DocumentId = Guid.NewGuid(), Order = 1, Title = "Κεφάλαιο 2: Η φωτοσύνθεση", StartPage = 3, EndPage = 4,
    };

    private readonly FakeAiClient _ai = new();
    private readonly FakeStudySetRepository _studySets = new();

    private StudySetService Service() => new(
        new FakeDocumentRepository(_section,
        [
            new DocumentPage { PageNumber = 2, Text = "Εκτός ενότητας" },
            new DocumentPage { PageNumber = 3, Text = "Η φωτοσύνθεση γίνεται στους χλωροπλάστες." },
            new DocumentPage { PageNumber = 4, Text = "Παράγεται οξυγόνο." },
        ]),
        _studySets, _ai, new GeneratedStudySetValidator(), TimeProvider.System, NullLogger<StudySetService>.Instance);

    private CreateStudySetRequest Request() => new(_section.Id, "hard", Counts);

    private static AiResult ValidJson(int page = 3) => AiResult.Ok(TestStudySets.ModelJson(TestStudySets.Valid(Counts, page)));

    [Fact]
    public async Task Valid_output_is_saved_with_server_ids_and_section_title()
    {
        _ai.Returns(ValidJson());

        var result = await Service().CreateAsync(Request(), CancellationToken.None);

        Assert.Null(result.ErrorCode);
        var content = result.StudySet!.Content;
        Assert.Equal(_section.Title, content.SectionTitle);
        Assert.Equal("hard", content.Difficulty);
        var ids = content.Theory.Select(x => x.Id).Concat(content.MultipleChoice.Select(x => x.Id)).Concat(content.FillBlanks.Select(x => x.Id)).ToList();
        Assert.All(ids, id => Assert.False(string.IsNullOrEmpty(id)));
        Assert.Equal(ids.Count, ids.Distinct().Count());

        var saved = Assert.Single(_studySets.Saved);
        Assert.Equal(_section.Id, saved.SectionId);
        Assert.Equal("hard", saved.Difficulty);
        Assert.Contains("\"sectionTitle\"", saved.Content);
        Assert.Single(_ai.Calls);
    }

    [Fact]
    public async Task Prompt_contains_only_section_pages_with_markers_and_counts()
    {
        _ai.Returns(ValidJson());

        await Service().CreateAsync(Request(), CancellationToken.None);

        var (system, user) = Assert.Single(_ai.Calls);
        Assert.Equal(GenerateStudySetPrompt.System, system);
        Assert.Contains("[σελ. 3]\nΗ φωτοσύνθεση γίνεται στους χλωροπλάστες.", user.Replace("\r\n", "\n"));
        Assert.Contains("[σελ. 4]", user);
        Assert.DoesNotContain("Εκτός ενότητας", user);
        Assert.Contains("- multipleChoice: 2", user);
        Assert.Contains("Δυσκολία: hard", user);
    }

    [Fact]
    public async Task Invalid_output_is_retried_once_with_the_errors()
    {
        _ai.Returns(ValidJson(page: 9), ValidJson());

        var result = await Service().CreateAsync(Request(), CancellationToken.None);

        Assert.Null(result.ErrorCode);
        Assert.Equal(2, _ai.Calls.Count);
        Assert.Contains("απορρίφθηκε", _ai.Calls[1].User);
        Assert.Contains("page", _ai.Calls[1].User);
    }

    [Fact]
    public async Task Invalid_output_twice_returns_ai_invalid_output_and_saves_nothing()
    {
        _ai.Returns(AiResult.Ok("{not json"), ValidJson(page: 9));

        var result = await Service().CreateAsync(Request(), CancellationToken.None);

        Assert.Equal(ErrorCodes.AiInvalidOutput, result.ErrorCode);
        Assert.Equal(StudySetService.MaxAttempts, _ai.Calls.Count);
        Assert.Empty(_studySets.Saved);
    }

    [Fact]
    public async Task Truncated_output_is_retried()
    {
        _ai.Returns(AiResult.Fail(ErrorCodes.AiInvalidOutput), ValidJson());

        var result = await Service().CreateAsync(Request(), CancellationToken.None);

        Assert.Null(result.ErrorCode);
        Assert.Equal(2, _ai.Calls.Count);
    }

    [Theory]
    [InlineData(ErrorCodes.AiFailed)]
    [InlineData(ErrorCodes.AiNotConfigured)]
    [InlineData(ErrorCodes.AiRefused)]
    public async Task Provider_errors_are_not_retried(string code)
    {
        _ai.Returns(AiResult.Fail(code), ValidJson());

        var result = await Service().CreateAsync(Request(), CancellationToken.None);

        Assert.Equal(code, result.ErrorCode);
        Assert.Single(_ai.Calls);
    }

    [Fact]
    public async Task Unknown_section_returns_not_found_without_calling_ai()
    {
        var result = await Service().CreateAsync(new CreateStudySetRequest(Guid.NewGuid(), "easy", Counts), CancellationToken.None);

        Assert.Equal(ErrorCodes.NotFound, result.ErrorCode);
        Assert.Empty(_ai.Calls);
    }

    [Fact]
    public async Task Get_returns_saved_content()
    {
        _ai.Returns(ValidJson());
        var service = Service();
        var created = (await service.CreateAsync(Request(), CancellationToken.None)).StudySet!;

        var fetched = await service.GetAsync(created.Id, CancellationToken.None);

        Assert.NotNull(fetched);
        Assert.Equal(created.Content.Theory.Select(t => t.Id), fetched.Content.Theory.Select(t => t.Id));
        Assert.Equal(created.Content.MultipleChoice[0].Options, fetched.Content.MultipleChoice[0].Options);
    }

    [Fact]
    public void Output_schema_is_closed_and_has_no_ids()
    {
        var schema = GenerateStudySetPrompt.OutputSchema();

        void Check(JsonNode? node)
        {
            if (node is not JsonObject obj) return;
            if ((string?)obj["type"] == "object")
            {
                Assert.False((bool)obj["additionalProperties"]!);
                var props = obj["properties"]!.AsObject();
                Assert.DoesNotContain("id", props.Select(p => p.Key));
                Assert.Equal(props.Count, obj["required"]!.AsArray().Count);
                foreach (var (_, child) in props) Check(child);
            }
            Check(obj["items"]);
        }

        Check(schema);
        Assert.Equal(8, schema["properties"]!.AsObject().Count);
    }

    [Fact]
    public async Task Anthropic_client_without_key_returns_ai_not_configured()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Anthropic:ApiKey"] = "" }).Build();
        using var client = new AnthropicAiClient(config, NullLogger<AnthropicAiClient>.Instance);

        var result = await client.CompleteJsonAsync("s", "u", GenerateStudySetPrompt.OutputSchema(), CancellationToken.None);

        Assert.Equal(ErrorCodes.AiNotConfigured, result.ErrorCode);
    }

    private sealed class FakeDocumentRepository(Section section, List<DocumentPage> pages) : IDocumentRepository
    {
        public Task AddAsync(Document document, CancellationToken ct) => throw new NotSupportedException();
        public Task<Document?> GetAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<DocumentPage>> GetPagesAsync(Guid documentId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<Section>> GetSectionsAsync(Guid documentId, CancellationToken ct) => throw new NotSupportedException();

        public Task<Section?> GetSectionAsync(Guid sectionId, CancellationToken ct) =>
            Task.FromResult(sectionId == section.Id ? section : null);

        public Task<IReadOnlyList<DocumentPage>> GetPagesAsync(Guid documentId, int fromPage, int toPage, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<DocumentPage>>(pages.Where(p => p.PageNumber >= fromPage && p.PageNumber <= toPage).ToList());
    }

    private sealed class FakeStudySetRepository : IStudySetRepository
    {
        public List<StudySetEntity> Saved { get; } = [];

        public Task AddAsync(StudySetEntity studySet, CancellationToken ct)
        {
            Saved.Add(studySet);
            return Task.CompletedTask;
        }

        public Task<StudySetEntity?> GetAsync(Guid id, CancellationToken ct) =>
            Task.FromResult(Saved.FirstOrDefault(s => s.Id == id));
    }
}
