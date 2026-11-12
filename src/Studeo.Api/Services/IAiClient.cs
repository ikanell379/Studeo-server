using System.Text.Json.Nodes;

namespace Studeo.Api.Services;

/// <summary>Thin wrapper over the AI provider, so services can be tested with a fake.</summary>
public interface IAiClient
{
    /// <summary>Sends one request and returns the model's JSON text, constrained to <paramref name="schema"/>.</summary>
    Task<AiResult> CompleteJsonAsync(string system, string user, JsonObject schema, CancellationToken ct);
}

/// <summary>Either Json (success) or ErrorCode (see Contracts/ErrorCodes).</summary>
public record AiResult(string? Json, string? ErrorCode)
{
    public static AiResult Ok(string json) => new(json, null);
    public static AiResult Fail(string code) => new(null, code);
}
