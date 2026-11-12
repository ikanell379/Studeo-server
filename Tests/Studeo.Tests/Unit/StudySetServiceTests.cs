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

    private const string Owner = "owner-1";

    private readonly Section _section = new()
    {
        Id = Guid.NewGuid(), DocumentId = Guid.NewGuid(), Order = 1, Title = "Κεφάλαιο 2: Η φωτοσύνθεση", StartPage = 3, EndPage = 4,
    };

    private readonly FakeAiClient _ai = new();
    private readonly FakeStudySetRepository _studySets = new();

    public StudySetServiceTests() => _studySets.Section = _section;

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

        var result = await Service().CreateAsync(Request(), Owner, null, CancellationToken.None);

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

        await Service().CreateAsync(Request(), Owner, null, CancellationToken.None);

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

        var result = await Service().CreateAsync(Request(), Owner, null, CancellationToken.None);

        Assert.Null(result.ErrorCode);
        Assert.Equal(2, _ai.Calls.Count);
        Assert.Contains("απορρίφθηκε", _ai.Calls[1].User);
        Assert.Contains("page", _ai.Calls[1].User);
    }

    [Fact]
    public async Task Invalid_output_twice_returns_ai_invalid_output_and_saves_nothing()
    {
        _ai.Returns(AiResult.Ok("{not json"), ValidJson(page: 9));

        var result = await Service().CreateAsync(Request(), Owner, null, CancellationToken.None);

        Assert.Equal(ErrorCodes.AiInvalidOutput, result.ErrorCode);
        Assert.Equal(StudySetService.MaxAttempts, _ai.Calls.Count);
        Assert.Empty(_studySets.Saved);
    }

    [Fact]
    public async Task Truncated_output_is_retried()
    {
        _ai.Returns(AiResult.Fail(ErrorCodes.AiInvalidOutput), ValidJson());

        var result = await Service().CreateAsync(Request(), Owner, null, CancellationToken.None);

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

        var result = await Service().CreateAsync(Request(), Owner, null, CancellationToken.None);

        Assert.Equal(code, result.ErrorCode);
        Assert.Single(_ai.Calls);
    }

    [Fact]
    public async Task Unknown_section_returns_not_found_without_calling_ai()
    {
        var result = await Service().CreateAsync(new CreateStudySetRequest(Guid.NewGuid(), "easy", Counts), Owner, null, CancellationToken.None);

        Assert.Equal(ErrorCodes.NotFound, result.ErrorCode);
        Assert.Empty(_ai.Calls);
    }

    [Fact]
    public async Task Section_of_someone_elses_document_returns_not_found_without_calling_ai()
    {
        var result = await Service().CreateAsync(Request(), "someone-else", null, CancellationToken.None);

        Assert.Equal(ErrorCodes.NotFound, result.ErrorCode);
        Assert.Empty(_ai.Calls);
        Assert.Empty(_studySets.Saved);
    }

    [Fact]
    public async Task Material_is_named_by_the_user_or_after_its_section()
    {
        _ai.Returns(ValidJson());
        var named = await Service().CreateAsync(Request() with { Name = "  Επανάληψη για το τεστ  " }, Owner, null, CancellationToken.None);
        _ai.Returns(ValidJson());
        var unnamed = await Service().CreateAsync(Request() with { Name = "   " }, Owner, null, CancellationToken.None);

        Assert.Equal("Επανάληψη για το τεστ", named.StudySet!.Name);
        Assert.Equal("Κεφάλαιο 2: Η φωτοσύνθεση", unnamed.StudySet!.Name);
        Assert.Equal(_section.DocumentId, named.StudySet.DocumentId);
    }

    [Fact]
    public void Long_section_title_is_cut_to_the_name_limit()
    {
        var name = StudySetService.NameOrDefault(null, new string('α', 300));

        Assert.Equal(StudySetLimits.MaxNameLength, name.Length);
    }

    [Fact]
    public async Task Get_returns_saved_content()
    {
        _ai.Returns(ValidJson());
        var service = Service();
        var created = (await service.CreateAsync(Request(), Owner, null, CancellationToken.None)).StudySet!;

        var fetched = await service.GetAsync(created.Id, Owner, CancellationToken.None);

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

    private StudySetService ServiceWithPages(params DocumentPage[] pages) => new(
        new FakeDocumentRepository(_section, [.. pages]),
        _studySets, _ai, new GeneratedStudySetValidator(), TimeProvider.System, NullLogger<StudySetService>.Instance);

    [Fact]
    public async Task Unreadable_pages_are_left_out_of_the_prompt()
    {
        _ai.Returns(ValidJson());

        var result = await ServiceWithPages(
                new DocumentPage { PageNumber = 3, Text = PageQualityTests.Garbage },
                new DocumentPage { PageNumber = 4, Text = PageQualityTests.NoisyButReadable })
            .CreateAsync(Request(), Owner, null, CancellationToken.None);

        var prompt = _ai.Calls.Single().User;
        Assert.DoesNotContain("[σελ. 3]", prompt);
        Assert.DoesNotContain("5quodýl3rI", prompt);
        Assert.Contains("[σελ. 4]", prompt);
        Assert.NotEqual(ErrorCodes.NoText, result.ErrorCode);
    }

    [Fact]
    public async Task A_section_with_only_unreadable_pages_returns_no_text_without_calling_the_model()
    {
        var result = await ServiceWithPages(
                new DocumentPage { PageNumber = 3, Text = PageQualityTests.Garbage },
                new DocumentPage { PageNumber = 4, Text = PageQualityTests.Garbage })
            .CreateAsync(Request(), Owner, null, CancellationToken.None);

        Assert.Equal(ErrorCodes.NoText, result.ErrorCode);
        Assert.Empty(_ai.Calls);
    }

    [Fact]
    public async Task Full_mode_amounts_count_only_readable_words()
    {
        var garbageWords = Sectioner.CountWords(PageQualityTests.Garbage);
        _section.WordCount = 1200 + garbageWords;
        var expected = FullCoverage.CountsFor(1200);
        _ai.Returns(AiResult.Ok(TestStudySets.ModelJson(TestStudySets.Valid(expected, page: 4))));

        await ServiceWithPages(
                new DocumentPage { PageNumber = 3, Text = PageQualityTests.Garbage },
                new DocumentPage { PageNumber = 4, Text = PageQualityTests.NoisyButReadable })
            .CreateAsync(new CreateStudySetRequest(_section.Id, "medium", null, StudySetModes.Full), Owner, null, CancellationToken.None);

        Assert.Contains($"- theory: {expected.Theory}", _ai.Calls.Single().User);
    }

    [Fact]
    public async Task Full_mode_covers_the_whole_section_with_amounts_from_its_length()
    {
        _section.WordCount = 1200;
        var expected = FullCoverage.CountsFor(1200);
        _ai.Returns(AiResult.Ok(TestStudySets.ModelJson(TestStudySets.Valid(expected, page: 3))));
        var progress = new RecordingProgress();

        var result = await Service().CreateAsync(
            new CreateStudySetRequest(_section.Id, "medium", null, StudySetModes.Full), Owner, progress, CancellationToken.None);

        Assert.Null(result.ErrorCode);
        Assert.Equal(expected.Theory, result.StudySet!.Content.Theory.Count);
        Assert.Equal(expected.FillBlanks, result.StudySet.Content.FillBlanks.Count);
        var prompt = _ai.Calls.Single().User;
        Assert.Contains("ΠΛΗΡΟΥΣ ΚΑΛΥΨΗΣ", prompt);
        Assert.Contains($"- theory: {expected.Theory}", prompt);
        Assert.All(progress.Reports, p => Assert.Equal(StudySetService.TotalItems(expected), p.Total));
    }

    [Fact]
    public async Task Custom_mode_prompt_has_no_full_coverage_instructions()
    {
        _ai.Returns(ValidJson());

        await Service().CreateAsync(Request(), Owner, null, CancellationToken.None);

        Assert.DoesNotContain("ΠΛΗΡΟΥΣ ΚΑΛΥΨΗΣ", _ai.Calls.Single().User);
    }

    [Fact]
    public async Task Reports_real_progress_item_by_item_then_saving()
    {
        _ai.Returns(ValidJson());
        var progress = new RecordingProgress();

        var result = await Service().CreateAsync(Request(), Owner, progress, CancellationToken.None);

        Assert.Null(result.ErrorCode);
        var total = StudySetService.TotalItems(Counts);
        var writing = progress.Reports.Where(p => p.Stage == GenerationProgress.Writing).Select(p => p.Done).ToList();
        Assert.Equal(GenerationProgress.Reading, progress.Reports[0].Stage);
        Assert.Equal(Enumerable.Range(1, total), writing); // every finished item, in order
        Assert.Equal(GenerationProgress.Saving, progress.Reports[^1].Stage);
        Assert.All(progress.Reports, p => Assert.Equal(total, p.Total));
        var percents = progress.Reports.Select(p => p.Percent).ToList();
        Assert.Equal(percents.Order(), percents); // only goes up
        Assert.Equal(95, percents[^1]);
    }

    [Fact]
    public async Task Second_attempt_is_reported_as_attempt_2_without_the_bar_going_back()
    {
        _ai.Returns(AiResult.Ok("{\"theory\":[{\"page\":1}]}"), ValidJson());
        var progress = new RecordingProgress();

        await Service().CreateAsync(Request(), Owner, progress, CancellationToken.None);

        var second = progress.Reports.SkipWhile(p => p.Attempt == 1).ToList();
        // The first answer got one item done: the second one starts there (writing 1 of N), not back at "reading".
        Assert.Equal(new GenerationProgress(GenerationProgress.Writing, 2, 1, StudySetService.TotalItems(Counts),
            GenerationProgress.For(GenerationProgress.Writing, 2, 1, StudySetService.TotalItems(Counts)).Percent), second[0]);
        Assert.Equal(GenerationProgress.Saving, second[^1].Stage);
        Assert.Equal(2, second[^1].Attempt);
        AssertNeverGoesBack(progress);
    }

    [Fact]
    public async Task An_answer_that_starts_over_does_not_move_the_bar_back()
    {
        // The connection broke after part of the answer and the client asked again (Restart, then the full answer).
        var json = TestStudySets.ModelJson(TestStudySets.Valid(Counts, page: 3));
        var ai = new RestartingAiClient(json[..(json.Length * 3 / 4)], json);
        var progress = new RecordingProgress();
        var service = new StudySetService(
            new FakeDocumentRepository(_section,
                [new DocumentPage { PageNumber = 3, Text = "Η φωτοσύνθεση." }, new DocumentPage { PageNumber = 4, Text = "Οξυγόνο." }]),
            _studySets, ai, new GeneratedStudySetValidator(), TimeProvider.System, NullLogger<StudySetService>.Instance);

        var result = await service.CreateAsync(Request(), Owner, progress, CancellationToken.None);

        Assert.Null(result.ErrorCode);
        AssertNeverGoesBack(progress);
        Assert.DoesNotContain(progress.Reports.SkipWhile(p => p.Done == 0), p => p.Stage == GenerationProgress.Reading);
    }

    private static void AssertNeverGoesBack(RecordingProgress progress)
    {
        var percents = progress.Reports.Select(p => p.Percent).ToList();
        Assert.Equal(percents.Order(), percents);
        var done = progress.Reports.Select(p => p.Done).ToList();
        Assert.Equal(done.Order(), done);
    }

    /// <summary>Streams a partial answer, starts over (like a retry after a broken stream), then the full one.</summary>
    private sealed class RestartingAiClient(string partial, string full) : IAiClient
    {
        public Task<AiResult> CompleteJsonAsync(string system, string user, System.Text.Json.Nodes.JsonObject schema,
            IAiOutputObserver? output, CancellationToken ct)
        {
            output?.Restart();
            output?.Append(partial);
            output?.Restart();
            for (var i = 0; i < full.Length; i += 7) output?.Append(full.Substring(i, Math.Min(7, full.Length - i)));
            return Task.FromResult(AiResult.Ok(full));
        }
    }

    private sealed class RecordingProgress : IProgress<GenerationProgress>
    {
        public List<GenerationProgress> Reports { get; } = [];
        public void Report(GenerationProgress value) => Reports.Add(value);
    }

    [Fact]
    public async Task Anthropic_client_without_key_returns_ai_not_configured()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Anthropic:ApiKey"] = "" }).Build();
        using var client = new AnthropicAiClient(config, NullLogger<AnthropicAiClient>.Instance);

        var result = await client.CompleteJsonAsync("s", "u", GenerateStudySetPrompt.OutputSchema(), null, CancellationToken.None);

        Assert.Equal(ErrorCodes.AiNotConfigured, result.ErrorCode);
    }

    private sealed class FakeDocumentRepository(Section section, List<DocumentPage> pages) : IDocumentRepository
    {
        public Task AddAsync(Document document, CancellationToken ct) => throw new NotSupportedException();
        public Task<Document?> GetAsync(Guid id, string ownerId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<DocumentSummaryResponse>> ListAsync(string ownerId, CancellationToken ct) => throw new NotSupportedException();
        public Task<Document?> FindByHashAsync(string ownerId, string contentHash, CancellationToken ct) => throw new NotSupportedException();
        public Task<Document?> FindByTextHashAsync(string ownerId, string textHash, CancellationToken ct) => throw new NotSupportedException();
        public Task<Document?> SetCourseAsync(Guid id, string ownerId, Guid? courseId, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(Guid id, string ownerId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<DocumentPage>> GetPagesAsync(Guid documentId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<Section>> GetSectionsAsync(Guid documentId, CancellationToken ct) => throw new NotSupportedException();

        /// <summary>The section belongs to <see cref="Owner"/>.</summary>
        public Task<Section?> GetSectionAsync(Guid sectionId, string ownerId, CancellationToken ct) =>
            Task.FromResult(sectionId == section.Id && ownerId == Owner ? section : null);

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

        /// <summary>The section that saved sets are loaded with, like the real repository's Include.</summary>
        public Section? Section { get; set; }

        public Task<StudySetEntity?> GetAsync(Guid id, string ownerId, CancellationToken ct)
        {
            var saved = ownerId == Owner ? Saved.FirstOrDefault(s => s.Id == id) : null;
            if (saved is not null) saved.Section = Section;
            return Task.FromResult(saved);
        }

        public Task<IReadOnlyList<StudySetSummaryResponse>> ListAsync(string ownerId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<StudySetEntity?> RenameAsync(Guid id, string ownerId, string name, DateTimeOffset now, CancellationToken ct) =>
            throw new NotSupportedException();
    }
}
