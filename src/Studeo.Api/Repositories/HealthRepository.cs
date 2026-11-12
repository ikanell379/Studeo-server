using Studeo.Api.Models;

namespace Studeo.Api.Repositories;

public class HealthRepository(StudeoDbContext db) : IHealthRepository
{
    public Task<bool> CanConnectAsync(CancellationToken ct) => db.Database.CanConnectAsync(ct);
}
