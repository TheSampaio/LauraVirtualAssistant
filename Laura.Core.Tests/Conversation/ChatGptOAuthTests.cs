using System.Security.Cryptography;
using System.Text;
using Laura.Core.ChatGpt;

namespace Laura.Core.Tests.Conversation;

/// <summary>Exercises synthetic OAuth transactions without claiming account authorization.</summary>
public sealed class ChatGptOAuthTests
{
    private static readonly Uri Callback = new("http://127.0.0.1:54321/auth/callback");
    /// <summary>Checks the exact official request contract, independent entropy and S256 encoding.</summary>
    [Fact]
    public void InitialRegistrationUsesOfficialEndpointAndPersistentHost()
    {
        var attempt = new ChatGptOAuthAttempt(Callback); string host = "urn:uuid:" + Guid.NewGuid().ToString("D");
        var url = attempt.Authorize(host); var query = Query(url);
        Assert.Equal("auth.openai.com", url.Host); Assert.Equal("/api/accounts/authorize", url.AbsolutePath);
        Assert.Equal("dynamic_agent_client", query["client_id"]); Assert.Equal("Laura", query["agent_name_hint"]);
        Assert.Equal(host, query["ext_agent_host_id"]); Assert.Equal(Callback.AbsoluteUri, query["redirect_uri"]);
        Assert.Equal("S256", query["code_challenge_method"]); Assert.Equal(ChatGptOAuthAttempt.Resource, query["resource"]);
        Assert.Contains("chatgpt.tokens.use.direct", query["scope"], StringComparison.Ordinal);
        string challenge = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(attempt.Verifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Assert.Equal(challenge, query["code_challenge"]); Assert.NotEqual(attempt.State, attempt.Nonce); Assert.NotEqual(attempt.Nonce, attempt.Verifier);
        Assert.DoesNotContain(attempt.Verifier, url.AbsoluteUri, StringComparison.Ordinal);
        Assert.NotEqual(attempt.State, new ChatGptOAuthAttempt(Callback).State);
        Assert.DoesNotContain(attempt.Nonce, attempt.ToString(), StringComparison.Ordinal);
    }
    /// <summary>Checks issued-client reuse and hints without conflating it with the installation identity.</summary>
    [Fact]
    public void ReturningRegistrationReusesIssuedClientAndOmitsAgentName()
    {
        var attempt = new ChatGptOAuthAttempt(Callback, "oaiapp_saved");
        var query = Query(attempt.Authorize("urn:uuid:" + Guid.NewGuid().ToString("D"), "test@example.invalid", "synthetic_id_token"));
        Assert.Equal("oaiapp_saved", query["client_id"]); Assert.False(query.ContainsKey("agent_name_hint"));
        Assert.Equal("test@example.invalid", query["login_hint"]); Assert.Equal("synthetic_id_token", query["id_token_hint"]);
        Assert.Equal(("synthetic_code", "oaiapp_saved"), attempt.Consume(new Uri(Callback + "?state=" + attempt.State + "&code=synthetic_code")));
    }
    /// <summary>Checks new registration requires an issued client and accepts its callback exactly once.</summary>
    [Fact]
    public void CallbackConsumesCodeExactlyOnce()
    {
        var attempt = new ChatGptOAuthAttempt(Callback);
        var response = new Uri(Callback + "?state=" + attempt.State + "&code=synthetic_code&client_id=oaiapp_new");
        Assert.Equal(("synthetic_code", "oaiapp_new"), attempt.Consume(response));
        Assert.Throws<InvalidDataException>(() => attempt.Consume(response));
    }
    /// <summary>Rejects invalid callbacks, identities, duplicate fields and authorization denial.</summary>
    [Theory]
    [InlineData("?state=wrong&code=x&client_id=oaiapp_new", "oauth_state_invalid")]
    [InlineData("?state={state}&error=access_denied", "oauth_access_denied")]
    [InlineData("?state=wrong&error=access_denied", "oauth_state_invalid")]
    [InlineData("?state={state}&code=x", "oauth_client_invalid")]
    [InlineData("?state={state}&code=x&client_id=dynamic_agent_client", "oauth_client_invalid")]
    [InlineData("?state={state}&code=x&client_id=oaiapp_new&code=y", "oauth_callback_duplicate")]
    [InlineData("?state={state}&client_id=oaiapp_new", "oauth_code_invalid")]
    public void RejectsInvalidCallbackBeforeExchange(string query, string expected)
    {
        var attempt = new ChatGptOAuthAttempt(Callback);
        Assert.Equal(expected, Assert.Throws<InvalidDataException>(() => attempt.Consume(new Uri(Callback + query.Replace("{state}", attempt.State, StringComparison.Ordinal)))).Message);
    }
    /// <summary>Rejects callback substitution or switching a returning registration's issued client.</summary>
    [Fact]
    public void RejectsCallbackAndRegistrationSubstitution()
    {
        var first = new ChatGptOAuthAttempt(Callback, "oaiapp_saved");
        Assert.Throws<InvalidDataException>(() => first.Consume(new Uri(Callback + "?state=" + first.State + "&code=x&client_id=oaiapp_other")));
        var second = new ChatGptOAuthAttempt(Callback);
        Assert.Throws<InvalidDataException>(() => second.Consume(new Uri("http://localhost:54321/auth/callback?state=" + second.State + "&code=x&client_id=oaiapp_new")));
    }
    /// <summary>Rejects non-loopback transports, host aliases and changed callback paths.</summary>
    [Theory]
    [InlineData("http://localhost:54321/auth/callback")]
    [InlineData("https://127.0.0.1:54321/auth/callback")]
    [InlineData("http://127.0.0.1:54321/callback")]
    [InlineData("http://127.0.0.1:54321/auth/callback?x=1")]
    public void RequiresLiteralLoopbackCallback(string url) => Assert.Throws<ArgumentException>(() => new ChatGptOAuthAttempt(new Uri(url)));
    /// <summary>Decodes synthetic request parameters for contract assertions.</summary>
    private static Dictionary<string, string> Query(Uri url) => url.Query.TrimStart('?').Split('&').Select(pair => pair.Split('=', 2))
        .ToDictionary(pair => Uri.UnescapeDataString(pair[0]), pair => Uri.UnescapeDataString(pair[1]), StringComparer.Ordinal);
}
