using Laura.Core.ChatGpt;

namespace Laura.Core.Abstractions;

/// <summary>Exposes native ChatGPT connection controls without exposing credentials to chat or tools.</summary>
public interface IChatGptConnection
{
    /// <summary>Reads protected registrations as safe display metadata. Returns: Current selected connection.</summary>
    Task<ChatGptConnectionState> ReadAsync(CancellationToken cancellationToken = default);
    /// <summary>Opens official browser authorization. Args: registration: Saved registration to reconnect, or null for a new account.</summary>
    Task<ChatGptConnectionState> SignInAsync(Guid? registration, CancellationToken cancellationToken = default);
    /// <summary>Selects a previously validated registration without merging credentials.</summary>
    Task<ChatGptConnectionState> SelectAsync(Guid registration, CancellationToken cancellationToken = default);
    /// <summary>Discovers the selected account's current catalog without claiming inference access.</summary>
    Task<IReadOnlyList<ChatGptModel>> ModelsAsync(CancellationToken cancellationToken = default);
    /// <summary>Checks a model with a fixed synthetic prompt. Args: includedPlanOnlyConfirmed: Native owner confirmation that this app's additional-credit allowance is zero. Returns: Completed inference evidence only.</summary>
    Task<ChatGptProbeResult> ProbeAsync(string model, bool includedPlanOnlyConfirmed, CancellationToken cancellationToken = default);
    /// <summary>Stops connection work, attempts remote revocation, then clears local tokens while retaining registration identity.</summary>
    Task<ChatGptConnectionState> DisconnectAsync(CancellationToken cancellationToken = default);
}
