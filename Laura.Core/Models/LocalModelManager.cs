using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Laura.Core.Abstractions;

namespace Laura.Core.Models;

/// <summary>Resumable, verified GGUF installation with atomic publication and one writer.</summary>
public sealed class LocalModelManager : ILocalModelManager, IDisposable
{
    private readonly string _directory;
    private readonly HttpClient _client;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Initializes isolated model storage and an injected, HTTPS-only download client.</summary>
    public LocalModelManager(string directory, HttpClient client, IReadOnlyList<ModelArtifact> catalog)
    {
        _directory = Path.GetFullPath(directory);
        _client = client;
        Catalog = catalog;
    }

    /// <inheritdoc />
    public IReadOnlyList<ModelArtifact> Catalog { get; }

    /// <inheritdoc />
    public string GetModelPath(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (id.Length > 100 || id.Any(static c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.'))
            || id.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException("Unsafe model identifier.", nameof(id));
        }
        string path = Path.Combine(_directory, id + ".gguf");
        if (Directory.Exists(_directory) && (File.GetAttributes(_directory) & FileAttributes.ReparsePoint) != 0
            || File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException("Linked model storage is not supported.");
        }
        return path;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetInstalledModels() => !Directory.Exists(_directory) ? [] :
        [.. Directory.EnumerateFiles(_directory, "*.gguf")
            .Where(static file => (File.GetAttributes(file) & FileAttributes.ReparsePoint) == 0)
            .Select(Path.GetFileNameWithoutExtension).OfType<string>().Order(StringComparer.Ordinal)];

    /// <inheritdoc />
    public async Task InstallAsync(ModelArtifact artifact, IProgress<ModelDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        if (!Catalog.Contains(artifact) || artifact.Source.Scheme != Uri.UriSchemeHttps
            || artifact.Source.Host != "huggingface.co" || artifact.Size < 4
            || artifact.Sha256.Length != 64 || artifact.Sha256.Any(static c => !Uri.IsHexDigit(c)))
        {
            throw new ArgumentException("Model is not a reviewed artifact.", nameof(artifact));
        }
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string target = GetModelPath(artifact.Id);
            Directory.CreateDirectory(_directory);
            string partial = target + ".partial";
            if (File.Exists(partial) && (File.GetAttributes(partial) & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException("Linked partial downloads are not supported.");
            }
            long offset = File.Exists(partial) ? new FileInfo(partial).Length : 0;
            if (offset > artifact.Size) { File.Delete(partial); offset = 0; }
            long free = new DriveInfo(Path.GetPathRoot(_directory)!).AvailableFreeSpace;
            if (free < artifact.Size - offset + 64 * 1024 * 1024)
            {
                throw new IOException("Insufficient disk space for the model download.");
            }
            if (offset < artifact.Size)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, artifact.Source);
                if (offset > 0) { request.Headers.Range = new RangeHeaderValue(offset, null); }
                using HttpResponseMessage response = await _client.SendAsync(request,
                    HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                if (response.RequestMessage?.RequestUri?.Scheme != Uri.UriSchemeHttps)
                {
                    throw new HttpRequestException("Insecure model redirect rejected.");
                }
                if (offset > 0 && response.StatusCode == HttpStatusCode.PartialContent)
                {
                    if (response.Content.Headers.ContentRange?.From != offset
                        || response.Content.Headers.ContentRange?.Length != artifact.Size)
                    {
                        throw new IOException("Invalid resumed download range.");
                    }
                }
                else { offset = 0; }
                await using Stream input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using var output = new FileStream(partial, offset == 0 ? FileMode.Create : FileMode.Append,
                    FileAccess.Write, FileShare.None, 81920, useAsync: true);
                byte[] buffer = new byte[81920];
                int read;
                while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    if (offset + read > artifact.Size) { throw new IOException("Model download exceeds expected size."); }
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    offset += read;
                    progress?.Report(new(offset, artifact.Size, false));
                }
            }
            progress?.Report(new(offset, artifact.Size, true));
            await using (var file = new FileStream(partial, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true))
            {
                byte[] digest = await SHA256.HashDataAsync(file, cancellationToken).ConfigureAwait(false);
                file.Position = 0;
                byte[] magic = new byte[4];
                int count = await file.ReadAsync(magic, cancellationToken).ConfigureAwait(false);
                if (file.Length != artifact.Size || !Convert.ToHexString(digest).Equals(artifact.Sha256, StringComparison.OrdinalIgnoreCase)
                    || count != 4 || !magic.AsSpan().SequenceEqual("GGUF"u8))
                {
                    file.Close();
                    File.Delete(partial);
                    throw new IOException("Model verification failed. Retry the download.");
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(partial, target, overwrite: true);
        }
        finally { _gate.Release(); }
    }

    /// <inheritdoc />
    public void Delete(string id)
    {
        if (!_gate.Wait(0)) { throw new InvalidOperationException("A model installation is active."); }
        try
        {
            string path = GetModelPath(id);
            File.Delete(path);
            string partial = path + ".partial";
            if (File.Exists(partial) && (File.GetAttributes(partial) & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException("Linked partial downloads are not supported.");
            }
            File.Delete(partial);
        }
        finally { _gate.Release(); }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _gate.Dispose();
        _client.Dispose();
    }
}
