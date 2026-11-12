using Studeo.Api.Services;

namespace Studeo.Api.Auth;

/// <summary>
/// The Keycloak tokens live only in httpOnly cookies: JavaScript in the page can't read them.
/// SameSite=Strict, and the UI is served from the same origin (Vite proxy), so the browser sends them only to us.
/// </summary>
public static class AuthCookies
{
    public const string Access = "studeo_access";
    public const string Refresh = "studeo_refresh";

    /// <summary>The refresh token is only needed by /api/auth (refresh, logout).</summary>
    private const string RefreshPath = "/api/auth";

    public static void Set(HttpResponse response, KeycloakTokens tokens)
    {
        response.Cookies.Append(Access, tokens.AccessToken, Options(response, "/", tokens.ExpiresIn));
        response.Cookies.Append(Refresh, tokens.RefreshToken, Options(response, RefreshPath, tokens.RefreshExpiresIn));
    }

    public static void Clear(HttpResponse response)
    {
        response.Cookies.Delete(Access, Options(response, "/", null));
        response.Cookies.Delete(Refresh, Options(response, RefreshPath, null));
    }

    public static string? ReadAccess(HttpRequest request) => NonEmpty(request.Cookies[Access]);

    public static string? ReadRefresh(HttpRequest request) => NonEmpty(request.Cookies[Refresh]);

    private static string? NonEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;

    private static CookieOptions Options(HttpResponse response, string path, int? maxAgeSeconds) => new()
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Strict,
        Secure = response.HttpContext.Request.IsHttps,
        Path = path,
        // 0 = session-long (Keycloak gives 0 for offline / no expiry).
        MaxAge = maxAgeSeconds is > 0 ? TimeSpan.FromSeconds(maxAgeSeconds.Value) : null,
        IsEssential = true,
    };
}
