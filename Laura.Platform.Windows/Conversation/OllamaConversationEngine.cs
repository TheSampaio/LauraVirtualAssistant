using System.Text;
using System.Text.Json;
using Laura.Core.Abstractions;
using Laura.Core.Configuration;
using Laura.Core.Conversation;
using Microsoft.Extensions.Logging;
using OllamaSharp;
using OllamaSharp.Models;

namespace Laura.Platform.Windows.Conversation;

/// <summary>
/// Generative engine backed by a local Ollama server through OllamaSharp.
///
/// It is only consulted after every skill declines a command, and it degrades
/// gracefully: any connection or model error becomes a <see langword="null"/>
/// answer so Laura falls back to her "I didn't catch that" response instead of crashing.
/// </summary>
public sealed class OllamaConversationEngine : IConversationEngine
{
    private readonly ISettingsService _settings;
    private readonly ILogger<OllamaConversationEngine> _logger;

    /// <summary>
    /// Initializes the engine.
    ///
    /// Args:
    ///     settings: Source of the endpoint, model and persona at call time.
    ///     logger: Destination for diagnostic logs.
    /// </summary>
    public OllamaConversationEngine(ISettingsService settings, ILogger<OllamaConversationEngine> logger)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);

        _settings = settings;
        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsAvailable
    {
        get
        {
            GenerativeAiOptions options = _settings.Current.GenerativeAi;
            return !string.IsNullOrWhiteSpace(options.Model) && Uri.TryCreate(options.Endpoint, UriKind.Absolute, out _);
        }
    }

    /// <inheritdoc />
    public async Task<string?> AskAsync(ConversationTurn turn, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(turn);

        GenerativeAiOptions options = _settings.Current.GenerativeAi;

        if (string.IsNullOrWhiteSpace(options.Model)
            || !Uri.TryCreate(options.Endpoint, UriKind.Absolute, out Uri? endpoint))
        {
            return null;
        }

        try
        {
            using var httpClient = new HttpClient { BaseAddress = endpoint, Timeout = options.Timeout };
            using var client = new OllamaApiClient(httpClient);

            string prompt =
                $"User display name: {turn.UserDisplayName}\n" +
                $"Response language: {turn.Culture}\n" +
                "Reply for voice playback, concise and direct.\n\n" +
                $"User: {turn.Prompt}";

            var request = new GenerateRequest
            {
                Model = options.Model,
                Prompt = prompt,
                System = options.Persona,
                Stream = true,
            };

            var answer = new StringBuilder();

            await foreach (GenerateResponseStream? chunk in client.GenerateAsync(request, cancellationToken).ConfigureAwait(false))
            {
                if (chunk?.Response is { Length: > 0 } text)
                {
                    answer.Append(text);
                }
            }

            string result = answer.ToString().Trim();
            return result.Length > 0 ? result : null;
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or InvalidOperationException or TaskCanceledException)
        {
            // A local model that is offline or slow must not break the assistant.
            _logger.LogWarning(exception, "The Ollama request failed for model {Model}.", options.Model);
            return null;
        }
    }
}
