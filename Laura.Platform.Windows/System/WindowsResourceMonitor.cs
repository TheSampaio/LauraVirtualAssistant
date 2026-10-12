using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace Laura.Platform.Windows.System;

/// <summary>Samples actual system CPU, RAM, storage, GPU and network-adapter traffic.</summary>
public sealed class WindowsResourceMonitor(WindowsGpuMonitor gpu, WindowsDiskMonitor disks)
{
    private ulong _idle, _total;
    private long _received, _sent;
    private long _time;
    private string? _ethernetId;
    private long _ethernetReceived, _ethernetSent, _ethernetTime;
    private readonly object _gate = new();
    /// <summary>Returns deltas from the previous sample; network usage is relative to active adapter link capacity.</summary>
    public ResourceSnapshot Sample(string storagePath)
    { lock (_gate) { return Collect(storagePath); } }
    /// <summary>Collects one serialized delta sample shared by the HUD and model tools.</summary>
    private ResourceSnapshot Collect(string storagePath)
    {
        GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);
        ulong total = kernel + user, delta = total - _total;
        double cpu = _time == 0 || delta == 0 ? 0 : Math.Clamp(1 - (idle - _idle) / (double)delta, 0, 1);
        _idle = idle; _total = total;
        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        GlobalMemoryStatusEx(ref memory);
        var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(storagePath))!);
        long received = 0, sent = 0, capacity = 0;
        foreach (NetworkInterface network in NetworkInterface.GetAllNetworkInterfaces().Where(network => network.OperationalStatus == OperationalStatus.Up && network.NetworkInterfaceType != NetworkInterfaceType.Loopback))
        {
            try { var traffic = network.GetIPStatistics(); received += traffic.BytesReceived; sent += traffic.BytesSent; capacity += Math.Max(0, network.Speed); }
            catch (NetworkInformationException) { }
        }
        long now = Environment.TickCount64;
        double seconds = Math.Max(.001, (now - _time) / 1000d);
        double down = _time == 0 ? 0 : Math.Max(0, received - _received) / seconds;
        double up = _time == 0 ? 0 : Math.Max(0, sent - _sent) / seconds;
        _received = received; _sent = sent; _time = now;
        GpuSnapshot? graphics = gpu.SampleResources();
        using var process = Process.GetCurrentProcess();
        return new(cpu, memory.MemoryLoad / 100d, drive.TotalSize == 0 ? 0 : 1 - drive.AvailableFreeSpace / (double)drive.TotalSize,
            capacity == 0 ? 0 : Math.Clamp((down + up) * 8 / capacity, 0, 1),
            memory.TotalPhysical - memory.AvailablePhysical, memory.TotalPhysical, drive.AvailableFreeSpace, drive.TotalSize,
            down, up, capacity > 0, process.WorkingSet64, DateTime.Now - process.StartTime, graphics?.Usage)
        { Graphics = graphics, Disks = disks.Sample(), Ethernet = SampleEthernet(now) };
    }
    /// <summary>Measures a connected wired adapter independently, resetting deltas when its identity changes.</summary>
    private EthernetSnapshot SampleEthernet(long now)
    {
        try
        {
            NetworkInterface? adapter = NetworkInterface.GetAllNetworkInterfaces()
                .Where(network => network.OperationalStatus == OperationalStatus.Up && network.NetworkInterfaceType is
                    NetworkInterfaceType.Ethernet or NetworkInterfaceType.FastEthernetT or NetworkInterfaceType.FastEthernetFx or NetworkInterfaceType.GigabitEthernet)
                .Where(WindowsNetworkAdapter.IsHardware)
                .OrderByDescending(network => network.Speed).ThenBy(network => network.Id, StringComparer.Ordinal).FirstOrDefault();
            if (adapter is null) { _ethernetId = null; return new(false, "", null, null, 0); }
            IPInterfaceStatistics statistics = adapter.GetIPStatistics();
            double seconds = (now - _ethernetTime) / 1000d;
            bool measured = _ethernetId == adapter.Id && seconds > 0;
            double? download = measured ? Math.Max(0, statistics.BytesReceived - _ethernetReceived) / seconds : null;
            double? upload = measured ? Math.Max(0, statistics.BytesSent - _ethernetSent) / seconds : null;
            _ethernetId = adapter.Id; _ethernetReceived = statistics.BytesReceived; _ethernetSent = statistics.BytesSent; _ethernetTime = now;
            return new(true, adapter.Name, download, upload, Math.Max(0, adapter.Speed));
        }
        catch (NetworkInformationException) { _ethernetId = null; return new(false, "", null, null, 0); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct MemoryStatus
    {
        internal uint Length, MemoryLoad;
        internal ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
}

/// <summary>Contains measured resource utilization and human-readable supporting values.</summary>
public sealed record ResourceSnapshot(double Cpu, double Memory, double Disk, double Network, ulong UsedMemory, ulong TotalMemory,
    long FreeDisk, long TotalDisk, double DownloadBytesPerSecond, double UploadBytesPerSecond, bool NetworkConnected, long AppMemory, TimeSpan Uptime, double? Gpu)
{
    /// <summary>Gets the selected hardware GPU's utilization and dedicated memory.</summary>
    public GpuSnapshot? Graphics { get; init; }
    /// <summary>Gets every discovered physical disk in Windows number order.</summary>
    public IReadOnlyList<PhysicalDiskSnapshot> Disks { get; init; } = [];
    /// <summary>Gets independently measured wired network traffic.</summary>
    public EthernetSnapshot Ethernet { get; init; } = new(false, "", null, null, 0);
}

/// <summary>Contains wired adapter identity, actual traffic rates and link capacity.</summary>
public sealed record EthernetSnapshot(bool Connected, string Name, double? DownloadBytesPerSecond, double? UploadBytesPerSecond, long LinkBitsPerSecond);
