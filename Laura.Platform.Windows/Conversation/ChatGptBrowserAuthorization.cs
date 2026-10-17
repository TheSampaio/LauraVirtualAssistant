using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Laura.Core.Abstractions;

namespace Laura.Platform.Windows.Conversation;

/// <summary>Receives one bounded literal IPv4 loopback callback after opening the Windows default browser.</summary>
public sealed class ChatGptBrowserAuthorization : IChatGptBrowserAuthorization
{
    /// <summary>Starts the listener before browser launch. Args: authorize: Creates the authorization URL for the exact bound callback; token: Login deadline. Returns: Callback URI for OAuth state validation.</summary>
    public async Task<Uri> ReceiveAsync(Func<Uri, Uri> authorize, CancellationToken token)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(4);
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var callback = new Uri($"http://127.0.0.1:{port}/auth/callback");
        Uri url = authorize(callback);
        if (url.Scheme != "https" || url.Host != "auth.openai.com" || url.AbsolutePath != "/api/accounts/authorize")
        { throw new InvalidDataException("oauth_authorization_endpoint_invalid"); }
        using var browser = Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true });
        while (true)
        {
            using TcpClient connection = await listener.AcceptTcpClientAsync(token).ConfigureAwait(false);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(5));
            await using NetworkStream network = connection.GetStream();
            using var reader = new StreamReader(network, Encoding.ASCII, false, 1024, leaveOpen: true);
            var header = new StringBuilder();
            try
            {
                while (header.Length < 16384)
                {
                    var character = new char[1];
                    if (await reader.ReadAsync(character, deadline.Token).ConfigureAwait(false) == 0) { break; }
                    header.Append(character[0]);
                    if (header.Length >= 4 && header.ToString(header.Length - 4, 4) == "\r\n\r\n") { break; }
                }
                string[] lines = header.ToString().Split("\r\n", StringSplitOptions.None);
                string[] request = lines[0].Split(' ');
                bool valid = request.Length == 3 && request[0] == "GET" && request[1].StartsWith("/auth/callback?", StringComparison.Ordinal)
                    && request[2] == "HTTP/1.1" && lines.Count(line => line.Equals($"Host: 127.0.0.1:{port}", StringComparison.OrdinalIgnoreCase)) == 1
                    && header.Length < 16384;
                const string text = "Return to Laura to check the connection. You may close this tab.";
                string response = $"HTTP/1.1 {(valid ? "200 OK" : "400 Bad Request")}\r\nContent-Type: text/plain; charset=utf-8\r\nContent-Length: {Encoding.UTF8.GetByteCount(text)}\r\nCache-Control: no-store\r\nContent-Security-Policy: default-src 'none'\r\nConnection: close\r\n\r\n{text}";
                await network.WriteAsync(Encoding.UTF8.GetBytes(response), deadline.Token).ConfigureAwait(false);
                if (valid) { return new Uri(callback.GetLeftPart(UriPartial.Authority) + request[1]); }
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
            catch (IOException) { }
        }
    }
}
