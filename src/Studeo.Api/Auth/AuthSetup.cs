using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;

namespace Studeo.Api.Auth;

public static class AuthSetup
{
    /// <summary>
    /// Validates Keycloak access tokens (JWT, from the httpOnly cookie or an Authorization: Bearer header)
    /// and maps realm roles to role claims. Every endpoint needs a signed-in user unless marked [AllowAnonymous].
    /// </summary>
    public static IServiceCollection AddKeycloakAuth(this IServiceCollection services, IConfiguration configuration, bool isDevelopment)
    {
        var section = configuration.GetSection(KeycloakOptions.Section);
        var options = section.Get<KeycloakOptions>() ?? new KeycloakOptions();
        if (string.IsNullOrWhiteSpace(options.Url))
        {
            throw new InvalidOperationException("Keycloak:Url is missing (set it in appsettings.Development.json).");
        }

        services.Configure<KeycloakOptions>(section);

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(jwt =>
            {
                jwt.Authority = options.Authority;
                jwt.Audience = options.Audience;
                jwt.RequireHttpsMetadata = !isDevelopment; // local Keycloak runs on http
                jwt.MapInboundClaims = false;
                jwt.TokenValidationParameters.NameClaimType = KeycloakClaims.Username;
                jwt.TokenValidationParameters.RoleClaimType = KeycloakClaims.Role;
                jwt.Events = new JwtBearerEvents
                {
                    // The browser sends the token as a cookie; tools like Swagger/curl can still use the header.
                    OnMessageReceived = context =>
                    {
                        context.Token ??= AuthCookies.ReadAccess(context.Request);
                        return Task.CompletedTask;
                    },
                    OnTokenValidated = context =>
                    {
                        if (context.Principal?.Identity is ClaimsIdentity identity) KeycloakClaims.AddRealmRoles(identity);
                        return Task.CompletedTask;
                    },
                };
            });

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        return services;
    }
}
