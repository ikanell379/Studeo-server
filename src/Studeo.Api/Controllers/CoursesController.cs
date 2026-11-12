using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Studeo.Api.Auth;
using Studeo.Api.Contracts;
using Studeo.Api.Services;

namespace Studeo.Api.Controllers;

/// <summary>The courses of the user's library: folders their PDFs (with the quizzes made from them) go into.</summary>
[ApiController]
[Authorize(Roles = Roles.Any)]
[Route("api/courses")]
public class CoursesController(ICourseService courseService, IValidator<CourseNameRequest> nameValidator) : ControllerBase
{
    /// <summary>The signed-in user's courses, by name, with how many PDFs each holds.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<CourseResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CourseResponse>>> List(CancellationToken ct) =>
        Ok(await courseService.ListAsync(User.UserId(), ct));

    /// <summary>Makes a new (empty) course. 409 course_exists if the user has one with this name.</summary>
    [HttpPost]
    [ProducesResponseType<CourseResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CourseResponse>> Create(CourseNameRequest request, CancellationToken ct)
    {
        var validation = await nameValidator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            return Error(StatusCodes.Status400BadRequest, ErrorCodes.InvalidRequest, validation.Errors[0].ErrorMessage);
        }

        var result = await courseService.CreateAsync(User.UserId(), request.Name, ct);
        return result.ErrorCode is null
            ? StatusCode(StatusCodes.Status201Created, result.Course)
            : Error(StatusCodes.Status409Conflict, result.ErrorCode, null);
    }

    /// <summary>Renames one of the user's courses.</summary>
    [HttpPatch("{id:guid}")]
    [ProducesResponseType<CourseResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CourseResponse>> Rename(Guid id, CourseNameRequest request, CancellationToken ct)
    {
        var validation = await nameValidator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            return Error(StatusCodes.Status400BadRequest, ErrorCodes.InvalidRequest, validation.Errors[0].ErrorMessage);
        }

        var result = await courseService.RenameAsync(id, User.UserId(), request.Name, ct);
        return result.ErrorCode switch
        {
            null => Ok(result.Course),
            ErrorCodes.NotFound => Error(StatusCodes.Status404NotFound, result.ErrorCode, null),
            _ => Error(StatusCodes.Status409Conflict, result.ErrorCode, null),
        };
    }

    /// <summary>Deletes the course for good, with all its PDFs (their text, sections and quizzes).</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) =>
        await courseService.DeleteAsync(id, User.UserId(), ct)
            ? NoContent()
            : Error(StatusCodes.Status404NotFound, ErrorCodes.NotFound, null);

    private ObjectResult Error(int status, string code, string? detail)
    {
        var problem = ProblemDetailsFactory.CreateProblemDetails(HttpContext, status, title: code, detail: detail);
        problem.Extensions["code"] = code;
        return new ObjectResult(problem) { StatusCode = status };
    }
}
