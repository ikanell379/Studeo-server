namespace Studeo.Api.Contracts;

/// <summary>
/// A lesson for the student, like a private tutor's: written by the AI from the section text only (analogies and
/// everyday examples are the AI's own), stored in <see cref="StudySet.Lesson"/>. Mirrored in Studeo-UI/src/lib/types.ts.
/// </summary>
public record StudentLesson
{
    /// <summary>«Τι θα μάθεις»: what the section is about and why it is worth knowing, in a few sentences.</summary>
    public string Intro { get; init; } = "";

    /// <summary>«Στο τέλος θα μπορείς να…»: what the student will be able to do.</summary>
    public List<string> Goals { get; init; } = [];

    /// <summary>The section explained step by step, in the order of the text.</summary>
    public List<LessonPart> Steps { get; init; } = [];

    /// <summary>Cards to revise the key terms and facts: front (term or short question), back (short answer).</summary>
    public List<Flashcard> Flashcards { get; init; } = [];

    /// <summary>Ways to remember (a mnemonic, a pattern, a picture to keep in mind); may be empty.</summary>
    public List<string> MemoryTips { get; init; } = [];

    /// <summary>Mistakes students often make here, and what is right instead.</summary>
    public List<Misconception> Mistakes { get; init; } = [];

    /// <summary>«Με μια ματιά»: the main points to keep.</summary>
    public List<string> Summary { get; init; } = [];

    /// <summary>Science lessons: «Τυπολόγιο», the section's formulas with what their symbols mean. Empty otherwise.</summary>
    public List<FormulaEntry> Formulas { get; init; } = [];
}

/// <summary>
/// One step of the lesson. Explanation: short paragraphs, "- " bullet lines and **bold** key terms (like theory).
/// Example: a worked example from the text or an everyday analogy ("" if none fits).
/// </summary>
public record LessonPart
{
    public string Title { get; init; } = "";
    public string Explanation { get; init; } = "";
    public string Example { get; init; } = "";
    public SelfCheck Check { get; init; } = new();

    /// <summary>Science lessons: a problem solved step by step for this step («Λυμένο παράδειγμα»); null otherwise.</summary>
    public WorkedExample? Worked { get; init; }

    /// <summary>Science lessons: «Δοκίμασε εσύ», a similar problem with a hint, its solution hidden; null otherwise.</summary>
    public PracticeProblem? Practice { get; init; }

    public int Page { get; init; }
}

/// <summary>A problem with its solution step by step and the result (formulas in LaTeX).</summary>
public record WorkedExample
{
    public string Problem { get; init; } = "";
    public List<string> Steps { get; init; } = [];
    public string Answer { get; init; } = "";
}

/// <summary>A problem for the student to try: a hint, then the solution step by step and the result.</summary>
public record PracticeProblem
{
    public string Problem { get; init; } = "";
    public string Hint { get; init; } = "";
    public List<string> Steps { get; init; } = [];
    public string Answer { get; init; } = "";
}

/// <summary>
/// A formula of the section: its name («Μέση ταχύτητα»), the formula in LaTeX (without $), and what each
/// symbol means with its unit.
/// </summary>
public record FormulaEntry
{
    public string Name { get; init; } = "";
    public string Formula { get; init; } = "";
    public string Meaning { get; init; } = "";
    public int Page { get; init; }
}

/// <summary>A quick question to check understanding of a step, with its answer (shown when the student asks).</summary>
public record SelfCheck
{
    public string Question { get; init; } = "";
    public string Answer { get; init; } = "";
}

public record Flashcard
{
    public string Front { get; init; } = "";
    public string Back { get; init; } = "";
    public int Page { get; init; }
}

public static class StudentLessonLimits
{
    public const int MinGoals = 2;
    public const int MaxGoals = 6;
    public const int MinSteps = 2;
    public const int MaxSteps = 12;
    public const int MinFlashcards = 4;
    public const int MaxFlashcards = 40;
    public const int MaxMemoryTips = 5;
    public const int MaxMistakes = 6;
    public const int MinSummary = 3;
    public const int MaxSummary = 10;
    public const int MaxFormulas = 25;
    public const int MaxSolutionSteps = 8;
}
