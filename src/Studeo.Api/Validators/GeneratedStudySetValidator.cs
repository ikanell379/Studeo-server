using FluentValidation;
using Studeo.Api.Contracts;

namespace Studeo.Api.Validators;

/// <summary>AI output plus what was asked for, so the validator can check counts and page numbers.</summary>
public record GeneratedStudySet(StudySet StudySet, QuestionCounts Counts, int StartPage, int EndPage);

/// <summary>Validates the model's StudySet against the request: exact counts, non-empty fields, pages inside the section.</summary>
public class GeneratedStudySetValidator : AbstractValidator<GeneratedStudySet>
{
    public GeneratedStudySetValidator()
    {
        RuleFor(g => g.StudySet.Theory.Count).Equal(g => g.Counts.Theory).WithName("theory count");
        RuleFor(g => g.StudySet.OpenQuestions.Count).Equal(g => g.Counts.OpenQuestions).WithName("openQuestions count");
        RuleFor(g => g.StudySet.TrueFalse.Count).Equal(g => g.Counts.TrueFalse).WithName("trueFalse count");
        RuleFor(g => g.StudySet.Matching.Count).Equal(g => g.Counts.Matching).WithName("matching count");
        RuleFor(g => g.StudySet.MultipleChoice.Count).Equal(g => g.Counts.MultipleChoice).WithName("multipleChoice count");
        RuleFor(g => g.StudySet.FillBlanks.Count).Equal(g => g.Counts.FillBlanks).WithName("fillBlanks count");

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

        RuleFor(g => g)
            .Must(g => Pages(g.StudySet).All(p => p >= g.StartPage && p <= g.EndPage))
            .WithName("page")
            .WithMessage("Every item's page must be inside the section's page range.");
    }

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
            .Concat(s.FillBlanks.Select(x => x.Page));
}
