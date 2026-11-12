using Studeo.Api.Contracts;

namespace Studeo.Api.Services;

public interface IHealthService
{
    Task<HealthResponse> GetAsync(CancellationToken ct);
}
