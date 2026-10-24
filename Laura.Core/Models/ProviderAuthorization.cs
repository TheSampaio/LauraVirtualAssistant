namespace Laura.Core.Models;

/// <summary>Separates official account connection evidence from inference capability and billing permission.</summary>
public enum ProviderConnectionState
{
    /// <summary>The official runtime is absent.</summary>
    NotInstalled,
    /// <summary>No account is connected.</summary>
    NotConnected,
    /// <summary>An explicit official sign-in is in progress.</summary>
    Connecting,
    /// <summary>An account is connected; subscription billing is not established.</summary>
    Connected,
    /// <summary>The official channel confirms subscription eligibility.</summary>
    SubscriptionEligible,
    /// <summary>The official channel reports a usage limit.</summary>
    LimitReached,
    /// <summary>The account authorization expired.</summary>
    AuthorizationExpired,
    /// <summary>Only separately billed API use is available.</summary>
    ApiBillingOnly,
    /// <summary>The official channel is unavailable.</summary>
    Unavailable
}
/// <summary>Describes only officially established billing provenance; an account login alone cannot establish it.</summary>
public enum ProviderBillingMode
{
    /// <summary>No official billing evidence.</summary>
    Unknown,
    /// <summary>Local computation without cloud billing.</summary>
    Local,
    /// <summary>Officially confirmed included subscription usage.</summary>
    Subscription,
    /// <summary>Separately billed API usage.</summary>
    Api
}
/// <summary>Inert official connection evidence for a future adapter; it contains no tokens or execution grants.</summary>
public sealed record ProviderAuthorization(string Provider, ProviderConnectionState State, ProviderBillingMode Billing, bool OfficiallySupported, bool ApiKeyOverridePresent = false)
{
    /// <summary>Admits subscription-only inference only after official eligibility and billing confirmation, with no API-key override.</summary>
    public bool CanUseSubscriptionOnly => OfficiallySupported && !ApiKeyOverridePresent && Billing == ProviderBillingMode.Subscription && State == ProviderConnectionState.SubscriptionEligible;
}
