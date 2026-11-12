using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Studeo.Api.Models;
using Studeo.Api.Services;

namespace Studeo.Tests.Integration;

/// <summary>
/// Runs the API against a throwaway database on the docker-compose PostgreSQL
/// (needs `docker compose up -d`). The database is dropped after the tests.
/// The AI is always replaced by <see cref="Ai"/>, so tests never call the real API (even if a key is in user-secrets).
/// Auth: Keycloak tokens are replaced by <see cref="TestAuthHandler"/> (use <see cref="CreateClientAs"/>), and
/// Keycloak itself by the in-memory <see cref="Keycloak"/>.
/// </summary>
public class StudeoApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string ServerConnectionString =
        "Host=localhost;Port=5440;Username=studeo;Password=studeo_dev";

    private readonly string _connectionString = new NpgsqlConnectionStringBuilder(ServerConnectionString)
    {
        Database = $"studeo_test_{Guid.NewGuid():N}",
    }.ConnectionString;

    public FakeAiClient Ai { get; } = new();

    public FakeKeycloakClient Keycloak { get; } = new();

    /// <summary>A client signed in as <paramref name="user"/>.</summary>
    public HttpClient CreateClientAs(TestUser user)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(TestUser.Header, user.ToHeader());
        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development"); // applies migrations on startup
        builder.UseSetting("ConnectionStrings:Studeo", _connectionString);
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IAiClient>(Ai);
            services.AddSingleton<IKeycloakClient>(Keycloak);
            services.AddAuthentication(TestAuthHandler.Scheme)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.Scheme, null);
            services.PostConfigure<AuthenticationOptions>(o =>
            {
                o.DefaultScheme = TestAuthHandler.Scheme;
                o.DefaultAuthenticateScheme = TestAuthHandler.Scheme;
                o.DefaultChallengeScheme = TestAuthHandler.Scheme;
                o.DefaultForbidScheme = TestAuthHandler.Scheme;
            });
        });
    }

    public Task InitializeAsync() => Task.CompletedTask;

    async Task IAsyncLifetime.DisposeAsync()
    {
        using (var scope = Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<StudeoDbContext>().Database.EnsureDeletedAsync();
        }
        await base.DisposeAsync();
    }
}
