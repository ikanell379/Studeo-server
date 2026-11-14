using System.Text.Json;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.Extensions.Logging.Abstractions;
using Studeo.Api.Contracts;
using Studeo.Api.Prompts;
using Studeo.Api.Services;
using Studeo.Api.Services.Export;
using Studeo.Api.Validators;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace Studeo.Tests.Unit;

/// <summary>Science material: the lesson with worked examples and a formula sheet, and problems in the teacher's test.</summary>
public class ScienceLessonAndTestTests
{
    private readonly FakeAiClient _ai = new();

    private StudentLessonGenerator Generator() =>
        new(_ai, new GeneratedStudentLessonValidator(), NullLogger<StudentLessonGenerator>.Instance);

    private static AiResult Json(StudentLesson lesson) => AiResult.Ok(JsonSerializer.Serialize(lesson, StudySetJson.Options));

    private static readonly MaterialSubject Science = new(Subjects.Science);

    private static StudentLesson ScienceLesson()
    {
        var lesson = TestStudySets.ValidLesson(3);
        var empty = new WorkedExample();
        return lesson with
        {
            Steps =
            [
                lesson.Steps[0] with
                {
                    Worked = new WorkedExample
                    {
                        Problem = "Αυτοκίνητο με $v = 20\\,\\text{m/s}$ για $5\\,\\text{s}$.",
                        Steps = ["$\\Delta x = v t$", "$\\Delta x = 100\\,\\text{m}$"],
                        Answer = "$100\\,\\text{m}$",
                    },
                    Practice = new PracticeProblem
                    {
                        Problem = "Ποδήλατο με $v = 5\\,\\text{m/s}$ για $4\\,\\text{s}$.",
                        Hint = "Ο ίδιος τύπος.",
                        Steps = ["$\\Delta x = 5 \\cdot 4$"],
                        Answer = "$20\\,\\text{m}$",
                    },
                },
                // A definition: the schema still has the fields, empty.
                .. lesson.Steps.Skip(1).Select(s => s with { Worked = empty, Practice = new PracticeProblem() }),
            ],
            Formulas =
            [
                new FormulaEntry { Name = "Μέση ταχύτητα", Formula = "$v = \\frac{\\Delta x}{\\Delta t}$", Meaning = "$v$ σε m/s", Page = 9 },
                new FormulaEntry { Name = "Κενό", Formula = " ", Meaning = "—", Page = 3 },
            ],
        };
    }

    [Fact]
    public async Task Science_lesson_keeps_worked_examples_and_formulas_and_drops_the_empty_ones()
    {
        _ai.Lesson = () => Json(ScienceLesson());

        var result = await Generator().GenerateAsync("Κινηματική", "[σελ. 3]\nΚείμενο", 300, "easy", 3, 4, Science, CancellationToken.None);

        Assert.Null(result.ErrorCode);
        var lesson = result.Lesson!;
        Assert.Equal(2, lesson.Steps[0].Worked!.Steps.Count);
        Assert.Equal("Ο ίδιος τύπος.", lesson.Steps[0].Practice!.Hint);
        Assert.All(lesson.Steps.Skip(1), s => Assert.Null(s.Worked));
        Assert.All(lesson.Steps.Skip(1), s => Assert.Null(s.Practice));
        var formula = Assert.Single(lesson.Formulas);
        Assert.Equal((@"v = \frac{\Delta x}{\Delta t}", 4), (formula.Formula, formula.Page)); // $ removed, page inside the section
        Assert.Contains("θετικές επιστήμες", Assert.Single(_ai.LessonCalls));
    }

    [Fact]
    public async Task A_worked_example_without_its_solution_is_rewritten()
    {
        var broken = ScienceLesson();
        broken = broken with { Steps = [broken.Steps[0] with { Worked = broken.Steps[0].Worked! with { Steps = [] } }, .. broken.Steps.Skip(1)] };
        var calls = 0;
        _ai.Lesson = () => ++calls == 1 ? Json(broken) : Json(ScienceLesson());

        var result = await Generator().GenerateAsync("Κινηματική", "κείμενο", 300, "easy", 3, 4, Science, CancellationToken.None);

        Assert.NotNull(result.Lesson);
        Assert.Equal(2, calls);
        Assert.Contains("worked steps count", _ai.LessonCalls[1]);
    }

    [Fact]
    public void Lesson_prompt_and_schema_ask_for_worked_examples_and_the_formula_sheet()
    {
        Assert.Contains("Τυπολόγιο", StudentLessonPrompt.System);
        var schema = StudentLessonPrompt.OutputSchema();
        Assert.NotNull(schema["properties"]!["formulas"]);
        var step = schema["properties"]!["steps"]!["items"]!["properties"]!;
        Assert.NotNull(step["worked"]);
        Assert.NotNull(step["practice"]);
        Assert.Contains("θετικών επιστημών", TeachingGuidePrompt.System);
    }

    // ---- Teacher's test ----

    private static StudySet WithProblems() =>
        TestStudySets.Valid(new QuestionCounts(0, 1, 0, 0, 0, 0, Problems: 2), page: 3) with { SectionTitle = "Κινηματική" };

    [Fact]
    public void Problems_are_the_last_part_of_the_test_with_room_to_write_and_the_solution_in_the_key()
    {
        var test = TestSheetBuilder.Build(WithProblems(), "", answerKey: false, showPages: false, types: null);
        var key = TestSheetBuilder.Build(WithProblems(), "", answerKey: true, showPages: false, types: null);

        var part = test.Parts[^1];
        Assert.Equal("Β. Ασκήσεις", part.Heading);
        Assert.All(part.Questions, q => Assert.Equal(10, q.AnswerLines));
        Assert.Equal(
            "1. $\\Delta x = v \\cdot t$\n2. $\\Delta x = 10 \\cdot 5 = 50\\,\\text{m}$\nΑποτέλεσμα: $\\Delta x = 50\\,\\text{m}$",
            key.Parts[^1].Questions[0].Answer);
        Assert.DoesNotContain(TestSheetBuilder.Build(WithProblems(), "", false, false, [TestQuestionTypes.OpenQuestions]).Parts,
            p => p.Heading.EndsWith("Ασκήσεις"));
    }

    [Fact]
    public void Word_answer_key_puts_each_solution_step_on_its_own_line()
    {
        var bytes = DocxTestRenderer.Render(TestSheetBuilder.Build(WithProblems(), "", answerKey: true, showPages: false, types: [TestQuestionTypes.Problems]));

        using var doc = WordprocessingDocument.Open(new MemoryStream(bytes), false);
        var answer = doc.MainDocumentPart!.Document!.Body!.Descendants<W.Run>().First(r => r.InnerText.Contains("Αποτέλεσμα"));
        Assert.Equal(2, answer.Elements<W.Break>().Count());
        Assert.Equal(3, answer.Elements<W.Text>().Count());
    }

    [Fact]
    public void Pdf_with_problems_renders()
    {
        var bytes = PdfTestRenderer.Render(TestSheetBuilder.Build(WithProblems(), "", answerKey: true, showPages: true, types: null));

        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes[..4]));
    }
}
