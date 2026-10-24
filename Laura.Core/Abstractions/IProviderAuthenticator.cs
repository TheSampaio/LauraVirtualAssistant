using Laura.Core.Models;

namespace Laura.Core.Abstractions;

/// <summary>Extension point for official account authentication, separate from text adapters and native tool authorization.</summary>
public interface IProviderAuthenticator
{
    /// <summary>Gets the native registered provider identity.</summary>
    string Provider { get; }
    /// <summary>Reads official connection/billing evidence without initiating inference or copying another application's credentials.</summary>
    Task<ProviderAuthorization> InspectAsync(CancellationToken cancellationToken);
    /// <summary>Starts only a supported official sign-in after an explicit native owner gesture.</summary>
    Task<ProviderAuthorization> ConnectAsync(CancellationToken cancellationToken);
    /// <summary>Disconnects this application's official authorization without signing out other applications.</summary>
    Task DisconnectAsync(CancellationToken cancellationToken);
}
