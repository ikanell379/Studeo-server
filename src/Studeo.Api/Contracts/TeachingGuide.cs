namespace Studeo.Api.Contracts;

/// <summary>
/// Help for the teacher on how to teach one section: written by the AI from the section text only, stored in
/// <see cref="StudySet.TeachingGuide"/>. Mirrored in Studeo-UI/src/lib/types.ts.
/// </summary>
public record TeachingGuide
{
    /// <summary>What the section is about and why it matters, in a few sentences.</summary>
    public string Overview { get; init; } = "";

    /// <summary>Learning goals: what the students should be able to do afterwards.</summary>
    public List<string> Objectives { get; init; } = [];

    /// <summary>What the students should already know (may be empty).</summary>
    public List<string> Prerequisites { get; init; } = [];

    /// <summary>The lesson step by step, with minutes.</summary>
    public List<LessonStep> LessonPlan { get; init; } = [];

    /// <summary>The key points of the text and how to explain each one.</summary>
    public List<TeachingPoint> KeyPoints { get; init; } = [];

    /// <summary>Mistakes students usually make here, and how to correct them.</summary>
    public List<Misconception> Misconceptions { get; init; } = [];

    /// <summary>Questions to ask the class during the lesson.</summary>
    public List<string> DiscussionQuestions { get; init; } = [];
}

public record LessonStep
{
    public string Title { get; init; } = "";
    public int Minutes { get; init; }
    public string Description { get; init; } = "";
}

public record TeachingPoint
{
    public string Point { get; init; } = "";
    public string HowToTeach { get; init; } = "";
    public int Page { get; init; }
}

public record Misconception
{
    public string Mistake { get; init; } = "";
    public string Correction { get; init; } = "";
}

public static class TeachingGuideLimits
{
    public const int MinObjectives = 2;
    public const int MaxObjectives = 8;
    public const int MaxPrerequisites = 6;
    public const int MinLessonSteps = 3;
    public const int MaxLessonSteps = 8;
    public const int MaxStepMinutes = 90;
    public const int MinKeyPoints = 3;
    public const int MaxKeyPoints = 15;
    public const int MaxMisconceptions = 8;
    public const int MaxDiscussionQuestions = 8;
}
