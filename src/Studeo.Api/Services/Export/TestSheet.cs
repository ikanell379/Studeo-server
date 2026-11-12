using Studeo.Api.Contracts;

namespace Studeo.Api.Services.Export;

/// <summary>
/// A printable test, independent of the file format: the PDF and Word renderers draw the same sheet.
/// IsAnswerKey: the answer sheet (each question with its answer) instead of the worksheet.
/// </summary>
public sealed record TestSheet(string Title, string Subtitle, bool IsAnswerKey, IReadOnlyList<TestPart> Parts);

/// <summary>One group of questions of the same type, e.g. "Α. Σωστό ή Λάθος".</summary>
public sealed record TestPart(string Heading, string Instructions, IReadOnlyList<TestQuestion> Questions);

public sealed record TestQuestion(int Number, string Text)
{
    /// <summary>Source page in the PDF, shown only when the teacher asked for it.</summary>
    public int? Page { get; init; }

    /// <summary>Choices already labelled, e.g. "α) …" (multiple choice).</summary>
    public IReadOnlyList<string> Options { get; init; } = [];

    /// <summary>The two columns of a matching question.</summary>
    public TestMatching? Matching { get; init; }

    /// <summary>Written after the question on the worksheet, e.g. "Σ  /  Λ".</summary>
    public string? Mark { get; init; }

    /// <summary>Empty lines to write the answer on (worksheet only).</summary>
    public int AnswerLines { get; init; }

    /// <summary>The answer (answer key only).</summary>
    public string? Answer { get; init; }
}

/// <summary>Left column numbered "1. …", right column lettered and shuffled "α. …".</summary>
public sealed record TestMatching(IReadOnlyList<string> Left, IReadOnlyList<string> Right);

/// <summary>Builds the test from the questions of a study set.</summary>
public static class TestSheetBuilder
{
    /// <summary>A long blank to write in (fill-in-the-blanks on the worksheet).</summary>
    public const string WriteBlank = "_______________";

    private static readonly string[] PartLetters = ["Α", "Β", "Γ", "Δ", "Ε", "ΣΤ"];
    private static readonly string[] OptionLetters = ["α", "β", "γ", "δ", "ε", "στ", "ζ", "η"];

    public static TestSheet Build(StudySet set, string title, bool answerKey, bool showPages, IReadOnlyCollection<string>? types)
    {
        var wanted = types is { Count: > 0 } ? types.ToHashSet() : TestQuestionTypes.All.ToHashSet();
        int? Page(int page) => showPages ? page : null;
        var parts = new List<(string Heading, string Instructions, List<TestQuestion> Questions)>();

        if (wanted.Contains(TestQuestionTypes.TrueFalse) && set.TrueFalse.Count > 0)
        {
            parts.Add(("Σωστό ή Λάθος",
                answerKey ? "" : "Χαρακτηρίστε κάθε πρόταση ως Σωστή (Σ) ή Λανθασμένη (Λ), κυκλώνοντας το αντίστοιχο γράμμα.",
                set.TrueFalse.Select((q, i) => new TestQuestion(i + 1, q.Statement)
                {
                    Page = Page(q.Page),
                    Mark = answerKey ? null : "Σ  /  Λ",
                    Answer = answerKey ? $"{(q.IsTrue ? "Σωστό" : "Λάθος")}. {q.Justification}" : null,
                }).ToList()));
        }

        if (wanted.Contains(TestQuestionTypes.MultipleChoice) && set.MultipleChoice.Count > 0)
        {
            parts.Add(("Ερωτήσεις πολλαπλής επιλογής",
                answerKey ? "" : "Κυκλώστε τη σωστή απάντηση. Μόνο μία απάντηση είναι σωστή.",
                set.MultipleChoice.Select((q, i) => new TestQuestion(i + 1, q.Question)
                {
                    Page = Page(q.Page),
                    Options = q.Options.Select((o, j) => $"{OptionLetters[j]}) {o}").ToList(),
                    Answer = answerKey
                        ? $"{OptionLetters[q.CorrectIndex]}) {q.Options[q.CorrectIndex]}. {q.Explanation}"
                        : null,
                }).ToList()));
        }

        if (wanted.Contains(TestQuestionTypes.FillBlanks) && set.FillBlanks.Count > 0)
        {
            parts.Add(("Συμπλήρωση κενών",
                answerKey ? "" : "Συμπληρώστε τα κενά με τη λέξη ή τη φράση που λείπει.",
                set.FillBlanks.Select((q, i) => new TestQuestion(i + 1, q.Sentence.Replace(StudySetLimits.Blank, WriteBlank))
                {
                    Page = Page(q.Page),
                    Answer = answerKey ? q.Answer : null,
                }).ToList()));
        }

        if (wanted.Contains(TestQuestionTypes.Matching) && set.Matching.Count > 0)
        {
            parts.Add(("Αντιστοίχιση",
                answerKey ? "" : "Αντιστοιχίστε κάθε στοιχείο της στήλης Α με ένα στοιχείο της στήλης Β.",
                set.Matching.Select((q, i) =>
                {
                    var order = ShuffledOrder(q.Pairs.Count, q.Id);
                    // order[k] = the pair shown at letter k of the right column.
                    var letterOf = new int[q.Pairs.Count];
                    for (var k = 0; k < order.Count; k++) letterOf[order[k]] = k;
                    return new TestQuestion(i + 1, q.Instructions)
                    {
                        Page = Page(q.Page),
                        Matching = new TestMatching(
                            q.Pairs.Select((p, j) => $"{j + 1}. {p.Left}").ToList(),
                            order.Select((pair, k) => $"{OptionLetters[k]}. {q.Pairs[pair].Right}").ToList()),
                        AnswerLines = answerKey ? 0 : 1,
                        Answer = answerKey
                            ? string.Join(",  ", q.Pairs.Select((_, j) => $"{j + 1} → {OptionLetters[letterOf[j]]}"))
                            : null,
                    };
                }).ToList()));
        }

        if (wanted.Contains(TestQuestionTypes.OpenQuestions) && set.OpenQuestions.Count > 0)
        {
            parts.Add(("Ερωτήσεις ανάπτυξης",
                answerKey ? "" : "Απαντήστε στις παρακάτω ερωτήσεις.",
                set.OpenQuestions.Select((q, i) => new TestQuestion(i + 1, q.Question)
                {
                    Page = Page(q.Page),
                    AnswerLines = answerKey ? 0 : 6,
                    Answer = answerKey ? q.Answer : null,
                }).ToList()));
        }

        var sheetTitle = string.IsNullOrWhiteSpace(title) ? set.SectionTitle : title.Trim();
        return new TestSheet(
            sheetTitle,
            answerKey ? "Απαντήσεις" : set.SectionTitle == sheetTitle ? "" : set.SectionTitle,
            answerKey,
            parts.Select((p, i) => new TestPart($"{PartLetters[i]}. {p.Heading}", p.Instructions, p.Questions)).ToList());
    }

    /// <summary>
    /// The right column's order: shuffled, but always the same for the same question (so the test and its answers
    /// match), and never the pairs' own order.
    /// </summary>
    internal static IReadOnlyList<int> ShuffledOrder(int count, string seed)
    {
        var hash = 17;
        foreach (var c in seed) hash = unchecked(hash * 31 + c);
        var random = new Random(hash);
        var order = Enumerable.Range(0, count).ToArray();
        random.Shuffle(order);
        if (count > 1 && order.Select((v, i) => v == i).All(x => x))
        {
            order = [.. order.Skip(1), order[0]];
        }
        return order;
    }

    /// <summary>A file name from the title: no characters Windows or browsers refuse.</summary>
    public static string FileName(string title, bool answerKey, string extension)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(['"', '<', '>', '|', ':', '*', '?', '\\', '/']).ToHashSet();
        var clean = new string(title.Where(c => !invalid.Contains(c) && !char.IsControl(c)).ToArray()).Trim();
        if (clean.Length > 80) clean = clean[..80].Trim();
        if (clean.Length == 0) clean = "Τεστ";
        return $"{clean}{(answerKey ? " - Απαντήσεις" : "")}.{extension}";
    }
}
