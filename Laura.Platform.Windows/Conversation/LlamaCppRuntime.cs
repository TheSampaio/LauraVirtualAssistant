using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using Laura.Core.Abstractions;
using Laura.Core.Configuration;
using Laura.Core.Conversation;

namespace Laura.Platform.Windows.Conversation;

/// <summary>Supervises an owner-enabled loopback-only llama.cpp child process and releases idle model memory.</summary>
public sealed class LlamaCppRuntime : IAsyncDisposable
{
    private readonly ISettingsService settings;
    private readonly HttpClient http;
    /// <summary>Tracks owner routing changes so an idle local child releases GPU and RAM immediately.</summary>
    public LlamaCppRuntime(ISettingsService settings, HttpClient http)
    { this.settings = settings; this.http = http; settings.Changed += OnSettingsChanged; }
    /// <summary>A request-scoped reference that keeps the supervised model alive until inference completes.</summary>
    public sealed class Lease(Uri endpoint, string key, Action release) : IDisposable
    {
        /// <summary>Gets the private loopback endpoint selected by the supervisor.</summary>
        public Uri Endpoint { get; } = endpoint;
        /// <summary>Gets an ephemeral process-only authentication value.</summary>
        public string Key { get; } = key;
        private Action? _release = release;
        /// <inheritdoc />
        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Dictionary<string, (long Length, DateTime Time, string Hash)> _verified = new(StringComparer.OrdinalIgnoreCase);
    private Process? _process;
    private LocalModelOptions? _running;
    private Uri? _endpoint;
    private string _key = "";
    private int _leases;
    private long _lastUse = Environment.TickCount64;
    private Task? _reaper;
    /// <summary>Gets the actual selected inference device after startup.</summary>
    public string ActiveDevice { get; private set; } = "Not started";
    /// <summary>Gets the layer count reported by the native runtime without retaining diagnostic text.</summary>
    public int? OffloadedLayers { get; private set; }
    /// <summary>Gets actual dedicated GPU memory attributed by Windows to the owned inference process, or null when unavailable.</summary>
    public long? DedicatedGpuBytes { get; private set; }
    /// <summary>Gets whether required installation paths and recorded integrity values exist without starting a process.</summary>
    public bool IsAvailable => settings.Current.LocalModel is { Enabled: true, ExecutableHash.Length: 64, ModelHash.Length: 64 } model
        && File.Exists(model.Executable) && File.Exists(model.ModelPath);

    /// <summary>Starts or reuses the verified owned runtime. Args: token: Cancellation. Returns: A private inference lease.</summary>
    public async Task<Lease> AcquireAsync(CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var model = settings.Current.LocalModel.Sanitized();
            if (!IsAvailable) { throw new ConversationProviderException(new(ConversationFailureKind.Unavailable)); }
            if (_process is null || _process.HasExited || _running != model)
            {
                StopProcess();
                await VerifyAsync(model.Executable, model.ExecutableHash, token).ConfigureAwait(false);
                await VerifyAsync(model.ModelPath, model.ModelHash, token).ConfigureAwait(false);
                await using (var weights = File.OpenRead(model.ModelPath))
                {
                    byte[] magic = new byte[4]; await weights.ReadExactlyAsync(magic, token).ConfigureAwait(false);
                    if (!magic.AsSpan().SequenceEqual("GGUF"u8)) { throw new ConversationProviderException(new(ConversationFailureKind.LocalIntegrity)); }
                }
                await VerifyLibrariesAsync(model.Executable, token).ConfigureAwait(false);
                string? gpu = model.GpuLayers == 0 ? null : await FindGpuAsync(model.Executable, token).ConfigureAwait(false);
                ActiveDevice = gpu ?? "CPU"; OffloadedLayers = null; DedicatedGpuBytes = null;
                var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); int port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
                _key = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)); _endpoint = new Uri("http://127.0.0.1:" + port + "/");
                var start = new ProcessStartInfo(model.Executable) { WorkingDirectory = Path.GetDirectoryName(model.Executable)!, UseShellExecute = false,
                    CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = true, RedirectStandardError = true };
                foreach (string name in start.Environment.Keys.Where(name => name.StartsWith("LLAMA_", StringComparison.OrdinalIgnoreCase) || name.StartsWith("HF_", StringComparison.OrdinalIgnoreCase)).ToArray())
                { start.Environment.Remove(name); }
                start.Environment["LLAMA_API_KEY"] = _key;
                string[] arguments = ["--model", model.ModelPath, "--alias", "laura-local", "--host", "127.0.0.1", "--port", port.ToString(CultureInfo.InvariantCulture),
                    "--ctx-size", model.ContextTokens.ToString(CultureInfo.InvariantCulture), "--threads", model.Threads.ToString(CultureInfo.InvariantCulture),
                    "--n-gpu-layers", (gpu is null ? 0 : model.GpuLayers < 0 ? 128 : model.GpuLayers).ToString(CultureInfo.InvariantCulture), "--parallel", "1", "--jinja", "--reasoning-format", "deepseek",
                    "--offline", "--no-webui", "--no-agent", "--log-colors", "off", "--log-verbosity", "3"];
                foreach (string argument in arguments) { start.ArgumentList.Add(argument); }
                if (gpu is not null) { start.ArgumentList.Add("--device"); start.ArgumentList.Add(gpu); }
                _process = Process.Start(start) ?? throw new ConversationProviderException(new(ConversationFailureKind.Unavailable));
                _process.OutputDataReceived += DiscardOutput; _process.ErrorDataReceived += DiscardOutput;
                _process.BeginOutputReadLine(); _process.BeginErrorReadLine(); _running = model;
                using var startup = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token); startup.CancelAfter(TimeSpan.FromMinutes(3));
                try
                {
                    while (true)
                    {
                        if (_process.HasExited) { throw new ConversationProviderException(new(ConversationFailureKind.Unavailable)); }
                        try
                        {
                            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(_endpoint, "v1/models"));
                            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _key);
                            using var response = await http.SendAsync(request, startup.Token).ConfigureAwait(false);
                            if (response.IsSuccessStatusCode) { break; }
                        }
                        catch (HttpRequestException) { }
                        await Task.Delay(200, startup.Token).ConfigureAwait(false);
                    }
                }
                catch { StopProcess(); throw; }
                if (gpu is not null)
                {
                    const string counter = @"\GPU Process Memory(*)\Dedicated Usage";
                    using var query = new Laura.Platform.Windows.System.WindowsPerformanceQuery(counter);
                    _ = query.Collect(); await Task.Delay(100, token).ConfigureAwait(false);
                    if (query.Collect())
                    {
                        string prefix = "pid_" + _process.Id.ToString(CultureInfo.InvariantCulture) + "_";
                        double[] values = [.. query.Read(counter).Where(item => item.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).Select(item => item.Value)];
                        if (values.Length > 0) { DedicatedGpuBytes = (long)values.Sum(); }
                    }
                }
                _reaper ??= Task.Run(ReapAsync, CancellationToken.None);
            }
            Interlocked.Increment(ref _leases); Interlocked.Exchange(ref _lastUse, Environment.TickCount64);
            return new(_endpoint!, _key, () => { Interlocked.Exchange(ref _lastUse, Environment.TickCount64); Interlocked.Decrement(ref _leases);
                if (ShouldRelease(settings.Current)) { _ = Task.Run(ReleaseUnusedAsync); } });
        }
        finally { _gate.Release(); }
    }

    /// <summary>Rejects modified runtime dependencies before loading native code from the reviewed installation.</summary>
    private async Task VerifyLibrariesAsync(string executable, CancellationToken token)
    {
        string directory = Path.GetDirectoryName(executable)!;
        string manifest = Path.Combine(directory, "laura-runtime.json");
        if (!File.Exists(manifest) || new FileInfo(manifest).Length > 64 * 1024) { throw new ConversationProviderException(new(ConversationFailureKind.LocalIntegrity)); }
        var files = JsonSerializer.Deserialize<Dictionary<string, string>>(await File.ReadAllTextAsync(manifest, token).ConfigureAwait(false))
            ?? throw new ConversationProviderException(new(ConversationFailureKind.LocalIntegrity));
        foreach (string file in Directory.EnumerateFiles(directory, "*.dll"))
        {
            if (!files.TryGetValue(Path.GetFileName(file), out string? expected)) { throw new ConversationProviderException(new(ConversationFailureKind.LocalIntegrity)); }
            await VerifyAsync(file, expected, token).ConfigureAwait(false);
        }
    }
    /// <summary>Reuses hashes only while observed file size and modification time stay unchanged.</summary>
    private async Task VerifyAsync(string path, string expected, CancellationToken token)
    {
        var file = new FileInfo(path);
        if (file.LinkTarget is not null || !file.Exists) { throw new ConversationProviderException(new(ConversationFailureKind.LocalIntegrity)); }
        if (_verified.TryGetValue(path, out var cached) && cached.Length == file.Length && cached.Time == file.LastWriteTimeUtc && cached.Hash == expected) { return; }
        await using var stream = File.OpenRead(path);
        string hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, token).ConfigureAwait(false));
        if (!hash.Equals(expected, StringComparison.OrdinalIgnoreCase)) { throw new ConversationProviderException(new(ConversationFailureKind.LocalIntegrity)); }
        _verified[path] = (file.Length, file.LastWriteTimeUtc, expected);
    }
    /// <summary>Consumes private native diagnostic lines without persisting or displaying their content.</summary>
    private void DiscardOutput(object sender, DataReceivedEventArgs args)
    {
        if (args.Data is not { } line) { return; }
        var match = global::System.Text.RegularExpressions.Regex.Match(line, @"offloaded (\d+)/\d+ layers", global::System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(30));
        if (match.Success && int.TryParse(match.Groups[1].Value, out int layers)) { OffloadedLayers = layers; }
    }
    /// <summary>Queries only the verified runtime for usable Vulkan hardware; software renderers never qualify as a GPU.</summary>
    private static async Task<string?> FindGpuAsync(string executable, CancellationToken token)
    {
        var start = new ProcessStartInfo(executable) { WorkingDirectory = Path.GetDirectoryName(executable)!, UseShellExecute = false, CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("--list-devices");
        using var process = Process.Start(start) ?? throw new IOException("The local runtime could not enumerate graphics devices.");
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(token); limit.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            Task<string> output = process.StandardOutput.ReadToEndAsync(limit.Token), error = process.StandardError.ReadToEndAsync(limit.Token);
            await process.WaitForExitAsync(limit.Token).ConfigureAwait(false);
            string devices = await output.ConfigureAwait(false) + "\n" + await error.ConfigureAwait(false);
            if (devices.Length > 65536) { throw new IOException("The runtime device response exceeded its bound."); }
            foreach (string line in devices.Split('\n'))
            {
                if (line.Contains("llvmpipe", StringComparison.OrdinalIgnoreCase) || line.Contains("lavapipe", StringComparison.OrdinalIgnoreCase) || line.Contains("software", StringComparison.OrdinalIgnoreCase)) { continue; }
                var match = global::System.Text.RegularExpressions.Regex.Match(line, @"^\s*(Vulkan\d+):", global::System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(30));
                if (match.Success) { return match.Groups[1].Value; }
            }
            return null;
        }
        finally { if (!process.HasExited) { process.Kill(true); } }
    }
    /// <summary>Releases idle memory without stopping an active inference lease.</summary>
    private async Task ReapAsync()
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
            while (await timer.WaitForNextTickAsync(_lifetime.Token).ConfigureAwait(false))
            {
                await _gate.WaitAsync(_lifetime.Token).ConfigureAwait(false);
                try { if (Volatile.Read(ref _leases) == 0 && Environment.TickCount64 - Interlocked.Read(ref _lastUse) >= settings.Current.LocalModel.IdleMinutes * 60000L) { StopProcess(); } }
                finally { _gate.Release(); }
            }
        }
        catch (OperationCanceledException) { }
    }
    /// <summary>Releases a supervised child when local inference is deselected, without interrupting an active request.</summary>
    private void OnSettingsChanged(object? sender, LauraSettings value)
    { if (ShouldRelease(value)) { _ = Task.Run(ReleaseUnusedAsync); } }
    /// <summary>Checks whether current owner preferences can leave the model entirely unloaded.</summary>
    private static bool ShouldRelease(LauraSettings value) => !value.LocalModel.Enabled
        || value.ProviderSelection is ConversationProviderSelection.ChatGpt or ConversationProviderSelection.Gemini
        || value.ProviderSelection == ConversationProviderSelection.Automatic
            && value.GenerativeAi.LightProvider == LightProviderSelection.Gemini;
    /// <summary>Stops only an unused child after obtaining the same gate used by inference startup.</summary>
    private async Task ReleaseUnusedAsync()
    {
        try
        {
            await _gate.WaitAsync(_lifetime.Token).ConfigureAwait(false);
            try { if (Volatile.Read(ref _leases) == 0 && ShouldRelease(settings.Current)) { StopProcess(); } }
            finally { _gate.Release(); }
        }
        catch (OperationCanceledException) { }
    }
    /// <summary>Stops only the child process created by this supervisor, including its owned descendants.</summary>
    private void StopProcess()
    {
        if (_process is { } process)
        {
            try { if (!process.HasExited) { process.Kill(entireProcessTree: true); } }
            catch (InvalidOperationException) { }
            finally { process.Dispose(); _process = null; _running = null; }
        }
    }
    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        settings.Changed -= OnSettingsChanged;
        await _lifetime.CancelAsync().ConfigureAwait(false);
        if (_reaper is not null) { await _reaper.ConfigureAwait(false); }
        await _gate.WaitAsync().ConfigureAwait(false);
        try { StopProcess(); } finally { _gate.Release(); _lifetime.Dispose(); }
        GC.SuppressFinalize(this);
    }
}
