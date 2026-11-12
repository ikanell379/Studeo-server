using System.Security.Claims;
using Studeo.Api.Auth;
using Studeo.Tests.Integration;

namespace Studeo.Tests.Unit;

public class KeycloakClaimsTests
{
    private static ClaimsIdentity Identity(params Claim[] claims) =>
        new(claims, "jwt", KeycloakClaims.Username, KeycloakClaims.Role);

    [Fact]
    public void Maps_realm_roles_to_role_claims()
    {
        var identity = Identity(new Claim(KeycloakClaims.RealmAccess, """{"roles":["teacher","offline_access"]}""", "JSON"));

        KeycloakClaims.AddRealmRoles(identity);

        var principal = new ClaimsPrincipal(identity);
        Assert.True(principal.IsInRole(Roles.Teacher));
        Assert.True(principal.IsInRole("offline_access"));
        Assert.False(principal.IsInRole(Roles.Student));
        Assert.Equal(Roles.Teacher, Roles.Of(principal));
    }

    [Fact]
    public void Teacher_wins_when_user_has_both_roles()
    {
        var identity = Identity(new Claim(KeycloakClaims.RealmAccess, """{"roles":["student","teacher"]}""", "JSON"));

        KeycloakClaims.AddRealmRoles(identity);

        Assert.Equal(Roles.Teacher, Roles.Of(new ClaimsPrincipal(identity)));
    }

    [Fact]
    public void Running_twice_does_not_duplicate_roles()
    {
        var identity = Identity(new Claim(KeycloakClaims.RealmAccess, """{"roles":["student"]}""", "JSON"));

        KeycloakClaims.AddRealmRoles(identity);
        KeycloakClaims.AddRealmRoles(identity);

        Assert.Single(identity.FindAll(KeycloakClaims.Role));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"roles":"teacher"}""")]
    [InlineData("""{"other":[]}""")]
    [InlineData("""{"roles":[1, null]}""")]
    public void Malformed_realm_access_gives_no_roles(string value)
    {
        var identity = Identity(new Claim(KeycloakClaims.RealmAccess, value));

        KeycloakClaims.AddRealmRoles(identity);

        Assert.Empty(identity.FindAll(KeycloakClaims.Role));
        Assert.Null(Roles.Of(new ClaimsPrincipal(identity)));
    }

    [Fact]
    public void No_realm_access_gives_no_role()
    {
        var identity = Identity(new Claim(KeycloakClaims.Subject, "u1"));

        KeycloakClaims.AddRealmRoles(identity);

        Assert.Null(Roles.Of(new ClaimsPrincipal(identity)));
    }

    [Fact]
    public void Reads_user_and_roles_from_a_keycloak_access_token()
    {
        var user = new TestUser("u-42", [Roles.Student, "offline_access"], "Νίκος Παπάς");

        var principal = KeycloakClaims.ReadTrustedToken(user.ToAccessToken());

        Assert.Equal("u-42", principal.FindFirst(KeycloakClaims.Subject)?.Value);
        Assert.Equal("Νίκος Παπάς", principal.FindFirst(KeycloakClaims.Name)?.Value);
        Assert.Equal(Roles.Student, Roles.Of(principal));
    }
}
