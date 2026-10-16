using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Laura.Core.Abstractions;

namespace Laura.Core.Models;

/// <summary>Bounded authenticated discovery with immutable per-credential caches and no credential-bearing URLs or logs.</summary>
public sealed class ProviderModelCatalog(HttpClient http, string cacheDirectory, TimeProvider? time = null) : IProviderModelCatalog, IDisposable
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly SemaphoreSlim _requests = new(1, 1);
    private static readonly TimeSpan Expiration = TimeSpan.FromHours(24);
    private sealed class ListingException(string category) : IOException(category)
    { internal string Category { get; } = category; }

    /// <inheritdoc />
    public async Task<ProviderModelCatalogue> ListAsync(string provider, string credential, bool refresh = false, CancellationToken cancellationToken = default)
    {
        if (provider is not ("gemini" or "openai")) { throw new ArgumentException("Unknown catalogue provider.", nameof(provider)); }
        if (string.IsNullOrWhiteSpace(credential)) { return new(provider, [], null, true, "key_required"); }
        if (credential.Any(char.IsControl)) { return new(provider, [], null, true, "invalid_credentials"); }
        string identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(credential)));
        string path = Path.Combine(Path.GetFullPath(cacheDirectory), provider + "-" + identity + ".json");
        await _requests.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ProviderModelCatalogue? cached = await ReadCacheAsync(provider, path, cancellationToken).ConfigureAwait(false);
            if (!refresh && cached?.FetchedAt is { } fetched && fetched <= _time.GetUtcNow() && _time.GetUtcNow() - fetched < Expiration) { return cached; }
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(30));
            try
            {
                var models = new Dictionary<string, ProviderModel>(StringComparer.Ordinal);
                var pages = new HashSet<string>(StringComparer.Ordinal); string next = "";
                do
                {
                    if (pages.Count >= 32 || !pages.Add(next)) { throw new ListingException("invalid_response"); }
                    Uri endpoint = provider == "gemini" ? new("https://generativelanguage.googleapis.com/v1beta/models?pageSize=1000" + (next.Length > 0 ? "&pageToken=" + Uri.EscapeDataString(next) : ""))
                        : new("https://api.openai.com/v1/models");
                    using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
                    if (provider == "gemini") { request.Headers.Add("x-goog-api-key", credential); }
                    else { request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential); }
                    using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
                    if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) { throw new ListingException("invalid_credentials"); }
                    if (response.StatusCode == HttpStatusCode.TooManyRequests) { throw new ListingException("rate_limit"); }
                    if (!response.IsSuccessStatusCode) { throw new ListingException("provider_unavailable"); }
                    await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
                    using var bytes = new MemoryStream(); byte[] buffer = new byte[8192]; int read;
                    while ((read = await stream.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) > 0)
                    { if (bytes.Length + read > 4 * 1024 * 1024) { throw new ListingException("invalid_response"); } await bytes.WriteAsync(buffer.AsMemory(0, read), timeout.Token).ConfigureAwait(false); }
                    using var document = JsonDocument.Parse(bytes.ToArray(), new JsonDocumentOptions { MaxDepth = 16 });
                    string collection = provider == "gemini" ? "models" : "data";
                    if (!document.RootElement.TryGetProperty(collection, out var items) || items.ValueKind != JsonValueKind.Array) { throw new ListingException("invalid_response"); }
                    foreach (var item in items.EnumerateArray())
                    {
                        string id = item.GetProperty(provider == "gemini" ? "name" : "id").GetString() ?? "";
                        if (provider == "gemini" && id.StartsWith("models/", StringComparison.Ordinal)) { id = id[7..]; }
                        if (!ProviderModelCompatibility.ValidIdentifier(id)) { continue; }
                        string name = item.TryGetProperty("displayName", out var label) && label.ValueKind == JsonValueKind.String ? label.GetString() ?? id : id;
                        if (name.Length > 240 || name.Any(char.IsControl)) { name = id; }
                        var methods = item.TryGetProperty("supportedGenerationMethods", out var operations) && operations.ValueKind == JsonValueKind.Array
                            ? operations.EnumerateArray().Where(method => method.ValueKind == JsonValueKind.String).Select(method => method.GetString()!).ToHashSet(StringComparer.Ordinal) : [];
                        ModelCapabilities capabilities = provider == "gemini" ? ProviderModelCompatibility.Gemini(id, methods) : ProviderModelCompatibility.OpenAi(id);
                        int? context = item.TryGetProperty("inputTokenLimit", out var limit) && limit.TryGetInt32(out int tokens) && tokens > 0 ? tokens : null;
                        bool deprecated = item.TryGetProperty("deprecated", out var retirement) && retirement.ValueKind == JsonValueKind.True;
                        models[id] = new(id, name, capabilities, context, deprecated);
                        if (models.Count > 5000) { throw new ListingException("invalid_response"); }
                    }
                    next = provider == "gemini" && document.RootElement.TryGetProperty("nextPageToken", out var page) && page.ValueKind == JsonValueKind.String ? page.GetString() ?? "" : "";
                    if (next.Length > 2048) { throw new ListingException("invalid_response"); }
                } while (next.Length > 0);
                var result = new ProviderModelCatalogue(provider, models.Values.OrderBy(model => model.Name, StringComparer.OrdinalIgnoreCase).ToArray(), _time.GetUtcNow(), false, null);
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!); string temporary = path + ".tmp";
                    await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(result), timeout.Token).ConfigureAwait(false); File.Move(temporary, path, true);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return result with { Error = "cache_unavailable" }; }
                return result;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return Stale(cached, provider, "timeout"); }
            catch (ListingException error) { return Stale(cached, provider, error.Category); }
            catch (Exception error) when (error is HttpRequestException or IOException or JsonException or KeyNotFoundException or InvalidOperationException)
            { return Stale(cached, provider, error is HttpRequestException or IOException ? "network" : "invalid_response"); }
        }
        finally { _requests.Release(); }
    }

    /// <summary>Restores a bounded catalogue without exposing corrupt cache contents.</summary>
    private static async Task<ProviderModelCatalogue?> ReadCacheAsync(string provider, string path, CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > 4 * 1024 * 1024) { return null; }
            var cached = JsonSerializer.Deserialize<ProviderModelCatalogue>(await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false));
            return cached is { Stale: false, Error: null } && cached.Provider == provider && cached.Models is { Count: <= 5000 }
                && cached.Models.All(model => ProviderModelCompatibility.ValidIdentifier(model.Id) && model.Name is { Length: <= 240 } && !model.Name.Any(char.IsControl)) ? cached : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    /// <summary>Returns only the last complete valid catalogue, explicitly marked stale after refresh failure.</summary>
    private static ProviderModelCatalogue Stale(ProviderModelCatalogue? cached, string provider, string category) => cached is null
        ? new(provider, [], null, true, category) : cached with { Stale = true, Error = category };
    /// <inheritdoc />
    public void Dispose() { _requests.Dispose(); http.Dispose(); }
}
