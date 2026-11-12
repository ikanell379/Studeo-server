using FluentValidation;
using Studeo.Api.Contracts;

namespace Studeo.Api.Validators;

public class UploadDocumentRequestValidator : AbstractValidator<UploadDocumentRequest>
{
    public UploadDocumentRequestValidator()
    {
        RuleFor(r => r.File)
            .NotNull().WithErrorCode(ErrorCodes.FileMissing).WithMessage("A PDF file is required.");

        When(r => r.File is not null, () =>
        {
            RuleFor(r => r.File!.Length)
                .GreaterThan(0).WithErrorCode(ErrorCodes.FileEmpty).WithMessage("The file is empty.")
                .LessThanOrEqualTo(DocumentLimits.MaxFileBytes).WithErrorCode(ErrorCodes.FileTooLarge)
                .WithMessage("The file is larger than 20 MB.");

            RuleFor(r => r.File!.FileName)
                .Must(name => string.Equals(Path.GetExtension(name), ".pdf", StringComparison.OrdinalIgnoreCase))
                .WithErrorCode(ErrorCodes.NotPdf).WithMessage("Only PDF files are accepted.");
        });
    }
}
