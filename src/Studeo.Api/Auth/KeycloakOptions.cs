namespace Studeo.Api.Auth;

/// <summary>"Keycloak" section of appsettings.Development.json.</summary>
public class KeycloakOptions
{
    public const string Section = "Keycloak";

    /// <summary>Keycloak base URL, e.g. http://localhost:8180. Only the API talks to it; the browser never does.</summary>
    public string Url { get; set; } = "";

    public string Realm { get; set; } = "studeo";

    /// <summary>Required "aud" of access tokens (added by the studeo-api client's audience mapper).</summary>
    public string Audience { get; set; } = "studeo-api";

    /// <summary>
    /// Confidential client of the API: signs users in (password grant) and, as its service account,
    /// creates users and gives them a realm role (Admin API).
    /// </summary>
    public string ClientId { get; set; } = "studeo-api";

    public string ClientSecret { get; set; } = "";

    public string Authority => $"{Url.TrimEnd('/')}/realms/{Realm}";

    public string AdminBase => $"{Url.TrimEnd('/')}/admin/realms/{Uri.EscapeDataString(Realm)}";
}
