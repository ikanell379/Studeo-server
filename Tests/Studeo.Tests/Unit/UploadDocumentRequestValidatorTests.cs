using Microsoft.AspNetCore.Http;
using Studeo.Api.Contracts;
using Studeo.Api.Validators;

namespace Studeo.Tests.Unit;

public class UploadDocumentRequestValidatorTests
{
    private readonly UploadDocumentRequestValidator _validator = new();

    private static IFormFile File(string name, long length) =>
        new FormFile(Stream.Null, 0, length, "file", name);

    [Fact]
    public void Valid_pdf_passes()
    {
        Assert.True(_validator.Validate(new UploadDocumentRequest { File = File("notes.PDF", 1000) }).IsValid);
    }

    [Theory]
    [InlineData(null, 0, ErrorCodes.FileMissing)]
    [InlineData("notes.pdf", 0, ErrorCodes.FileEmpty)]
    [InlineData("notes.pdf", DocumentLimits.MaxFileBytes + 1, ErrorCodes.FileTooLarge)]
    [InlineData("notes.doc", 1000, ErrorCodes.UnsupportedFile)]
    [InlineData("slides.pps", 1000, ErrorCodes.UnsupportedFile)]
    [InlineData("notes", 1000, ErrorCodes.UnsupportedFile)]
    public void Invalid_files_fail_with_code(string? name, long length, string code)
    {
        var request = new UploadDocumentRequest { File = name is null ? null : File(name, length) };

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Equal(code, result.Errors[0].ErrorCode);
    }
}
