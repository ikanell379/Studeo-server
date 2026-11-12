using System.Text.Json;
using System.Text.Json.Nodes;
using Studeo.Api.Contracts;
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

    public List<(string System, string User)> Calls { get; } = [];

    public FakeAiClient Returns(params AiResult[] results)
    {
        _results.Clear();
        Calls.Clear();
        foreach (var r in results)
        {
            _results.Enqueue(r);
        }
        return this;
    }

    public Task<AiResult> CompleteJsonAsync(string system, string user, JsonObject schema, CancellationToken ct)
    {
        Calls.Add((system, user));
        return Task.FromResult(_results.Count > 0 ? _results.Dequeue() : AiResult.Fail("no_fake_result"));
    }
}
