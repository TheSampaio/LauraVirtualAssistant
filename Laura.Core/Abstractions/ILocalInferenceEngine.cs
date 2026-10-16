using Laura.Core.Conversation;

namespace Laura.Core.Abstractions;

/// <summary>Embedded model lifecycle, independent of presentation.</summary>
public interface ILocalInferenceEngine : IConversationEngine
{
    /// <summary>Gets the most recent health or failure status.</summary>
    string Status { get; }
    /// <summary>Gets the current loaded model, or null.</summary>
    string? LoadedModel { get; }
    /// <summary>Notifies subscribers of streamed response text.</summary>
    event EventHandler<string>? TokenReceived;
    /// <summary>Reports native weight-loading fraction when supported by the backend.</summary>
    event EventHandler<float>? LoadingProgress { add { } remove { } }
    /// <summary>Loads the model on a worker, releasing the previous native model.</summary>
    Task LoadAsync(string id, CancellationToken cancellationToken);
    /// <summary>Cancels generation without queueing behind native inference.</summary>
    void Cancel();
    /// <summary>Releases model resources once in-flight work has stopped.</summary>
    Task UnloadAsync(CancellationToken cancellationToken);
}
