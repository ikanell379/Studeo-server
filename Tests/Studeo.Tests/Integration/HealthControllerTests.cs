using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Studeo.Api.Contracts;
using Studeo.Api.Models;

namespace Studeo.Tests.Integration;

public class HealthControllerTests(StudeoApiFactory factory) : IClassFixture<StudeoApiFactory>
{
    [Fact]
    public async Task Get_health_returns_ok_with_database()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<HealthResponse>();
        Assert.NotNull(body);
        Assert.Equal("ok", body.Status);
        Assert.True(body.Database);
    }

    [Fact]
    public async Task Migrations_are_applied_on_startup()
    {
        _ = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StudeoDbContext>();

        var pending = await db.Database.GetPendingMigrationsAsync();

        Assert.Empty(pending);
        Assert.Equal(0, await db.Documents.CountAsync());
    }
}
