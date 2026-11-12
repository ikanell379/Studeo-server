namespace Studeo.Api.Services;

/// <summary>Tokens from Keycloak's token endpoint. Lifetimes in seconds.</summary>
public record KeycloakTokens(string AccessToken, int ExpiresIn, string RefreshToken, int RefreshExpiresIn);

public enum CreateUserOutcome
{
    Created,
    EmailTaken,
    PasswordRejected,
}

/// <summary>
/// The Keycloak calls Studeo needs. Only the API talks to Keycloak; the browser never does.
/// Methods throw <see cref="HttpRequestException"/> when Keycloak is down or answers unexpectedly.
/// </summary>
public interface IKeycloakClient
{
    /// <summary>Creates an enabled user (email = username) with a password and one realm role.</summary>
    Task<CreateUserOutcome> CreateUserAsync(string email, string password, string firstName, string lastName, string role, CancellationToken ct);

    /// <summary>Signs in with email + password; null if they are wrong (or the account is locked/disabled).</summary>
    Task<KeycloakTokens?> PasswordSignInAsync(string email, string password, CancellationToken ct);

    /// <summary>New tokens for a refresh token; null if the session has ended.</summary>
    Task<KeycloakTokens?> RefreshAsync(string refreshToken, CancellationToken ct);

    /// <summary>Ends the Keycloak session of this refresh token.</summary>
    Task SignOutAsync(string refreshToken, CancellationToken ct);
}
