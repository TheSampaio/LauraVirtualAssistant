using Laura.Core.Abstractions;
using Laura.Core.ChatGpt;

namespace Laura.App.Infrastructure;

/// <summary>Disconnected offline connection for isolated UI rendering; never reads the owner's service account or opens a browser.</summary>
internal sealed class UiSmokeChatGptConnection : IChatGptConnection, IChatGptInference
{
    /// <inheritdoc />
    public bool IsAvailable => false;
    /// <inheritdoc />
    public Task<ChatGptConnectionState> ReadAsync(CancellationToken cancellationToken = default) => Task.FromResult(new ChatGptConnectionState(null, [], "disconnected", "broker_required"));
    /// <inheritdoc />
    public Task<ChatGptConnectionState> SignInAsync(Guid? registration, CancellationToken cancellationToken = default) => Unavailable<ChatGptConnectionState>();
    /// <inheritdoc />
    public Task<ChatGptConnectionState> SelectAsync(Guid registration, CancellationToken cancellationToken = default) => Unavailable<ChatGptConnectionState>();
    /// <inheritdoc />
    public Task<IReadOnlyList<ChatGptModel>> ModelsAsync(CancellationToken cancellationToken = default) => Unavailable<IReadOnlyList<ChatGptModel>>();
    /// <inheritdoc />
    public Task<ChatGptProbeResult> ProbeAsync(string model, bool includedPlanOnlyConfirmed, CancellationToken cancellationToken = default) => Unavailable<ChatGptProbeResult>();
    /// <inheritdoc />
    public Task<ChatGptConnectionState> DisconnectAsync(CancellationToken cancellationToken = default) => ReadAsync(cancellationToken);
    /// <inheritdoc />
    public Task<ChatGptInferenceResult> CompleteAsync(ChatGptInferenceRequest inference, Action<string>? textDelta, CancellationToken cancellationToken) => Unavailable<ChatGptInferenceResult>();
    /// <summary>Fails explicitly instead of simulating authenticated behavior.</summary>
    private static Task<T> Unavailable<T>() => Task.FromException<T>(new ChatGptConnectionException("broker_required"));
}
