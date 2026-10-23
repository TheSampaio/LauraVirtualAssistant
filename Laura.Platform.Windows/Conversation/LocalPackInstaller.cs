using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Laura.Core.Configuration;
using Laura.Core.Models;
using SharpCompress.Compressors.BZip2;

namespace Laura.Platform.Windows.Conversation;

/// <summary>Installs an explicitly requested local pack using resumable, hash-verified downloads and staging-only extraction.</summary>
public sealed class LocalPackInstaller(HttpClient http, string? applicationDirectory = null) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    /// <summary>Describes an exact Microsoft runtime binary from the application's verified redistribution inventory.</summary>
    private sealed record RuntimeComponent(string File, string Sha256);
    /// <summary>Publishes bounded numeric download progress without credentials or signed CDN URLs.</summary>
    public sealed record Progress(string Component, long Received, long Total);
    /// <summary>Returns verified installation settings without enabling inference or changing personal settings.</summary>
    public sealed record Installation(LocalModelOptions Model, LocalVoiceOptions Voice, string Directory);

    /// <summary>Installs the reviewed pack into a flat model directory with runtime and extraction caches outside it.</summary>
    public async Task<Installation> InstallAsync(string modelsDirectory, IProgress<Progress>? progress, CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            string root = Path.GetFullPath(modelsDirectory);
            ValidatePath(root); Directory.CreateDirectory(root);
            string support = Path.Combine(Path.GetDirectoryName(root)!, "Runtime"); ValidatePath(support); Directory.CreateDirectory(support);
            string downloads = Path.Combine(support, "Downloads"); ValidatePath(downloads); Directory.CreateDirectory(downloads);
            long required = RequiredFreeBytes(root, support, downloads);
            if (new DriveInfo(Path.GetPathRoot(root)!).AvailableFreeSpace < required)
            { throw new IOException("Insufficient free storage for the remaining verified model downloads and staging."); }
            string runtimeArchive = await DownloadAsync(LocalModelPackages.Runtime, downloads, progress, token).ConfigureAwait(false);
            string runtime = await ExtractAsync(runtimeArchive, Path.Combine(support, "llama-b11669-vulkan"), false, token).ConfigureAwait(false);
            string executable = Directory.GetFiles(runtime, "llama-server.exe", SearchOption.AllDirectories).Single();
            string executableDirectory = Path.GetDirectoryName(executable)!;
            await CopyBundledRuntimeAsync(executableDirectory, token).ConfigureAwait(false);
            var libraries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string dll in Directory.GetFiles(executableDirectory, "*.dll")) { libraries[Path.GetFileName(dll)] = await HashAsync(dll, token).ConfigureAwait(false); }
            await File.WriteAllTextAsync(Path.Combine(executableDirectory, "laura-runtime.json"), JsonSerializer.Serialize(libraries), token).ConfigureAwait(false);
            string cachedModel = await DownloadAsync(LocalModelPackages.Qwen, downloads, progress, token).ConfigureAwait(false);
            string model = Path.Combine(root, LocalModelPackages.Qwen.FileName);
            await CopyVerifiedAsync(cachedModel, model, LocalModelPackages.Qwen.Sha256, token).ConfigureAwait(false);
            string kokoroArchive = await DownloadAsync(LocalModelPackages.Kokoro, downloads, progress, token).ConfigureAwait(false);
            string kokoroRoot = await ExtractAsync(kokoroArchive, Path.Combine(support, "Kokoro-v1.0"), true, token).ConfigureAwait(false);
            string kokoro = Path.GetDirectoryName(Directory.GetFiles(kokoroRoot, "voices.bin", SearchOption.AllDirectories).Single())!;
            foreach (string name in new[] { "model.onnx", "voices.bin", "tokens.txt" })
            { string source = Path.Combine(kokoro, name); await CopyVerifiedAsync(source, Path.Combine(root, "kokoro-" + name), await HashAsync(source, token).ConfigureAwait(false), token).ConfigureAwait(false); }
            await File.WriteAllTextAsync(Path.Combine(root, "kokoro-voices.json"), JsonSerializer.Serialize(new Dictionary<string, int>
            { ["af_heart"] = 3, ["af_bella"] = 2, ["bf_alice"] = 20, ["bf_emma"] = 21, ["bf_isabella"] = 22, ["bf_lily"] = 23, ["pf_dora"] = 42 }), token).ConfigureAwait(false);
            await InstallPiperVoiceAsync(LocalModelPackages.BrazilianVoice, "Piper-Dii", "pt_BR-dii-high.onnx", "piper-pt_BR-dii", root, support, downloads, progress, token).ConfigureAwait(false);
            await InstallPiperVoiceAsync(LocalModelPackages.BritishVoice, "Piper-Cori", "en_GB-cori-medium.onnx", "piper-en_GB-cori", root, support, downloads, progress, token).ConfigureAwait(false);
            string whisper = root;
            foreach (var asset in LocalModelPackages.Whisper) { await DownloadAsync(asset, whisper, progress, token).ConfigureAwait(false); }
            foreach (var asset in LocalModelPackages.WhisperSmall) { await DownloadAsync(asset, whisper, progress, token).ConfigureAwait(false); }
            await File.WriteAllTextAsync(Path.Combine(root, "laura-installation.json"), JsonSerializer.Serialize(new
            { schema = 1, installed = DateTimeOffset.UtcNow, runtime = LocalModelPackages.Runtime, model = LocalModelPackages.Qwen, kokoro = LocalModelPackages.Kokoro,
                whisper = LocalModelPackages.Whisper, whisperSmall = LocalModelPackages.WhisperSmall,
                brazilianVoice = LocalModelPackages.BrazilianVoice, britishVoice = LocalModelPackages.BritishVoice }), token).ConfigureAwait(false);
            return new(new LocalModelOptions { Enabled = true, GpuLayers = -1, Executable = executable, ExecutableHash = await HashAsync(executable, token).ConfigureAwait(false), ModelPath = model, ModelHash = LocalModelPackages.Qwen.Sha256.ToUpperInvariant() },
                new LocalVoiceOptions { KokoroDirectory = root, KokoroDataDirectory = Path.Combine(kokoro, "espeak-ng-data"), WhisperDirectory = whisper, DetectLanguage = true }, root);
        }
        finally { _gate.Release(); }
    }

    /// <summary>Estimates remaining downloads, copies and extraction with a reserve instead of rejecting a complete installation.</summary>
    private static long RequiredFreeBytes(string models, string support, string downloads)
    {
        long bytes = 0;
        foreach (var asset in new[] { LocalModelPackages.Runtime, LocalModelPackages.Qwen, LocalModelPackages.Kokoro,
            LocalModelPackages.BrazilianVoice, LocalModelPackages.BritishVoice }.Concat(LocalModelPackages.Whisper).Concat(LocalModelPackages.WhisperSmall))
        { if (!File.Exists(Path.Combine(downloads, asset.FileName)) && !File.Exists(Path.Combine(models, asset.FileName))) { bytes += asset.Bytes; } }
        if (!File.Exists(Path.Combine(models, LocalModelPackages.Qwen.FileName))) { bytes += LocalModelPackages.Qwen.Bytes; }
        if (!Directory.Exists(Path.Combine(support, "llama-b11669-vulkan"))) { bytes += LocalModelPackages.Runtime.Bytes * 3; }
        if (!Directory.Exists(Path.Combine(support, "Kokoro-v1.0"))) { bytes += LocalModelPackages.Kokoro.Bytes * 2; }
        if (!File.Exists(Path.Combine(models, "kokoro-model.onnx"))) { bytes += LocalModelPackages.Kokoro.Bytes; }
        foreach (var (asset, folder, file) in new[] {
            (LocalModelPackages.BrazilianVoice, "Piper-Dii", "piper-pt_BR-dii.onnx"),
            (LocalModelPackages.BritishVoice, "Piper-Cori", "piper-en_GB-cori.onnx") })
        {
            if (!Directory.Exists(Path.Combine(support, folder))) { bytes += asset.Bytes * 2; }
            if (!File.Exists(Path.Combine(models, file))) { bytes += asset.Bytes; }
        }
        return bytes + (bytes == 0 ? 64L : 512L) * 1024 * 1024;
    }

    /// <summary>Installs one pinned Piper archive outside Models and copies only its model and tokens into the flat model directory.</summary>
    private async Task InstallPiperVoiceAsync(LocalModelAsset asset, string folder, string modelName, string prefix,
        string models, string support, string downloads, IProgress<Progress>? progress, CancellationToken token)
    {
        string archive = await DownloadAsync(asset, downloads, progress, token).ConfigureAwait(false);
        string extracted = await ExtractAsync(archive, Path.Combine(support, folder), true, token).ConfigureAwait(false);
        string voice = Path.GetDirectoryName(Directory.GetFiles(extracted, modelName, SearchOption.AllDirectories).Single())!;
        foreach (var (sourceName, targetName) in new[] { (modelName, prefix + ".onnx"), ("tokens.txt", prefix + "-tokens.txt") })
        {
            string source = Path.Combine(voice, sourceName);
            await CopyVerifiedAsync(source, Path.Combine(models, targetName), await HashAsync(source, token).ConfigureAwait(false), token).ConfigureAwait(false);
        }
    }

    /// <summary>Places the application's hash-pinned CRT next to the child executable so a portable installation needs no global VC prerequisite.</summary>
    private async Task CopyBundledRuntimeAsync(string destination, CancellationToken token)
    {
        string sourceRoot = Path.GetFullPath(applicationDirectory ?? AppContext.BaseDirectory);
        string inventory = Path.Combine(sourceRoot, "licenses", "vc-runtime-inventory.json"); ValidatePath(inventory);
        // SDK development outputs may use the installed toolchain; published distributions always carry this inventory.
        if (!File.Exists(inventory)) { return; }
        if (new FileInfo(inventory).Length > 65536) { throw new IOException("The bundled C++ runtime inventory is invalid."); }
        var components = JsonSerializer.Deserialize<RuntimeComponent[]>(await File.ReadAllTextAsync(inventory, token).ConfigureAwait(false));
        if (components is null || components.Length is < 1 or > 32) { throw new IOException("The bundled C++ runtime inventory is invalid."); }
        foreach (var component in components)
        {
            if (component.File.IndexOfAny(['/', '\\', ':']) >= 0 || !component.File.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                || component.Sha256.Length != 64 || !component.Sha256.All(char.IsAsciiHexDigit)) { throw new IOException("The bundled C++ runtime inventory is invalid."); }
            string source = Path.Combine(sourceRoot, component.File); ValidatePath(source);
            if (!File.Exists(source) || !(await HashAsync(source, token).ConfigureAwait(false)).Equals(component.Sha256, StringComparison.OrdinalIgnoreCase))
            { throw new IOException("A bundled C++ runtime binary failed integrity verification."); }
        }
        foreach (var component in components)
        { await CopyVerifiedAsync(Path.Combine(sourceRoot, component.File), Path.Combine(destination, component.File), component.Sha256, token).ConfigureAwait(false); }
        string notices = Path.Combine(destination, "RuntimeLicenses"); ValidatePath(notices); Directory.CreateDirectory(notices);
        string license = Path.Combine(sourceRoot, "licenses", "VisualCpp-Runtime-LICENSE.txt"); ValidatePath(license);
        await CopyVerifiedAsync(license, Path.Combine(notices, Path.GetFileName(license)), await HashAsync(license, token).ConfigureAwait(false), token).ConfigureAwait(false);
        await CopyVerifiedAsync(inventory, Path.Combine(notices, Path.GetFileName(inventory)), await HashAsync(inventory, token).ConfigureAwait(false), token).ConfigureAwait(false);
    }
    /// <summary>Commits a small verified runtime dependency by rename without overwriting an unknown installed file.</summary>
    private static async Task CopyVerifiedAsync(string source, string target, string expected, CancellationToken token)
    {
        ValidatePath(target);
        if (File.Exists(target))
        {
            if (!(await HashAsync(target, token).ConfigureAwait(false)).Equals(expected, StringComparison.OrdinalIgnoreCase)) { throw new IOException("An installed runtime dependency differs from the reviewed application copy."); }
            return;
        }
        string temporary = target + ".installing"; ValidatePath(temporary);
        if (!File.Exists(temporary))
        {
            await using var input = File.OpenRead(source);
            await using var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true);
            await input.CopyToAsync(output, token).ConfigureAwait(false); await output.FlushAsync(token).ConfigureAwait(false);
        }
        if (!(await HashAsync(temporary, token).ConfigureAwait(false)).Equals(expected, StringComparison.OrdinalIgnoreCase)) { throw new IOException("A staged runtime dependency failed integrity verification."); }
        File.Move(temporary, target);
    }

    /// <summary>Downloads only immutable HTTPS assets; cancellation retains a bounded partial file for resumption.</summary>
    private async Task<string> DownloadAsync(LocalModelAsset asset, string directory, IProgress<Progress>? progress, CancellationToken token)
    {
        string target = Path.Combine(directory, asset.FileName); ValidatePath(target);
        if (File.Exists(target))
        {
            if (new FileInfo(target).Length == asset.Bytes && (await HashAsync(target, token).ConfigureAwait(false)).Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase)) { return target; }
            throw new IOException("An installed asset failed integrity verification. Preserve it for inspection before replacing it.");
        }
        string partial = target + ".partial"; ValidatePath(partial);
        long offset = File.Exists(partial) ? new FileInfo(partial).Length : 0;
        if (offset > asset.Bytes) { throw new IOException("The partial asset exceeds the reviewed download size."); }
        if (offset == asset.Bytes)
        {
            if (!(await HashAsync(partial, token).ConfigureAwait(false)).Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase)) { throw new IOException("The complete partial asset failed integrity verification."); }
            File.Move(partial, target); progress?.Report(new(asset.Name, asset.Bytes, asset.Bytes)); return target;
        }
        Uri endpoint = new(asset.Url); HttpResponseMessage? response = null;
        try
        {
            for (int redirect = 0; redirect < 10; redirect++)
            {
                if (!AllowedHost(endpoint)) { throw new IOException("The model download redirected outside its reviewed distribution hosts."); }
                using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
                if (offset > 0) { request.Headers.Range = new RangeHeaderValue(offset, null); }
                response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } location)
                { endpoint = location.IsAbsoluteUri ? location : new Uri(endpoint, location); response.Dispose(); response = null; continue; }
                break;
            }
            if (response is null || !response.IsSuccessStatusCode) { throw new IOException("The optional model download is unavailable."); }
            bool append = response.StatusCode == HttpStatusCode.PartialContent && response.Content.Headers.ContentRange?.From == offset;
            if (!append) { offset = 0; }
            await using var output = new FileStream(partial, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 65536, true);
            await using var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            byte[] buffer = new byte[65536]; int count; long received = offset, reported = 0;
            while ((count = await input.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
            {
                received += count; if (received > asset.Bytes) { throw new IOException("The download exceeded its pinned size."); }
                await output.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
                if (Environment.TickCount64 - reported > 200) { progress?.Report(new(asset.Name, received, asset.Bytes)); reported = Environment.TickCount64; }
            }
            await output.FlushAsync(token).ConfigureAwait(false); output.Close();
            if (received != asset.Bytes || !(await HashAsync(partial, token).ConfigureAwait(false)).Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase))
            { throw new IOException("The downloaded model failed its pinned size or SHA-256 check."); }
            File.Move(partial, target); progress?.Report(new(asset.Name, asset.Bytes, asset.Bytes)); return target;
        }
        finally { response?.Dispose(); }
    }
    /// <summary>Extracts only regular files under a new staging root and commits by directory rename.</summary>
    private static async Task<string> ExtractAsync(string archive, string destination, bool bzip, CancellationToken token)
    {
        ValidatePath(destination);
        if (Directory.Exists(destination))
        {
            string marker = Path.Combine(destination, "laura-extracted.json"); ValidatePath(marker);
            if (!File.Exists(marker)) { throw new IOException("An unmanaged installation directory already exists."); }
            using var metadata = JsonDocument.Parse(await File.ReadAllTextAsync(marker, token).ConfigureAwait(false));
            string archiveHash = await HashAsync(archive, token).ConfigureAwait(false);
            if (!metadata.RootElement.TryGetProperty("archiveHash", out var recorded) || recorded.GetString() != archiveHash) { throw new IOException("The extraction provenance changed."); }
            if (metadata.RootElement.TryGetProperty("files", out var inventory))
            {
                foreach (var item in inventory.EnumerateObject())
                {
                    string installed = EntryPath(destination, item.Name); ValidatePath(installed);
                    if (!File.Exists(installed) || await HashAsync(installed, token).ConfigureAwait(false) != item.Value.GetString()) { throw new IOException("An extracted artifact failed integrity verification."); }
                }
                return destination;
            }
        }
        string stage = destination + ".staging-" + Guid.NewGuid().ToString("N"); Directory.CreateDirectory(stage); long total = 0;
        try
        {
        await using var file = File.OpenRead(archive);
        if (bzip)
        {
            await using var decoded = BZip2Stream.Create(file, SharpCompress.Compressors.CompressionMode.Decompress, false);
            using var tar = new TarReader(decoded);
            while (await tar.GetNextEntryAsync(cancellationToken: token).ConfigureAwait(false) is { } entry)
            {
                string path = EntryPath(stage, entry.Name);
                if (entry.EntryType == TarEntryType.Directory) { Directory.CreateDirectory(path); continue; }
                if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile) || entry.DataStream is null) { throw new IOException("Archive links or unsupported entry types were rejected."); }
                total += entry.Length; if (total > 2L * 1024 * 1024 * 1024) { throw new IOException("The voice archive exceeded its extraction budget."); }
                Directory.CreateDirectory(Path.GetDirectoryName(path)!); await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
                await entry.DataStream.CopyToAsync(output, token).ConfigureAwait(false);
            }
        }
        else
        {
            using var zip = new ZipArchive(file, ZipArchiveMode.Read, true);
            foreach (var entry in zip.Entries)
            {
                string path = EntryPath(stage, entry.FullName);
                if (entry.FullName.EndsWith('/')) { Directory.CreateDirectory(path); continue; }
                if ((entry.ExternalAttributes >> 16 & 0xF000) == 0xA000) { throw new IOException("Archive symbolic links were rejected."); }
                total += entry.Length; if (total > 2L * 1024 * 1024 * 1024) { throw new IOException("The runtime archive exceeded its extraction budget."); }
                Directory.CreateDirectory(Path.GetDirectoryName(path)!); await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
                await using var input = entry.Open(); await input.CopyToAsync(output, token).ConfigureAwait(false);
            }
        }
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string artifact in Directory.GetFiles(stage, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(stage, artifact); string hash = await HashAsync(artifact, token).ConfigureAwait(false); files[relative] = hash;
            if (Directory.Exists(destination))
            {
                string existing = EntryPath(destination, relative); ValidatePath(existing);
                if (!File.Exists(existing) || await HashAsync(existing, token).ConfigureAwait(false) != hash) { throw new IOException("A legacy extracted artifact differs from the verified archive."); }
            }
        }
        string manifest = JsonSerializer.Serialize(new { archiveHash = await HashAsync(archive, token).ConfigureAwait(false), files });
        if (Directory.Exists(destination)) { await File.WriteAllTextAsync(Path.Combine(destination, "laura-extracted.json"), manifest, token).ConfigureAwait(false); }
        else
        {
            await File.WriteAllTextAsync(Path.Combine(stage, "laura-extracted.json"), manifest, token).ConfigureAwait(false);
            Directory.Move(stage, destination);
        }
        return destination;
        }
        finally
        {
            // Only this invocation's randomly named, non-redirected staging tree is disposable.
            if (Directory.Exists(stage))
            {
                ValidatePath(stage);
                foreach (string entry in Directory.EnumerateFileSystemEntries(stage, "*", SearchOption.AllDirectories)) { ValidatePath(entry); }
                Directory.Delete(stage, recursive: true);
            }
        }
    }
    /// <summary>Rejects archive traversal, drive syntax and alternate streams before writing an entry.</summary>
    private static string EntryPath(string root, string name)
    {
        if (name.Contains(':') || name.StartsWith('/') || name.StartsWith('\\')) { throw new IOException("Invalid archive path."); }
        string path = Path.GetFullPath(Path.Combine(root, name));
        if (!path.StartsWith(Path.GetFullPath(root).TrimEnd('\\') + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) { throw new IOException("Archive path escaped staging."); }
        return path;
    }
    /// <summary>Rejects redirected ancestors, including existing files, for every installation write.</summary>
    private static void ValidatePath(string path)
    {
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        { if (Path.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) { throw new IOException("Local model installation cannot traverse redirected paths."); } }
    }
    /// <summary>Limits redirects to the official distribution/CDN families without forwarding credentials.</summary>
    private static bool AllowedHost(Uri uri) => uri.Scheme == "https" && uri.IsDefaultPort && uri.UserInfo.Length == 0
        && (uri.Host is "github.com" or "huggingface.co" || uri.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith(".huggingface.co", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".hf.co", StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith(".xethub.hf.co", StringComparison.OrdinalIgnoreCase));
    /// <summary>Computes a complete SHA-256 without loading a model into managed memory.</summary>
    private static async Task<string> HashAsync(string path, CancellationToken token)
    { await using var input = File.OpenRead(path); return Convert.ToHexString(await SHA256.HashDataAsync(input, token).ConfigureAwait(false)); }
    /// <inheritdoc />
    public void Dispose() { _gate.Dispose(); http.Dispose(); GC.SuppressFinalize(this); }
}
