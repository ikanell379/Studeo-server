using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Messages;
using Studeo.Api.Contracts;

namespace Studeo.Api.Services;

/// <summary>
/// Calls the Anthropic Messages API with structured output (output_config.format = json_schema), streamed,
/// so the caller can follow the answer while it is written. The key comes from user-secrets (Anthropic:ApiKey),
/// the model from appsettings.Development.json (Anthropic:Model).
/// </summary>
public sealed class AnthropicAiClient(IConfiguration configuration, ILogger<AnthropicAiClient> logger) : IAiClient, IDisposable
{
    private const string DefaultModel = "claude-opus-5-5";

    /// <summary>Streaming, so a high limit doesn't risk HTTP timeouts; the JSON itself is far smaller.</summary>
    private const int MaxTokens = 64000;

    private readonly Lazy<AnthropicClient?> _client = new(() =>
    {
        var apiKey = configuration["Anthropic:ApiKey"];
        return string.IsNullOrWhiteSpace(apiKey) ? null : new AnthropicClient { ApiKey = apiKey };
    });

    public async Task<AiResult> CompleteJsonAsync(string system, string user, JsonObject schema, IAiOutputObserver? output, CancellationToken ct)
    {
        var client = _client.Value;
        if (client is null)
        {
            logger.LogWarning("Anthropic:ApiKey is not set (dotnet user-secrets set \"Anthropic:ApiKey\" ...).");
            return AiResult.Fail(ErrorCodes.AiNotConfigured);
        }

        var model = configuration["Anthropic:Model"] ?? DefaultModel;
        var parameters = new MessageCreateParams
        {
            Model = model,
            MaxTokens = MaxTokens,
            System = system,
            Messages = [new UserMessageParam { Content = user }],
            OutputConfig = new OutputConfig
            {
                Format = new JsonOutputFormat
                {
                    Schema = schema.ToDictionary(p => p.Key, p => JsonSerializer.SerializeToElement(p.Value)),
                },
            },
        };

        var text = new StringBuilder();
        RawMessageDeltaEvent? last = null;
        long? inputTokens = null, outputTokens = null;
        output?.Restart();
        try
        {
            await foreach (var streamEvent in client.Messages.CreateStreaming(parameters, ct))
            {
                if (streamEvent.TryPickContentBlockDelta(out var blockDelta) && blockDelta.Delta.TryPickText(out var delta))
                {
                    text.Append(delta.Text);
                    output?.Append(delta.Text);
                }
                else if (streamEvent.TryPickStart(out var start))
                {
                    inputTokens = start.Message.Usage.InputTokens;
                }
                else if (streamEvent.TryPickDelta(out var messageDelta))
                {
                    last = messageDelta;
                    outputTokens = messageDelta.Usage.OutputTokens;
                }
            }
        }
        catch (AnthropicApiException ex)
        {
            logger.LogError(ex, "Anthropic API call failed.");
            return AiResult.Fail(ErrorCodes.AiFailed);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Anthropic API could not be reached.");
            return AiResult.Fail(ErrorCodes.AiFailed);
        }

        var stopReason = last?.Delta.StopReason;
        logger.LogInformation("Anthropic {Model}: stop={Stop}, in={In}, out={Out} tokens.",
            model, stopReason?.ToString(), inputTokens, outputTokens);

        if (stopReason == StopReason.Refusal)
        {
            return AiResult.Fail(ErrorCodes.AiRefused);
        }
        if (stopReason == StopReason.MaxTokens)
        {
            return AiResult.Fail(ErrorCodes.AiInvalidOutput); // truncated JSON
        }

        return text.Length == 0 ? AiResult.Fail(ErrorCodes.AiInvalidOutput) : AiResult.Ok(text.ToString());
    }

    public void Dispose()
    {
        if (_client.IsValueCreated)
        {
            (_client.Value as IDisposable)?.Dispose();
        }
    }
}
