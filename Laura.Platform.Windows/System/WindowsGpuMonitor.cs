using System.Runtime.InteropServices;

namespace Laura.Platform.Windows.System;

/// <summary>Measures the busiest physical GPU engine across all processes using Windows PDH counters.</summary>
public sealed class WindowsGpuMonitor : IDisposable
{
    private nint _query;
    private nint _counter;
    private bool _initialized;
    private double? _lastValue;
    private long _lastSample;
    private readonly object _gate = new();
    /// <summary>Returns measured GPU utilization, or null when the driver does not expose GPU counters.</summary>
    public double? Sample()
    {
        lock (_gate)
        {
            long now = Environment.TickCount64;
            if (_initialized && now - _lastSample < 500) { return _lastValue; }
            _lastSample = now;
            if (!_initialized)
            {
                _initialized = true;
                if (PdhOpenQuery(null, 0, out _query) != 0 ||
                    PdhAddEnglishCounter(_query, @"\GPU Engine(*)\Utilization Percentage", 0, out _counter) != 0)
                { Close(); return null; }
                _ = PdhCollectQueryData(_query);
                return null;
            }
            if (_query == 0 || PdhCollectQueryData(_query) != 0) { _lastValue = null; return null; }
            uint size = 0;
            _ = PdhGetFormattedCounterArray(_counter, 0x200, ref size, out _, 0);
            if (size == 0 || size > 8 * 1024 * 1024) { _lastValue = null; return null; }
            nint buffer = Marshal.AllocHGlobal((int)size);
            try
            {
                if (PdhGetFormattedCounterArray(_counter, 0x200, ref size, out uint count, buffer) != 0) { _lastValue = null; return null; }
                var engines = new Dictionary<string, double>(StringComparer.Ordinal);
                int stride = Marshal.SizeOf<CounterItem>();
                for (int index = 0; index < count && (long)(index + 1) * stride <= size; index++)
                {
                    CounterItem item = Marshal.PtrToStructure<CounterItem>(buffer + index * stride);
                    if (item.Value.Status > 1 || !double.IsFinite(item.Value.Value)) { continue; }
                    string instance = Marshal.PtrToStringUni(item.Name) ?? "";
                    int adapter = instance.IndexOf("_luid_", StringComparison.Ordinal);
                    if (adapter < 0) { continue; }
                    // Sum processes on the same engine; parallel engines are not additive GPU percentages.
                    string engine = instance[adapter..];
                    engines[engine] = engines.GetValueOrDefault(engine) + Math.Max(0, item.Value.Value);
                }
                _lastValue = engines.Count == 0 ? null : Math.Clamp(engines.Values.Max() / 100, 0, 1);
                return _lastValue;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
    }
    /// <summary>Closes the query and all of its native counter handles.</summary>
    private void Close() { if (_query != 0) { _ = PdhCloseQuery(_query); _query = 0; _counter = 0; } }
    /// <inheritdoc />
    public void Dispose() { lock (_gate) { Close(); } GC.SuppressFinalize(this); }
    [StructLayout(LayoutKind.Sequential)] private struct CounterValue { internal uint Status; internal double Value; }
    [StructLayout(LayoutKind.Sequential)] private struct CounterItem { internal nint Name; internal CounterValue Value; }
    [DllImport("pdh.dll", EntryPoint = "PdhOpenQueryW", CharSet = CharSet.Unicode)] private static extern uint PdhOpenQuery(string? source, nuint userData, out nint query);
    [DllImport("pdh.dll", EntryPoint = "PdhAddEnglishCounterW", CharSet = CharSet.Unicode)] private static extern uint PdhAddEnglishCounter(nint query, string path, nuint userData, out nint counter);
    [DllImport("pdh.dll")] private static extern uint PdhCollectQueryData(nint query);
    [DllImport("pdh.dll", EntryPoint = "PdhGetFormattedCounterArrayW")] private static extern uint PdhGetFormattedCounterArray(nint counter, uint format, ref uint size, out uint count, nint buffer);
    [DllImport("pdh.dll")] private static extern uint PdhCloseQuery(nint query);
}
