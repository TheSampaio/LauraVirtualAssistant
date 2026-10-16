using Laura.Core.Conversation;

namespace Laura.Core.Abstractions;

/// <summary>Embedded model lifecycle, independent of presentation.</summary>
public interface ILocalInferenceEngine : IConversationEngine
{
    /// <summary>Gets the most recent health or failure status.</summary>
    string Status { get; }
    /// <summary>Gets the current loaded model, or null.</summary>
    string? LoadedModel { get; }
    /// <summary>Gets the actually loaded backend, or unloaded when no weights are resident.</summary>
    string Backend => "unloaded";
    /// <summary>Gets a closed backend admission or fallback status for native presentation.</summary>
    string BackendStatus => "none";
    /// <summary>Gets the actual native offloaded layer count for the resident model.</summary>
    int OffloadedLayers => 0;
    /// <summary>Gets measured free device memory before the current weight load, or null when unavailable.</summary>
    long? FreeGpuMemoryBeforeLoad => null;
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
