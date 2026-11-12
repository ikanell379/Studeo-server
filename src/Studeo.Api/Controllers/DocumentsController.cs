using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Studeo.Api.Contracts;
using Studeo.Api.Services;

namespace Studeo.Api.Controllers;

[ApiController]
[Route("api/documents")]
public class DocumentsController(
    IDocumentService documentService,
    IValidator<UploadDocumentRequest> uploadValidator) : ControllerBase
{
    /// <summary>Uploads a PDF, extracts its text per page and stores it.</summary>
    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(DocumentLimits.MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = DocumentLimits.MaxRequestBytes)]
    [ProducesResponseType<DocumentResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<DocumentResponse>> Upload([FromForm] UploadDocumentRequest request, CancellationToken ct)
    {
        var validation = await uploadValidator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            var first = validation.Errors[0];
            return Error(StatusCodes.Status400BadRequest, first.ErrorCode, first.ErrorMessage);
        }

        var file = request.File!;
        byte[] content;
        using (var buffer = new MemoryStream((int)file.Length))
        {
            await file.CopyToAsync(buffer, ct);
            content = buffer.ToArray();
        }

        var result = await documentService.UploadAsync(file.FileName, content, ct);
        if (result.ErrorCode is not null)
        {
            return Error(StatusCodes.Status422UnprocessableEntity, result.ErrorCode, null);
        }

        return CreatedAtAction(nameof(Get), new { id = result.Document!.Id }, result.Document);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<DocumentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DocumentResponse>> Get(Guid id, CancellationToken ct)
    {
        var document = await documentService.GetAsync(id, ct);
        return document is null
            ? Error(StatusCodes.Status404NotFound, ErrorCodes.NotFound, null)
            : Ok(document);
    }

    /// <summary>The sections the document was split into (by headings, or ~3,000-word chunks).</summary>
    [HttpGet("{id:guid}/sections")]
    [ProducesResponseType<IReadOnlyList<SectionResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<SectionResponse>>> GetSections(Guid id, CancellationToken ct)
    {
        var sections = await documentService.GetSectionsAsync(id, ct);
        return sections is null
            ? Error(StatusCodes.Status404NotFound, ErrorCodes.NotFound, null)
            : Ok(sections);
    }

    private ObjectResult Error(int status, string code, string? detail)
    {
        var problem = ProblemDetailsFactory.CreateProblemDetails(HttpContext, status, title: code, detail: detail);
        problem.Extensions["code"] = code;
        return new ObjectResult(problem) { StatusCode = status };
    }
}
