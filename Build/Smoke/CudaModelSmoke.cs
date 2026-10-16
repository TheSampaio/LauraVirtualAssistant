using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Laura.Core.Configuration;
using Laura.Core.Models;
using Laura.Platform.Windows.Conversation;
using Laura.Platform.Windows.System;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>Requires real CUDA offload evidence for pinned Qwen weights without accepting a silent CPU result.</summary>
internal static class CudaModelSmoke
{
    /// <summary>Measures actual app-local CUDA load/generation/cancellation/unload with synthetic input and no Toolkit installation.</summary>
    internal static async Task RunAsync(string directory, string runtimeDirectory, CancellationToken token, int preference = -1)
    {
        using var http = new HttpClient();
        using var models = new LocalModelManager(Path.Combine(directory, "Models"), http, SupportedModels.All);
        using var settings = new SettingsService(new IniSettingsStore(Path.Combine(directory, "cuda-settings.ini"), NullLogger<IniSettingsStore>.Instance), NullLogger<SettingsService>.Instance);
        await settings.LoadAsync(token);
        await settings.UpdateAsync(settings.Current with { GenerativeAi = settings.Current.GenerativeAi with { LocalGpuLayers = preference } }, token);
        var backend = new WindowsInferenceBackend(true, runtimeDirectory, allowCpuFallback: false);
        await using var inference = new LlamaLocalInferenceEngine(models, settings, backend: backend);
        using var gpu = new WindowsGpuMonitor(); _ = gpu.SampleResources(); await Task.Delay(550, token);
        var gpuBefore = gpu.SampleResources(); var watch = Stopwatch.StartNew();
        await inference.LoadAsync(SupportedModels.Intelligence.Id, token); double loadMs = watch.Elapsed.TotalMilliseconds;
        if (!backend.SupportsGpu || inference.Backend != "cuda12" || backend.OffloadedLayers <= 0
            || preference == 16 && backend.OffloadedLayers != 16)
        { throw new InvalidOperationException("Real CUDA offload was not verified; a CPU result cannot satisfy this fixture."); }
        int observedLayers = inference.OffloadedLayers; long? freeGpuBefore = inference.FreeGpuMemoryBeforeLoad;
        int chunks = 0; double firstVisibleMs = 0;
        inference.TokenReceived += (_, _) => { if (chunks++ == 0) { firstVisibleMs = watch.Elapsed.TotalMilliseconds; } };
        watch.Restart();
        string? answer = await inference.AskAsync(new("Answer only with the number: what is 2 + 2?", CultureInfo.GetCultureInfo("en-US"), "Synthetic tester"), token);
        double arithmeticMs = watch.Elapsed.TotalMilliseconds;
        if (answer is null || !answer.Contains('4') || chunks == 0) { throw new InvalidOperationException("Real CUDA inference did not return its verified streamed arithmetic answer."); }
        var arithmeticMetrics = inference.LastMetrics;
        watch.Restart();
        string? sustained = await inference.AskAsync(new("Write exactly three paragraphs describing a fictional garden, with at least 120 words total. Use complete sentences and no tools.", CultureInfo.GetCultureInfo("en-US"), "Synthetic tester"), token);
        double sustainedMs = watch.Elapsed.TotalMilliseconds; var sustainedMetrics = inference.LastMetrics;
        if (string.IsNullOrWhiteSpace(sustained) || sustainedMetrics is not { SampledTokens: >= 16, SampledTokensPerSecond: > 0 })
        { throw new InvalidOperationException("Real CUDA sustained generation did not establish measured token throughput."); }
        var gpuLoaded = gpu.SampleResources();
        var cudaMemoryLoaded = backend.ReadDeviceMemory();
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(token); cancelled.CancelAfter(100);
        watch.Restart(); bool cancellationObserved = false;
        try { await inference.AskAsync(new("Write a very long synthetic story.", CultureInfo.InvariantCulture, "Synthetic"), cancelled.Token); }
        catch (OperationCanceledException) { cancellationObserved = true; }
        double cancellationMs = watch.Elapsed.TotalMilliseconds;
        if (!cancellationObserved) { throw new InvalidOperationException("CUDA generation did not honor cancellation."); }
        await settings.UpdateAsync(settings.Current with { GenerativeAi = settings.Current.GenerativeAi with { LocalGpuLayers = 0 } }, token);
        await inference.LoadAsync(SupportedModels.Intelligence.Id, token);
        if (inference.Backend != "cpu" || inference.OffloadedLayers != 0) { throw new InvalidOperationException("Explicit CPU selection reused GPU weights or offload evidence."); }
        string? cpuAnswer = await inference.AskAsync(new("Answer only with the number: what is 2 + 2?", CultureInfo.GetCultureInfo("en-US"), "Synthetic"), token);
        if (cpuAnswer is null || !cpuAnswer.Contains('4')) { throw new InvalidOperationException("Explicit CPU selection did not produce a verified answer."); }
        using var process = Process.GetCurrentProcess(); process.Refresh();
        await inference.UnloadAsync(token); await Task.Delay(600, token); var gpuAfter = gpu.SampleResources();
        var cudaMemoryAfter = backend.ReadDeviceMemory();
        if (inference.LoadedModel is not null || inference.Status != "unloaded") { throw new InvalidOperationException("CUDA weights did not unload."); }
        await File.WriteAllTextAsync(Path.Combine(directory, preference == 16 ? "cuda-partial-benchmark.json" : "cuda-benchmark.json"), JsonSerializer.Serialize(new
        {
            artifact = SupportedModels.Intelligence.Id, nativeVersion = "LLamaSharp 0.27.0", backend = "cuda12",
            backend.SupportsGpu, preference, OffloadedLayers = observedLayers, freeGpuBefore, loadMs, firstVisibleMs, arithmeticMs, arithmeticMetrics,
            sustainedMs, sustainedMetrics, sustainedVisibleCharacters = sustained.Length, cancellationObserved, cancellationMs,
            gpuBefore, gpuLoaded, gpuAfter, gpuCounterScope = "system-wide dedicated usage for one matched DXGI adapter, not per-process allocation",
            cudaMemoryLoaded = cudaMemoryLoaded is { } loaded ? new { loaded.Free, loaded.Total } : null,
            cudaMemoryAfter = cudaMemoryAfter is { } unloaded ? new { unloaded.Free, unloaded.Total } : null,
            explicitCpuSelectionVerified = true,
            process.PrivateMemorySize64, process.PeakWorkingSet64, syntheticOnly = true, toolkitRequired = false,
        }, new JsonSerializerOptions { WriteIndented = true }), token);
        Console.WriteLine("Pinned Qwen CUDA offload, visible streaming, sustained token throughput, cancellation and unload verified using app-local redistributables. Numeric benchmark saved without prompts or answers.");
    }
}
