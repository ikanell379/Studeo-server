using FluentValidation;
using Studeo.Api.Contracts;

namespace Studeo.Api.Validators;

public class UploadDocumentRequestValidator : AbstractValidator<UploadDocumentRequest>
{
    public UploadDocumentRequestValidator()
    {
        RuleFor(r => r.File)
            .NotNull().WithErrorCode(ErrorCodes.FileMissing).WithMessage("A PDF, Word (.docx), PowerPoint (.pptx, .ppt) or text (.txt, .md) file is required.");

        When(r => r.File is not null, () =>
        {
            RuleFor(r => r.File!.Length)
                .GreaterThan(0).WithErrorCode(ErrorCodes.FileEmpty).WithMessage("The file is empty.")
                .LessThanOrEqualTo(DocumentLimits.MaxFileBytes).WithErrorCode(ErrorCodes.FileTooLarge)
                .WithMessage("The file is larger than 20 MB.");

            RuleFor(r => r.File!.FileName)
                .Must(name => DocumentKinds.FromFileName(name) is not null)
                .WithErrorCode(ErrorCodes.UnsupportedFile)
                .WithMessage("Only PDF, Word (.docx), PowerPoint (.pptx, .ppt) and text (.txt, .md) files are accepted.");
        });
    }
}
