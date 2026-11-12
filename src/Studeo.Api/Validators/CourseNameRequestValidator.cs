using FluentValidation;
using Studeo.Api.Contracts;

namespace Studeo.Api.Validators;

public class CourseNameRequestValidator : AbstractValidator<CourseNameRequest>
{
    public CourseNameRequestValidator()
    {
        RuleFor(r => r.Name)
            .Must(n => !string.IsNullOrWhiteSpace(n)).WithErrorCode(ErrorCodes.InvalidRequest)
            .WithMessage("The name must not be empty.")
            .Must(n => n is null || n.Trim().Length <= CourseLimits.MaxNameLength).WithErrorCode(ErrorCodes.InvalidRequest)
            .WithMessage($"The name can have at most {CourseLimits.MaxNameLength} characters.");
    }
}
