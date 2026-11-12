using Studeo.Api.Repositories;
using Studeo.Api.Services;

namespace Studeo.Tests.Unit;

public class HealthServiceTests
{
    private sealed class FakeHealthRepository(bool canConnect) : IHealthRepository
    {
        public Task<bool> CanConnectAsync(CancellationToken ct) => Task.FromResult(canConnect);
    }

    [Fact]
    public async Task Returns_ok_when_database_is_reachable()
    {
        var health = await new HealthService(new FakeHealthRepository(true)).GetAsync(CancellationToken.None);

        Assert.Equal("ok", health.Status);
        Assert.True(health.Database);
    }

    [Fact]
    public async Task Returns_degraded_when_database_is_unreachable()
    {
        var health = await new HealthService(new FakeHealthRepository(false)).GetAsync(CancellationToken.None);

        Assert.Equal("degraded", health.Status);
        Assert.False(health.Database);
    }
}
