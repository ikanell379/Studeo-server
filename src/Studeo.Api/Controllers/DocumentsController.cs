using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Studeo.Api.Auth;
using Studeo.Api.Contracts;
using Studeo.Api.Services;

namespace Studeo.Api.Controllers;

[ApiController]
[Authorize(Roles = Roles.Any)]
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
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
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

        var result = await documentService.UploadAsync(User.UserId(), file.FileName, content, request.CourseId, ct);
        switch (result.ErrorCode)
        {
            case null:
                break;
            case ErrorCodes.DuplicateDocument:
                var duplicate = Error(StatusCodes.Status409Conflict, result.ErrorCode, null);
                ((ProblemDetails)duplicate.Value!).Extensions["document"] = result.Existing;
                return duplicate;
            case ErrorCodes.NotFound:
                return Error(StatusCodes.Status404NotFound, result.ErrorCode, null);
            default:
                return Error(StatusCodes.Status422UnprocessableEntity, result.ErrorCode, null);
        }

        return CreatedAtAction(nameof(Get), new { id = result.Document!.Id }, result.Document);
    }

    /// <summary>The signed-in user's PDFs ("Τα αρχεία μου"), newest first.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<DocumentSummaryResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<DocumentSummaryResponse>>> List(CancellationToken ct) =>
        Ok(await documentService.ListAsync(User.UserId(), ct));

    /// <summary>One of the signed-in user's PDFs; someone else's gives 404 like a missing one.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<DocumentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DocumentResponse>> Get(Guid id, CancellationToken ct)
    {
        var document = await documentService.GetAsync(id, User.UserId(), ct);
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
        var sections = await documentService.GetSectionsAsync(id, User.UserId(), ct);
        return sections is null
            ? Error(StatusCodes.Status404NotFound, ErrorCodes.NotFound, null)
            : Ok(sections);
    }

    /// <summary>Files the PDF (with its quizzes) under one of the user's courses; courseId null takes it out.</summary>
    [HttpPut("{id:guid}/course")]
    [ProducesResponseType<DocumentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DocumentResponse>> Move(Guid id, MoveDocumentRequest request, CancellationToken ct)
    {
        var result = await documentService.MoveAsync(id, User.UserId(), request.CourseId, ct);
        return result.ErrorCode is null
            ? Ok(result.Document)
            : Error(StatusCodes.Status404NotFound, result.ErrorCode, null);
    }

    /// <summary>
    /// Deletes the PDF for good, with everything made from it: its text, sections and all its quizzes.
    /// The same file can then be uploaded again.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) =>
        await documentService.DeleteAsync(id, User.UserId(), ct)
            ? NoContent()
            : Error(StatusCodes.Status404NotFound, ErrorCodes.NotFound, null);

    private ObjectResult Error(int status, string code, string? detail)
    {
        var problem = ProblemDetailsFactory.CreateProblemDetails(HttpContext, status, title: code, detail: detail);
        problem.Extensions["code"] = code;
        return new ObjectResult(problem) { StatusCode = status };
    }
}
