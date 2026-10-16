using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Laura.Core.Configuration;
using Laura.Core.Conversation;
using Laura.Core.Models;
using Laura.Platform.Windows.Conversation;
using Laura.Platform.Windows.System;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>Explicit opt-in benchmark for real pinned weights; never part of ordinary unit tests.</summary>
internal static class LocalModelSmoke
{
    /// <summary>Downloads only with the explicit flag, verifies real load/generation/stream/cancel/unload and records measured hardware/resource metadata.</summary>
    internal static async Task RunAsync(string directory, bool download, CancellationToken cancellationToken)
    {
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        using var models = new LocalModelManager(Path.Combine(directory, "Models"), http, SupportedModels.All);
        using var settings = new SettingsService(new IniSettingsStore(Path.Combine(directory, "settings.ini"), NullLogger<IniSettingsStore>.Instance), NullLogger<SettingsService>.Instance);
        await settings.LoadAsync(cancellationToken);
        string path = models.GetModelPath(SupportedModels.Intelligence.Id);
        if (!File.Exists(path))
        {
            if (!download) { throw new FileNotFoundException("Pass --download to install the reviewed Qwen3.5 artifact in isolated storage."); }
            Console.WriteLine("Downloading pinned Qwen3.5-4B Q4_K_M: " + SupportedModels.Intelligence.Size + " bytes.");
            await models.InstallAsync(SupportedModels.Intelligence, null, cancellationToken);
            Console.WriteLine("Managed weight download and SHA-256 verified.");
        }
        await using var inference = new LlamaLocalInferenceEngine(models, settings);
        HardwareProfile hardware = WindowsModelHardware.Read(directory); var watch = Stopwatch.StartNew();
        Console.WriteLine("Measured hardware: " + JsonSerializer.Serialize(hardware));
        await inference.LoadAsync(SupportedModels.Intelligence.Id, cancellationToken); double loadMs = watch.Elapsed.TotalMilliseconds;
        Console.WriteLine("Native load milliseconds: " + loadMs.ToString("F0", CultureInfo.InvariantCulture));
        int streamed = 0; double firstTokenMs = 0;
        inference.TokenReceived += (_, _) => { if (streamed++ == 0) { firstTokenMs = watch.Elapsed.TotalMilliseconds; } };
        watch.Restart();
        Console.WriteLine("Starting native synthetic generation.");
        string? answer = await inference.AskAsync(new("Answer only with the number: what is 2 + 2?", CultureInfo.GetCultureInfo("en-US"), "Synthetic tester") { TurnId = Guid.NewGuid() }, cancellationToken);
        double generateMs = watch.Elapsed.TotalMilliseconds;
        if (string.IsNullOrWhiteSpace(answer) || !answer.Contains('4') || answer.Contains("<think>", StringComparison.Ordinal) || streamed == 0)
        { throw new InvalidOperationException("Local generation did not produce a verified visible streamed answer."); }
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); cancel.CancelAfter(300);
        bool cancellationObserved = false;
        try { await inference.AskAsync(new("Write a very long story about a synthetic red reactor.", CultureInfo.GetCultureInfo("en-US"), "Synthetic tester"), cancel.Token); }
        catch (OperationCanceledException) { cancellationObserved = true; }
        if (!cancellationObserved) { throw new InvalidOperationException("Local generation cancellation was not observed."); }
        using var process = Process.GetCurrentProcess(); process.Refresh();
        var benchmark = new { artifact = SupportedModels.Intelligence, hardware, loadMs, generateMs, firstTokenMs, streamedChunks = streamed,
            answerCharacters = answer.Length, privateBytes = process.PrivateMemorySize64, peakWorkingSetBytes = process.PeakWorkingSet64,
            cpuOnly = true, gpuMeasurements = "not available: no GPU backend tested", cancellationObserved };
        await File.WriteAllTextAsync(Path.Combine(directory, "local-benchmark.json"), JsonSerializer.Serialize(benchmark, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
        await inference.UnloadAsync(cancellationToken);
        if (inference.LoadedModel is not null || inference.Status != "unloaded") { throw new InvalidOperationException("Native model did not unload."); }
        Console.WriteLine("Embedded Qwen3.5 CPU load, visible streaming, arithmetic answer, cancellation and unload verified. Benchmark saved without prompts, responses or credentials.");
    }
}
