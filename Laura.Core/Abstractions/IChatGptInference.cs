using Laura.Core.ChatGpt;

namespace Laura.Core.Abstractions;

/// <summary>Performs official plan inference without returning credentials to domain code.</summary>
public interface IChatGptInference
{
    /// <summary>Gets whether protected connection metadata currently permits plan inference.</summary>
    bool IsAvailable { get; }
    /// <summary>Gets the selected native registration identity, without account subjects, tokens or issued clients.</summary>
    Guid? ActiveRegistration => null;
    /// <summary>Completes one bounded stateless Responses round. Args: request: Authorized context and native tool schemas; textDelta: Provisional visible text only; cancellationToken: Round cancellation.</summary>
    Task<ChatGptInferenceResult> CompleteAsync(ChatGptInferenceRequest inference, Action<string>? textDelta, CancellationToken cancellationToken);
}
