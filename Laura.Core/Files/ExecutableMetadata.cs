using System.Buffers.Binary;
using System.Reflection.PortableExecutable;
using System.Xml;

namespace Laura.Core.Files;

/// <summary>Describes a PE file without loading or executing it. Metadata is not proof of publisher trust or runtime success.</summary>
public sealed record ExecutableMetadata(bool IsGraphicalApplication, bool RequestsElevation)
{
    /// <summary>Inspects bounded PE headers and the executable manifest; malformed resources fail closed.</summary>
    public static ExecutableMetadata Read(Stream input)
    {
        using var pe = new PEReader(input, PEStreamOptions.LeaveOpen);
        bool graphical = pe.PEHeaders.PEHeader?.Subsystem == Subsystem.WindowsGui
            && (pe.PEHeaders.CoffHeader.Characteristics & Characteristics.Dll) == 0;
        if (!graphical) { return new(false, false); }
        DirectoryEntry resources = pe.PEHeaders.PEHeader!.ResourceTableDirectory;
        if (resources.Size == 0) { return new(true, false); }
        if (resources.Size > 1024 * 1024) { throw new BadImageFormatException("Executable resource metadata exceeds its bound."); }
        byte[] directory = pe.GetSectionData(resources.RelativeVirtualAddress).GetContent(0, resources.Size).ToArray();
        int type = FindEntry(directory, 0, 24, true);
        if (type < 0) { return new(true, false); }
        int name = FindEntry(directory, type, 1, true);
        if (name < 0) { return new(true, false); }
        int data = FindEntry(directory, name, null, false);
        if (data < 0 || data > directory.Length - 16) { throw new BadImageFormatException("Invalid manifest resource."); }
        int rva = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(directory.AsSpan(data, 4)));
        int length = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(directory.AsSpan(data + 4, 4)));
        if (length is <= 0 or > 65536) { throw new BadImageFormatException("Manifest exceeds its bound."); }
        return new(true, ManifestRequestsElevation(pe.GetSectionData(rva).GetContent(0, length).ToArray()));
    }
    internal static bool ManifestRequestsElevation(byte[] manifest)
    {
        using var data = new MemoryStream(manifest, writable: false);
        using var reader = XmlReader.Create(data, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 65536 });
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "requestedExecutionLevel"
                && (reader.GetAttribute("level") != "asInvoker" || reader.GetAttribute("uiAccess") is { } access && !access.Equals("false", StringComparison.OrdinalIgnoreCase)))
            { return true; }
        }
        return false;
    }
    private static int FindEntry(byte[] directory, int offset, int? id, bool isDirectory)
    {
        if (offset < 0 || offset > directory.Length - 16) { throw new BadImageFormatException("Invalid PE resource directory."); }
        int count = BinaryPrimitives.ReadUInt16LittleEndian(directory.AsSpan(offset + 12, 2)) + BinaryPrimitives.ReadUInt16LittleEndian(directory.AsSpan(offset + 14, 2));
        if (count > 4096 || (long)offset + 16 + count * 8 > directory.Length) { throw new BadImageFormatException("Invalid PE resource entries."); }
        for (int index = 0; index < count; index++)
        {
            int entry = offset + 16 + index * 8;
            uint key = BinaryPrimitives.ReadUInt32LittleEndian(directory.AsSpan(entry, 4));
            if (id is not null && key != id.Value) { continue; }
            uint target = BinaryPrimitives.ReadUInt32LittleEndian(directory.AsSpan(entry + 4, 4));
            if (((target & 0x80000000) != 0) != isDirectory) { throw new BadImageFormatException("Unexpected PE resource kind."); }
            return checked((int)(target & 0x7fffffff));
        }
        return -1;
    }
}
