using FluentValidation;
using Studeo.Api.Contracts;

namespace Studeo.Api.Validators;

public class CreateStudySetRequestValidator : AbstractValidator<CreateStudySetRequest>
{
    public CreateStudySetRequestValidator()
    {
        RuleFor(r => r.SectionId).NotEmpty().WithErrorCode(ErrorCodes.InvalidRequest);

        RuleFor(r => r.Difficulty)
            .Must(d => StudySetLimits.Difficulties.Contains(d)).WithErrorCode(ErrorCodes.InvalidRequest)
            .WithMessage("Difficulty must be easy, medium or hard.");

        RuleFor(r => r.Mode)
            .Must(m => m is null || StudySetModes.All.Contains(m)).WithErrorCode(ErrorCodes.InvalidRequest)
            .WithMessage("Mode must be custom or full.");

        RuleFor(r => r.Subject)
            .Must(s => s is null || Subjects.All.Contains(s)).WithErrorCode(ErrorCodes.InvalidRequest)
            .WithMessage("Subject must be general, science or programming.");

        RuleFor(r => r.Language)
            .Must(l => l is null || ProgrammingLanguages.All.Contains(l)).WithErrorCode(ErrorCodes.InvalidRequest)
            .WithMessage($"Language must be one of: {string.Join(", ", ProgrammingLanguages.All)}.");

        RuleFor(r => r.Name)
            .MaximumLength(StudySetLimits.MaxNameLength).WithErrorCode(ErrorCodes.InvalidRequest);

        // In "full" mode the server picks the amounts, so Counts is not needed.
        RuleFor(r => r.Counts)
            .NotNull().When(r => !r.IsFull).WithErrorCode(ErrorCodes.InvalidRequest)
            .DependentRules(() =>
            {
                RuleFor(r => r.Counts!)
                    .Must(c => Values(c).All(v => v is >= 0 and <= StudySetLimits.MaxPerType))
                    .WithErrorCode(ErrorCodes.InvalidRequest)
                    .WithMessage($"Each count must be between 0 and {StudySetLimits.MaxPerType}.")
                    .Must(c => Values(c).Sum() > 0)
                    .WithErrorCode(ErrorCodes.InvalidRequest)
                    .WithMessage("Ask for at least one item.");
            }).When(r => !r.IsFull);
    }

    private static int[] Values(QuestionCounts c) =>
        [c.Theory, c.OpenQuestions, c.TrueFalse, c.Matching, c.MultipleChoice, c.FillBlanks, c.Problems];
}
