using System.Runtime.InteropServices;

namespace Laura.Platform.Windows.System;

/// <summary>Owns a native PDH query with English wildcard counters and validated readings.</summary>
internal sealed class WindowsPerformanceQuery : IDisposable
{
    private nint _query;
    private readonly Dictionary<string, nint> _counters = new(StringComparer.Ordinal);
    /// <summary>Opens supported counters without requiring a performance-counter package.</summary>
    public WindowsPerformanceQuery(params string[] paths)
    {
        if (PdhOpenQuery(null, 0, out _query) != 0) { return; }
        foreach (string path in paths)
        { if (PdhAddEnglishCounter(_query, path, 0, out nint counter) == 0) { _counters.Add(path, counter); } }
    }
    /// <summary>Collects one sample for every counter in the query.</summary>
    public bool Collect() => _query != 0 && PdhCollectQueryData(_query) == 0;
    /// <summary>Returns only valid, finite instance values; missing counters remain unavailable.</summary>
    public Dictionary<string, double> Read(string path)
    {
        var values = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        if (!_counters.TryGetValue(path, out nint counter)) { return values; }
        uint size = 0;
        _ = PdhGetFormattedCounterArray(counter, 0x200, ref size, out _, 0);
        if (size == 0 || size > 8 * 1024 * 1024) { return values; }
        nint buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (PdhGetFormattedCounterArray(counter, 0x200, ref size, out uint count, buffer) != 0) { return values; }
            int stride = Marshal.SizeOf<CounterItem>();
            for (int index = 0; index < count && (long)(index + 1) * stride <= size; index++)
            {
                CounterItem item = Marshal.PtrToStructure<CounterItem>(buffer + index * stride);
                if (item.Value.Status > 1 || !double.IsFinite(item.Value.Value) || item.Value.Value < 0) { continue; }
                string? name = Marshal.PtrToStringUni(item.Name);
                if (name is not null) { values[name] = item.Value.Value; }
            }
        }
        finally { Marshal.FreeHGlobal(buffer); }
        return values;
    }
    /// <inheritdoc />
    public void Dispose() { if (_query != 0) { _ = PdhCloseQuery(_query); _query = 0; } _counters.Clear(); GC.SuppressFinalize(this); }
    [StructLayout(LayoutKind.Sequential)] private struct CounterValue { internal uint Status; internal double Value; }
    [StructLayout(LayoutKind.Sequential)] private struct CounterItem { internal nint Name; internal CounterValue Value; }
    [DllImport("pdh.dll", EntryPoint = "PdhOpenQueryW", CharSet = CharSet.Unicode)] private static extern uint PdhOpenQuery(string? source, nuint userData, out nint query);
    [DllImport("pdh.dll", EntryPoint = "PdhAddEnglishCounterW", CharSet = CharSet.Unicode)] private static extern uint PdhAddEnglishCounter(nint query, string path, nuint userData, out nint counter);
    [DllImport("pdh.dll")] private static extern uint PdhCollectQueryData(nint query);
    [DllImport("pdh.dll", EntryPoint = "PdhGetFormattedCounterArrayW")] private static extern uint PdhGetFormattedCounterArray(nint counter, uint format, ref uint size, out uint count, nint buffer);
    [DllImport("pdh.dll")] private static extern uint PdhCloseQuery(nint query);
}
