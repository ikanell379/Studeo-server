using System.Text.Json;
using System.Text.Json.Nodes;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Messages;
using Studeo.Api.Contracts;

namespace Studeo.Api.Services;

/// <summary>
/// Calls the Anthropic Messages API with structured output (output_config.format = json_schema).
/// The key comes from user-secrets (Anthropic:ApiKey), the model from appsettings.Development.json (Anthropic:Model).
/// </summary>
public sealed class AnthropicAiClient(IConfiguration configuration, ILogger<AnthropicAiClient> logger) : IAiClient, IDisposable
{
    private const string DefaultModel = "claude-opus-5-5";
    private const int MaxTokens = 16000;

    private readonly Lazy<AnthropicClient?> _client = new(() =>
    {
        var apiKey = configuration["Anthropic:ApiKey"];
        return string.IsNullOrWhiteSpace(apiKey) ? null : new AnthropicClient { ApiKey = apiKey };
    });

    public async Task<AiResult> CompleteJsonAsync(string system, string user, JsonObject schema, CancellationToken ct)
    {
        var client = _client.Value;
        if (client is null)
        {
            logger.LogWarning("Anthropic:ApiKey is not set (dotnet user-secrets set \"Anthropic:ApiKey\" ...).");
            return AiResult.Fail(ErrorCodes.AiNotConfigured);
        }

        var model = configuration["Anthropic:Model"] ?? DefaultModel;
        Message response;
        try
        {
            response = await client.Messages.Create(new MessageCreateParams
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
            }, ct);
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

        logger.LogInformation("Anthropic {Model}: stop={Stop}, in={In}, out={Out} tokens.",
            model, response.StopReason?.ToString(), response.Usage.InputTokens, response.Usage.OutputTokens);

        if (response.StopReason == StopReason.Refusal)
        {
            return AiResult.Fail(ErrorCodes.AiRefused);
        }
        if (response.StopReason == StopReason.MaxTokens)
        {
            return AiResult.Fail(ErrorCodes.AiInvalidOutput); // truncated JSON
        }

        var text = string.Concat(response.Content.Select(b => b.Value).OfType<TextBlock>().Select(t => t.Text));
        return text.Length == 0 ? AiResult.Fail(ErrorCodes.AiInvalidOutput) : AiResult.Ok(text);
    }

    public void Dispose()
    {
        if (_client.IsValueCreated)
        {
            (_client.Value as IDisposable)?.Dispose();
        }
    }
}
