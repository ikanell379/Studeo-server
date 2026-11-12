using Studeo.Api.Contracts;
using Studeo.Api.Repositories;

namespace Studeo.Api.Services;

public class HealthService(IHealthRepository repository) : IHealthService
{
    public async Task<HealthResponse> GetAsync(CancellationToken ct)
    {
        var database = await repository.CanConnectAsync(ct);
        return new HealthResponse(database ? "ok" : "degraded", database);
    }
}
