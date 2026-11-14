namespace Studeo.Api.Contracts;

/// <summary>
/// The generated (and later edited) study material for one section. Stored as jsonb in StudySets.Content.
/// Mirrored in Studeo-UI/src/lib/types.ts. Item ids are assigned by the server, never by the model.
/// </summary>
public record StudySet
{
    public string SectionTitle { get; init; } = "";
    public string Difficulty { get; init; } = "";

    /// <summary>The kind of material it was made as (general / science / programming); "general" for older sets.</summary>
    public string Subject { get; init; } = Subjects.General;

    /// <summary>For programming: the language of its code (null when unknown or not programming).</summary>
    public string? Language { get; init; }
    public List<TheoryItem> Theory { get; init; } = [];
    public List<OpenQuestion> OpenQuestions { get; init; } = [];
    public List<TrueFalseItem> TrueFalse { get; init; } = [];
    public List<MatchingItem> Matching { get; init; } = [];
    public List<MultipleChoiceItem> MultipleChoice { get; init; } = [];
    public List<FillBlankItem> FillBlanks { get; init; } = [];

    /// <summary>Only in science material: problems to solve, with hints and a step-by-step solution.</summary>
    public List<ProblemItem> Problems { get; init; } = [];

    /// <summary>Only in teachers' material: how to teach the section (shown before the test). Null otherwise.</summary>
    public TeachingGuide? TeachingGuide { get; init; }

    /// <summary>Only in students' material: a tutor's lesson on the section, with flashcards. Null otherwise.</summary>
    public StudentLesson? Lesson { get; init; }
}

public record TheoryItem
{
    public string Id { get; init; } = "";
    public string Concept { get; init; } = "";
    public string Explanation { get; init; } = "";
    public int Page { get; init; }
}

public record OpenQuestion
{
    public string Id { get; init; } = "";
    public string Question { get; init; } = "";
    public string Answer { get; init; } = "";
    public int Page { get; init; }
}

public record TrueFalseItem
{
    public string Id { get; init; } = "";
    public string Statement { get; init; } = "";
    public bool IsTrue { get; init; }
    public string Justification { get; init; } = "";
    public int Page { get; init; }
}

public record MatchingItem
{
    public string Id { get; init; } = "";
    public string Instructions { get; init; } = "";
    public List<MatchingPair> Pairs { get; init; } = [];
    public int Page { get; init; }
}

public record MatchingPair
{
    public string Left { get; init; } = "";
    public string Right { get; init; } = "";
}

public record MultipleChoiceItem
{
    public string Id { get; init; } = "";
    public string Question { get; init; } = "";
    public List<string> Options { get; init; } = [];
    public int CorrectIndex { get; init; }
    public string Explanation { get; init; } = "";
    public int Page { get; init; }
}

/// <summary>The blank is marked with "____" in Sentence.</summary>
public record FillBlankItem
{
    public string Id { get; init; } = "";
    public string Sentence { get; init; } = "";
    public string Answer { get; init; } = "";
    public int Page { get; init; }
}

/// <summary>
/// A problem to solve, as in a physics, chemistry or maths exam: the statement with its data, hints that help
/// without giving the answer away, the solution step by step and the final answer (formulas in LaTeX).
/// IsNumeric: the answer is one number, NumericValue in Unit, so the student's number can be checked
/// (within <see cref="ProblemLimits.RelativeTolerance"/>); otherwise (a proof, an equation) they compare with the solution.
/// </summary>
public record ProblemItem
{
    public string Id { get; init; } = "";
    public string Statement { get; init; } = "";
    public List<string> Hints { get; init; } = [];
    public List<string> Steps { get; init; } = [];
    public string Answer { get; init; } = "";
    public bool IsNumeric { get; init; }
    public double NumericValue { get; init; }

    /// <summary>Plain text, e.g. "m/s", "mol"; "" for a pure number.</summary>
    public string Unit { get; init; } = "";

    public int Page { get; init; }
}

public static class ProblemLimits
{
    public const int MaxHints = 3;
    public const int MinSteps = 1;
    public const int MaxSteps = 10;

    /// <summary>A typed number within 2% of the answer counts as right (rounding in between steps).</summary>
    public const double RelativeTolerance = 0.02;
}
