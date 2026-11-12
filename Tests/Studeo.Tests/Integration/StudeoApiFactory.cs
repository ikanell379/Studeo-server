using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Studeo.Api.Models;

namespace Studeo.Tests.Integration;

/// <summary>
/// Runs the API against a throwaway database on the docker-compose PostgreSQL
/// (needs `docker compose up -d`). The database is dropped after the tests.
/// </summary>
public class StudeoApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string ServerConnectionString =
        "Host=localhost;Port=5440;Username=studeo;Password=studeo_dev";

    private readonly string _connectionString = new NpgsqlConnectionStringBuilder(ServerConnectionString)
    {
        Database = $"studeo_test_{Guid.NewGuid():N}",
    }.ConnectionString;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development"); // applies migrations on startup
        builder.UseSetting("ConnectionStrings:Studeo", _connectionString);
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
