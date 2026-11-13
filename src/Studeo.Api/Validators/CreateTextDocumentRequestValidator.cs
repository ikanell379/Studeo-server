using FluentValidation;
using Studeo.Api.Contracts;

namespace Studeo.Api.Validators;

public class CreateTextDocumentRequestValidator : AbstractValidator<CreateTextDocumentRequest>
{
    public CreateTextDocumentRequestValidator()
    {
        RuleFor(r => r.Text)
            .Must(t => !string.IsNullOrWhiteSpace(t)).WithErrorCode(ErrorCodes.TextMissing)
            .WithMessage("The text must not be empty.")
            .Must(t => t is null || t.Length <= TextDocumentLimits.MaxTextLength).WithErrorCode(ErrorCodes.TextTooLong)
            .WithMessage($"The text can have at most {TextDocumentLimits.MaxTextLength:N0} characters.");

        RuleFor(r => r.Title)
            .Must(t => t is null || t.Trim().Length <= TextDocumentLimits.MaxTitleLength).WithErrorCode(ErrorCodes.InvalidRequest)
            .WithMessage($"The title can have at most {TextDocumentLimits.MaxTitleLength} characters.");
    }
}
