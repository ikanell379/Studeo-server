using System.Text.Json;
using System.Text.Json.Nodes;
using Studeo.Api.Contracts;

namespace Studeo.Api.Services;

/// <summary>
/// Calls the Google Gemini API (streamGenerateContent, server-sent events) with structured output (responseJsonSchema),
/// so the caller can follow the answer while it is written.
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
    /// Longest silence allowed in one attempt (before the first piece, or between pieces); a hung call is
    /// abandoned and retried. The model may think for a while before writing, so this is generous; a call
    /// that keeps sending text is never cut off, however long the section.
    /// </summary>
    internal TimeSpan AttemptTimeout { get; init; } = TimeSpan.FromSeconds(90);

    public async Task<AiResult> CompleteJsonAsync(string system, string user, JsonObject schema, IAiOutputObserver? output, CancellationToken ct)
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

                output?.Restart();
                var (result, transientError) = await SendAsync(model, body, apiKey, output, ct);
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

    /// <summary>One streamed call. Returns a final result, or a transient error code worth retrying.</summary>
    private async Task<(AiResult? Result, string? TransientError)> SendAsync(
        string model, string body, string apiKey, IAiOutputObserver? output, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"{BaseUrl}models/{Uri.EscapeDataString(model)}:streamGenerateContent?alt=sse")
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("x-goog-api-key", apiKey);

        using var idle = CancellationTokenSource.CreateLinkedTokenSource(ct);
        idle.CancelAfter(AttemptTimeout);

        var answer = new StreamedAnswer();
        try
        {
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, idle.Token);
            if (!response.IsSuccessStatusCode)
            {
                var text = await response.Content.ReadAsStringAsync(idle.Token);
                var status = (int)response.StatusCode;
                logger.LogError("Gemini {Model} call failed: {Status} {Body}", model, status, text);
                return status switch
                {
                    429 or 503 => (null, ErrorCodes.AiBusy),
                    500 or 502 or 504 => (null, ErrorCodes.AiFailed),
                    _ => (AiResult.Fail(ErrorCodes.AiFailed), null), // 400/401/403/404: retrying won't help
                };
            }

            using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(idle.Token));
            while (await reader.ReadLineAsync(idle.Token) is { } line)
            {
                idle.CancelAfter(AttemptTimeout); // still alive
                if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
                using var chunk = JsonDocument.Parse(line.AsMemory(5));
                var piece = answer.Add(chunk.RootElement);
                if (piece.Length > 0) output?.Append(piece);
            }
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Gemini API could not be reached.");
            return (null, ErrorCodes.AiFailed);
        }
        catch (IOException ex)
        {
            logger.LogError(ex, "Gemini stream broke off.");
            return (null, ErrorCodes.AiFailed);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogError("Gemini {Model} sent nothing for {Timeout}.", model, AttemptTimeout);
            return (null, ErrorCodes.AiFailed);
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Gemini API sent a piece that is not JSON.");
            return (AiResult.Fail(ErrorCodes.AiFailed), null);
        }

        return (Read(answer, model), null);
    }

    private AiResult Read(StreamedAnswer answer, string model)
    {
        if (answer.BlockReason is not null)
        {
            logger.LogWarning("Gemini {Model} blocked the prompt: {Reason}", model, answer.BlockReason);
            return AiResult.Fail(ErrorCodes.AiRefused);
        }

        if (!answer.SawCandidate)
        {
            return AiResult.Fail(ErrorCodes.AiInvalidOutput);
        }

        logger.LogInformation("Gemini {Model}: finish={Finish}, usage={Usage}", model, answer.FinishReason, answer.Usage);

        if (answer.FinishReason == "MAX_TOKENS")
        {
            return AiResult.Fail(ErrorCodes.AiInvalidOutput); // truncated JSON
        }
        if (answer.FinishReason is not null && RefusalReasons.Contains(answer.FinishReason))
        {
            return AiResult.Fail(ErrorCodes.AiRefused);
        }

        return answer.Text.Length == 0 ? AiResult.Fail(ErrorCodes.AiInvalidOutput) : AiResult.Ok(answer.Text.ToString());
    }

    /// <summary>The pieces of one streamed answer, put together.</summary>
    private sealed class StreamedAnswer
    {
        public System.Text.StringBuilder Text { get; } = new();
        public bool SawCandidate { get; private set; }
        public string? FinishReason { get; private set; }
        public string? BlockReason { get; private set; }
        public string? Usage { get; private set; }

        /// <summary>Adds one chunk; returns its visible text (thought parts are skipped).</summary>
        public string Add(JsonElement chunk)
        {
            if (chunk.TryGetProperty("promptFeedback", out var feedback) && feedback.TryGetProperty("blockReason", out var block))
            {
                BlockReason = block.GetString();
            }
            if (chunk.TryGetProperty("usageMetadata", out var usage))
            {
                Usage = usage.GetRawText();
            }
            if (!chunk.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
            {
                return "";
            }

            SawCandidate = true;
            var candidate = candidates[0];
            if (candidate.TryGetProperty("finishReason", out var finish))
            {
                FinishReason = finish.GetString();
            }

            var piece = string.Concat(
                candidate.TryGetProperty("content", out var content) && content.TryGetProperty("parts", out var parts)
                    ? parts.EnumerateArray()
                        .Where(p => !(p.TryGetProperty("thought", out var thought) && thought.ValueKind == JsonValueKind.True))
                        .Select(p => p.TryGetProperty("text", out var t) ? t.GetString() : null)
                    : []);
            Text.Append(piece);
            return piece;
        }
    }
}
