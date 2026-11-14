using FluentValidation;
using Studeo.Api.Contracts;

namespace Studeo.Api.Validators;

/// <summary>
/// AI output plus what was asked for, so the validator can check counts and page numbers. Each list must have between
/// MinCounts (default: exactly Counts) and Counts items.
/// </summary>
public record GeneratedStudySet(StudySet StudySet, QuestionCounts Counts, int StartPage, int EndPage, QuestionCounts? MinCounts = null)
{
    public QuestionCounts Min => MinCounts ?? Counts;
}

/// <summary>Validates the model's StudySet against the request: counts, non-empty fields, pages inside the section.</summary>
public class GeneratedStudySetValidator : AbstractValidator<GeneratedStudySet>
{
    public GeneratedStudySetValidator()
    {
        CountRule("theory", s => s.Theory.Count, c => c.Theory);
        CountRule("openQuestions", s => s.OpenQuestions.Count, c => c.OpenQuestions);
        CountRule("trueFalse", s => s.TrueFalse.Count, c => c.TrueFalse);
        CountRule("matching", s => s.Matching.Count, c => c.Matching);
        CountRule("multipleChoice", s => s.MultipleChoice.Count, c => c.MultipleChoice);
        CountRule("fillBlanks", s => s.FillBlanks.Count, c => c.FillBlanks);
        CountRule("problems", s => s.Problems.Count, c => c.Problems);

        RuleForEach(g => g.StudySet.Theory).ChildRules(i =>
        {
            i.RuleFor(x => x.Concept).NotEmpty();
            i.RuleFor(x => x.Explanation).NotEmpty();
        });

        RuleForEach(g => g.StudySet.OpenQuestions).ChildRules(i =>
        {
            i.RuleFor(x => x.Question).NotEmpty();
            i.RuleFor(x => x.Answer).NotEmpty();
        });

        RuleForEach(g => g.StudySet.TrueFalse).ChildRules(i =>
        {
            i.RuleFor(x => x.Statement).NotEmpty();
            i.RuleFor(x => x.Justification).NotEmpty();
        });

        RuleForEach(g => g.StudySet.Matching).ChildRules(i =>
        {
            i.RuleFor(x => x.Instructions).NotEmpty();
            i.RuleFor(x => x.Pairs.Count)
                .InclusiveBetween(StudySetLimits.MinMatchingPairs, StudySetLimits.MaxMatchingPairs).WithName("pairs count");
            i.RuleForEach(x => x.Pairs).ChildRules(p =>
            {
                p.RuleFor(x => x.Left).NotEmpty();
                p.RuleFor(x => x.Right).NotEmpty();
            });
            i.RuleFor(x => x.Pairs)
                .Must(pairs => pairs.Select(p => p.Left.Trim()).Distinct().Count() == pairs.Count
                    && pairs.Select(p => p.Right.Trim()).Distinct().Count() == pairs.Count)
                .WithMessage("Matching sides must be unique.");
        });

        RuleForEach(g => g.StudySet.MultipleChoice).ChildRules(i =>
        {
            i.RuleFor(x => x.Question).NotEmpty();
            i.RuleFor(x => x.Explanation).NotEmpty();
            i.RuleFor(x => x.Options.Count).Equal(StudySetLimits.OptionsPerMultipleChoice).WithName("options count");
            i.RuleForEach(x => x.Options).NotEmpty();
            i.RuleFor(x => x.Options)
                .Must(o => o.Select(s => s.Trim()).Distinct().Count() == o.Count)
                .WithMessage("Options must be unique.");
            i.RuleFor(x => x.CorrectIndex).InclusiveBetween(0, StudySetLimits.OptionsPerMultipleChoice - 1);
        });

        RuleForEach(g => g.StudySet.FillBlanks).ChildRules(i =>
        {
            i.RuleFor(x => x.Sentence)
                .Must(s => CountBlanks(s) == 1).WithMessage($"The sentence must contain exactly one \"{StudySetLimits.Blank}\".");
            i.RuleFor(x => x.Answer).NotEmpty();
        });

        RuleForEach(g => g.StudySet.Problems).ChildRules(i =>
        {
            i.RuleFor(x => x.Statement).NotEmpty();
            i.RuleFor(x => x.Answer).NotEmpty();
            i.RuleFor(x => x.Steps.Count).InclusiveBetween(ProblemLimits.MinSteps, ProblemLimits.MaxSteps).WithName("steps count");
            i.RuleForEach(x => x.Steps).NotEmpty();
            i.RuleFor(x => x.Hints.Count).LessThanOrEqualTo(ProblemLimits.MaxHints).WithName("hints count");
            i.RuleForEach(x => x.Hints).NotEmpty();
            i.RuleFor(x => x.NumericValue)
                .Must(double.IsFinite).When(x => x.IsNumeric)
                .WithMessage("numericValue must be a number when isNumeric is true.");
        });

        RuleFor(g => g)
            .Must(g => Pages(g.StudySet).All(p => p >= g.StartPage && p <= g.EndPage))
            .WithName("page")
            .WithMessage("Every item's page must be inside the section's page range.");
    }

    /// <summary>"multipleChoice: 9 items, exactly 10 are needed" (the retry note shows it to the model).</summary>
    private void CountRule(string type, Func<StudySet, int> count, Func<QuestionCounts, int> asked) =>
        RuleFor(g => g)
            .Must(g => count(g.StudySet) >= asked(g.Min) && count(g.StudySet) <= asked(g.Counts))
            .WithName($"{type} count")
            .WithMessage(g => asked(g.Min) == asked(g.Counts)
                ? $"{type}: {count(g.StudySet)} items, exactly {asked(g.Counts)} are needed."
                : $"{type}: {count(g.StudySet)} items, {asked(g.Min)}–{asked(g.Counts)} are needed.");

    private static int CountBlanks(string s)
    {
        var count = 0;
        for (var i = s.IndexOf(StudySetLimits.Blank, StringComparison.Ordinal); i >= 0;
             i = s.IndexOf(StudySetLimits.Blank, i + StudySetLimits.Blank.Length, StringComparison.Ordinal))
        {
            count++;
        }
        return count;
    }

    private static IEnumerable<int> Pages(StudySet s) =>
        s.Theory.Select(x => x.Page)
            .Concat(s.OpenQuestions.Select(x => x.Page))
            .Concat(s.TrueFalse.Select(x => x.Page))
            .Concat(s.Matching.Select(x => x.Page))
            .Concat(s.MultipleChoice.Select(x => x.Page))
            .Concat(s.FillBlanks.Select(x => x.Page))
            .Concat(s.Problems.Select(x => x.Page));
}
