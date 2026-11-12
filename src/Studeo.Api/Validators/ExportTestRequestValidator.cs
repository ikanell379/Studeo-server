using FluentValidation;
using Studeo.Api.Contracts;

namespace Studeo.Api.Validators;

public class ExportTestRequestValidator : AbstractValidator<ExportTestRequest>
{
    public ExportTestRequestValidator()
    {
        RuleFor(r => r.Format).Must(f => ExportFormats.All.Contains(f))
            .WithMessage($"Format must be one of: {string.Join(", ", ExportFormats.All)}.");
        RuleFor(r => r.Variant).Must(v => ExportVariants.All.Contains(v))
            .WithMessage($"Variant must be one of: {string.Join(", ", ExportVariants.All)}.");
        RuleFor(r => r.Title).MaximumLength(TestQuestionTypes.MaxTitleLength);
        RuleForEach(r => r.Types).Must(t => TestQuestionTypes.All.Contains(t))
            .WithMessage($"Types must be among: {string.Join(", ", TestQuestionTypes.All)}.");
    }
}
