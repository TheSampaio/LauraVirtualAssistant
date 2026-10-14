using System.Buffers.Binary;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Xml;
using Laura.Core.Files;

namespace Laura.Core.Tests.Permissions;

public sealed class ExecutableMetadataTests
{
    // These derived PE fixtures are never written to an installation directory or executed.
    private static byte[] Fixture(string? manifest = null, bool graphical = true)
    {
        byte[] image = File.ReadAllBytes(typeof(ExecutableMetadata).Assembly.Location);
        using var reader = new PEReader(new MemoryStream(image)); var headers = reader.PEHeaders;
        int optional = headers.PEHeaderStartOffset;
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(optional + 68, 2), (ushort)(graphical ? Subsystem.WindowsGui : Subsystem.WindowsCui));
        int coff = optional - 20;
        ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(coff + 18, 2));
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(coff + 18, 2), (ushort)(flags & ~(ushort)Characteristics.Dll));
        int resourceEntry = optional + (headers.PEHeader!.Magic == PEMagic.PE32Plus ? 112 : 96) + 16;
        if (manifest is null) { image.AsSpan(resourceEntry, 8).Clear(); return image; }
        SectionHeader section = headers.SectionHeaders[^1]; byte[] text = Encoding.UTF8.GetBytes(manifest); byte[] resource = new byte[96 + text.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(resource.AsSpan(14, 2), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(resource.AsSpan(16, 4), 24); BinaryPrimitives.WriteUInt32LittleEndian(resource.AsSpan(20, 4), 0x80000018);
        BinaryPrimitives.WriteUInt16LittleEndian(resource.AsSpan(38, 2), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(resource.AsSpan(40, 4), 1); BinaryPrimitives.WriteUInt32LittleEndian(resource.AsSpan(44, 4), 0x80000030);
        BinaryPrimitives.WriteUInt16LittleEndian(resource.AsSpan(62, 2), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(resource.AsSpan(64, 4), 1033); BinaryPrimitives.WriteUInt32LittleEndian(resource.AsSpan(68, 4), 80);
        BinaryPrimitives.WriteInt32LittleEndian(resource.AsSpan(80, 4), section.VirtualAddress + 96); BinaryPrimitives.WriteInt32LittleEndian(resource.AsSpan(84, 4), text.Length);
        text.CopyTo(resource, 96);
        int size = (resource.Length + 511) / 512 * 512;
        Array.Resize(ref image, Math.Max(image.Length, section.PointerToRawData + size)); resource.CopyTo(image, section.PointerToRawData);
        int sectionHeader = optional + headers.CoffHeader.SizeOfOptionalHeader + (headers.SectionHeaders.Length - 1) * 40;
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(sectionHeader + 8, 4), resource.Length); BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(sectionHeader + 16, 4), size);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(resourceEntry, 4), section.VirtualAddress); BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(resourceEntry + 4, 4), resource.Length);
        return image;
    }
    [Fact]
    public void GraphicalEntrypointIsDistinguishedFromConsoleAndDllWithoutExecutingEither()
    {
        Assert.True(ExecutableMetadata.Read(new MemoryStream(Fixture())).IsGraphicalApplication);
        Assert.False(ExecutableMetadata.Read(new MemoryStream(Fixture(graphical: false))).IsGraphicalApplication);
        using var library = File.OpenRead(typeof(ExecutableMetadata).Assembly.Location); Assert.False(ExecutableMetadata.Read(library).IsGraphicalApplication);
        Assert.Throws<BadImageFormatException>(() => ExecutableMetadata.Read(new MemoryStream(Encoding.UTF8.GetBytes("Not an executable."))));
    }
    [Theory]
    [InlineData("asInvoker", "false", false)]
    [InlineData("requireAdministrator", "false", true)]
    [InlineData("highestAvailable", "false", true)]
    [InlineData("asInvoker", "true", true)]
    public void ElevationAndUiAccessDeclarationsRemainProtected(string level, string uiAccess, bool elevated)
    {
        byte[] image = Fixture("<assembly><trustInfo><requestedExecutionLevel level=\"" + level + "\" uiAccess=\"" + uiAccess + "\"/></trustInfo></assembly>");
        Assert.Equal(elevated, ExecutableMetadata.Read(new MemoryStream(image)).RequestsElevation);
    }
    [Fact]
    public void ManifestExternalEntitiesAreNeverResolved()
    {
        byte[] image = Fixture("<!DOCTYPE assembly [<!ENTITY external SYSTEM 'file:///invalid'>]><assembly>&external;</assembly>");
        Assert.Throws<XmlException>(() => ExecutableMetadata.Read(new MemoryStream(image)));
    }
}
