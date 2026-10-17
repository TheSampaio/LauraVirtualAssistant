using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Laura.Platform.Windows.Conversation;

/// <summary>Validates official OpenID discovery and signed ID tokens with Microsoft's maintained JWT implementation.</summary>
public sealed class ChatGptIdTokenValidator(HttpClient http) : IDisposable
{
    private JsonWebKeySet? _keys;
    private DateTimeOffset _keysLoaded;
    /// <summary>Reads discovery without following redirects or accepting arbitrary credential destinations.</summary>
    internal async Task<Uri> RevocationEndpointAsync(CancellationToken token)
    {
        using var response = await http.GetAsync("https://auth.openai.com/.well-known/openid-configuration", token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) { throw new InvalidDataException("oidc_discovery_unavailable"); }
        await response.Content.LoadIntoBufferAsync(256 * 1024, token).ConfigureAwait(false);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token).ConfigureAwait(false));
        var root = document.RootElement;
        if (root.GetProperty("issuer").GetString() != "https://auth.openai.com"
            || root.GetProperty("authorization_endpoint").GetString() != "https://auth.openai.com/api/accounts/authorize"
            || root.GetProperty("token_endpoint").GetString() != "https://auth.openai.com/api/accounts/oauth/token"
            || root.GetProperty("jwks_uri").GetString() != "https://auth.openai.com/.well-known/jwks.json"
            || !Uri.TryCreate(root.GetProperty("revocation_endpoint").GetString(), UriKind.Absolute, out var revoke)
            || revoke.Scheme != "https" || revoke.Host != "auth.openai.com" || !revoke.IsDefaultPort || revoke.UserInfo.Length != 0 || revoke.Query.Length != 0)
        { throw new InvalidDataException("oidc_discovery_invalid"); }
        return revoke;
    }
    /// <summary>Checks signature, issuer, issued-client audience, expiry, nonce and identity. Returns: Validated subject and optional email.</summary>
    internal async Task<(string Subject, string Email)> ValidateAsync(string raw, string client, string? nonce, CancellationToken token)
    {
        if (raw.Length is < 1 or > 65536) { throw new InvalidDataException("oidc_token_invalid"); }
        await RevocationEndpointAsync(token).ConfigureAwait(false);
        for (int attempt = 0; attempt < 2; attempt++)
        {
            if (_keys is null || DateTimeOffset.UtcNow - _keysLoaded > TimeSpan.FromHours(1) || attempt == 1)
            {
                using var response = await http.GetAsync("https://auth.openai.com/.well-known/jwks.json", token).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) { throw new InvalidDataException("oidc_keys_unavailable"); }
                await response.Content.LoadIntoBufferAsync(256 * 1024, token).ConfigureAwait(false);
                _keys = new JsonWebKeySet(await response.Content.ReadAsStringAsync(token).ConfigureAwait(false)); _keysLoaded = DateTimeOffset.UtcNow;
            }
            var validation = await new JsonWebTokenHandler { MapInboundClaims = false }.ValidateTokenAsync(raw, new TokenValidationParameters
            {
                ValidIssuer = "https://auth.openai.com", ValidAudience = client, ValidateIssuer = true, ValidateAudience = true,
                ValidateLifetime = true, RequireExpirationTime = true, RequireSignedTokens = true, ValidateIssuerSigningKey = true,
                IssuerSigningKeys = _keys.GetSigningKeys(), ClockSkew = TimeSpan.FromSeconds(5),
                ValidAlgorithms = [SecurityAlgorithms.RsaSha256, SecurityAlgorithms.EcdsaSha256], IncludeTokenOnFailedValidation = false,
                LogTokenId = false, LogValidationExceptions = false
            }).ConfigureAwait(false);
            if (!validation.IsValid)
            {
                if (attempt == 0 && validation.Exception is SecurityTokenSignatureKeyNotFoundException) { continue; }
                throw new InvalidDataException("oidc_token_invalid");
            }
            var jwt = (JsonWebToken)validation.SecurityToken;
            if (!jwt.TryGetPayloadValue<string>("sub", out string? subject) || string.IsNullOrWhiteSpace(subject) || subject.Length > 512
                || !jwt.TryGetPayloadValue<long>("iat", out long issued) || issued > DateTimeOffset.UtcNow.AddSeconds(5).ToUnixTimeSeconds()
                || nonce is not null && (!jwt.TryGetPayloadValue<string>("nonce", out string? actual) || actual != nonce))
            { throw new InvalidDataException("oidc_identity_invalid"); }
            string email = jwt.TryGetPayloadValue<string>("email", out string? value) && value?.Length <= 512 ? value : "";
            return (subject, email);
        }
        throw new InvalidDataException("oidc_token_invalid");
    }
    /// <inheritdoc />
    public void Dispose() { http.Dispose(); GC.SuppressFinalize(this); }
}
