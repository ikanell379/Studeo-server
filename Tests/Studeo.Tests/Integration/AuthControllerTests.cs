using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Studeo.Api.Auth;
using Studeo.Api.Contracts;

namespace Studeo.Tests.Integration;

public class AuthControllerTests(StudeoApiFactory factory) : IClassFixture<StudeoApiFactory>
{
    private const string Password = "kalimera-123";

    private static RegisterRequest Register(string role = Roles.Student, string? email = null, string password = Password) =>
        new(email ?? $"{Guid.NewGuid():N}@example.com", password, "Νίκος", "Παπάς", role);

    private static async Task<string?> ErrorCode(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("code").GetString();
    }

    /// <summary>Set-Cookie headers by cookie name.</summary>
    private static Dictionary<string, string> SetCookies(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.ToDictionary(v => v[..v.IndexOf('=')], v => v)
            : [];

    private static string CookieValue(string setCookie) => setCookie[(setCookie.IndexOf('=') + 1)..setCookie.IndexOf(';')];

    [Theory]
    [InlineData(Roles.Student)]
    [InlineData(Roles.Teacher)]
    public async Task Register_creates_account_with_role_and_signs_in(string role)
    {
        var request = Register(role);

        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/register", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var me = await response.Content.ReadFromJsonAsync<MeResponse>();
        Assert.Equal(role, me!.Role);
        Assert.Equal("Νίκος Παπάς", me.Name);

        var cookies = SetCookies(response);
        Assert.Contains("httponly", cookies[AuthCookies.Access].ToLowerInvariant());
        Assert.Contains("samesite=strict", cookies[AuthCookies.Access].ToLowerInvariant());
        Assert.Contains("path=/;", cookies[AuthCookies.Access].ToLowerInvariant());
        Assert.Contains("path=/api/auth", cookies[AuthCookies.Refresh].ToLowerInvariant());
        Assert.Contains("httponly", cookies[AuthCookies.Refresh].ToLowerInvariant());
    }

    [Fact]
    public async Task Register_normalizes_the_email()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/register", Register(email: "  Maria.P@Example.COM "));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(factory.Keycloak.HasAccount("maria.p@example.com"));
    }

    [Fact]
    public async Task Register_same_email_twice_gives_409()
    {
        var request = Register();
        await factory.CreateClient().PostAsJsonAsync("/api/auth/register", request);

        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/register", request with { Role = Roles.Teacher });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(ErrorCodes.EmailTaken, await ErrorCode(response));
    }

    [Theory]
    [InlineData("admin", "kalimera-123", "invalid_request")]
    [InlineData("", "kalimera-123", "invalid_request")]
    [InlineData(Roles.Student, "short", "weak_password")]
    public async Task Register_rejects_bad_input(string role, string password, string code)
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/register", Register(role, password: password));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(code, await ErrorCode(response));
        Assert.Empty(SetCookies(response));
    }

    [Fact]
    public async Task Register_rejects_bad_email_and_missing_names()
    {
        var client = factory.CreateClient();

        var badEmail = await client.PostAsJsonAsync("/api/auth/register", Register(email: "not-an-email"));
        var noName = await client.PostAsJsonAsync("/api/auth/register", Register() with { FirstName = "  " });

        Assert.Equal(ErrorCodes.InvalidEmail, await ErrorCode(badEmail));
        Assert.Equal(ErrorCodes.InvalidRequest, await ErrorCode(noName));
    }

    [Fact]
    public async Task Login_with_right_password_signs_in()
    {
        var request = Register(Roles.Teacher);
        await factory.CreateClient().PostAsJsonAsync("/api/auth/register", request);

        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/login", new LoginRequest(request.Email.ToUpperInvariant(), Password));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Roles.Teacher, (await response.Content.ReadFromJsonAsync<MeResponse>())!.Role);
        Assert.Contains(AuthCookies.Access, SetCookies(response).Keys);
    }

    [Fact]
    public async Task Login_with_wrong_password_gives_401_invalid_credentials()
    {
        var request = Register();
        await factory.CreateClient().PostAsJsonAsync("/api/auth/register", request);

        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/login", new LoginRequest(request.Email, "wrong-password"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(ErrorCodes.InvalidCredentials, await ErrorCode(response));
        Assert.Empty(SetCookies(response));
    }

    [Fact]
    public async Task Login_of_account_without_role_gets_403_and_no_session()
    {
        factory.Keycloak.AddAccount("norole@example.com", Password, TestUser.WithoutRole());

        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/login", new LoginRequest("norole@example.com", Password));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(ErrorCodes.NoRole, await ErrorCode(response));
        Assert.Empty(SetCookies(response));
    }

    [Fact]
    public async Task Refresh_without_cookie_gives_401_session_expired()
    {
        var response = await factory.CreateClient().PostAsync("/api/auth/refresh", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(ErrorCodes.SessionExpired, await ErrorCode(response));
    }

    [Fact]
    public async Task Refresh_with_cookie_gives_new_tokens_and_the_old_one_stops_working()
    {
        var registered = await factory.CreateClient().PostAsJsonAsync("/api/auth/register", Register(Roles.Teacher));
        var refresh = CookieValue(SetCookies(registered)[AuthCookies.Refresh]);

        var response = await PostWithCookie("/api/auth/refresh", refresh);
        var again = await PostWithCookie("/api/auth/refresh", refresh);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Roles.Teacher, (await response.Content.ReadFromJsonAsync<MeResponse>())!.Role);
        Assert.NotEqual(refresh, CookieValue(SetCookies(response)[AuthCookies.Refresh]));
        Assert.Equal(HttpStatusCode.Unauthorized, again.StatusCode);
    }

    [Fact]
    public async Task Logout_ends_the_keycloak_session_and_clears_cookies()
    {
        var registered = await factory.CreateClient().PostAsJsonAsync("/api/auth/register", Register());
        var refresh = CookieValue(SetCookies(registered)[AuthCookies.Refresh]);

        var response = await PostWithCookie("/api/auth/logout", refresh);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Contains(refresh, factory.Keycloak.SignedOut);
        var cookies = SetCookies(response);
        Assert.Contains("expires=thu, 01 jan 1970", cookies[AuthCookies.Access].ToLowerInvariant());
        Assert.Contains("expires=thu, 01 jan 1970", cookies[AuthCookies.Refresh].ToLowerInvariant());
    }

    [Fact]
    public async Task Logout_without_session_still_succeeds()
    {
        var response = await factory.CreateClient().PostAsync("/api/auth/logout", null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private Task<HttpResponseMessage> PostWithCookie(string url, string refreshToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("Cookie", $"{AuthCookies.Refresh}={refreshToken}");
        return factory.CreateClient().SendAsync(request);
    }
}

public class AuthControllerKeycloakDownTests(StudeoApiFactory factory) : IClassFixture<StudeoApiFactory>
{
    [Fact]
    public async Task Keycloak_down_gives_503_auth_failed()
    {
        factory.Keycloak.Down = true;
        var client = factory.CreateClient();

        var register = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest("a@example.com", "kalimera-123", "Α", "Β", Roles.Student));
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("a@example.com", "kalimera-123"));

        foreach (var response in new[] { register, login })
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(ErrorCodes.AuthFailed, json.RootElement.GetProperty("code").GetString());
        }
    }
}
