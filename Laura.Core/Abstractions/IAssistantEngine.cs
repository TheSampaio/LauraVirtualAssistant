using Laura.Core.Engine;
using Laura.Core.Conversation;

namespace Laura.Core.Abstractions;

/// <summary>
/// Assistant engine: orchestrates chat commands, skill dispatch, and speech.
/// </summary>
public interface IAssistantEngine : IAsyncDisposable
{
    /// <summary>
    /// Occurs when the assistant state changes.
    ///
    /// Raised from the internal processing loop; UI subscribers
    /// must marshal to the UI thread.
    /// </summary>
    event EventHandler<AssistantState>? StateChanged;

    /// <summary>
    /// Occurs when a user or assistant message should be shown in the conversation UI.
    /// </summary>
    event EventHandler<ConversationMessage>? ConversationMessageReceived;

    /// <summary>
    /// Gets the current state.
    /// </summary>
    AssistantState State { get; }

    /// <summary>
    /// Gets recent conversation messages for UI surfaces that are created after
    /// the conversation has already started.
    /// </summary>
    IReadOnlyList<ConversationMessage> ConversationHistory { get; }

    /// <summary>
    /// Starts the engine and speaks the opening greeting.
    ///
    /// Args:
    ///     cancellationToken: Token that aborts startup.
    ///
    /// Returns:
    ///     A task completed when the engine is operational. The task does not
    ///     represent the engine lifetime and returns without waiting for speech.
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops command processing.
    ///
    /// Args:
    ///     cancellationToken: Token that aborts shutdown.
    ///
    /// Returns:
    ///     A task completed when the engine is stopped.
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a text command from the chat interface.
    ///
    /// Args:
    ///     text: Command to execute.
    ///     cancellationToken: Token that aborts execution.
    ///
    /// Returns:
    ///     A task completed when the command has finished processing.
    /// </summary>
    Task SubmitCommandAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// Makes Laura speak text while respecting the configured voice profile.
    ///
    /// Args:
    ///     text: Text to speak.
    ///     cancellationToken: Token that interrupts speech.
    ///
    /// Returns:
    ///     A task completed when speech ends.
    /// </summary>
    Task SpeakAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// Immediately stops speech.
    /// </summary>
    void StopSpeaking();
}
