using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Studeo.Api.Contracts;
using Studeo.Api.Prompts;
using Studeo.Api.Services;

namespace Studeo.Tests.Unit;

public class GeminiAiClientTests
{
    private readonly FakeHandler _handler = new();

    private GeminiAiClient Client(string? apiKey = "test-key", string? model = null, string? fallback = null) =>
        new(new HttpClient(_handler), new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Gemini:ApiKey"] = apiKey,
            ["Gemini:Model"] = model,
            ["Gemini:FallbackModel"] = fallback,
        }).Build(), NullLogger<GeminiAiClient>.Instance)
        {
            RetryDelays = [TimeSpan.Zero, TimeSpan.Zero],
            AttemptTimeout = TimeSpan.FromMilliseconds(500),
        };

    private const string Busy = "{\"error\":{\"code\":503,\"message\":\"high demand\",\"status\":\"UNAVAILABLE\"}}";

    private static string Candidate(string finishReason, params object[] parts) => new JsonObject
    {
        ["candidates"] = new JsonArray(new JsonObject
        {
            ["content"] = new JsonObject { ["role"] = "model", ["parts"] = JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(parts)) },
            ["finishReason"] = finishReason,
        }),
        ["usageMetadata"] = new JsonObject { ["promptTokenCount"] = 10, ["candidatesTokenCount"] = 20 },
    }.ToJsonString();

    private Task<AiResult> Call(GeminiAiClient client) =>
        client.CompleteJsonAsync("system text", "user text", GenerateStudySetPrompt.OutputSchema(), CancellationToken.None);

    [Fact]
    public async Task Sends_generate_content_with_schema_key_and_system_instruction()
    {
        _handler.Respond(HttpStatusCode.OK, Candidate("STOP", new { text = "{}" }));

        await Call(Client(model: "gemini-test"));

        var request = Assert.Single(_handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal($"{GeminiAiClient.BaseUrl}models/gemini-test:generateContent", request.Uri);
        Assert.Equal("test-key", request.ApiKey);
        var body = JsonNode.Parse(request.Body)!;
        Assert.Equal("system text", (string?)body["systemInstruction"]!["parts"]![0]!["text"]);
        Assert.Equal("user text", (string?)body["contents"]![0]!["parts"]![0]!["text"]);
        Assert.Equal("application/json", (string?)body["generationConfig"]!["responseMimeType"]);
        Assert.Equal("object", (string?)body["generationConfig"]!["responseJsonSchema"]!["type"]);
    }

    [Fact]
    public async Task Returns_text_without_thought_parts()
    {
        _handler.Respond(HttpStatusCode.OK, Candidate("STOP", new { text = "thinking…", thought = true }, new { text = "{\"a\":" }, new { text = "1}" }));

        var result = await Call(Client());

        Assert.Null(result.ErrorCode);
        Assert.Equal("{\"a\":1}", result.Json);
    }

    [Fact]
    public async Task Missing_key_returns_ai_not_configured_without_calling()
    {
        var result = await Call(Client(apiKey: ""));

        Assert.Equal(ErrorCodes.AiNotConfigured, result.ErrorCode);
        Assert.Empty(_handler.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Client_error_returns_ai_failed_without_retry(HttpStatusCode status)
    {
        _handler.Respond(status, "{\"error\":{\"message\":\"nope\"}}");

        Assert.Equal(ErrorCodes.AiFailed, (await Call(Client())).ErrorCode);
        Assert.Single(_handler.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, "ai_busy")]
    [InlineData(HttpStatusCode.TooManyRequests, "ai_busy")]
    [InlineData(HttpStatusCode.InternalServerError, "ai_failed")]
    public async Task Transient_error_is_retried_three_times_then_reported(HttpStatusCode status, string expected)
    {
        _handler.Respond(status, Busy);

        Assert.Equal(expected, (await Call(Client())).ErrorCode);
        Assert.Equal(3, _handler.Requests.Count);
    }

    [Fact]
    public async Task Overloaded_model_succeeds_on_retry()
    {
        _handler.Respond(HttpStatusCode.ServiceUnavailable, Busy);
        _handler.Then(HttpStatusCode.OK, Candidate("STOP", new { text = "{}" }));

        var result = await Call(Client());

        Assert.Equal("{}", result.Json);
        Assert.Equal(2, _handler.Requests.Count);
    }

    [Fact]
    public async Task Overloaded_model_falls_back_to_the_fallback_model()
    {
        _handler.Respond(HttpStatusCode.ServiceUnavailable, Busy);
        _handler.Then(HttpStatusCode.ServiceUnavailable, Busy);
        _handler.Then(HttpStatusCode.ServiceUnavailable, Busy);
        _handler.Then(HttpStatusCode.OK, Candidate("STOP", new { text = "{}" }));

        var result = await Call(Client(model: "main", fallback: "lite"));

        Assert.Equal("{}", result.Json);
        Assert.Equal(4, _handler.Requests.Count);
        Assert.All(_handler.Requests.Take(3), r => Assert.Contains("models/main:", r.Uri));
        Assert.Contains("models/lite:", _handler.Requests[3].Uri);
    }

    [Fact]
    public async Task Hung_call_times_out_and_is_retried()
    {
        _handler.Hang();
        _handler.Then(HttpStatusCode.OK, Candidate("STOP", new { text = "{}" }));

        var result = await Call(Client());

        Assert.Equal("{}", result.Json);
        Assert.Equal(2, _handler.Requests.Count);
    }

    [Fact]
    public async Task Max_tokens_returns_ai_invalid_output()
    {
        _handler.Respond(HttpStatusCode.OK, Candidate("MAX_TOKENS", new { text = "{\"cut" }));

        Assert.Equal(ErrorCodes.AiInvalidOutput, (await Call(Client())).ErrorCode);
    }

    [Fact]
    public async Task Safety_finish_returns_ai_refused()
    {
        _handler.Respond(HttpStatusCode.OK, Candidate("SAFETY"));

        Assert.Equal(ErrorCodes.AiRefused, (await Call(Client())).ErrorCode);
    }

    [Fact]
    public async Task Blocked_prompt_returns_ai_refused()
    {
        _handler.Respond(HttpStatusCode.OK, "{\"promptFeedback\":{\"blockReason\":\"SAFETY\"}}");

        Assert.Equal(ErrorCodes.AiRefused, (await Call(Client())).ErrorCode);
    }

    [Fact]
    public async Task No_candidates_returns_ai_invalid_output()
    {
        _handler.Respond(HttpStatusCode.OK, "{\"candidates\":[]}");

        Assert.Equal(ErrorCodes.AiInvalidOutput, (await Call(Client())).ErrorCode);
    }

    [Theory]
    [InlineData(null, AiProvider.Claude)]
    [InlineData("", AiProvider.Claude)]
    [InlineData("Claude", AiProvider.Claude)]
    [InlineData("gemini", AiProvider.Gemini)]
    [InlineData(" Gemini ", AiProvider.Gemini)]
    public void Provider_is_parsed(string? value, AiProvider expected) =>
        Assert.Equal(expected, AiProviderRegistration.Parse(value));

    [Theory]
    [InlineData("openai")]
    [InlineData("5")]
    public void Unknown_provider_fails_with_a_clear_message(string value)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => AiProviderRegistration.Parse(value));
        Assert.Contains("Ai:Provider", ex.Message);
    }

    [Theory]
    [InlineData("Gemini", typeof(GeminiAiClient))]
    [InlineData("Claude", typeof(AnthropicAiClient))]
    public void Registers_the_selected_client(string provider, Type expected)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Ai:Provider"] = provider }).Build();
        var services = new ServiceCollection().AddLogging().AddSingleton<IConfiguration>(config);

        services.AddAiClient(config);

        using var provider_ = services.BuildServiceProvider();
        Assert.IsType(expected, provider_.GetRequiredService<IAiClient>());
    }

    /// <summary>Answers with queued responses; the last one repeats. A null status hangs until cancelled.</summary>
    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly List<(HttpStatusCode? Status, string Body)> _responses = [];

        public List<(HttpMethod Method, string Uri, string? ApiKey, string Body)> Requests { get; } = [];

        public void Respond(HttpStatusCode status, string body)
        {
            _responses.Clear();
            _responses.Add((status, body));
        }

        public void Then(HttpStatusCode status, string body) => _responses.Add((status, body));

        public void Hang()
        {
            _responses.Clear();
            _responses.Add((null, ""));
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add((
                request.Method,
                request.RequestUri!.ToString(),
                request.Headers.TryGetValues("x-goog-api-key", out var keys) ? keys.Single() : null,
                request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct)));
            var (status, body) = _responses[Math.Min(Requests.Count - 1, _responses.Count - 1)];
            if (status is null)
            {
                await Task.Delay(Timeout.Infinite, ct);
            }
            return new HttpResponseMessage(status!.Value) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }
}
