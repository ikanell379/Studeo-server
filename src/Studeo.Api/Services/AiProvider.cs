namespace Studeo.Api.Services;

/// <summary>Which AI service generates the material. Chosen with "Ai:Provider" in appsettings.Development.json.</summary>
public enum AiProvider
{
    Claude,
    Gemini,
}

public static class AiProviderRegistration
{
    /// <summary>Reads "Ai:Provider" ("Claude" or "Gemini", case-insensitive; default Claude).</summary>
    public static AiProvider Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return AiProvider.Claude;
        }
        return Enum.TryParse<AiProvider>(value.Trim(), ignoreCase: true, out var provider) && Enum.IsDefined(provider)
            ? provider
            : throw new InvalidOperationException($"Ai:Provider must be \"Claude\" or \"Gemini\" (was \"{value}\").");
    }

    public static IServiceCollection AddAiClient(this IServiceCollection services, IConfiguration configuration)
    {
        switch (Parse(configuration["Ai:Provider"]))
        {
            case AiProvider.Gemini:
                // Outer safety limit only; the per-attempt limit and retries are in GeminiAiClient.
                services.AddHttpClient<IAiClient, GeminiAiClient>(c => c.Timeout = TimeSpan.FromMinutes(10));
                break;
            default:
                services.AddSingleton<IAiClient, AnthropicAiClient>();
                break;
        }
        return services;
    }
}
