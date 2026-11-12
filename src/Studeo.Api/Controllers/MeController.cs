using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Studeo.Api.Contracts;
using Studeo.Api.Services;

namespace Studeo.Api.Controllers;

[ApiController]
[Route("api/me")]
[Authorize]
public class MeController(IAuthService authService) : ControllerBase
{
    /// <summary>The signed-in user and their role (teacher / student). 401 when not signed in.</summary>
    [HttpGet]
    [ProducesResponseType<MeResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public ActionResult<MeResponse> Get()
    {
        var me = authService.GetMe(User);
        if (me is not null) return Ok(me);

        var problem = ProblemDetailsFactory.CreateProblemDetails(HttpContext, StatusCodes.Status403Forbidden, title: ErrorCodes.NoRole);
        problem.Extensions["code"] = ErrorCodes.NoRole;
        return new ObjectResult(problem) { StatusCode = StatusCodes.Status403Forbidden };
    }
}
