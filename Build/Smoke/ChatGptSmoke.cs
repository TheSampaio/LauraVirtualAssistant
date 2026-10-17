using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Laura.Core.Abstractions;
using Laura.Core.ChatGpt;
using Laura.Core.Diagnostics;
using Laura.Platform.Windows.Conversation;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

/// <summary>Validates the real Windows credential adapter with synthetic OAuth servers, separately from owner-authorized live checks.</summary>
internal static class ChatGptSmoke
{
    /// <summary>Runs deterministic protocol, identity, rotation, persistence and no-payload-log checks; no real authentication is claimed.</summary>
    internal static async Task<int> RunSyntheticAsync(string directory)
    {
        Directory.CreateDirectory(directory); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90)); var token = timeout.Token;
        using var handler = new SyntheticServer(); using var http = new HttpClient(handler, false); using var identityHttp = new HttpClient(handler, false);
        using var validator = new ChatGptIdTokenValidator(identityHttp); var browser = new SyntheticBrowser(handler);
        var store = new ChatGptCredentialStore(Path.Combine(directory, "Credentials"), new WindowsPrivateDataProtector());
        using var connection = new ChatGptConnection(http, store, browser, validator);
        using var capture = LauraDiagnostics.Capture(); var checks = new List<string>();
        var first = await connection.SignInAsync(null, token); Require(first.Status == "plan_authorized", "synthetic_sign_in"); checks.Add("signed_identity_and_plan_permission");
        Guid active = first.Active!.Value; string host;
        using (await store.AcquireAsync(token))
        {
            var credentials = await store.ReadAsync(token); host = credentials.Host;
            Require(credentials.Accounts.Single().Client == "oaiapp_synthetic" && credentials.Accounts.Single().Subject == "synthetic_owner", "issued_registration");
        }
        string encrypted = Encoding.UTF8.GetString(await File.ReadAllBytesAsync(Path.Combine(directory, "Credentials", "chatgpt-credentials.dat"), token));
        Require(!encrypted.Contains("synthetic_access", StringComparison.Ordinal) && !encrypted.Contains("synthetic_owner", StringComparison.Ordinal), "dpapi_plaintext"); checks.Add("dpapi_encryption");
        using (var restarted = new ChatGptConnection(new HttpClient(handler, false), new ChatGptCredentialStore(Path.Combine(directory, "Credentials"), new WindowsPrivateDataProtector()), browser, validator))
        { var recovered = await restarted.ReadAsync(token); Require(recovered.Active == active && recovered.Status == "plan_authorized", "restart"); }
        checks.Add("restart_preserves_registration");
        var models = await connection.ModelsAsync(token); Require(models.Count == 1 && models[0].Slug == "synthetic-model", "catalog"); checks.Add("account_catalog_filters_visibility");
        var probe = await connection.ProbeAsync(models[0].Slug, true, token); Require(probe.Completed && probe.InputTokens == 9 && probe.OutputTokens == 4, "terminal"); checks.Add("completed_stream_and_usage");
        handler.ResponseMode = "partial"; await RejectAsync(() => connection.ProbeAsync("synthetic-model", true, token), "response_terminal_missing"); checks.Add("partial_stream_rejected");
        handler.ResponseMode = "failed"; await RejectAsync(() => connection.ProbeAsync("synthetic-model", true, token), "response_failed"); checks.Add("failed_stream_rejected");
        handler.ResponseMode = "incomplete"; await RejectAsync(() => connection.ProbeAsync("synthetic-model", true, token), "response_incomplete"); checks.Add("incomplete_stream_rejected");
        handler.ResponseMode = "limit"; await RejectAsync(() => connection.ProbeAsync("synthetic-model", true, token), "subscription_sharing_usage_limit_exceeded"); checks.Add("usage_limit_no_billing_fallback");
        handler.ResponseMode = "detail";
        try { await connection.ProbeAsync("synthetic-model", true, token); throw new InvalidOperationException("Expected direct-admission failure."); }
        catch (ChatGptConnectionException error) { Require(error.HttpStatus == 403 && error.BodyShape == "detail" && error.RequestId == "req_synthetic", "admission"); }
        checks.Add("direct_admission_shape_and_status"); handler.ResponseMode = "completed";
        await ExpireAsync(store, token); await Task.WhenAll(connection.ModelsAsync(token), connection.ModelsAsync(token));
        Require(handler.Refreshes == 1, "refresh_serialization");
        using (await store.AcquireAsync(token)) { var saved = await store.ReadAsync(token); Require(saved.Accounts.Single().RefreshToken == "synthetic_refresh_1" && saved.Host == host, "rotation"); }
        checks.Add("serialized_refresh_and_atomic_rotation");
        handler.Subject = "different_owner"; await RejectAsync(() => connection.SignInAsync(active, token), "account_identity_mismatch"); handler.Subject = "synthetic_owner";
        Require((await connection.ReadAsync(token)).Active == active, "selected_identity_preserved"); checks.Add("returning_identity_mismatch_preserves_account");
        foreach (string failure in new[] { "signature", "issuer", "audience", "nonce", "expired" })
        {
            handler.InvalidIdentity = failure;
            await RejectAsync(() => connection.SignInAsync(active, token), failure == "nonce" ? "oidc_identity_invalid" : "oidc_token_invalid");
            checks.Add("id_token_rejects_" + failure);
        }
        handler.InvalidIdentity = "";
        handler.GrantPlan = false; var identityOnly = await connection.SignInAsync(active, token);
        Require(identityOnly.Status == "identity_only", "identity_only"); int requests = handler.Inferences;
        await RejectAsync(() => connection.ProbeAsync("synthetic-model", true, token), "plan_permission_missing"); Require(handler.Inferences == requests, "missing_permission_no_inference");
        checks.Add("identity_without_plan_never_infers");
        handler.GrantPlan = true; await connection.SignInAsync(active, token);
        using (var cancellation = new CancellationTokenSource())
        {
            handler.ResponseMode = "wait";
            Task<ChatGptProbeResult> waiting = connection.ProbeAsync("synthetic-model", true, cancellation.Token);
            await handler.Started.Task.WaitAsync(token); cancellation.Cancel();
            try { await waiting; throw new InvalidOperationException("Expected cancellation."); } catch (OperationCanceledException) { }
        }
        checks.Add("cancel_stops_inference"); handler.ResponseMode = "completed";
        await ExpireAsync(store, token); handler.RefreshInvalid = true;
        await RejectAsync(() => connection.ModelsAsync(token), "invalid_grant");
        Require((await connection.ReadAsync(token)).Status == "disconnected", "invalid_refresh_clears"); checks.Add("terminal_refresh_clears_tokens_retains_registration");
        handler.RefreshInvalid = false; await connection.SignInAsync(active, token); var disconnected = await connection.DisconnectAsync(token);
        Require(disconnected.Status == "disconnected" && disconnected.Active == active && handler.Revocations == 1, "revoke"); checks.Add("remote_revocation_and_local_logout");
        await connection.SignInAsync(active, token); handler.RevocationFails = true;
        var localOnly = await connection.DisconnectAsync(token); Require(localOnly.Error == "revocation_unconfirmed" && localOnly.Status == "disconnected", "revoke_unconfirmed"); checks.Add("unconfirmed_revocation_is_explicit");
        string diagnostics = JsonSerializer.Serialize(LauraDiagnostics.Snapshot());
        Require(!diagnostics.Contains("synthetic_access", StringComparison.Ordinal) && !diagnostics.Contains("synthetic_refresh", StringComparison.Ordinal)
            && !diagnostics.Contains("synthetic_owner", StringComparison.Ordinal) && !diagnostics.Contains("Reply with exactly", StringComparison.Ordinal), "diagnostics_payload"); checks.Add("credentials_and_prompts_absent_from_diagnostics");
        var evidence = new { Kind = "synthetic_protocol_only", RealAuthentication = false, Passed = checks.Count, Checks = checks, handler.Inferences, handler.Refreshes, handler.Revocations };
        await File.WriteAllTextAsync(Path.Combine(directory, "synthetic-result.json"), JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }), token);
        Console.WriteLine($"ChatGPT synthetic protocol checks passed: {checks.Count}. Real account authorization was not tested."); return 0;
    }
    /// <summary>Opens only the owner's official sign-in; a separate explicit probe flag is required to consume included plan usage.</summary>
    internal static async Task<int> RunLiveAsync(string directory, string[] args)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        using var http = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
        using var validator = new ChatGptIdTokenValidator(new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan });
        var store = new ChatGptCredentialStore(Path.Combine(directory, "Credentials"), new WindowsPrivateDataProtector());
        using var connection = new ChatGptConnection(http, store, new ChatGptBrowserAuthorization(), validator);
        try
        {
            var state = await connection.ReadAsync(timeout.Token);
            if (args.Contains("--chatgpt-login", StringComparer.Ordinal))
            {
                Console.WriteLine("Opening official ChatGPT authorization in the default browser. Complete account selection and consent; no paid API key is used.");
                state = await connection.SignInAsync(state.Active, timeout.Token);
            }
            Console.WriteLine("Connection status: " + state.Status);
            if (state.Status != "plan_authorized") { return 3; }
            var models = await connection.ModelsAsync(timeout.Token);
            Console.WriteLine(JsonSerializer.Serialize(new { CatalogOnly = true, Models = models }));
            int model = Array.IndexOf(args, "--chatgpt-probe-model");
            if (model >= 0)
            {
                if (model + 1 >= args.Length || !args.Contains("--included-plan-only-confirmed", StringComparer.Ordinal))
                { Console.WriteLine("Set additional-credit usage to zero in ChatGPT settings, then pass --included-plan-only-confirmed and a discovered --chatgpt-probe-model."); return 2; }
                if (!models.Any(item => item.Slug == args[model + 1])) { Console.WriteLine("Select a model from this account's actual catalog."); return 2; }
                var result = await connection.ProbeAsync(args[model + 1], true, timeout.Token);
                Console.WriteLine(JsonSerializer.Serialize(result));
            }
            return 0;
        }
        catch (ChatGptConnectionException error)
        { Console.WriteLine(JsonSerializer.Serialize(new { Error = error.Code, error.HttpStatus, error.RequestId, error.BodyShape })); return 1; }
        catch (OperationCanceledException) { Console.WriteLine("Official sign-in or probe canceled or timed out. No completion is claimed."); return 1; }
    }
    /// <summary>Exhausts only a synthetic access token's expiry to exercise real refresh persistence.</summary>
    private static async Task ExpireAsync(ChatGptCredentialStore store, CancellationToken token)
    { using (await store.AcquireAsync(token)) { var data = await store.ReadAsync(token); data.Accounts.Single().Expires = DateTimeOffset.UtcNow.AddSeconds(-30); await store.SaveAsync(data, token); } }
    /// <summary>Asserts a safe symbolic failure without printing credential-bearing objects.</summary>
    private static async Task RejectAsync<T>(Func<Task<T>> action, string expected)
    { try { await action(); throw new InvalidOperationException("Expected rejection: " + expected); } catch (ChatGptConnectionException error) { Require(error.Code == expected, "wrong_rejection_" + expected + "_" + error.Code); } }
    /// <summary>Reports only symbolic check names.</summary>
    private static void Require(bool success, string check) { if (!success) { throw new InvalidOperationException("Synthetic ChatGPT check failed: " + check); } }

    /// <summary>Emulates only the browser callback using values from the actual generated authorization transaction.</summary>
    private sealed class SyntheticBrowser(SyntheticServer server) : IChatGptBrowserAuthorization
    {
        /// <inheritdoc />
        public Task<Uri> ReceiveAsync(Func<Uri, Uri> authorize, CancellationToken token)
        {
            var callback = new Uri("http://127.0.0.1:54321/auth/callback"); var url = authorize(callback);
            var query = url.Query.TrimStart('?').Split('&').Select(pair => pair.Split('=', 2)).ToDictionary(pair => pair[0], pair => Uri.UnescapeDataString(pair[1]), StringComparer.Ordinal);
            server.Nonce = query["nonce"];
            Require(query["resource"] == ChatGptOAuthAttempt.Resource && query["code_challenge_method"] == "S256", "browser_contract");
            return Task.FromResult(new Uri(callback + "?code=synthetic_code&client_id=oaiapp_synthetic&state=" + query["state"]));
        }
    }
    /// <summary>In-process official-shape HTTP fixture; no requests leave the process.</summary>
    private sealed class SyntheticServer : HttpMessageHandler
    {
        private readonly RSA _rsa = RSA.Create(2048), _other = RSA.Create(2048);
        internal string Nonce = "", Subject = "synthetic_owner", InvalidIdentity = "", ResponseMode = "completed";
        internal bool GrantPlan = true, RefreshInvalid, RevocationFails;
        internal int Inferences, Refreshes, Revocations;
        internal TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        /// <inheritdoc />
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            string url = request.RequestUri!.AbsoluteUri;
            if (url.EndsWith("openid-configuration", StringComparison.Ordinal))
            {
                return Json(new { issuer = "https://auth.openai.com", authorization_endpoint = "https://auth.openai.com/api/accounts/authorize",
                    token_endpoint = "https://auth.openai.com/api/accounts/oauth/token", jwks_uri = "https://auth.openai.com/.well-known/jwks.json", revocation_endpoint = "https://auth.openai.com/api/accounts/oauth/revoke" });
            }
            if (url.EndsWith("jwks.json", StringComparison.Ordinal))
            {
                var key = JsonWebKeyConverter.ConvertFromRSASecurityKey(new RsaSecurityKey(_rsa.ExportParameters(false)) { KeyId = "synthetic" });
                return Json(new { keys = new[] { new { kty = "RSA", kid = "synthetic", use = "sig", alg = "RS256", n = key.N, e = key.E } } });
            }
            if (url.EndsWith("/oauth/token", StringComparison.Ordinal))
            {
                string form = await request.Content!.ReadAsStringAsync(token); bool refresh = form.Contains("grant_type=refresh_token", StringComparison.Ordinal);
                Require(form.Contains("client_id=oaiapp_synthetic", StringComparison.Ordinal) && !form.Contains("client_secret", StringComparison.Ordinal), "token_client");
                if (refresh)
                {
                    Refreshes++; Require(!form.Contains("scope=", StringComparison.Ordinal), "refresh_scope");
                    if (RefreshInvalid) { return Json(new { error = "invalid_grant" }, HttpStatusCode.BadRequest); }
                }
                string idToken = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
                {
                    Issuer = InvalidIdentity == "issuer" ? "https://invalid.example" : "https://auth.openai.com",
                    Audience = InvalidIdentity == "audience" ? "oaiapp_other" : "oaiapp_synthetic",
                    Claims = new Dictionary<string, object> { ["sub"] = Subject, ["email"] = "synthetic@example.invalid", ["nonce"] = InvalidIdentity == "nonce" ? "wrong" : Nonce },
                    IssuedAt = DateTime.UtcNow.AddMinutes(-10), NotBefore = DateTime.UtcNow.AddMinutes(-10), Expires = InvalidIdentity == "expired" ? DateTime.UtcNow.AddMinutes(-1) : DateTime.UtcNow.AddHours(1),
                    SigningCredentials = new SigningCredentials(new RsaSecurityKey(InvalidIdentity == "signature" ? _other : _rsa) { KeyId = "synthetic" }, SecurityAlgorithms.RsaSha256)
                });
                return Json(new { access_token = "synthetic_access", refresh_token = "synthetic_refresh_" + Refreshes, id_token = idToken, token_type = "Bearer", expires_in = 3600,
                    scope = GrantPlan ? "openid profile email offline_access resource.invoke chatgpt.tokens.use.direct" : "openid email" });
            }
            if (url.EndsWith("/oauth/revoke", StringComparison.Ordinal)) { Revocations++; return new(RevocationFails ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK); }
            Require(request.Headers.Authorization?.Parameter == "synthetic_access", "bearer_only");
            if (url == "https://api.openai.com/v1/models")
            { return Json(new { models = new[] { new { slug = "synthetic-model", display_name = "Synthetic model", visibility = "list" }, new { slug = "hidden", display_name = "Hidden", visibility = "hidden" } } }); }
            Require(url == "https://api.openai.com/v1/responses", "public_endpoint"); Inferences++;
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token)); var root = body.RootElement;
            Require(!root.GetProperty("store").GetBoolean() && root.GetProperty("stream").GetBoolean() && root.GetProperty("input").ValueKind == JsonValueKind.Array
                && root.EnumerateObject().Count() == 4, "inference_contract");
            if (ResponseMode == "limit") { return Json(new { error = new { code = "subscription_sharing_usage_limit_exceeded", message = "synthetic_access must never appear in diagnostics" } }, HttpStatusCode.TooManyRequests); }
            if (ResponseMode == "detail") { var error = Json(new { detail = "synthetic_access private diagnostic" }, HttpStatusCode.Forbidden); error.Headers.Add("x-request-id", "req_synthetic"); return error; }
            if (ResponseMode == "wait") { Started.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            string terminal = ResponseMode == "partial" ? "" : ResponseMode is "failed" or "incomplete"
                ? "data: {\"type\":\"response." + ResponseMode + "\"}\n\n"
                : "data: {\"type\":\"response.completed\",\"response\":{\"status\":\"completed\",\"usage\":{\"input_tokens\":9,\"output_tokens\":4}}}\n\n";
            return new(HttpStatusCode.OK) { Content = new StringContent("data: {\"type\":\"response.output_text.delta\",\"delta\":\"Laura connection verified.\"}\n\n" + terminal, Encoding.UTF8, "text/event-stream") };
        }
        /// <summary>Encodes official-shape synthetic JSON without touching the network.</summary>
        private static HttpResponseMessage Json(object value, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
        /// <inheritdoc />
        protected override void Dispose(bool disposing) { if (disposing) { _rsa.Dispose(); _other.Dispose(); } base.Dispose(disposing); }
    }
}
