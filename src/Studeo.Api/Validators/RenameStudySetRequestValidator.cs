using FluentValidation;
using Studeo.Api.Contracts;

namespace Studeo.Api.Validators;

public class RenameStudySetRequestValidator : AbstractValidator<RenameStudySetRequest>
{
    public RenameStudySetRequestValidator()
    {
        RuleFor(r => r.Name)
            .Must(n => !string.IsNullOrWhiteSpace(n)).WithErrorCode(ErrorCodes.InvalidRequest)
            .WithMessage("The name must not be empty.")
            .Must(n => n is null || n.Trim().Length <= StudySetLimits.MaxNameLength).WithErrorCode(ErrorCodes.InvalidRequest)
            .WithMessage($"The name can have at most {StudySetLimits.MaxNameLength} characters.");
    }
}
