namespace Studeo.Api.Repositories;

public interface IHealthRepository
{
    Task<bool> CanConnectAsync(CancellationToken ct);
}
