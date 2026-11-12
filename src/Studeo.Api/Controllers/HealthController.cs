using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Studeo.Api.Contracts;
using Studeo.Api.Services;

namespace Studeo.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/health")]
public class HealthController(IHealthService healthService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<HealthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HealthResponse>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<HealthResponse>> Get(CancellationToken ct)
    {
        var health = await healthService.GetAsync(ct);
        return health.Database ? Ok(health) : StatusCode(StatusCodes.Status503ServiceUnavailable, health);
    }
}
