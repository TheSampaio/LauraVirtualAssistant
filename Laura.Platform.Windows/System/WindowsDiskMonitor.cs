namespace Laura.Platform.Windows.System;

/// <summary>Samples real physical disk activity in Windows disk-number order.</summary>
public sealed class WindowsDiskMonitor : IDisposable
{
    private const string IdleTime = @"\PhysicalDisk(*)\% Idle Time";
    private WindowsPerformanceQuery? _query;
    private Dictionary<uint, long?> _disks = [];
    private Dictionary<uint, string> _volumes = [];
    private long _nextInventory;
    private readonly object _gate = new();
    /// <summary>Returns current activity and periodically refreshes hot-plugged disk metadata.</summary>
    public IReadOnlyList<PhysicalDiskSnapshot> Sample()
    { lock (_gate) { return Collect(); } }
    /// <summary>Serializes query collection and physical device discovery.</summary>
    private IReadOnlyList<PhysicalDiskSnapshot> Collect()
    {
        if (Environment.TickCount64 >= _nextInventory)
        {
            _nextInventory = Environment.TickCount64 + 15000;
            _disks = WindowsDiskInventory.Discover();
            _volumes = [];
            foreach (DriveInfo drive in DriveInfo.GetDrives().Where(drive => drive.DriveType is DriveType.Fixed or DriveType.Removable))
            {
                uint? number = WindowsDiskInventory.VolumeDisk(drive.Name);
                if (!number.HasValue) { continue; }
                _volumes[number.Value] = _volumes.TryGetValue(number.Value, out string? names) ? names + " · " + drive.Name.TrimEnd('\\') : drive.Name.TrimEnd('\\');
            }
        }
        bool warmup = _query is null;
        _query ??= new WindowsPerformanceQuery(IdleTime);
        var activity = new Dictionary<uint, double>();
        if (_query.Collect() && !warmup)
        {
            foreach ((string name, double idle) in _query.Read(IdleTime))
            {
                if (!uint.TryParse(name.Split(' ', 2)[0], out uint number)) { continue; }
                activity[number] = Math.Clamp(1 - idle / 100, 0, 1);
            }
        }
        return [.. _disks.OrderBy(disk => disk.Key).Select(disk => new PhysicalDiskSnapshot(disk.Key,
            activity.TryGetValue(disk.Key, out double value) ? value : null, disk.Value, _volumes.GetValueOrDefault(disk.Key, "")))];
    }
    /// <inheritdoc />
    public void Dispose() { lock (_gate) { _query?.Dispose(); _query = null; } GC.SuppressFinalize(this); }
}

/// <summary>Contains real physical identity, active-time usage, capacity and associated drive letters.</summary>
public sealed record PhysicalDiskSnapshot(uint Number, double? Activity, long? Capacity, string Volumes);
