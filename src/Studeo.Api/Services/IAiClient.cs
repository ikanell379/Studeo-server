using System.Text.Json.Nodes;

namespace Studeo.Api.Services;

/// <summary>Thin wrapper over the AI provider, so services can be tested with a fake.</summary>
public interface IAiClient
{
    /// <summary>
    /// Sends one request and returns the model's JSON text, constrained to <paramref name="schema"/>.
    /// The answer is streamed: <paramref name="output"/> (optional) sees the text while it is being written.
    /// </summary>
    Task<AiResult> CompleteJsonAsync(string system, string user, JsonObject schema, IAiOutputObserver? output, CancellationToken ct);
}

/// <summary>Watches the model's answer while it streams in (used for real progress).</summary>
public interface IAiOutputObserver
{
    /// <summary>A new attempt starts (e.g. a retry after an overloaded model): forget the text seen so far.</summary>
    void Restart();

    /// <summary>The next piece of the answer text.</summary>
    void Append(string text);
}

/// <summary>Either Json (success) or ErrorCode (see Contracts/ErrorCodes).</summary>
public record AiResult(string? Json, string? ErrorCode)
{
    public static AiResult Ok(string json) => new(json, null);
    public static AiResult Fail(string code) => new(null, code);
}
