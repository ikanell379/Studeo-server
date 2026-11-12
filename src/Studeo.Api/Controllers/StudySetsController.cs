using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Studeo.Api.Contracts;
using Studeo.Api.Services;

namespace Studeo.Api.Controllers;

[ApiController]
[Route("api/study-sets")]
public class StudySetsController(
    IStudySetService studySetService,
    IValidator<CreateStudySetRequest> createValidator) : ControllerBase
{
    /// <summary>Generates study material for a section with the AI (can take a minute), validates and stores it.</summary>
    [HttpPost]
    [ProducesResponseType<StudySetResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<StudySetResponse>> Create(CreateStudySetRequest request, CancellationToken ct)
    {
        var validation = await createValidator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            var first = validation.Errors[0];
            return Error(StatusCodes.Status400BadRequest, ErrorCodes.InvalidRequest, first.ErrorMessage);
        }

        var result = await studySetService.CreateAsync(request, ct);
        if (result.ErrorCode is not null)
        {
            return Error(StatusFor(result.ErrorCode), result.ErrorCode, null);
        }

        return CreatedAtAction(nameof(Get), new { id = result.StudySet!.Id }, result.StudySet);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<StudySetResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<StudySetResponse>> Get(Guid id, CancellationToken ct)
    {
        var studySet = await studySetService.GetAsync(id, ct);
        return studySet is null
            ? Error(StatusCodes.Status404NotFound, ErrorCodes.NotFound, null)
            : Ok(studySet);
    }

    private static int StatusFor(string code) => code switch
    {
        ErrorCodes.NotFound => StatusCodes.Status404NotFound,
        ErrorCodes.AiInvalidOutput or ErrorCodes.AiRefused => StatusCodes.Status422UnprocessableEntity,
        _ => StatusCodes.Status503ServiceUnavailable, // ai_not_configured, ai_failed
    };

    private ObjectResult Error(int status, string code, string? detail)
    {
        var problem = ProblemDetailsFactory.CreateProblemDetails(HttpContext, status, title: code, detail: detail);
        problem.Extensions["code"] = code;
        return new ObjectResult(problem) { StatusCode = status };
    }
}
