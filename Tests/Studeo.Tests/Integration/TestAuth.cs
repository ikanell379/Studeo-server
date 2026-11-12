using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Studeo.Api.Auth;
using Studeo.Api.Services;

namespace Studeo.Tests.Integration;

/// <summary>A signed-in user as Keycloak would describe them in an access token.</summary>
public record TestUser(string Id, string[] RealmRoles, string Name = "Μαρία Παπαδοπούλου")
{
    public static readonly TestUser Student = new("11111111-1111-1111-1111-111111111111", [Roles.Student]);
    public static readonly TestUser Teacher = new("22222222-2222-2222-2222-222222222222", [Roles.Teacher]);

    /// <summary>A Keycloak account without a Studeo role (e.g. made by hand in the admin console).</summary>
    public static TestUser WithoutRole() => new(Guid.NewGuid().ToString(), ["offline_access"]);

    public const string Header = "X-Test-User";

    public string ToHeader() => JsonSerializer.Serialize(this);

    public string Email => $"{Id}@example.com";

    public IEnumerable<Claim> Claims =>
    [
        new(KeycloakClaims.Subject, Id),
        new(KeycloakClaims.Name, Name),
        new(KeycloakClaims.Email, Email),
        new(KeycloakClaims.RealmAccess, JsonSerializer.Serialize(new { roles = RealmRoles }), "JSON"),
    ];

    /// <summary>An (unsigned) access token with this user's claims, like the ones Keycloak issues.</summary>
    public string ToAccessToken() => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
    {
        Claims = new Dictionary<string, object>
        {
            [KeycloakClaims.Subject] = Id,
            [KeycloakClaims.Name] = Name,
            [KeycloakClaims.Email] = Email,
            [KeycloakClaims.RealmAccess] = new Dictionary<string, object> { ["roles"] = RealmRoles },
        },
    });
}

/// <summary>
/// Replaces JWT bearer in tests: builds the same claims a Keycloak token has (incl. "realm_access")
/// from the <see cref="TestUser.Header"/> header, and maps roles with the real <see cref="KeycloakClaims.AddRealmRoles"/>.
/// No header → not signed in (401).
/// </summary>
public class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string Scheme = "Test";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(TestUser.Header, out var header))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var user = JsonSerializer.Deserialize<TestUser>(header.ToString())!;
        var identity = new ClaimsIdentity(user.Claims, Scheme, KeycloakClaims.Username, KeycloakClaims.Role);
        KeycloakClaims.AddRealmRoles(identity);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme)));
    }
}

/// <summary>An in-memory Keycloak: accounts by email, sessions by refresh token.</summary>
public class FakeKeycloakClient : IKeycloakClient
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, (string Password, TestUser User)> _accounts = [];
    private readonly Dictionary<string, TestUser> _sessions = [];

    public bool Down { get; set; }

    public List<string> SignedOut { get; } = [];

    /// <summary>Adds an account directly, as if made in the admin console.</summary>
    public void AddAccount(string email, string password, TestUser user)
    {
        lock (_lock) _accounts[email] = (password, user);
    }

    public bool HasAccount(string email)
    {
        lock (_lock) return _accounts.ContainsKey(email);
    }

    public Task<CreateUserOutcome> CreateUserAsync(string email, string password, string firstName, string lastName, string role, CancellationToken ct)
    {
        ThrowIfDown();
        lock (_lock)
        {
            if (_accounts.ContainsKey(email)) return Task.FromResult(CreateUserOutcome.EmailTaken);
            _accounts[email] = (password, new TestUser(Guid.NewGuid().ToString(), [role], $"{firstName} {lastName}"));
        }
        return Task.FromResult(CreateUserOutcome.Created);
    }

    public Task<KeycloakTokens?> PasswordSignInAsync(string email, string password, CancellationToken ct)
    {
        ThrowIfDown();
        lock (_lock)
        {
            return Task.FromResult(_accounts.TryGetValue(email, out var a) && a.Password == password ? NewSession(a.User) : null);
        }
    }

    public Task<KeycloakTokens?> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        ThrowIfDown();
        lock (_lock)
        {
            if (!_sessions.Remove(refreshToken, out var user)) return Task.FromResult<KeycloakTokens?>(null);
            return Task.FromResult(NewSession(user));
        }
    }

    public Task SignOutAsync(string refreshToken, CancellationToken ct)
    {
        ThrowIfDown();
        lock (_lock)
        {
            _sessions.Remove(refreshToken);
            SignedOut.Add(refreshToken);
        }
        return Task.CompletedTask;
    }

    private KeycloakTokens? NewSession(TestUser user)
    {
        var refresh = $"refresh-{Guid.NewGuid():N}";
        _sessions[refresh] = user;
        return new KeycloakTokens(user.ToAccessToken(), 300, refresh, 1800);
    }

    private void ThrowIfDown()
    {
        if (Down) throw new HttpRequestException("Keycloak is down");
    }
}
