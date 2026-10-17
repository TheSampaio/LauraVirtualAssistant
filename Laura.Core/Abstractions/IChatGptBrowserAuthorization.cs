namespace Laura.Core.Abstractions;

/// <summary>Opens the native system browser and receives a bounded local OAuth callback.</summary>
public interface IChatGptBrowserAuthorization
{
    /// <summary>Starts a loopback listener before opening the authorization URL. Returns: Callback for state validation.</summary>
    Task<Uri> ReceiveAsync(Func<Uri, Uri> authorize, CancellationToken token);
}
