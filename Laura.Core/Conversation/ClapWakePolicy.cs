using Laura.Core.Configuration;

namespace Laura.Core.Conversation;

/// <summary>Decides whether the optional local detector may open, independently of native audio APIs.</summary>
public static class ClapWakePolicy
{
    /// <summary>Requires opt-in, an unmuted idle assistant and the selected provider's actual prerequisites.</summary>
    public static bool CanArm(LauraSettings settings, ClapWakeState state) => settings.ClapWakeEnabled && !settings.MicrophoneMuted
        && state.Provider is "chatgpt" or "local" or "gemini" && state.LocalSpeechInstalled && state.ProviderAvailable && state.EngineReady
        && !state.ConversationBusy && !state.Suspended;
}
