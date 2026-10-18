using Laura.Core.ChatGpt;

namespace Laura.Core.Configuration;

/// <summary>Preserves saved account choices during transient broker failures and scopes consent to the selected registration.</summary>
public static class ChatGptSettingsDraft
{
    /// <summary>Reconciles metadata without treating an unavailable account list as logout. Explicit disconnect and account changes clear consent.</summary>
    public static ChatGptOptions Reconcile(ChatGptOptions saved, ChatGptConnectionState state, bool disconnectedByOwner = false)
    {
        ArgumentNullException.ThrowIfNull(saved); ArgumentNullException.ThrowIfNull(state);
        Guid? registration = disconnectedByOwner ? null : state.Active ?? saved.Registration;
        return registration == saved.Registration && !disconnectedByOwner ? saved : saved with
        { Registration = registration, Enabled = false, IncludedPlanOnly = false, ShareContext = false, AllowAutomatic = false };
    }
}
