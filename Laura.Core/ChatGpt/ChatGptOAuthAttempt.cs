using System.Security.Cryptography;
using System.Text;

namespace Laura.Core.ChatGpt;

/// <summary>One-use public-client OAuth transaction with independent PKCE, state, and nonce values.</summary>
public sealed class ChatGptOAuthAttempt
{
    /// <summary>The official resource for ChatGPT plan inference; no API-key billing route is configured.</summary>
    public const string Resource = "https://api.openai.com/v1";
    private const string DynamicClient = "dynamic_agent_client";
    private readonly string _client;
    private readonly DateTimeOffset _expires = DateTimeOffset.UtcNow.AddMinutes(5);
    private int _consumed;
    /// <summary>Gets the exact callback used in both authorization and code exchange.</summary>
    public Uri Callback { get; }
    /// <summary>Gets the transaction nonce for signature-validated ID-token checking.</summary>
    public string Nonce { get; } = RandomValue();
    /// <summary>Gets the private PKCE verifier for token exchange only.</summary>
    public string Verifier { get; } = RandomValue();
    /// <summary>Gets the unpredictable callback correlation value.</summary>
    public string State { get; } = RandomValue();
    /// <summary>Creates a transaction. Args: callback: Literal IPv4 loopback URI; client: Previously issued client, or null.</summary>
    public ChatGptOAuthAttempt(Uri callback, string? client = null)
    {
        ArgumentNullException.ThrowIfNull(callback);
        if (callback.Scheme != "http" || callback.Host != "127.0.0.1" || callback.AbsolutePath != "/auth/callback"
            || callback.Query.Length > 0 || callback.Fragment.Length > 0 || callback.UserInfo.Length > 0)
        { throw new ArgumentException("OAuth requires an exact IPv4 loopback callback.", nameof(callback)); }
        if (client is not null && !IssuedClient(client)) { throw new ArgumentException("An issued registration client is required.", nameof(client)); }
        Callback = callback; _client = client ?? DynamicClient;
    }
    /// <summary>Builds official authorization. Args: host: Stable installation UUID URI; email: Optional account hint; idToken: Retained ID token for this registration only; consent: Explicit permission renewal.</summary>
    public Uri Authorize(string host, string? email = null, string? idToken = null, bool consent = false)
    {
        if (!host.StartsWith("urn:uuid:", StringComparison.Ordinal) || !Guid.TryParse(host[9..], out _))
        { throw new ArgumentException("A persistent opaque host identity is required.", nameof(host)); }
        var values = new Dictionary<string, string>
        {
            ["client_id"] = _client, ["ext_agent_host_id"] = host, ["response_type"] = "code", ["redirect_uri"] = Callback.AbsoluteUri,
            ["scope"] = "openid profile email offline_access resource.invoke chatgpt.tokens.use.direct", ["resource"] = Resource,
            ["state"] = State, ["nonce"] = Nonce, ["code_challenge_method"] = "S256",
            ["code_challenge"] = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(Verifier)))
        };
        if (_client == DynamicClient) { values["agent_name_hint"] = "Laura"; }
        else
        {
            if (!string.IsNullOrEmpty(idToken)) { values["id_token_hint"] = idToken; }
            if (!string.IsNullOrEmpty(email)) { values["login_hint"] = email; }
        }
        if (consent) { values["prompt"] = "consent"; }
        return new Uri("https://auth.openai.com/api/accounts/authorize?" + string.Join("&", values.Select(pair => Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value))));
    }
    /// <summary>Consumes a callback exactly once, including failures. Returns: Code and issued client, never a temporary registration ID. Raises: InvalidDataException for untrusted callbacks.</summary>
    public (string Code, string Client) Consume(Uri response)
    {
        if (Interlocked.Exchange(ref _consumed, 1) != 0 || DateTimeOffset.UtcNow > _expires) { throw new InvalidDataException("oauth_attempt_expired"); }
        if (response.GetLeftPart(UriPartial.Path) != Callback.GetLeftPart(UriPartial.Path) || response.Fragment.Length > 0)
        { throw new InvalidDataException("oauth_callback_invalid"); }
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string pair in response.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = pair.Split('=', 2);
            string key = Uri.UnescapeDataString(parts[0].Replace('+', ' '));
            if (!values.TryAdd(key, parts.Length == 2 ? Uri.UnescapeDataString(parts[1].Replace('+', ' ')) : ""))
            { throw new InvalidDataException("oauth_callback_duplicate"); }
        }
        if (!values.TryGetValue("state", out string? state) || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(state), Encoding.UTF8.GetBytes(State)))
        { throw new InvalidDataException("oauth_state_invalid"); }
        if (values.TryGetValue("error", out string? error)) { throw new InvalidDataException(error == "access_denied" ? "oauth_access_denied" : "oauth_authorization_failed"); }
        string client = values.GetValueOrDefault("client_id") ?? _client;
        if (!IssuedClient(client) || _client != DynamicClient && client != _client) { throw new InvalidDataException("oauth_client_invalid"); }
        if (!values.TryGetValue("code", out string? code) || string.IsNullOrWhiteSpace(code) || code.Length > 8192)
        { throw new InvalidDataException("oauth_code_invalid"); }
        return (code, client);
    }
    /// <summary>Prevents accidental transaction disclosure through interpolation or diagnostics.</summary>
    public override string ToString() => "ChatGPT OAuth transaction (redacted)";
    /// <summary>Validates only a bounded issued-client shape, never an email or temporary registration identifier.</summary>
    private static bool IssuedClient(string client) => client.StartsWith("oaiapp_", StringComparison.Ordinal) && client.Length is > 7 and <= 256
        && client.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-');
    /// <summary>Generates independent cryptographic transaction entropy.</summary>
    private static string RandomValue() => Base64Url(RandomNumberGenerator.GetBytes(32));
    /// <summary>Encodes PKCE bytes without Base64 padding.</summary>
    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
