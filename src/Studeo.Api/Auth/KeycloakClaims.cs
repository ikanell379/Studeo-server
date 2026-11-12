using System.Security.Claims;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Studeo.Api.Auth;

/// <summary>Claims of a Keycloak access token (inbound claim mapping is off, so names are as in the JWT).</summary>
public static class KeycloakClaims
{
    public const string Subject = "sub";
    public const string Name = "name";
    public const string Email = "email";
    public const string Username = "preferred_username";
    public const string Role = "role";

    /// <summary>Keycloak puts realm roles in {"realm_access": {"roles": [...]}}.</summary>
    public const string RealmAccess = "realm_access";

    /// <summary>Adds a role claim for every realm role, so [Authorize(Roles = ...)] and IsInRole work.</summary>
    public static void AddRealmRoles(ClaimsIdentity identity)
    {
        foreach (var realmAccess in identity.FindAll(RealmAccess).ToList())
        {
            try
            {
                using var json = JsonDocument.Parse(realmAccess.Value);
                if (json.RootElement.ValueKind != JsonValueKind.Object
                    || !json.RootElement.TryGetProperty("roles", out var roles)
                    || roles.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var role in roles.EnumerateArray())
                {
                    var name = role.ValueKind == JsonValueKind.String ? role.GetString() : null;
                    if (!string.IsNullOrEmpty(name) && !identity.HasClaim(identity.RoleClaimType, name))
                    {
                        identity.AddClaim(new Claim(identity.RoleClaimType, name));
                    }
                }
            }
            catch (JsonException)
            {
                // Not the shape we expect: no roles from this claim.
            }
        }
    }

    /// <summary>
    /// Reads an access token the API just received from Keycloak over its own back channel
    /// (no signature check needed here; every later request is fully validated by JWT bearer).
    /// </summary>
    public static ClaimsPrincipal ReadTrustedToken(string accessToken)
    {
        var token = new JsonWebTokenHandler().ReadJsonWebToken(accessToken);
        var identity = new ClaimsIdentity(token.Claims, "keycloak", Username, Role);
        AddRealmRoles(identity);
        return new ClaimsPrincipal(identity);
    }
}
