using System.Text.Json;
using System.Text.Json.Nodes;
using Studeo.Api.Contracts;
using Studeo.Api.Prompts;
using Studeo.Api.Services;

namespace Studeo.Tests;

public static class TestStudySets
{
    public static readonly QuestionCounts SmallCounts = new(2, 1, 2, 1, 2, 1);

    /// <summary>A StudySet that satisfies <paramref name="counts"/>, every item on <paramref name="page"/>.</summary>
    public static StudySet Valid(QuestionCounts counts, int page = 1) => new()
    {
        SectionTitle = "Ενότητα",
        Difficulty = "medium",
        Theory = Enumerable.Range(1, counts.Theory)
            .Select(i => new TheoryItem { Concept = $"Έννοια {i}", Explanation = "Εξήγηση.", Page = page }).ToList(),
        OpenQuestions = Enumerable.Range(1, counts.OpenQuestions)
            .Select(i => new OpenQuestion { Question = $"Ερώτηση {i};", Answer = "Απάντηση.", Page = page }).ToList(),
        TrueFalse = Enumerable.Range(1, counts.TrueFalse)
            .Select(i => new TrueFalseItem { Statement = $"Πρόταση {i}.", IsTrue = i % 2 == 0, Justification = "Γιατί.", Page = page }).ToList(),
        Matching = Enumerable.Range(1, counts.Matching)
            .Select(i => new MatchingItem
            {
                Instructions = "Αντιστοίχισε.",
                Pairs = Enumerable.Range(1, 4).Select(j => new MatchingPair { Left = $"Α{j}", Right = $"Β{j}" }).ToList(),
                Page = page,
            }).ToList(),
        MultipleChoice = Enumerable.Range(1, counts.MultipleChoice)
            .Select(i => new MultipleChoiceItem
            {
                Question = $"Ποιο {i};", Options = ["α", "β", "γ", "δ"], CorrectIndex = 2, Explanation = "Επειδή.", Page = page,
            }).ToList(),
        FillBlanks = Enumerable.Range(1, counts.FillBlanks)
            .Select(i => new FillBlankItem { Sentence = "Το ____ είναι οργανίδιο.", Answer = "μιτοχόνδριο", Page = page }).ToList(),
    };

    /// <summary>A teaching guide that passes the check, every key point on <paramref name="page"/>.</summary>
    public static TeachingGuide ValidGuide(int page = 1) => new()
    {
        Overview = "Η ενότητα εξηγεί τη φωτοσύνθεση.",
        Objectives = ["Να εξηγούν τη φωτοσύνθεση.", "Να αναφέρουν τα προϊόντα της."],
        Prerequisites = ["Το κύτταρο."],
        LessonPlan =
        [
            new LessonStep { Title = "Αφόρμηση", Minutes = 5, Description = "Ερώτηση: από πού παίρνει ενέργεια το φυτό;" },
            new LessonStep { Title = "Παρουσίαση", Minutes = 25, Description = "Σχήμα του χλωροπλάστη στον πίνακα." },
            new LessonStep { Title = "Ανακεφαλαίωση", Minutes = 15, Description = "Σύντομο κουίζ." },
        ],
        KeyPoints = Enumerable.Range(1, 3)
            .Select(i => new TeachingPoint { Point = $"Σημείο {i}", HowToTeach = "Με παράδειγμα.", Page = page }).ToList(),
        Misconceptions = [new Misconception { Mistake = "Τα φυτά τρώνε χώμα.", Correction = "Φτιάχνουν τροφή με το φως." }],
        DiscussionQuestions = ["Τι θα γινόταν χωρίς φως;"],
    };

    /// <summary>A student lesson that passes the check, every step and card on <paramref name="page"/>.</summary>
    public static StudentLesson ValidLesson(int page = 1) => new()
    {
        Intro = "Θα δεις πώς τα φυτά φτιάχνουν την τροφή τους.",
        Goals = ["εξηγείς τι είναι η φωτοσύνθεση", "λες πού γίνεται"],
        Steps = Enumerable.Range(1, 3).Select(i => new LessonPart
        {
            Title = $"Βήμα {i}",
            Explanation = "Η **φωτοσύνθεση** γίνεται στους χλωροπλάστες.",
            Example = "Σκέψου τον χλωροπλάστη σαν μικρό εργοστάσιο.",
            Check = new SelfCheck { Question = "Πού γίνεται;", Answer = "Στους χλωροπλάστες." },
            Page = page,
        }).ToList(),
        Flashcards = Enumerable.Range(1, 6)
            .Select(i => new Flashcard { Front = $"Όρος {i}", Back = $"Ορισμός {i}.", Page = page }).ToList(),
        MemoryTips = ["Φως + νερό + CO₂ → τροφή + οξυγόνο."],
        Mistakes = [new Misconception { Mistake = "Τα φυτά τρώνε χώμα.", Correction = "Φτιάχνουν τροφή με το φως." }],
        Summary = ["Γίνεται στους χλωροπλάστες.", "Χρειάζεται φως.", "Παράγεται οξυγόνο."],
    };

    /// <summary>The amounts a generation prompt asks for ("- theory: 4" …).</summary>
    public static QuestionCounts AskedCounts(string userPrompt)
    {
        int Asked(string type) =>
            int.Parse(System.Text.RegularExpressions.Regex.Match(userPrompt, $@"- {type}: (\d+)").Groups[1].Value);
        return new QuestionCounts(Asked("theory"), Asked("openQuestions"), Asked("trueFalse"), Asked("matching"),
            Asked("multipleChoice"), Asked("fillBlanks"));
    }

    /// <summary>JSON as the model would return it (no ids).</summary>
    public static string ModelJson(StudySet s) => JsonSerializer.Serialize(s, StudySetJson.Options);
}

public static class StudySetJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}

/// <summary>Returns queued results in order and records every call.</summary>
public class FakeAiClient : IAiClient
{
    private readonly Queue<AiResult> _results = new();
    private readonly object _gate = new();
    private Func<string, AiResult>? _answer;

    /// <summary>Every call (system, user prompt). Parts of a large request call in parallel, so the order may vary.</summary>
    public List<(string System, string User)> Calls { get; } = [];

    /// <summary>Teaching guide calls (kept apart, so tests of the questions see only their own calls).</summary>
    public List<string> GuideCalls { get; } = [];

    /// <summary>Student lesson calls (kept apart like the guide calls).</summary>
    public List<string> LessonCalls { get; } = [];

    /// <summary>The answer to a lesson call: a valid lesson (page 3) unless a test sets another.</summary>
    public Func<AiResult> Lesson { get; set; } = () => AiResult.Ok(JsonSerializer.Serialize(TestStudySets.ValidLesson(3), StudySetJson.Options));

    /// <summary>The answer to a teaching guide call: a valid guide (page 3) unless a test sets another.</summary>
    public Func<AiResult> Guide { get; set; } = () => AiResult.Ok(JsonSerializer.Serialize(TestStudySets.ValidGuide(3), StudySetJson.Options));

    public FakeAiClient Returns(params AiResult[] results)
    {
        lock (_gate)
        {
            _results.Clear();
            Calls.Clear();
            GuideCalls.Clear();
            LessonCalls.Clear();
            _answer = null;
            foreach (var r in results)
            {
                _results.Enqueue(r);
            }
        }
        return this;
    }

    /// <summary>Answers each call from its prompt (e.g. valid JSON for exactly the amounts it asks for).</summary>
    public FakeAiClient Answers(Func<string, AiResult> answer)
    {
        lock (_gate)
        {
            _results.Clear();
            Calls.Clear();
            GuideCalls.Clear();
            LessonCalls.Clear();
            _answer = answer;
        }
        return this;
    }

    /// <summary>A valid answer for exactly the amounts the prompt asks for, every item on <paramref name="page"/>.</summary>
    public FakeAiClient AnswersWhatIsAsked(int page) =>
        Answers(user => AiResult.Ok(TestStudySets.ModelJson(TestStudySets.Valid(TestStudySets.AskedCounts(user), page))));

    /// <summary>Size of the pieces the answer is streamed in (small, so keys get split across pieces).</summary>
    public int PieceSize { get; set; } = 7;

    public Task<AiResult> CompleteJsonAsync(string system, string user, JsonObject schema, IAiOutputObserver? output, CancellationToken ct)
    {
        AiResult result;
        lock (_gate)
        {
            if (system == TeachingGuidePrompt.System)
            {
                GuideCalls.Add(user);
                return Task.FromResult(Guide());
            }
            if (system == StudentLessonPrompt.System)
            {
                LessonCalls.Add(user);
                return Task.FromResult(Lesson());
            }
            Calls.Add((system, user));
            result = _answer is not null ? _answer(user)
                : _results.Count > 0 ? _results.Dequeue()
                : AiResult.Fail("no_fake_result");
        }
        if (output is not null && result.Json is { } json)
        {
            output.Restart();
            for (var i = 0; i < json.Length; i += PieceSize) output.Append(json.Substring(i, Math.Min(PieceSize, json.Length - i)));
        }
        return Task.FromResult(result);
    }
}
