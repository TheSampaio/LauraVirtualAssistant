namespace Laura.Platform.Windows.System;

/// <summary>Measures GPU engines and system-wide dedicated memory for the same hardware adapter.</summary>
public sealed class WindowsGpuMonitor : IDisposable
{
    private const string Engines = @"\GPU Engine(*)\Utilization Percentage";
    private const string DedicatedUsage = @"\GPU Adapter Memory(*)\Dedicated Usage";
    private WindowsPerformanceQuery? _query;
    private WindowsGpuAdapter? _adapter;
    private GpuSnapshot? _lastValue;
    private long _lastSample;
    private readonly object _gate = new();
    /// <summary>Returns measured GPU utilization, or null when the driver does not expose GPU counters.</summary>
    public double? Sample() => SampleResources()?.Usage;
    /// <summary>Returns adapter usage and dedicated VRAM without substituting shared RAM or process memory.</summary>
    public GpuSnapshot? SampleResources()
    {
        lock (_gate)
        {
            long now = Environment.TickCount64;
            if (_query is not null && now - _lastSample < 500) { return _lastValue; }
            _lastSample = now;
            if (_query is null)
            {
                _adapter = WindowsGpuAdapter.Discover();
                _query = new WindowsPerformanceQuery(Engines, DedicatedUsage);
                _ = _query.Collect();
                _lastValue = _adapter is null ? null : new(_adapter.Name, null, null, _adapter.DedicatedMemory);
                return _lastValue;
            }
            if (_adapter is null) { return null; }
            if (!_query.Collect()) { _lastValue = new(_adapter.Name, null, null, _adapter.DedicatedMemory); return _lastValue; }
            var engines = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach ((string instance, double value) in _query.Read(Engines))
            {
                int adapter = instance.IndexOf(_adapter.CounterId + "_", StringComparison.OrdinalIgnoreCase);
                if (adapter < 0) { continue; }
                // Processes on one engine are additive; parallel engine percentages are not.
                string engine = instance[adapter..];
                engines[engine] = engines.GetValueOrDefault(engine) + value;
            }
            double? usage = engines.Count == 0 ? null : Math.Clamp(engines.Values.Max() / 100, 0, 1);
            double[] memory = [.. _query.Read(DedicatedUsage).Where(item => item.Key.StartsWith(_adapter.CounterId + "_", StringComparison.OrdinalIgnoreCase)).Select(item => item.Value)];
            _lastValue = new(_adapter.Name, usage, memory.Length == 0 ? null : (ulong)memory.Sum(), _adapter.DedicatedMemory);
            return _lastValue;
        }
    }
    /// <inheritdoc />
    public void Dispose() { lock (_gate) { _query?.Dispose(); _query = null; } GC.SuppressFinalize(this); }
}

/// <summary>Contains measurements for one explicitly identified hardware GPU.</summary>
public sealed record GpuSnapshot(string Name, double? Usage, ulong? UsedDedicatedMemory, ulong TotalDedicatedMemory);
