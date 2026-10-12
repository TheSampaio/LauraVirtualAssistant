using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace Laura.Platform.Windows.System;

/// <summary>Distinguishes physical network interfaces from VPN and Hyper-V Ethernet adapters.</summary>
internal static class WindowsNetworkAdapter
{
    /// <summary>Checks the Windows hardware-interface flag rather than guessing from an adapter name.</summary>
    public static bool IsHardware(NetworkInterface network)
    {
        try
        {
            IPInterfaceProperties properties = network.GetIPProperties();
            int? index = network.Supports(NetworkInterfaceComponent.IPv4) ? properties.GetIPv4Properties()?.Index : properties.GetIPv6Properties()?.Index;
            if (!index.HasValue) { return false; }
            var row = new InterfaceRow { Index = (uint)index.Value };
            return GetIfEntry2(ref row) == 0 && (row.Flags & 1) != 0;
        }
        catch (NetworkInformationException) { return false; }
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private unsafe struct InterfaceRow
    {
        internal ulong Luid;
        internal uint Index;
        internal Guid InterfaceId;
        internal fixed char Alias[257];
        internal fixed char Description[257];
        internal uint PhysicalAddressLength;
        internal fixed byte PhysicalAddress[32];
        internal fixed byte PermanentPhysicalAddress[32];
        internal uint Mtu, Type, TunnelType, MediaType, PhysicalMediumType, AccessType, DirectionType;
        internal byte Flags;
        internal uint OperationalStatus, AdminStatus, MediaConnectState;
        internal Guid NetworkId;
        internal uint ConnectionType;
        internal ulong TransmitLinkSpeed, ReceiveLinkSpeed;
        internal ulong InOctets, InUnicastPackets, InNonUnicastPackets, InDiscards, InErrors, InUnknownProtocols, InUnicastOctets, InMulticastOctets, InBroadcastOctets;
        internal ulong OutOctets, OutUnicastPackets, OutNonUnicastPackets, OutDiscards, OutErrors, OutUnicastOctets, OutMulticastOctets, OutBroadcastOctets, OutQueueLength;
    }
    [DllImport("iphlpapi.dll")] private static extern uint GetIfEntry2(ref InterfaceRow row);
}
