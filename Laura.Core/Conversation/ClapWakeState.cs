namespace Laura.Core.Conversation;

/// <summary>Reports native readiness without opening a device or selecting a different provider.</summary>
/// <param name="Provider">Actual selected runtime location.</param>
/// <param name="ProviderAvailable">Whether that runtime is configured and locally ready for admission.</param>
/// <param name="LocalSpeechInstalled">Whether the reviewed local recognition artifact is installed.</param>
/// <param name="EngineReady">Whether the assistant has finished its current reply or work.</param>
/// <param name="ConversationBusy">Whether capture, synthesis, inference or a wake workflow is active.</param>
/// <param name="Suspended">Whether an explicit capture handoff holds the detector closed.</param>
public sealed record ClapWakeState(string Provider, bool ProviderAvailable, bool LocalSpeechInstalled,
    bool EngineReady, bool ConversationBusy, bool Suspended);
