using System.Security.Claims;

namespace Studeo.Api.Auth;

public static class CurrentUser
{
    /// <summary>
    /// The signed-in user's Keycloak id ("sub"), which owns their documents and study sets.
    /// Only call it on endpoints that require a signed-in user.
    /// </summary>
    public static string UserId(this ClaimsPrincipal user) =>
        user.FindFirstValue(KeycloakClaims.Subject)
        ?? throw new InvalidOperationException("The signed-in user has no 'sub' claim.");
}
