using System.Text.Json;
using System.Text.Json.Nodes;
using Studeo.Api.Contracts;

namespace Studeo.Api.Services;

/// <summary>
/// Calls the Google Gemini API (generateContent) with structured output (responseJsonSchema).
/// Settings: Gemini:ApiKey, Gemini:Model, Gemini:FallbackModel (optional, used when the main model is overloaded).
/// Transient errors (429, 5xx, timeouts) are retried with a short backoff.
/// </summary>
public sealed class GeminiAiClient(HttpClient http, IConfiguration configuration, ILogger<GeminiAiClient> logger) : IAiClient
{
    public const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta/";
    private const string DefaultModel = "gemini-3.6-flash";

    /// <summary>Includes thinking tokens, so it is higher than the visible JSON needs.</summary>
    private const int MaxOutputTokens = 32768;

    private static readonly HashSet<string> RefusalReasons =
        ["SAFETY", "RECITATION", "BLOCKLIST", "PROHIBITED_CONTENT", "SPII", "LANGUAGE", "OTHER"];

    /// <summary>Waits before the 2nd, 3rd… attempt on the same model. One entry per retry.</summary>
    internal TimeSpan[] RetryDelays { get; init; } = [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(6)];

    /// <summary>
    /// Time limit of a single attempt; a hung call is abandoned and retried. A normal call takes ~20 s
    /// for a short section, so this leaves room for long sections without waiting minutes on a hung one.
    /// </summary>
    internal TimeSpan AttemptTimeout { get; init; } = TimeSpan.FromSeconds(90);

    public async Task<AiResult> CompleteJsonAsync(string system, string user, JsonObject schema, CancellationToken ct)
    {
        var apiKey = configuration["Gemini:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            logger.LogWarning("Gemini:ApiKey is not set.");
            return AiResult.Fail(ErrorCodes.AiNotConfigured);
        }

        var body = new JsonObject
        {
            ["systemInstruction"] = new JsonObject { ["parts"] = new JsonArray(new JsonObject { ["text"] = system }) },
            ["contents"] = new JsonArray(new JsonObject
            {
                ["role"] = "user",
                ["parts"] = new JsonArray(new JsonObject { ["text"] = user }),
            }),
            ["generationConfig"] = new JsonObject
            {
                ["responseMimeType"] = "application/json",
                ["responseJsonSchema"] = schema.DeepClone(),
                ["maxOutputTokens"] = MaxOutputTokens,
            },
        }.ToJsonString();

        var lastError = ErrorCodes.AiFailed;
        foreach (var model in Models())
        {
            for (var attempt = 0; attempt <= RetryDelays.Length; attempt++)
            {
                if (attempt > 0)
                {
                    await Task.Delay(RetryDelays[attempt - 1], ct);
                }

                var (result, transientError) = await SendAsync(model, body, apiKey, ct);
                if (transientError is null)
                {
                    return result!;
                }
                lastError = transientError;
                logger.LogWarning("Gemini {Model} attempt {Attempt} failed ({Error}).", model, attempt + 1, transientError);
            }
        }
        return AiResult.Fail(lastError);
    }

    private IEnumerable<string> Models()
    {
        var main = configuration["Gemini:Model"] ?? DefaultModel;
        yield return main;
        var fallback = configuration["Gemini:FallbackModel"];
        if (!string.IsNullOrWhiteSpace(fallback) && fallback != main)
        {
            yield return fallback;
        }
    }

    /// <summary>One call. Returns a final result, or a transient error code worth retrying.</summary>
    private async Task<(AiResult? Result, string? TransientError)> SendAsync(string model, string body, string apiKey, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}models/{Uri.EscapeDataString(model)}:generateContent")
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("x-goog-api-key", apiKey);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(AttemptTimeout);

        JsonElement root;
        try
        {
            using var response = await http.SendAsync(request, timeout.Token);
            var text = await response.Content.ReadAsStringAsync(timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                var status = (int)response.StatusCode;
                logger.LogError("Gemini {Model} call failed: {Status} {Body}", model, status, text);
                return status switch
                {
                    429 or 503 => (null, ErrorCodes.AiBusy),
                    500 or 502 or 504 => (null, ErrorCodes.AiFailed),
                    _ => (AiResult.Fail(ErrorCodes.AiFailed), null), // 400/401/403/404: retrying won't help
                };
            }
            root = JsonDocument.Parse(text).RootElement;
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Gemini API could not be reached.");
            return (null, ErrorCodes.AiFailed);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogError("Gemini {Model} call timed out after {Timeout}.", model, AttemptTimeout);
            return (null, ErrorCodes.AiFailed);
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Gemini API returned a body that is not JSON.");
            return (AiResult.Fail(ErrorCodes.AiFailed), null);
        }

        return (Read(root, model), null);
    }

    private AiResult Read(JsonElement root, string model)
    {
        if (root.TryGetProperty("promptFeedback", out var feedback) && feedback.TryGetProperty("blockReason", out var block))
        {
            logger.LogWarning("Gemini {Model} blocked the prompt: {Reason}", model, block.GetString());
            return AiResult.Fail(ErrorCodes.AiRefused);
        }

        if (!root.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
        {
            return AiResult.Fail(ErrorCodes.AiInvalidOutput);
        }

        var candidate = candidates[0];
        var finish = candidate.TryGetProperty("finishReason", out var f) ? f.GetString() : null;
        if (root.TryGetProperty("usageMetadata", out var usage))
        {
            logger.LogInformation("Gemini {Model}: finish={Finish}, usage={Usage}", model, finish, usage.GetRawText());
        }

        if (finish == "MAX_TOKENS")
        {
            return AiResult.Fail(ErrorCodes.AiInvalidOutput); // truncated JSON
        }
        if (finish is not null && RefusalReasons.Contains(finish))
        {
            return AiResult.Fail(ErrorCodes.AiRefused);
        }

        var text = string.Concat(
            candidate.TryGetProperty("content", out var content) && content.TryGetProperty("parts", out var parts)
                ? parts.EnumerateArray()
                    .Where(p => !(p.TryGetProperty("thought", out var thought) && thought.ValueKind == JsonValueKind.True))
                    .Select(p => p.TryGetProperty("text", out var t) ? t.GetString() : null)
                : []);
        return text.Length == 0 ? AiResult.Fail(ErrorCodes.AiInvalidOutput) : AiResult.Ok(text);
    }
}
