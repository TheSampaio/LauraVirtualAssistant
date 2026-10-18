using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net;
using System.ComponentModel;
using System.Security.Cryptography;
using System.Text.Json;
using Laura.Core.Abstractions;
using Laura.Core.ChatGpt;
using Laura.Core.Diagnostics;

namespace Laura.Platform.Windows.Conversation;

/// <summary>Native official ChatGPT plan connection, isolated from conversation tools and paid API credentials.</summary>
public sealed class ChatGptConnection(HttpClient http, ChatGptCredentialStore store, IChatGptBrowserAuthorization browser,
    ChatGptIdTokenValidator validator, bool includeIdentityHint = true) : IChatGptConnection, IChatGptInference, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _sessionGate = new();
    private CancellationTokenSource _sessionLifetime = new();
    private int _disposed;
    private string _lastError = "";
    private int? _httpStatus;
    private int _available;
    private string _registration = "";
    /// <inheritdoc />
    public bool IsAvailable => Volatile.Read(ref _available) == 1;
    /// <inheritdoc />
    public Guid? ActiveRegistration => Guid.TryParse(Volatile.Read(ref _registration), out var id) ? id : null;
    private static readonly string[] ResponseFields = ["error", "detail", "type", "status", "output", "response", "data"];
    private static readonly string[] InferenceIncludes = ["reasoning.encrypted_content"];
    private static readonly HashSet<string> KnownErrors = new(StringComparer.Ordinal)
    {
        "invalid_grant", "invalid_client", "invalid_refresh_token", "token_expired", "refresh_token_expired", "refresh_token_invalidated", "refresh_token_reused",
        "subscription_sharing_user_not_eligible", "subscription_sharing_usage_limit_exceeded", "subscription_sharing_usage_unavailable",
        "subscription_sharing_unsupported_capability", "subscription_sharing_route_not_supported", "subscription_sharing_invalid_user",
        "chatpass_v2_scope_not_authorized", "chatpass_v2_invalid_authorization_context", "subscription_sharing_user_unavailable"
    };
    /// <inheritdoc />
    public Task<ChatGptConnectionState> ReadAsync(CancellationToken cancellationToken = default) => RunAsync((data, _) => Task.FromResult(Snapshot(data)), cancellationToken);
    /// <inheritdoc />
    public Task<ChatGptConnectionState> SelectAsync(Guid registration, CancellationToken cancellationToken = default)
    {
        CancelInference();
        return RunAsync(async (data, token) =>
        {
        if (!data.Accounts.Any(account => account.Id == registration)) { throw new ChatGptConnectionException("account_unavailable"); }
        data.Active = registration; await store.SaveAsync(data, token).ConfigureAwait(false); return Snapshot(data);
        }, cancellationToken);
    }
    /// <inheritdoc />
    public Task<ChatGptConnectionState> SignInAsync(Guid? registration, CancellationToken cancellationToken = default) => RunAsync(async (data, token) =>
    {
        ChatGptRegistration? selected = registration is null ? null : data.Accounts.SingleOrDefault(account => account.Id == registration)
            ?? throw new ChatGptConnectionException("account_unavailable");
        if (selected is null && data.Accounts.Count >= 16) { throw new ChatGptConnectionException("account_limit"); }
        await validator.RevocationEndpointAsync(token).ConfigureAwait(false);
        ChatGptOAuthAttempt? attempt = null;
        Uri callback = await browser.ReceiveAsync(uri =>
        {
            string? client = selected?.Client ?? (data.PendingClient.Length > 0 ? data.PendingClient : null);
            attempt = new ChatGptOAuthAttempt(uri, client);
            return attempt.Authorize(data.Host, selected?.Email, includeIdentityHint ? selected?.IdToken : null, selected is { PlanAuthorized: false });
        }, token).ConfigureAwait(false);
        if (attempt is null) { throw new ChatGptConnectionException("oauth_attempt_missing"); }
        var (code, issuedClient) = attempt.Consume(callback);
        // Keep an issued registration across an invalid_grant without treating its unverified identity as an active account.
        if (selected is null) { data.PendingClient = issuedClient; await store.SaveAsync(data, CancellationToken.None).ConfigureAwait(false); }
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://auth.openai.com/api/accounts/oauth/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code", ["client_id"] = issuedClient, ["code"] = code,
                ["code_verifier"] = attempt.Verifier, ["redirect_uri"] = attempt.Callback.AbsoluteUri, ["resource"] = ChatGptOAuthAttempt.Resource
            })
        };
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        using var document = await JsonAsync(response, token).ConfigureAwait(false);
        var payload = document.RootElement;
        string idToken = Required(payload, "id_token");
        var identity = await validator.ValidateAsync(idToken, issuedClient, attempt.Nonce, token).ConfigureAwait(false);
        if (selected is not null && identity.Subject != selected.Subject) { throw new ChatGptConnectionException("account_identity_mismatch"); }
        var account = new ChatGptRegistration { Id = selected?.Id ?? Guid.NewGuid(), Client = issuedClient, Subject = identity.Subject, Email = identity.Email, IdToken = idToken };
        UpdateTokens(account, payload, refresh: false);
        int index = selected is null ? -1 : data.Accounts.IndexOf(selected);
        if (index < 0) { data.Accounts.Add(account); } else { data.Accounts[index] = account; }
        data.Active = account.Id; if (selected is null) { data.PendingClient = ""; }
        await store.SaveAsync(data, CancellationToken.None).ConfigureAwait(false);
        _lastError = ""; _httpStatus = null; LauraDiagnostics.Record("chatgpt.auth", outcome: account.PlanAuthorized ? "plan_authorized" : "identity_only");
        return Snapshot(data);
    }, cancellationToken, TimeSpan.FromMinutes(5));
    /// <inheritdoc />
    public Task<IReadOnlyList<ChatGptModel>> ModelsAsync(CancellationToken cancellationToken = default) => RunAsync<IReadOnlyList<ChatGptModel>>(async (data, token) =>
    {
        ChatGptRegistration account = await ReadyAsync(data, token).ConfigureAwait(false);
        using var request = Authorized(HttpMethod.Get, "https://api.openai.com/v1/models", account);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        using var document = await JsonAsync(response, token).ConfigureAwait(false);
        if (!document.RootElement.TryGetProperty("models", out var models) || models.ValueKind != JsonValueKind.Array || models.GetArrayLength() > 1000)
        { throw new ChatGptConnectionException("model_catalog_invalid"); }
        var results = new List<ChatGptModel>();
        foreach (var model in models.EnumerateArray())
        {
            if (model.TryGetProperty("visibility", out var visible) && visible.GetString() == "list")
            { results.Add(new(Required(model, "slug"), Required(model, "display_name"))); }
        }
        _lastError = ""; _httpStatus = null; return results;
    }, cancellationToken);
    /// <inheritdoc />
    public Task<ChatGptProbeResult> ProbeAsync(string model, bool includedPlanOnlyConfirmed, CancellationToken cancellationToken = default) => RunInferenceAsync(async (data, token) =>
    {
        if (!includedPlanOnlyConfirmed) { throw new ChatGptConnectionException("included_plan_confirmation_required"); }
        if (string.IsNullOrWhiteSpace(model) || model.Length > 256) { throw new ChatGptConnectionException("model_required"); }
        ChatGptRegistration account = await ReadyAsync(data, token).ConfigureAwait(false);
        using var request = Authorized(HttpMethod.Post, "https://api.openai.com/v1/responses", account);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Content = JsonContent.Create(new { model, input = new[] { new { role = "user", content = "Reply with exactly: Laura connection verified." } }, store = false, stream = true });
        LauraDiagnostics.Record("chatgpt.inference.start", outcome: "synthetic_probe");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) { using var ignored = await JsonAsync(response, token).ConfigureAwait(false); }
        // Some intermediaries omit Content-Type; SSE framing and its verified terminal event remain mandatory.
        if (response.Content.Headers.ContentType?.MediaType is { } mediaType && mediaType != "text/event-stream")
        {
            string shape = response.Content.Headers.ContentType?.MediaType switch { "application/json" => "json", "text/plain" => "plain_text", null => "missing_content_type", _ => "other_content_type" };
            if (shape == "json")
            {
                using var unexpected = await JsonAsync(response, token).ConfigureAwait(false);
                var root = unexpected.RootElement;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    shape += "_" + string.Join("_", ResponseFields.Where(field => root.TryGetProperty(field, out _)));
                }
            }
            throw new ChatGptConnectionException("response_stream_required", (int)response.StatusCode, bodyShape: shape);
        }
        await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        ChatGptProbeResult result = await ChatGptResponseParser.ReadProbeAsync(stream, model, token).ConfigureAwait(false);
        account.VerifiedModel = model; account.IncludedPlanOnlyConfirmed = true;
        await store.SaveAsync(data, CancellationToken.None).ConfigureAwait(false); Snapshot(data);
        _lastError = ""; _httpStatus = null;
        LauraDiagnostics.Record("chatgpt.inference.end", amount: result.OutputTokens ?? 0, outcome: "completed"); return result;
    }, TimeSpan.FromMinutes(2), cancellationToken);
    /// <inheritdoc />
    public Task<ChatGptInferenceResult> CompleteAsync(ChatGptInferenceRequest inference, Action<string>? textDelta, CancellationToken cancellationToken) => RunInferenceAsync(async (data, token) =>
    {
        if (!inference.IncludedPlanOnlyConfirmed) { throw new ChatGptConnectionException("included_plan_confirmation_required"); }
        if (string.IsNullOrWhiteSpace(inference.Model) || inference.Model.Length > 256 || inference.Input.Count is < 1 or > 256
            || inference.Instructions.Length > 32000 || inference.Input.Sum(item => item.GetRawText().Length) > 512 * 1024 || inference.Tools.Count > 1)
        { throw new ChatGptConnectionException("inference_input_limit"); }
        ChatGptRegistration account = await ReadyAsync(data, token).ConfigureAwait(false);
        if (!account.IncludedPlanOnlyConfirmed) { throw new ChatGptConnectionException("included_plan_confirmation_required"); }
        using var request = Authorized(HttpMethod.Post, "https://api.openai.com/v1/responses", account);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Content = JsonContent.Create(new { model = inference.Model, instructions = inference.Instructions, input = inference.Input, tools = inference.Tools,
            include = InferenceIncludes, store = false, stream = true });
        LauraDiagnostics.Record("chatgpt.inference.start", outcome: "native_round");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) { using var ignored = await JsonAsync(response, token).ConfigureAwait(false); }
        if (response.Content.Headers.ContentType?.MediaType is { } type && type != "text/event-stream")
        { throw new ChatGptConnectionException("response_stream_required", (int)response.StatusCode); }
        await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        var result = await ChatGptResponseParser.ReadAsync(stream, textDelta, token).ConfigureAwait(false);
        account.VerifiedModel = inference.Model; await store.SaveAsync(data, CancellationToken.None).ConfigureAwait(false);
        LauraDiagnostics.Record("chatgpt.inference.end", amount: result.OutputTokens ?? 0, outcome: "completed"); return result;
    }, TimeSpan.FromMinutes(2), cancellationToken);
    /// <inheritdoc />
    public Task<ChatGptConnectionState> DisconnectAsync(CancellationToken cancellationToken = default)
    {
        CancelInference();
        return RunAsync(async (data, token) =>
        {
        ChatGptRegistration? account = data.Accounts.SingleOrDefault(item => item.Id == data.Active);
        if (account is null) { return Snapshot(data); }
        bool revoked = account.RefreshToken.Length == 0 && account.AccessToken.Length == 0;
        try
        {
            if (account.RefreshToken.Length > 0)
            {
                Uri endpoint = await validator.RevocationEndpointAsync(token).ConfigureAwait(false);
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    try
                    {
                        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
                        { Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = account.RefreshToken, ["token_type_hint"] = "refresh_token", ["client_id"] = account.Client }) };
                        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                        revoked = response.StatusCode == HttpStatusCode.OK;
                        if (revoked || (int)response.StatusCode < 500) { break; }
                    }
                    catch (HttpRequestException) when (attempt < 2) { }
                    if (attempt < 2) { await Task.Delay(TimeSpan.FromSeconds(attempt + 1), token).ConfigureAwait(false); }
                }
            }
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or InvalidDataException) { }
        finally { account.ClearTokens(); await store.SaveAsync(data, CancellationToken.None).ConfigureAwait(false); }
        _lastError = revoked ? "" : "revocation_unconfirmed"; _httpStatus = null;
        LauraDiagnostics.Record("chatgpt.logout", outcome: revoked ? "revoked" : "local_only"); return Snapshot(data);
        }, cancellationToken);
    }

    /// <summary>Cancels the old account's in-flight inference before account switching, logout or shutdown.</summary>
    internal void CancelInference()
    {
        lock (_sessionGate)
        {
            var previous = _sessionLifetime; _sessionLifetime = new(); previous.Cancel(); previous.Dispose();
        }
    }
    /// <summary>Links account lifetime to a single network inference without placing credentials in cancellation state.</summary>
    private async Task<T> RunInferenceAsync<T>(Func<ChatGptCredentials, CancellationToken, Task<T>> action, TimeSpan timeout, CancellationToken token)
    {
        CancellationTokenSource linked;
        lock (_sessionGate) { linked = CancellationTokenSource.CreateLinkedTokenSource(token, _sessionLifetime.Token); }
        using (linked) { return await RunAsync(action, linked.Token, timeout).ConfigureAwait(false); }
    }
    /// <summary>Refreshes near expiry under the store's cross-process lease, preserving rotating credentials atomically.</summary>
    private async Task<ChatGptRegistration> ReadyAsync(ChatGptCredentials data, CancellationToken token)
    {
        Snapshot(data);
        ChatGptRegistration account = data.Accounts.SingleOrDefault(item => item.Id == data.Active) ?? throw new ChatGptConnectionException("account_unavailable");
        if (account.AccessToken.Length == 0) { throw new ChatGptConnectionException("sign_in_required"); }
        if (!account.PlanAuthorized) { throw new ChatGptConnectionException("plan_permission_missing"); }
        if (account.Expires > DateTimeOffset.UtcNow.AddSeconds(90) || account.EarliestRefresh > DateTimeOffset.UtcNow && account.Expires > DateTimeOffset.UtcNow) { return account; }
        if (account.RefreshToken.Length == 0) { throw new ChatGptConnectionException("sign_in_required"); }
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://auth.openai.com/api/accounts/oauth/token")
        { Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "refresh_token", ["client_id"] = account.Client, ["refresh_token"] = account.RefreshToken, ["resource"] = ChatGptOAuthAttempt.Resource }) };
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        try
        {
            using var document = await JsonAsync(response, token).ConfigureAwait(false);
            // Persist a rotating replacement even if the caller cancels after the server has consumed the old token.
            UpdateTokens(account, document.RootElement, refresh: true);
            string replacementAccess = account.AccessToken;
            // Quarantine inference until a supplied refreshed identity has been verified; retain its rotating renewal token for recovery.
            account.AccessToken = "";
            Snapshot(data);
            await store.SaveAsync(data, CancellationToken.None).ConfigureAwait(false);
            if (document.RootElement.TryGetProperty("id_token", out var id) && id.ValueKind == JsonValueKind.String)
            {
                var identity = await validator.ValidateAsync(id.GetString()!, account.Client, null, token).ConfigureAwait(false);
                if (identity.Subject != account.Subject)
                { account.ClearTokens(); await store.SaveAsync(data, CancellationToken.None).ConfigureAwait(false); throw new ChatGptConnectionException("account_identity_mismatch"); }
                account.IdToken = id.GetString()!;
            }
            account.AccessToken = replacementAccess; await store.SaveAsync(data, CancellationToken.None).ConfigureAwait(false); Snapshot(data);
        }
        catch (ChatGptConnectionException error) when (error.Code is "invalid_grant" or "invalid_refresh_token" or "token_expired" or "refresh_token_expired" or "refresh_token_invalidated" or "refresh_token_reused")
        { account.ClearTokens(); await store.SaveAsync(data, CancellationToken.None).ConfigureAwait(false); Snapshot(data); throw; }
        if (!account.PlanAuthorized) { throw new ChatGptConnectionException("plan_permission_missing"); }
        LauraDiagnostics.Record("chatgpt.refresh", outcome: "renewed"); return account;
    }
    /// <summary>Maps credentials only from bounded token fields; omitted refresh scopes preserve the original grant.</summary>
    private static void UpdateTokens(ChatGptRegistration account, JsonElement payload, bool refresh)
    {
        if (!payload.TryGetProperty("token_type", out var type) || !string.Equals(type.GetString(), "Bearer", StringComparison.OrdinalIgnoreCase)
            || !payload.TryGetProperty("expires_in", out var expiry) || expiry.ValueKind != JsonValueKind.Number || !expiry.TryGetInt32(out int seconds) || seconds is < 1 or > 86400)
        { throw new ChatGptConnectionException("token_response_invalid"); }
        string access = Required(payload, "access_token"); string renewal = refresh ? Required(payload, "refresh_token") : Optional(payload, "refresh_token");
        string[] scopes = payload.TryGetProperty("scope", out var scope) && scope.ValueKind == JsonValueKind.String
            ? (scope.GetString() ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries) : refresh ? account.Scopes : [];
        if (scopes.Length > 32) { throw new ChatGptConnectionException("token_response_invalid"); }
        account.AccessToken = access; account.RefreshToken = renewal; account.Scopes = scopes; account.Expires = DateTimeOffset.UtcNow.AddSeconds(seconds);
        account.EarliestRefresh = payload.TryGetProperty("earliest_refresh_at", out var earliest) && earliest.ValueKind == JsonValueKind.Number && earliest.TryGetInt64(out long epoch) && epoch is > 0 and < 253402300800
            ? DateTimeOffset.FromUnixTimeSeconds(epoch) : null;
    }
    /// <summary>Creates one request with a registration-scoped bearer header; it never sets process environment or shared client defaults.</summary>
    private static HttpRequestMessage Authorized(HttpMethod method, string url, ChatGptRegistration account)
    { var request = new HttpRequestMessage(method, url); request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", account.AccessToken); return request; }
    /// <summary>Reads bounded JSON and preserves safe error structure/status without retaining untrusted remote messages.</summary>
    private static async Task<JsonDocument> JsonAsync(HttpResponseMessage response, CancellationToken token)
    {
        JsonDocument? document = null;
        try
        {
            await response.Content.LoadIntoBufferAsync(2 * 1024 * 1024, token).ConfigureAwait(false);
            document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token).ConfigureAwait(false), new JsonDocumentOptions { MaxDepth = 32 });
        }
        catch (JsonException) when (!response.IsSuccessStatusCode) { }
        if (response.IsSuccessStatusCode) { return document ?? throw new ChatGptConnectionException("response_json_invalid"); }
        string code = "http_error", shape = "non_json";
        using (document)
        {
            if (document?.RootElement.ValueKind == JsonValueKind.Object)
            {
                var root = document.RootElement; shape = root.TryGetProperty("detail", out _) ? "detail" : "object";
                if (root.TryGetProperty("error", out var error))
                {
                    shape = error.ValueKind == JsonValueKind.Object ? "error_object" : "error_string";
                    string? candidate = error.ValueKind == JsonValueKind.Object && error.TryGetProperty("code", out var field) && field.ValueKind == JsonValueKind.String ? field.GetString()
                        : error.ValueKind == JsonValueKind.String ? error.GetString() : null;
                    if (candidate is not null && KnownErrors.Contains(candidate)) { code = candidate; }
                }
            }
        }
        string requestId = response.Headers.TryGetValues("x-request-id", out var values) || response.Headers.TryGetValues("openai-request-id", out values) ? values.FirstOrDefault() ?? "" : "";
        if (requestId.Length > 128 || requestId.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('_' or '-'))) { requestId = ""; }
        throw new ChatGptConnectionException(code, (int)response.StatusCode, requestId, shape);
    }
    /// <summary>Accepts a required bounded opaque credential field without logging it.</summary>
    private static string Required(JsonElement payload, string field) => Optional(payload, field) is { Length: > 0 } value ? value : throw new ChatGptConnectionException("response_field_missing");
    /// <summary>Reads only bounded strings from structured JSON.</summary>
    private static string Optional(JsonElement payload, string field) => payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty(field, out var value)
        && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: <= 65536 } text ? text : "";
    /// <summary>Builds native display metadata without token, subject or client-ID disclosure.</summary>
    private ChatGptConnectionState Snapshot(ChatGptCredentials data)
    {
        var active = data.Accounts.SingleOrDefault(account => account.Id == data.Active);
        Volatile.Write(ref _registration, active?.Id.ToString("N") ?? "");
        Volatile.Write(ref _available, active is { AccessToken.Length: > 0, PlanAuthorized: true, IncludedPlanOnlyConfirmed: true } ? 1 : 0);
        return new(data.Active, data.Accounts.Select(account => new ChatGptAccount(account.Id, account.Email, account.AccessToken.Length > 0, account.PlanAuthorized)).ToArray(),
            active?.AccessToken.Length > 0 ? active.PlanAuthorized ? "plan_authorized" : "identity_only" : "disconnected", _lastError, _httpStatus, active?.VerifiedModel ?? "", active?.IncludedPlanOnlyConfirmed == true);
    }
    /// <summary>Serializes connection operations with independent deadlines and a cross-process credential lease; all diagnostics are symbolic.</summary>
    private async Task<T> RunAsync<T>(Func<ChatGptCredentials, CancellationToken, Task<T>> action, CancellationToken cancellationToken, TimeSpan? timeout = null)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); deadline.CancelAfter(timeout ?? TimeSpan.FromSeconds(45));
        await _gate.WaitAsync(deadline.Token).ConfigureAwait(false);
        try
        {
            using var lease = await store.AcquireAsync(deadline.Token).ConfigureAwait(false);
            ChatGptCredentials data = await store.ReadAsync(deadline.Token).ConfigureAwait(false);
            return await action(data, deadline.Token).ConfigureAwait(false);
        }
        catch (ChatGptConnectionException error)
        { _lastError = error.Code; _httpStatus = error.HttpStatus; LauraDiagnostics.Record("chatgpt.failure", amount: error.HttpStatus ?? 0, outcome: error.Code); throw; }
        catch (Exception error) when (error is InvalidDataException or JsonException or CryptographicException or HttpRequestException or IOException or UnauthorizedAccessException or Win32Exception or OperationCanceledException)
        {
            _lastError = error switch { OperationCanceledException => cancellationToken.IsCancellationRequested ? "canceled" : "timeout", HttpRequestException => "network_unavailable", InvalidDataException => SafeCode(error.Message), JsonException => "response_json_invalid", _ => "connection_unavailable" };
            _httpStatus = null; LauraDiagnostics.Record("chatgpt.failure", outcome: _lastError);
            cancellationToken.ThrowIfCancellationRequested();
            throw new ChatGptConnectionException(_lastError);
        }
        finally { _gate.Release(); }
    }
    /// <summary>Maps only native validation codes to diagnostics; arbitrary parser messages never escape.</summary>
    private static string SafeCode(string value) => value is "oauth_attempt_expired" or "oauth_callback_invalid" or "oauth_callback_duplicate" or "oauth_state_invalid" or "oauth_access_denied"
        or "oauth_authorization_failed" or "oauth_client_invalid" or "oauth_code_invalid" or "oidc_discovery_unavailable" or "oidc_discovery_invalid" or "oidc_keys_unavailable"
        or "oidc_token_invalid" or "oidc_identity_invalid" or "response_stream_limit" or "response_failed" or "response_incomplete" or "error" or "response_terminal_invalid"
        or "response_terminal_missing" or "credential_store_invalid" or "credential_store_limit" ? value : "connection_unavailable";
    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) { return; }
        lock (_sessionGate) { _sessionLifetime.Cancel(); _sessionLifetime.Dispose(); }
        http.Dispose(); _gate.Dispose(); GC.SuppressFinalize(this);
    }
}
