using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Studeo.Api.Auth;
using Studeo.Api.Contracts;
using Studeo.Api.Services;

namespace Studeo.Api.Controllers;

/// <summary>
/// Sign-up / sign-in for the Studeo UI's own forms. Keycloak stays behind the API; on success the tokens are
/// set as httpOnly cookies and the user (with role) is returned.
/// </summary>
[ApiController]
[Route("api/auth")]
[AllowAnonymous]
public class AuthController(
    IAuthService authService,
    IValidator<RegisterRequest> registerValidator,
    IValidator<LoginRequest> loginValidator) : ControllerBase
{
    [HttpPost("register")]
    [ProducesResponseType<MeResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<MeResponse>> Register(RegisterRequest request, CancellationToken ct)
    {
        var validation = await registerValidator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            var first = validation.Errors[0];
            return Error(first.ErrorCode, first.ErrorMessage);
        }

        return SignedIn(await authService.RegisterAsync(request, ct));
    }

    [HttpPost("login")]
    [ProducesResponseType<MeResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<MeResponse>> Login(LoginRequest request, CancellationToken ct)
    {
        var validation = await loginValidator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            var first = validation.Errors[0];
            return Error(first.ErrorCode, first.ErrorMessage);
        }

        return SignedIn(await authService.LoginAsync(request, ct));
    }

    /// <summary>New tokens from the refresh-token cookie (the access token lives 5 minutes).</summary>
    [HttpPost("refresh")]
    [ProducesResponseType<MeResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<MeResponse>> Refresh(CancellationToken ct)
    {
        var result = await authService.RefreshAsync(AuthCookies.ReadRefresh(Request), ct);
        if (result.ErrorCode is ErrorCodes.SessionExpired or ErrorCodes.NoRole) AuthCookies.Clear(Response);
        return SignedIn(result);
    }

    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        await authService.LogoutAsync(AuthCookies.ReadRefresh(Request), ct);
        AuthCookies.Clear(Response);
        return NoContent();
    }

    private ActionResult<MeResponse> SignedIn(AuthResult result)
    {
        if (result.ErrorCode is not null) return Error(result.ErrorCode, null);

        AuthCookies.Set(Response, result.Tokens!);
        return Ok(result.Me);
    }

    private ObjectResult Error(string code, string? detail)
    {
        var status = code switch
        {
            ErrorCodes.EmailTaken => StatusCodes.Status409Conflict,
            ErrorCodes.InvalidCredentials or ErrorCodes.SessionExpired => StatusCodes.Status401Unauthorized,
            ErrorCodes.NoRole => StatusCodes.Status403Forbidden,
            ErrorCodes.AuthFailed => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status400BadRequest, // invalid_request, invalid_email, weak_password
        };
        var problem = ProblemDetailsFactory.CreateProblemDetails(HttpContext, status, title: code, detail: detail);
        problem.Extensions["code"] = code;
        return new ObjectResult(problem) { StatusCode = status };
    }
}
