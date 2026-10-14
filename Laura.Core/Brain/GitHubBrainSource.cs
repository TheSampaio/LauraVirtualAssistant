using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Laura.Core.Abstractions;

namespace Laura.Core.Brain;

/// <summary>Uses only the official GitHub HTTPS API and verified Git blobs; injected HTTP must disable automatic redirects.</summary>
public sealed class GitHubBrainSource(HttpClient http) : IRemoteBrainSource
{
    /// <summary>Builds a credential-free transport with manual redirects, no proxies/cookies and public IP pinning at connection time.</summary>
    public static SocketsHttpHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false, UseCookies = false, UseProxy = false, ConnectTimeout = TimeSpan.FromSeconds(10),
        ConnectCallback = async (context, token) =>
        {
            if (context.DnsEndPoint.Host != "api.github.com" || context.DnsEndPoint.Port != 443) { throw new IOException("Repository host is not authorized."); }
            IPAddress[] addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, token).ConfigureAwait(false);
            foreach (IPAddress address in addresses.Where(IsPublicAddress))
            {
                var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                try { await socket.ConnectAsync(new IPEndPoint(address, 443), token).ConfigureAwait(false); return new NetworkStream(socket, ownsSocket: true); }
                catch (SocketException) { socket.Dispose(); }
                catch { socket.Dispose(); throw; }
            }
            throw new IOException("No authorized public repository endpoint is reachable.");
        }
    };

    /// <summary>Rejects local, private, link-local, multicast, unspecified and IPv4-mapped private endpoints.</summary>
    public static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) { address = address.MapToIPv4(); }
        byte[] bytes = address.GetAddressBytes();
        if (IPAddress.IsLoopback(address)) { return false; }
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return bytes[0] is not (0 or 10 or 127) && bytes[0] < 224 && !(bytes[0] == 169 && bytes[1] == 254)
                && !(bytes[0] == 172 && bytes[1] is >= 16 and <= 31) && !(bytes[0] == 192 && bytes[1] == 168)
                && !(bytes[0] == 100 && bytes[1] is >= 64 and <= 127) && !(bytes[0] == 198 && bytes[1] is 18 or 19);
        }
        // Only ordinary global unicast IPv6; rejects ULA, link local, mapped/tunneled and multicast.
        return address.AddressFamily == AddressFamily.InterNetworkV6 && (bytes[0] & 0xe0) == 0x20;
    }

    /// <inheritdoc />
    public async Task<RemoteBrainRepository> InspectAsync(GitHubRepositoryAddress address, RemoteBrainBudget budget, CancellationToken cancellationToken)
    {
        string prefix = Prefix(address);
        using JsonDocument metadata = await JsonAsync(prefix, 131072, budget, cancellationToken).ConfigureAwait(false);
        JsonElement root = metadata.RootElement;
        if (root.TryGetProperty("private", out JsonElement privateFlag) && privateFlag.GetBoolean())
        { throw new IOException("Private repositories require explicitly configured authorized authentication; this adapter supports public sources only."); }
        string reference = address.Reference.Length > 0 ? address.Reference : root.GetProperty("default_branch").GetString()!;
        using JsonDocument commit = await JsonAsync(prefix + "/commits/" + Uri.EscapeDataString(reference), 131072, budget, cancellationToken).ConfigureAwait(false);
        string revision = commit.RootElement.GetProperty("sha").GetString()!;
        ValidateHash(revision);
        using JsonDocument tree = await JsonAsync(prefix + "/git/trees/" + revision + "?recursive=1", 1048576, budget, cancellationToken).ConfigureAwait(false);
        var files = new List<RemoteBrainFile>(); int visited = 0;
        bool truncated = tree.RootElement.GetProperty("truncated").GetBoolean();
        foreach (JsonElement entry in tree.RootElement.GetProperty("tree").EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++visited > 5000) { truncated = true; break; }
            if (entry.GetProperty("type").GetString() != "blob" || entry.GetProperty("mode").GetString() is not ("100644" or "100755")) { continue; }
            string path = entry.GetProperty("path").GetString()!;
            if (!SafePath(path)) { continue; }
            string hash = entry.GetProperty("sha").GetString()!; ValidateHash(hash);
            if (!entry.TryGetProperty("size", out JsonElement size) || !size.TryGetInt32(out int length) || length < 0) { continue; }
            files.Add(new(path, hash, length));
        }
        string license = root.TryGetProperty("license", out JsonElement licenseObject) && licenseObject.ValueKind == JsonValueKind.Object
            ? licenseObject.GetProperty("spdx_id").GetString() ?? "unknown" : "unknown";
        return new(address, revision, license, address.Owner + "/" + address.Repository, files, truncated);
    }

    /// <inheritdoc />
    public async Task<byte[]> ReadAsync(RemoteBrainRepository repository, RemoteBrainFile file, RemoteBrainBudget budget, CancellationToken cancellationToken)
    {
        ValidateHash(repository.Revision); ValidateHash(file.BlobHash);
        if (!SafePath(file.Path) || file.Size is <= 0 or > 65536 || !repository.Files.Contains(file)) { throw new IOException("Source blob is outside the frozen inventory or size limit."); }
        using JsonDocument blob = await JsonAsync(Prefix(repository.Address) + "/git/blobs/" + file.BlobHash, 100000, budget, cancellationToken).ConfigureAwait(false);
        if (blob.RootElement.GetProperty("encoding").GetString() != "base64") { throw new IOException("Unsupported Git blob encoding."); }
        byte[] bytes = Convert.FromBase64String(blob.RootElement.GetProperty("content").GetString()!);
        if (bytes.Length != file.Size || bytes.Length > 65536) { throw new IOException("Source byte length differs from the frozen inventory."); }
        byte[] header = Encoding.UTF8.GetBytes("blob " + bytes.Length.ToString(CultureInfo.InvariantCulture) + "\0");
        byte[] gitObject = new byte[header.Length + bytes.Length]; header.CopyTo(gitObject, 0); bytes.CopyTo(gitObject, header.Length);
        // SHA-1 here verifies the Git object identity, not a security signature; persisted evidence also uses SHA-256.
#pragma warning disable CA5350
        string actual = Convert.ToHexString(SHA1.HashData(gitObject));
#pragma warning restore CA5350
        if (!actual.Equals(file.BlobHash, StringComparison.OrdinalIgnoreCase)) { throw new IOException("Source Git blob hash mismatch."); }
        return bytes;
    }

    /// <summary>Checks paths as metadata; remote paths are never materialized on the filesystem.</summary>
    public static bool SafePath(string path) => path is { Length: > 0 and <= 240 } && !path.StartsWith('/') && !path.Contains('\\')
        && !path.Contains(':') && !path.Contains('%') && !path.Any(char.IsControl) && path.Split('/').Length <= 9
        && path.Split('/').All(part => part.Length > 0 && part is not ("." or ".."));

    /// <summary>Constructs fixed API paths from validated repository identity.</summary>
    private static string Prefix(GitHubRepositoryAddress address)
    {
        GitHubRepositoryAddress validated = GitHubRepositoryAddress.Parse(address.Url, address.Reference);
        return "https://api.github.com/repos/" + validated.Owner + "/" + validated.Repository;
    }

    /// <summary>Requires an immutable hexadecimal Git identity.</summary>
    private static void ValidateHash(string hash)
    { if (hash.Length != 40 || !hash.All(Uri.IsHexDigit)) { throw new IOException("Invalid immutable Git revision or blob identity."); } }

    /// <summary>Revalidates every redirect before dispatch and bounds streaming bytes, time, requests and JSON depth.</summary>
    private async Task<JsonDocument> JsonAsync(string url, int maximumBytes, RemoteBrainBudget budget, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(20));
        Uri destination = new(url);
        for (int redirect = 0; redirect <= 3; redirect++)
        {
            if (destination.Scheme != "https" || destination.Host != "api.github.com" || !destination.IsDefaultPort || destination.UserInfo.Length > 0
                || !destination.AbsolutePath.StartsWith("/repos/", StringComparison.Ordinal)) { throw new IOException("Repository redirect destination is prohibited."); }
            budget.Request();
            using var request = new HttpRequestMessage(HttpMethod.Get, destination);
            request.Headers.UserAgent.ParseAdd("Laura-Knowledge/1.0"); request.Headers.Accept.ParseAdd("application/vnd.github+json");
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
            using HttpResponseMessage response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if ((int)response.StatusCode is >= 300 and <= 399)
            { destination = response.Headers.Location is { } location ? new Uri(destination, location) : throw new IOException("Repository redirect has no destination."); continue; }
            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            { throw new IOException("GitHub access denied or rate limited. Retry only after the reported GitHub rate-limit window; no credentials were searched."); }
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Unauthorized)
            { throw new IOException("Repository or reference unavailable. Private repositories require explicit authorized authentication, which is not configured for this adapter."); }
            if (!response.IsSuccessStatusCode) { throw new IOException("GitHub repository request failed: HTTP " + (int)response.StatusCode + "."); }
            if (response.Content.Headers.ContentLength > maximumBytes) { throw new IOException("Repository response exceeds its byte limit."); }
            await using Stream input = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var output = new MemoryStream(); byte[] buffer = new byte[8192]; int count;
            while ((count = await input.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) > 0)
            { budget.Receive(count); if (output.Length + count > maximumBytes) { throw new IOException("Repository response exceeds its byte limit."); } output.Write(buffer, 0, count); }
            return JsonDocument.Parse(output.ToArray(), new JsonDocumentOptions { MaxDepth = 32 });
        }
        throw new IOException("Repository redirect budget exhausted.");
    }
}
