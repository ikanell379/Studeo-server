using System.Security.Claims;
using Studeo.Api.Contracts;

namespace Studeo.Api.Services;

/// <summary>On success Me and Tokens are set (the controller puts the tokens in httpOnly cookies).</summary>
public record AuthResult(MeResponse? Me, KeycloakTokens? Tokens, string? ErrorCode)
{
    public static AuthResult Fail(string code) => new(null, null, code);
}

/// <summary>Register / sign in / refresh / sign out through Keycloak, without the user ever seeing Keycloak.</summary>
public interface IAuthService
{
    /// <summary>Creates the account with the chosen role, then signs in.</summary>
    Task<AuthResult> RegisterAsync(RegisterRequest request, CancellationToken ct);

    Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken ct);

    Task<AuthResult> RefreshAsync(string? refreshToken, CancellationToken ct);

    /// <summary>Ends the Keycloak session if there is one. Never fails.</summary>
    Task LogoutAsync(string? refreshToken, CancellationToken ct);

    /// <summary>The user and their Studeo role, or null if the account has no role.</summary>
    MeResponse? GetMe(ClaimsPrincipal user);
}
