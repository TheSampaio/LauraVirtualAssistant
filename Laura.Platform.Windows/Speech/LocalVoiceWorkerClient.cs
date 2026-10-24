using System.Diagnostics;
using System.Text.Json;
using Laura.Core.Speech;

namespace Laura.Platform.Windows.Speech;

/// <summary>Supervises the bundled native voice child and keeps cancellation, crashes and private payloads outside the UI process.</summary>
public sealed class LocalVoiceWorkerClient : IAsyncDisposable
{
    private readonly SemaphoreSlim _requests = new(1, 1);
    private Process? _process;
    private readonly string _executable;
    private readonly CancellationTokenSource _lifetime = new();
    private Task? _reaper;
    private long _lastUse = Environment.TickCount64;
    /// <summary>Uses the bundled worker by default. Args: executable: Explicit isolated diagnostics executable, when supplied.</summary>
    public LocalVoiceWorkerClient(string? executable = null) => _executable = executable ?? Path.Combine(AppContext.BaseDirectory, "VoiceWorker", "Laura.VoiceWorker.exe");
    /// <summary>Gets whether the packaged local worker exists without starting native inference.</summary>
    public bool IsInstalled => File.Exists(_executable);
    /// <summary>Executes one serialized private request. Args: request: Local voice operation; receive: Bounded PCM or transcript callback; token: Cancellation. Returns: Completion after native confirmation.</summary>
    public async Task ExecuteAsync(VoiceWorkerRequest request, Action<VoiceWorkerEvent> receive, CancellationToken token)
    {
        await _requests.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (!IsInstalled) { throw new SpeechUnavailableException("The bundled local voice worker is unavailable."); }
            if (_process is null || _process.HasExited)
            {
                Stop();
                var start = new ProcessStartInfo(_executable) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                    WorkingDirectory = Path.GetDirectoryName(_executable)!, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
                _process = Process.Start(start) ?? throw new SpeechUnavailableException("The bundled local voice worker could not start.");
                _process.ErrorDataReceived += DiscardError; _process.BeginErrorReadLine();
            }
            _reaper ??= Task.Run(ReapAsync, CancellationToken.None);
            var process = _process;
            using var cancellation = token.Register(Stop);
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request).AsMemory(), token).ConfigureAwait(false);
            await process.StandardInput.FlushAsync(token).ConfigureAwait(false);
            while (true)
            {
                string? line = await process.StandardOutput.ReadLineAsync(token).ConfigureAwait(false);
                if (line is null) { throw new SpeechPlaybackException("The local voice worker exited before completing its request."); }
                if (line.Length > 32768) { throw new InvalidDataException("Local voice worker response exceeded its bound."); }
                var response = JsonSerializer.Deserialize<VoiceWorkerEvent>(line) ?? throw new InvalidDataException("Invalid local voice response.");
                if (response.Type == "done") { return; }
                if (response.Type == "error") { throw new SpeechPlaybackException("The local voice worker reported a synthesis or recognition failure."); }
                if (response.Type is not ("pcm" or "transcript")) { throw new InvalidDataException("Unknown local voice response."); }
                receive(response);
            }
        }
        catch (Exception) { Stop(); token.ThrowIfCancellationRequested(); throw; }
        finally { Interlocked.Exchange(ref _lastUse, Environment.TickCount64); _requests.Release(); }
    }
    /// <summary>Releases both native voice models after five idle minutes while excluding active requests.</summary>
    private async Task ReapAsync()
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
            while (await timer.WaitForNextTickAsync(_lifetime.Token).ConfigureAwait(false))
            {
                if (!await _requests.WaitAsync(0, _lifetime.Token).ConfigureAwait(false)) { continue; }
                try { if (Environment.TickCount64 - Interlocked.Read(ref _lastUse) > 300000) { Stop(); } }
                finally { _requests.Release(); }
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
    }
    /// <summary>Consumes native stderr without logging models, paths, transcript text or audio.</summary>
    private static void DiscardError(object sender, DataReceivedEventArgs args) { }
    /// <summary>Terminates only the owned helper; cancellation cannot attach to arbitrary PIDs.</summary>
    private void Stop()
    {
        var process = Interlocked.Exchange(ref _process, null);
        if (process is null) { return; }
        try { if (!process.HasExited) { process.Kill(entireProcessTree: true); } }
        catch (InvalidOperationException) { }
        finally { process.Dispose(); }
    }
    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _lifetime.CancelAsync().ConfigureAwait(false); Stop();
        if (_reaper is not null) { await _reaper.ConfigureAwait(false); }
        await _requests.WaitAsync().ConfigureAwait(false); _requests.Release(); _lifetime.Dispose(); GC.SuppressFinalize(this);
    }
}
