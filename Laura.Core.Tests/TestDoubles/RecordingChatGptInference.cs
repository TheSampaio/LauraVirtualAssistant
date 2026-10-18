using Laura.Core.Abstractions;
using Laura.Core.ChatGpt;

namespace Laura.Core.Tests.TestDoubles;

/// <summary>Scripts completed protocol outcomes and captures authorized inputs without contacting an account.</summary>
internal sealed class RecordingChatGptInference : IChatGptInference
{
    /// <inheritdoc />
    public bool IsAvailable { get; set; } = true;
    internal List<ChatGptInferenceRequest> Requests { get; } = [];
    internal Queue<ChatGptInferenceResult> Results { get; } = new();
    internal Func<CancellationToken, Task<ChatGptInferenceResult>>? Execute { get; set; }
    /// <inheritdoc />
    public Task<ChatGptInferenceResult> CompleteAsync(ChatGptInferenceRequest inference, Action<string>? textDelta, CancellationToken cancellationToken)
    {
        Requests.Add(inference with { Input = inference.Input.ToArray(), Tools = inference.Tools.ToArray() });
        if (Execute is not null) { return Execute(cancellationToken); }
        var result = Results.Count > 0 ? Results.Dequeue() : new("Synthetic completed response.", [], 1, 1);
        textDelta?.Invoke(result.Text); return Task.FromResult(result);
    }
}
