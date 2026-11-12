using System.Security.Claims;
using Studeo.Api.Auth;
using Studeo.Api.Contracts;

namespace Studeo.Api.Services;

public class AuthService(IKeycloakClient keycloak, ILogger<AuthService> logger) : IAuthService
{
    public async Task<AuthResult> RegisterAsync(RegisterRequest request, CancellationToken ct)
    {
        var email = NormalizeEmail(request.Email);
        try
        {
            var outcome = await keycloak.CreateUserAsync(
                email, request.Password, request.FirstName.Trim(), request.LastName.Trim(), request.Role, ct);
            switch (outcome)
            {
                case CreateUserOutcome.EmailTaken: return AuthResult.Fail(ErrorCodes.EmailTaken);
                case CreateUserOutcome.PasswordRejected: return AuthResult.Fail(ErrorCodes.WeakPassword);
            }
        }
        catch (HttpRequestException e)
        {
            logger.LogError(e, "Registration failed in Keycloak");
            return AuthResult.Fail(ErrorCodes.AuthFailed);
        }

        logger.LogInformation("Registered new {Role}", request.Role);
        return await LoginAsync(new LoginRequest(email, request.Password), ct);
    }

    public async Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        KeycloakTokens? tokens;
        try
        {
            tokens = await keycloak.PasswordSignInAsync(NormalizeEmail(request.Email), request.Password, ct);
        }
        catch (HttpRequestException e)
        {
            logger.LogError(e, "Sign-in failed in Keycloak");
            return AuthResult.Fail(ErrorCodes.AuthFailed);
        }

        return tokens is null ? AuthResult.Fail(ErrorCodes.InvalidCredentials) : FromTokens(tokens);
    }

    public async Task<AuthResult> RefreshAsync(string? refreshToken, CancellationToken ct)
    {
        if (refreshToken is null) return AuthResult.Fail(ErrorCodes.SessionExpired);

        KeycloakTokens? tokens;
        try
        {
            tokens = await keycloak.RefreshAsync(refreshToken, ct);
        }
        catch (HttpRequestException e)
        {
            logger.LogError(e, "Token refresh failed in Keycloak");
            return AuthResult.Fail(ErrorCodes.AuthFailed);
        }

        return tokens is null ? AuthResult.Fail(ErrorCodes.SessionExpired) : FromTokens(tokens);
    }

    public async Task LogoutAsync(string? refreshToken, CancellationToken ct)
    {
        if (refreshToken is null) return;
        try
        {
            await keycloak.SignOutAsync(refreshToken, ct);
        }
        catch (HttpRequestException e)
        {
            // The cookies are cleared anyway; the Keycloak session just times out on its own.
            logger.LogWarning(e, "Could not end the Keycloak session");
        }
    }

    public MeResponse? GetMe(ClaimsPrincipal user)
    {
        var id = user.FindFirstValue(KeycloakClaims.Subject);
        var role = Roles.Of(user);
        if (string.IsNullOrEmpty(id) || role is null) return null;

        return new MeResponse(
            id,
            user.FindFirstValue(KeycloakClaims.Name) ?? user.FindFirstValue(KeycloakClaims.Username) ?? "",
            user.FindFirstValue(KeycloakClaims.Email),
            role);
    }

    /// <summary>An account without a Studeo role gets no session (no cookies) at all.</summary>
    private AuthResult FromTokens(KeycloakTokens tokens)
    {
        var me = GetMe(KeycloakClaims.ReadTrustedToken(tokens.AccessToken));
        return me is null ? AuthResult.Fail(ErrorCodes.NoRole) : new AuthResult(me, tokens, null);
    }

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
}
