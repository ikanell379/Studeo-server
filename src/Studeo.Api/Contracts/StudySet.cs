namespace Studeo.Api.Contracts;

/// <summary>
/// The generated (and later edited) study material for one section. Stored as jsonb in StudySets.Content.
/// Mirrored in Studeo-UI/src/lib/types.ts. Item ids are assigned by the server, never by the model.
/// </summary>
public record StudySet
{
    public string SectionTitle { get; init; } = "";
    public string Difficulty { get; init; } = "";
    public List<TheoryItem> Theory { get; init; } = [];
    public List<OpenQuestion> OpenQuestions { get; init; } = [];
    public List<TrueFalseItem> TrueFalse { get; init; } = [];
    public List<MatchingItem> Matching { get; init; } = [];
    public List<MultipleChoiceItem> MultipleChoice { get; init; } = [];
    public List<FillBlankItem> FillBlanks { get; init; } = [];

    /// <summary>Only in teachers' material: how to teach the section (shown before the test). Null otherwise.</summary>
    public TeachingGuide? TeachingGuide { get; init; }
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
