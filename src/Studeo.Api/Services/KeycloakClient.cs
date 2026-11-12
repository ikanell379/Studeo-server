using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Studeo.Api.Auth;

namespace Studeo.Api.Services;

/// <summary>Keycloak token endpoint (as the studeo-api client) and Admin REST API (as its service account).</summary>
public class KeycloakClient(HttpClient http, IOptions<KeycloakOptions> options, ILogger<KeycloakClient> logger) : IKeycloakClient
{
    private readonly KeycloakOptions _options = options.Value;

    private string TokenEndpoint => $"{_options.Authority}/protocol/openid-connect/token";

    public async Task<CreateUserOutcome> CreateUserAsync(
        string email, string password, string firstName, string lastName, string role, CancellationToken ct)
    {
        var adminToken = await GetServiceTokenAsync(ct);

        using var create = Admin(HttpMethod.Post, "/users", adminToken, new
        {
            username = email,
            email,
            firstName,
            lastName,
            enabled = true,
            emailVerified = false,
            credentials = new[] { new { type = "password", value = password, temporary = false } },
        });
        using var created = await http.SendAsync(create, ct);
        if (created.StatusCode == HttpStatusCode.Conflict) return CreateUserOutcome.EmailTaken;
        if (created.StatusCode == HttpStatusCode.BadRequest)
        {
            // Keycloak's password policy said no (e.g. too short).
            logger.LogInformation("Keycloak refused the new user: {Body}", await created.Content.ReadAsStringAsync(ct));
            return CreateUserOutcome.PasswordRejected;
        }
        created.EnsureSuccessStatusCode();

        // Location: .../users/{id}
        var userId = created.Headers.Location?.Segments.LastOrDefault()
            ?? throw new HttpRequestException("Keycloak did not return the new user's id.");

        try
        {
            using var getRole = Admin(HttpMethod.Get, $"/roles/{Uri.EscapeDataString(role)}", adminToken);
            using var roleResponse = await http.SendAsync(getRole, ct);
            roleResponse.EnsureSuccessStatusCode();
            var roleRepresentation = await roleResponse.Content.ReadFromJsonAsync<JsonElement>(ct);

            using var assign = Admin(HttpMethod.Post, $"/users/{Uri.EscapeDataString(userId)}/role-mappings/realm", adminToken,
                new[] { roleRepresentation });
            using var assigned = await http.SendAsync(assign, ct);
            assigned.EnsureSuccessStatusCode();
        }
        catch
        {
            // Never leave a user without a role: undo the half-done registration.
            using var delete = Admin(HttpMethod.Delete, $"/users/{Uri.EscapeDataString(userId)}", adminToken);
            using var _ = await http.SendAsync(delete, CancellationToken.None);
            throw;
        }

        return CreateUserOutcome.Created;
    }

    public Task<KeycloakTokens?> PasswordSignInAsync(string email, string password, CancellationToken ct) =>
        RequestTokensAsync(new()
        {
            ["grant_type"] = "password",
            ["username"] = email,
            ["password"] = password,
            ["scope"] = "openid profile email",
        }, ct);

    public Task<KeycloakTokens?> RefreshAsync(string refreshToken, CancellationToken ct) =>
        RequestTokensAsync(new()
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
        }, ct);

    public async Task SignOutAsync(string refreshToken, CancellationToken ct)
    {
        using var response = await http.PostAsync($"{_options.Authority}/protocol/openid-connect/logout",
            new FormUrlEncodedContent(WithClient(new() { ["refresh_token"] = refreshToken })), ct);
        // 400 = session already gone; that's fine for a sign-out.
        if (response.StatusCode != HttpStatusCode.BadRequest) response.EnsureSuccessStatusCode();
    }

    /// <summary>Tokens, or null when Keycloak says the grant is invalid (wrong password, ended session).</summary>
    private async Task<KeycloakTokens?> RequestTokensAsync(Dictionary<string, string> form, CancellationToken ct)
    {
        using var response = await http.PostAsync(TokenEndpoint, new FormUrlEncodedContent(WithClient(form)), ct);
        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized) return null;
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        return new KeycloakTokens(
            body.GetProperty("access_token").GetString()!,
            body.GetProperty("expires_in").GetInt32(),
            body.GetProperty("refresh_token").GetString()!,
            body.TryGetProperty("refresh_expires_in", out var r) ? r.GetInt32() : 0);
    }

    private async Task<string> GetServiceTokenAsync(CancellationToken ct)
    {
        using var response = await http.PostAsync(TokenEndpoint,
            new FormUrlEncodedContent(WithClient(new() { ["grant_type"] = "client_credentials" })), ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        return body.GetProperty("access_token").GetString()
            ?? throw new HttpRequestException("Keycloak returned no access token.");
    }

    private Dictionary<string, string> WithClient(Dictionary<string, string> form)
    {
        form["client_id"] = _options.ClientId;
        form["client_secret"] = _options.ClientSecret;
        return form;
    }

    private HttpRequestMessage Admin(HttpMethod method, string path, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, _options.AdminBase + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return request;
    }
}
