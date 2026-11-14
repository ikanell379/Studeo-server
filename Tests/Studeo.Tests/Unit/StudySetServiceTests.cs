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
        _studySets, _ai, new GeneratedStudySetValidator(), Guides(_ai), Lessons(_ai), TimeProvider.System, NullLogger<StudySetService>.Instance);

    private static TeachingGuideGenerator Guides(IAiClient ai) =>
        new(ai, new GeneratedTeachingGuideValidator(), NullLogger<TeachingGuideGenerator>.Instance);

    private static StudentLessonGenerator Lessons(IAiClient ai) =>
        new(ai, new GeneratedStudentLessonValidator(), NullLogger<StudentLessonGenerator>.Instance);

    private CreateStudySetRequest Request() => new(_section.Id, "hard", Counts);

    private static AiResult ValidJson(int page = 3) => AiResult.Ok(TestStudySets.ModelJson(TestStudySets.Valid(Counts, page)));

    [Fact]
    public async Task For_a_teacher_the_guide_is_written_alongside_and_saved()
    {
        _ai.Returns(ValidJson());
        var progress = new RecordingProgress();

        var result = await Service().CreateAsync(Request(), Owner, MaterialExtra.TeachingGuide, progress, CancellationToken.None);

        Assert.Null(result.ErrorCode);
        var guide = result.StudySet!.Content.TeachingGuide;
        Assert.NotNull(guide);
        Assert.Equal(3, guide.KeyPoints.Count);
        Assert.Contains("[σελ. 3]", Assert.Single(_ai.GuideCalls));
        Assert.Contains("\"teachingGuide\"", _studySets.Saved.Single().Content);
        Assert.All(progress.Reports, p => Assert.Equal(StudySetService.TotalItems(Counts) + StudySetService.GuideProgressWeight, p.Total));
    }

    [Fact]
    public async Task The_chosen_kind_of_material_reaches_every_prompt_and_is_stored()
    {
        _ai.Returns(ValidJson());

        var result = await Service().CreateAsync(
            Request() with { Subject = Subjects.Programming, Language = "python" }, Owner, MaterialExtra.Lesson, null, CancellationToken.None);

        Assert.Equal((Subjects.Programming, "python"), (result.StudySet!.Content.Subject, result.StudySet.Content.Language));
        Assert.Contains("προγραμματισμός (γλώσσα: Python)", _ai.Calls.Single().User);
        Assert.Contains("προγραμματισμός (γλώσσα: Python)", Assert.Single(_ai.LessonCalls));
    }

    [Fact]
    public async Task Without_a_choice_the_kind_comes_from_the_text()
    {
        _ai.Returns(ValidJson());

        var result = await Service().CreateAsync(Request(), Owner, MaterialExtra.None, null, CancellationToken.None);

        Assert.Equal((Subjects.General, (string?)null), (result.StudySet!.Content.Subject, result.StudySet.Content.Language));
        Assert.DoesNotContain("Είδος υλικού", _ai.Calls.Single().User);
    }

    [Fact]
    public async Task Without_an_extra_there_is_no_guide_or_lesson()
    {
        _ai.Returns(ValidJson());

        var result = await Service().CreateAsync(Request(), Owner, MaterialExtra.None, null, CancellationToken.None);

        Assert.Null(result.StudySet!.Content.TeachingGuide);
        Assert.Null(result.StudySet.Content.Lesson);
        Assert.Empty(_ai.GuideCalls);
        Assert.Empty(_ai.LessonCalls);
    }

    [Fact]
    public async Task For_a_student_the_lesson_is_written_alongside_and_saved()
    {
        _ai.Returns(ValidJson());

        var result = await Service().CreateAsync(Request(), Owner, MaterialExtra.Lesson, null, CancellationToken.None);

        Assert.Null(result.ErrorCode);
        var lesson = result.StudySet!.Content.Lesson;
        Assert.NotNull(lesson);
        Assert.Equal(6, lesson.Flashcards.Count);
        Assert.Null(result.StudySet.Content.TeachingGuide);
        Assert.Contains("[σελ. 3]", Assert.Single(_ai.LessonCalls));
        Assert.Empty(_ai.GuideCalls);
        Assert.Contains("\"flashcards\"", _studySets.Saved.Single().Content);
    }

    [Fact]
    public async Task A_failed_lesson_does_not_lose_the_questions_and_can_be_written_later()
    {
        _ai.Returns(ValidJson());
        _ai.Lesson = () => AiResult.Fail(ErrorCodes.AiBusy);

        var created = await Service().CreateAsync(Request(), Owner, MaterialExtra.Lesson, null, CancellationToken.None);
        Assert.Null(created.ErrorCode);
        Assert.Null(created.StudySet!.Content.Lesson);

        _ai.Lesson = () => AiResult.Ok(System.Text.Json.JsonSerializer.Serialize(TestStudySets.ValidLesson(4), StudySetJson.Options));
        var later = await Service().AddLessonAsync(created.StudySet.Id, Owner, CancellationToken.None);
        var otherUser = await Service().AddLessonAsync(created.StudySet.Id, "someone-else", CancellationToken.None);

        Assert.Null(later.ErrorCode);
        Assert.Equal(4, later.StudySet!.Content.Lesson!.Steps[0].Page);
        Assert.Equal(created.StudySet.Content.TrueFalse.Select(t => t.Id), later.StudySet.Content.TrueFalse.Select(t => t.Id));
        Assert.Equal(ErrorCodes.NotFound, otherUser.ErrorCode);
    }

    [Fact]
    public async Task A_failed_guide_does_not_lose_the_questions_and_can_be_written_later()
    {
        _ai.Returns(ValidJson());
        _ai.Guide = () => AiResult.Ok("{}");

        var created = await Service().CreateAsync(Request(), Owner, MaterialExtra.TeachingGuide, null, CancellationToken.None);
        Assert.Null(created.ErrorCode);
        Assert.Null(created.StudySet!.Content.TeachingGuide);

        _ai.Guide = () => AiResult.Ok(System.Text.Json.JsonSerializer.Serialize(TestStudySets.ValidGuide(4), StudySetJson.Options));
        var later = await Service().AddTeachingGuideAsync(created.StudySet.Id, Owner, CancellationToken.None);
        var otherUser = await Service().AddTeachingGuideAsync(created.StudySet.Id, "someone-else", CancellationToken.None);

        Assert.Null(later.ErrorCode);
        Assert.Equal(4, later.StudySet!.Content.TeachingGuide!.KeyPoints[0].Page);
        Assert.Equal(created.StudySet.Content.TrueFalse.Select(t => t.Id), later.StudySet.Content.TrueFalse.Select(t => t.Id));
        Assert.Contains("\"teachingGuide\"", _studySets.Saved.Single().Content);
        Assert.Equal(ErrorCodes.NotFound, otherUser.ErrorCode);
    }

    [Fact]
    public async Task Valid_output_is_saved_with_server_ids_and_section_title()
    {
        _ai.Returns(ValidJson());

        var result = await Service().CreateAsync(Request(), Owner, MaterialExtra.None, null, CancellationToken.None);

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

        await Service().CreateAsync(Request(), Owner, MaterialExtra.None, null, CancellationToken.None);

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

        var result = await Service().CreateAsync(Request(), Owner, MaterialExtra.None, null, CancellationToken.None);

        Assert.Null(result.ErrorCode);
        Assert.Equal(2, _ai.Calls.Count);
        Assert.Contains("απορρίφθηκε", _ai.Calls[1].User);
        Assert.Contains("page", _ai.Calls[1].User);
    }

    [Fact]
    public async Task Invalid_output_twice_returns_ai_invalid_output_and_saves_nothing()
    {
        _ai.Returns(AiResult.Ok("{not json"), ValidJson(page: 9));

        var result = await Service().CreateAsync(Request(), Owner, MaterialExtra.None, null, CancellationToken.None);

        Assert.Equal(ErrorCodes.AiInvalidOutput, result.ErrorCode);
        Assert.Equal(StudySetService.MaxAttempts, _ai.Calls.Count);
        Assert.Empty(_studySets.Saved);
    }

    [Fact]
    public async Task Truncated_output_is_retried()
    {
        _ai.Returns(AiResult.Fail(ErrorCodes.AiInvalidOutput), ValidJson());

        var result = await Service().CreateAsync(Request(), Owner, MaterialExtra.None, null, CancellationToken.None);

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

        var result = await Service().CreateAsync(Request(), Owner, MaterialExtra.None, null, CancellationToken.None);

        Assert.Equal(code, result.ErrorCode);
        Assert.Single(_ai.Calls);
    }

    [Fact]
    public async Task Unknown_section_returns_not_found_without_calling_ai()
    {
        var result = await Service().CreateAsync(new CreateStudySetRequest(Guid.NewGuid(), "easy", Counts), Owner, MaterialExtra.None, null, CancellationToken.None);

        Assert.Equal(ErrorCodes.NotFound, result.ErrorCode);
        Assert.Empty(_ai.Calls);
    }

    [Fact]
    public async Task Section_of_someone_elses_document_returns_not_found_without_calling_ai()
    {
        var result = await Service().CreateAsync(Request(), "someone-else", MaterialExtra.None, null, CancellationToken.None);

        Assert.Equal(ErrorCodes.NotFound, result.ErrorCode);
        Assert.Empty(_ai.Calls);
        Assert.Empty(_studySets.Saved);
    }

    [Fact]
    public async Task Material_is_named_by_the_user_or_after_its_section()
    {
        _ai.Returns(ValidJson());
        var named = await Service().CreateAsync(Request() with { Name = "  Επανάληψη για το τεστ  " }, Owner, MaterialExtra.None, null, CancellationToken.None);
        _ai.Returns(ValidJson());
        var unnamed = await Service().CreateAsync(Request() with { Name = "   " }, Owner, MaterialExtra.None, null, CancellationToken.None);

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
        var created = (await service.CreateAsync(Request(), Owner, MaterialExtra.None, null, CancellationToken.None)).StudySet!;

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
        Assert.Equal(9, schema["properties"]!.AsObject().Count);
    }

    private StudySetService ServiceWithPages(params DocumentPage[] pages) => new(
        new FakeDocumentRepository(_section, [.. pages]),
        _studySets, _ai, new GeneratedStudySetValidator(), Guides(_ai), Lessons(_ai), TimeProvider.System, NullLogger<StudySetService>.Instance);

    // ---- Science: problems ----

    [Fact]
    public async Task Science_material_gets_the_problems_asked_for_with_ids()
    {
        _ai.AnswersWhatIsAsked(page: 3);
        var counts = new QuestionCounts(1, 0, 0, 0, 0, 0, Problems: 2);

        var result = await Service().CreateAsync(
            new CreateStudySetRequest(_section.Id, "medium", counts, Subject: Subjects.Science), Owner, MaterialExtra.None, null, CancellationToken.None);

        Assert.Null(result.ErrorCode);
        var problems = result.StudySet!.Content.Problems;
        Assert.Equal(2, problems.Count);
        Assert.All(problems, p => Assert.Equal(32, p.Id.Length));
        Assert.Equal((true, 50.0, "m"), (problems[0].IsNumeric, problems[0].NumericValue, problems[0].Unit));
        Assert.Contains("- problems: 2", _ai.Calls.Single().User);
    }

    [Fact]
    public async Task Problems_are_not_made_for_other_material_and_alone_they_are_an_invalid_request()
    {
        _ai.AnswersWhatIsAsked(page: 3);

        var withTheory = await Service().CreateAsync(
            new CreateStudySetRequest(_section.Id, "medium", new QuestionCounts(1, 0, 0, 0, 0, 0, Problems: 2)), Owner, MaterialExtra.None, null, CancellationToken.None);
        var onlyProblems = await Service().CreateAsync(
            new CreateStudySetRequest(_section.Id, "medium", new QuestionCounts(0, 0, 0, 0, 0, 0, Problems: 2)), Owner, MaterialExtra.None, null, CancellationToken.None);

        Assert.Empty(withTheory.StudySet!.Content.Problems);
        Assert.Contains("- problems: 0", _ai.Calls.Single().User);
        Assert.Equal(ErrorCodes.InvalidRequest, onlyProblems.ErrorCode);
    }

    [Fact]
    public void Full_coverage_of_science_has_problems_and_they_are_written_in_their_own_part()
    {
        Assert.Equal(0, FullCoverage.CountsFor(1000).Problems);
        var science = FullCoverage.CountsFor(1000, science: true);
        Assert.Equal(4, science.Problems);

        var parts = StudySetService.Parts(science);
        Assert.Equal(new QuestionCounts(0, 0, 0, 0, 0, 0, science.Problems), parts[^1]);
        Assert.Equal(StudySetService.TotalItems(science), parts.Sum(StudySetService.TotalItems));
    }

    [Fact]
    public void A_problem_needs_its_solution()
    {
        var counts = new QuestionCounts(0, 0, 0, 0, 0, 0, Problems: 1);
        var valid = TestStudySets.Valid(counts, page: 3);
        var validator = new GeneratedStudySetValidator();

        Assert.True(validator.Validate(new GeneratedStudySet(valid, counts, 3, 4)).IsValid);
        var noSteps = valid with { Problems = [valid.Problems[0] with { Steps = [] }] };
        Assert.Contains(validator.Validate(new GeneratedStudySet(noSteps, counts, 3, 4)).Errors, e => e.ErrorMessage.Contains("steps count"));
        var noAnswer = valid with { Problems = [valid.Problems[0] with { Answer = "" }] };
        Assert.False(validator.Validate(new GeneratedStudySet(noAnswer, counts, 3, 4)).IsValid);
    }

    [Fact]
    public async Task Unreadable_pages_are_left_out_of_the_prompt()
    {
        _ai.Returns(ValidJson());

        var result = await ServiceWithPages(
                new DocumentPage { PageNumber = 3, Text = PageQualityTests.Garbage },
                new DocumentPage { PageNumber = 4, Text = PageQualityTests.NoisyButReadable })
            .CreateAsync(Request(), Owner, MaterialExtra.None, null, CancellationToken.None);

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
            .CreateAsync(Request(), Owner, MaterialExtra.None, null, CancellationToken.None);

        Assert.Equal(ErrorCodes.NoText, result.ErrorCode);
        Assert.Empty(_ai.Calls);
    }

    [Fact]
    public async Task Full_mode_amounts_count_only_readable_words()
    {
        var garbageWords = Sectioner.CountWords(PageQualityTests.Garbage);
        _section.WordCount = 1200 + garbageWords;
        var expected = FullCoverage.CountsFor(1200);
        _ai.AnswersWhatIsAsked(page: 4);

        await ServiceWithPages(
                new DocumentPage { PageNumber = 3, Text = PageQualityTests.Garbage },
                new DocumentPage { PageNumber = 4, Text = PageQualityTests.NoisyButReadable })
            .CreateAsync(new CreateStudySetRequest(_section.Id, "medium", null, StudySetModes.Full), Owner, MaterialExtra.None, null, CancellationToken.None);

        Assert.Contains(_ai.Calls, c => c.User.Contains($"- theory: {expected.Theory}"));
    }

    [Fact]
    public async Task Full_mode_covers_the_whole_section_with_amounts_from_its_length()
    {
        _section.WordCount = 1200;
        var expected = FullCoverage.CountsFor(1200);
        _ai.AnswersWhatIsAsked(page: 3);
        var progress = new RecordingProgress();

        var result = await Service().CreateAsync(
            new CreateStudySetRequest(_section.Id, "medium", null, StudySetModes.Full), Owner, MaterialExtra.None, progress, CancellationToken.None);

        Assert.Null(result.ErrorCode);
        var content = result.StudySet!.Content;
        Assert.Equal(expected,
            new QuestionCounts(content.Theory.Count, content.OpenQuestions.Count, content.TrueFalse.Count, content.Matching.Count,
                content.MultipleChoice.Count, content.FillBlanks.Count));
        Assert.All(_ai.Calls, c => Assert.Contains("ΠΛΗΡΟΥΣ ΚΑΛΥΨΗΣ", c.User));
        Assert.Contains(_ai.Calls, c => c.User.Contains($"- theory: {expected.Theory}"));
        Assert.All(progress.Reports, p => Assert.Equal(StudySetService.TotalItems(expected), p.Total));
    }

    private static readonly QuestionCounts Large = new(6, 3, 5, 1, 5, 3); // 23 items: written in parts

    [Fact]
    public void Small_requests_are_one_part_large_ones_three()
    {
        Assert.Equal([Counts], StudySetService.Parts(Counts));
        Assert.Equal(
            [new QuestionCounts(6, 0, 0, 0, 0, 0), new QuestionCounts(0, 3, 5, 1, 0, 0), new QuestionCounts(0, 0, 0, 0, 5, 3)],
            StudySetService.Parts(Large));
        // Only one kind of item: nothing to split.
        Assert.Equal([new QuestionCounts(15, 0, 0, 0, 0, 0)], StudySetService.Parts(new QuestionCounts(15, 0, 0, 0, 0, 0)));
        // An empty part is dropped.
        Assert.Equal(2, StudySetService.Parts(new QuestionCounts(10, 0, 0, 0, 5, 0)).Count);
    }

    [Fact]
    public async Task A_large_request_is_written_in_parallel_parts_and_put_together()
    {
        _ai.AnswersWhatIsAsked(page: 3);
        var progress = new RecordingProgress();

        var result = await Service().CreateAsync(new CreateStudySetRequest(_section.Id, "hard", Large), Owner, MaterialExtra.None, progress, CancellationToken.None);

        Assert.Null(result.ErrorCode);
        Assert.Equal(3, _ai.Calls.Count);
        Assert.All(_ai.Calls, c => Assert.Contains(GenerateStudySetPrompt.PartOnly.Trim(), c.User));
        var asked = _ai.Calls.Select(c => TestStudySets.AskedCounts(c.User)).ToList();
        Assert.Contains(new QuestionCounts(6, 0, 0, 0, 0, 0), asked);
        Assert.Contains(new QuestionCounts(0, 3, 5, 1, 0, 0), asked);
        Assert.Contains(new QuestionCounts(0, 0, 0, 0, 5, 3), asked);

        var content = result.StudySet!.Content;
        Assert.Equal((6, 3, 5, 1, 5, 3),
            (content.Theory.Count, content.OpenQuestions.Count, content.TrueFalse.Count, content.Matching.Count,
                content.MultipleChoice.Count, content.FillBlanks.Count));
        var ids = content.Theory.Select(t => t.Id).Concat(content.MultipleChoice.Select(m => m.Id)).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());

        // One bar for all three parts: up to all 23 items, never back, then saving.
        AssertNeverGoesBack(progress);
        Assert.Contains(progress.Reports, p => p.Stage == GenerationProgress.Writing && p.Done == 23);
        Assert.Equal(GenerationProgress.Saving, progress.Reports[^1].Stage);
        Assert.Single(_studySets.Saved);
    }

    /// <summary>Answers what is asked, but with <paramref name="change"/> applied to the amounts (e.g. one short).</summary>
    private void AnswersWith(Func<QuestionCounts, QuestionCounts> change) =>
        _ai.Answers(user => AiResult.Ok(TestStudySets.ModelJson(TestStudySets.Valid(change(TestStudySets.AskedCounts(user)), page: 3))));

    [Fact]
    public async Task Full_mode_accepts_a_few_items_short_without_writing_again()
    {
        _section.WordCount = 2000; // full coverage: 10 multiple choice among others
        AnswersWith(c => c.MultipleChoice > 0 ? c with { MultipleChoice = c.MultipleChoice - 1 } : c);

        var result = await Service().CreateAsync(
            new CreateStudySetRequest(_section.Id, "medium", null, StudySetModes.Full), Owner, MaterialExtra.None, null, CancellationToken.None);

        Assert.Null(result.ErrorCode);
        Assert.Equal(FullCoverage.CountsFor(2000).MultipleChoice - 1, result.StudySet!.Content.MultipleChoice.Count);
        Assert.DoesNotContain(_ai.Calls, c => c.User.Contains("απορρίφθηκε")); // no second attempt
    }

    [Fact]
    public async Task Full_mode_still_rejects_far_too_few()
    {
        _section.WordCount = 2000;
        AnswersWith(c => c.MultipleChoice > 0 ? c with { MultipleChoice = 2 } : c); // 2 of 10

        var result = await Service().CreateAsync(
            new CreateStudySetRequest(_section.Id, "medium", null, StudySetModes.Full), Owner, MaterialExtra.None, null, CancellationToken.None);

        Assert.Equal(ErrorCodes.AiInvalidOutput, result.ErrorCode);
    }

    [Fact]
    public async Task Chosen_amounts_are_exact_first_but_a_short_retry_is_accepted()
    {
        AnswersWith(c => c.MultipleChoice > 0 ? c with { MultipleChoice = c.MultipleChoice - 1 } : c); // always 4 of 5

        var result = await Service().CreateAsync(new CreateStudySetRequest(_section.Id, "hard", Large), Owner, MaterialExtra.None, null, CancellationToken.None);

        Assert.Null(result.ErrorCode);
        var retried = Assert.Single(_ai.Calls, c => c.User.Contains("απορρίφθηκε"));
        Assert.Contains("multipleChoice: 4 items, exactly 5 are needed", retried.User);
        Assert.Equal(4, result.StudySet!.Content.MultipleChoice.Count);
    }

    [Fact]
    public async Task Extra_items_are_dropped_instead_of_failing()
    {
        AnswersWith(c => c.TrueFalse > 0 ? c with { TrueFalse = c.TrueFalse + 3 } : c);

        var result = await Service().CreateAsync(new CreateStudySetRequest(_section.Id, "hard", Large), Owner, MaterialExtra.None, null, CancellationToken.None);

        Assert.Null(result.ErrorCode);
        Assert.Equal(Large.TrueFalse, result.StudySet!.Content.TrueFalse.Count);
        Assert.Equal(3, _ai.Calls.Count); // accepted on the first try
    }

    [Theory]
    [InlineData(10, 7)]
    [InlineData(3, 3)]
    [InlineData(1, 1)]
    [InlineData(0, 0)]
    public void Tolerated_is_70_percent_of_asked_at_least_one(int asked, int min) =>
        Assert.Equal(min, StudySetService.Tolerated(new QuestionCounts(asked, 0, 0, 0, 0, 0)).Theory);

    [Fact]
    public async Task The_parts_really_run_at_the_same_time()
    {
        var ai = new SlowAiClient(TimeSpan.FromMilliseconds(300));
        var service = new StudySetService(
            new FakeDocumentRepository(_section, [new DocumentPage { PageNumber = 3, Text = "Η φωτοσύνθεση." }]),
            _studySets, ai, new GeneratedStudySetValidator(), Guides(ai), Lessons(ai), TimeProvider.System, NullLogger<StudySetService>.Instance);

        var result = await service.CreateAsync(new CreateStudySetRequest(_section.Id, "hard", Large), Owner, MaterialExtra.None, null, CancellationToken.None);

        Assert.Null(result.ErrorCode);
        Assert.Equal(3, ai.MaxConcurrent);
    }

    /// <summary>Answers what is asked after a delay, and records how many calls were running at once.</summary>
    private sealed class SlowAiClient(TimeSpan delay) : IAiClient
    {
        private int _running;
        public int MaxConcurrent { get; private set; }

        public async Task<AiResult> CompleteJsonAsync(string system, string user, JsonObject schema, IAiOutputObserver? output, CancellationToken ct)
        {
            var now = Interlocked.Increment(ref _running);
            lock (this) MaxConcurrent = Math.Max(MaxConcurrent, now);
            try
            {
                await Task.Delay(delay, ct);
                return AiResult.Ok(TestStudySets.ModelJson(TestStudySets.Valid(TestStudySets.AskedCounts(user), page: 3)));
            }
            finally
            {
                Interlocked.Decrement(ref _running);
            }
        }
    }

    [Fact]
    public async Task Only_the_part_that_fails_the_check_is_written_again()
    {
        var theoryTries = 0;
        _ai.Answers(user =>
        {
            var asked = TestStudySets.AskedCounts(user);
            // The theory part's first answer has one item too few.
            if (asked.Theory > 0 && Interlocked.Increment(ref theoryTries) == 1)
            {
                asked = asked with { Theory = asked.Theory - 1 };
            }
            return AiResult.Ok(TestStudySets.ModelJson(TestStudySets.Valid(asked, page: 3)));
        });

        var result = await Service().CreateAsync(new CreateStudySetRequest(_section.Id, "hard", Large), Owner, MaterialExtra.None, null, CancellationToken.None);

        Assert.Null(result.ErrorCode);
        Assert.Equal(4, _ai.Calls.Count); // 3 parts + the theory part once more
        var retried = Assert.Single(_ai.Calls, c => c.User.Contains("απορρίφθηκε"));
        Assert.Equal(6, TestStudySets.AskedCounts(retried.User).Theory);
        Assert.Equal(6, result.StudySet!.Content.Theory.Count);
    }

    [Fact]
    public async Task A_part_that_fails_makes_the_whole_request_fail_and_nothing_is_saved()
    {
        _ai.Answers(user => TestStudySets.AskedCounts(user).MultipleChoice > 0
            ? AiResult.Fail(ErrorCodes.AiBusy)
            : AiResult.Ok(TestStudySets.ModelJson(TestStudySets.Valid(TestStudySets.AskedCounts(user), page: 3))));

        var result = await Service().CreateAsync(new CreateStudySetRequest(_section.Id, "hard", Large), Owner, MaterialExtra.None, null, CancellationToken.None);

        Assert.Equal(ErrorCodes.AiBusy, result.ErrorCode);
        Assert.Empty(_studySets.Saved);
    }

    [Fact]
    public async Task A_part_that_fails_its_check_twice_returns_ai_invalid_output()
    {
        _ai.Answers(user =>
        {
            var asked = TestStudySets.AskedCounts(user);
            if (asked.TrueFalse > 0) asked = asked with { TrueFalse = 0 }; // never what was asked
            return AiResult.Ok(TestStudySets.ModelJson(TestStudySets.Valid(asked, page: 3)));
        });

        var result = await Service().CreateAsync(new CreateStudySetRequest(_section.Id, "hard", Large), Owner, MaterialExtra.None, null, CancellationToken.None);

        Assert.Equal(ErrorCodes.AiInvalidOutput, result.ErrorCode);
        Assert.Empty(_studySets.Saved);
    }

    [Fact]
    public async Task Custom_mode_prompt_has_no_full_coverage_instructions()
    {
        _ai.Returns(ValidJson());

        await Service().CreateAsync(Request(), Owner, MaterialExtra.None, null, CancellationToken.None);

        Assert.DoesNotContain("ΠΛΗΡΟΥΣ ΚΑΛΥΨΗΣ", _ai.Calls.Single().User);
    }

    [Fact]
    public async Task Reports_real_progress_item_by_item_then_saving()
    {
        _ai.Returns(ValidJson());
        var progress = new RecordingProgress();

        var result = await Service().CreateAsync(Request(), Owner, MaterialExtra.None, progress, CancellationToken.None);

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

        await Service().CreateAsync(Request(), Owner, MaterialExtra.None, progress, CancellationToken.None);

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
            _studySets, ai, new GeneratedStudySetValidator(), Guides(ai), Lessons(ai), TimeProvider.System, NullLogger<StudySetService>.Instance);

        var result = await service.CreateAsync(Request(), Owner, MaterialExtra.None, progress, CancellationToken.None);

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

        public Task<StudySetEntity?> UpdateContentAsync(Guid id, string ownerId, string content, DateTimeOffset now, CancellationToken ct)
        {
            var saved = ownerId == Owner ? Saved.FirstOrDefault(s => s.Id == id) : null;
            if (saved is not null)
            {
                saved.Content = content;
                saved.UpdatedAt = now;
                saved.Section = Section;
            }
            return Task.FromResult(saved);
        }

        public Task<StudySetEntity?> RenameAsync(Guid id, string ownerId, string name, DateTimeOffset now, CancellationToken ct) =>
            throw new NotSupportedException();
    }
}
