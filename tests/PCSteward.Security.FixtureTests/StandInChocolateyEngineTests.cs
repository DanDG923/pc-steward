using System.Buffers.Binary;
using System.Reflection.PortableExecutable;
using System.Text;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// FR-UPD-15, TM-03/04/13; N4b review M1 (portable): the record-printing stand-in engine used by the Windows-only native end-to-end test is a
/// well-formed PE32+ console image, read back with the BCL PE reader: AMD64, one RWX section holding the entry point, a
/// <c>KERNEL32.dll</c> import of exactly the four documented functions, no base relocation, and the two outputs in place.
/// </summary>
[TestClass]
public sealed class StandInChocolateyEngineTests
{
    private const string List = "git|1.2.0\r\n";
    private const string Outdated = "git|1.2.0|1.3.0|false\r\n";

    [TestMethod]
    public void TheStandInIsAWellFormedConsoleImageImportingOnlyTheFourKernel32Functions()
    {
        byte[] image = StandInChocolateyEngine.Build(List, Outdated);
        using PEReader reader = new(new MemoryStream(image));
        PEHeaders headers = reader.PEHeaders;

        Assert.AreEqual(Machine.Amd64, headers.CoffHeader.Machine);
        Assert.IsTrue(headers.IsExe);
        Assert.AreEqual(PEMagic.PE32Plus, headers.PEHeader!.Magic);
        Assert.AreEqual(Subsystem.WindowsCui, headers.PEHeader.Subsystem);
        Assert.IsTrue(headers.CoffHeader.Characteristics.HasFlag(Characteristics.RelocsStripped));
        Assert.IsFalse(headers.PEHeader.DllCharacteristics.HasFlag(DllCharacteristics.DynamicBase), "no relocation is needed or present");
        Assert.AreEqual(0, headers.PEHeader.BaseRelocationTableDirectory.Size);
        Assert.AreEqual(1, headers.SectionHeaders.Length);
        SectionHeader text = headers.SectionHeaders[0];
        Assert.AreEqual(".text", text.Name);
        Assert.AreEqual(StandInChocolateyEngine.TextRva, headers.PEHeader.AddressOfEntryPoint);
        Assert.AreEqual(StandInChocolateyEngine.TextRva, text.VirtualAddress);
        Assert.AreEqual(headers.PEHeader.SizeOfImage, text.VirtualAddress + 0x1000, "SizeOfImage is the section rounded to the section alignment");
        Assert.AreEqual(image.Length, text.PointerToRawData + text.SizeOfRawData);

        PEMemoryBlock section = reader.GetSectionData(StandInChocolateyEngine.TextRva);
        ReadOnlySpan<byte> data = section.GetContent().AsSpan();
        DirectoryEntry imports = headers.PEHeader.ImportTableDirectory;
        int descriptor = imports.RelativeVirtualAddress - StandInChocolateyEngine.TextRva;
        Assert.AreEqual(StandInChocolateyEngine.DescriptorsAt, descriptor);
        Assert.AreEqual(StandInChocolateyEngine.Dll, Name(data, BinaryPrimitives.ReadInt32LittleEndian(data[(descriptor + 12)..]) - StandInChocolateyEngine.TextRva));
        Assert.IsTrue(data.Slice(descriptor + 20, 20).IndexOfAnyExcept((byte)0) < 0, "one descriptor, then the terminator");
        int lookup = BinaryPrimitives.ReadInt32LittleEndian(data[descriptor..]) - StandInChocolateyEngine.TextRva;
        int iat = BinaryPrimitives.ReadInt32LittleEndian(data[(descriptor + 16)..]) - StandInChocolateyEngine.TextRva;
        Assert.AreEqual(StandInChocolateyEngine.IatAt, iat);
        Assert.AreEqual(headers.PEHeader.ImportAddressTableDirectory.RelativeVirtualAddress, StandInChocolateyEngine.TextRva + iat);
        List<string> names = [];
        for (int index = 0; ; index++)
        {
            long entry = BinaryPrimitives.ReadInt64LittleEndian(data[(lookup + 8 * index)..]);
            Assert.AreEqual(entry, BinaryPrimitives.ReadInt64LittleEndian(data[(iat + 8 * index)..]), "the unbound IAT equals the lookup table");
            if (entry == 0) break;
            Assert.IsTrue(entry is > 0 and < int.MaxValue, "import by name, never by ordinal");
            int hintName = (int)entry - StandInChocolateyEngine.TextRva;
            Assert.AreEqual(0, hintName % 2, "hint/name entries are 2-byte aligned");
            names.Add(Name(data, hintName + 2));
        }
        CollectionAssert.AreEqual(StandInChocolateyEngine.Imports, names);

        Assert.AreEqual(List, Encoding.ASCII.GetString(data.Slice(StandInChocolateyEngine.ListTextAt,
            BinaryPrimitives.ReadInt32LittleEndian(data[StandInChocolateyEngine.ListLengthAt..]))));
        Assert.AreEqual(Outdated, Encoding.ASCII.GetString(data.Slice(StandInChocolateyEngine.OutdatedTextAt,
            BinaryPrimitives.ReadInt32LittleEndian(data[StandInChocolateyEngine.OutdatedLengthAt..]))));
    }

    [TestMethod]
    public void AnOutputThatDoesNotFitOrIsNotPrintableAsciiIsRefused()
    {
        Assert.ThrowsExactly<ArgumentException>(() => StandInChocolateyEngine.Build(new string('x', 0x71), Outdated));
        Assert.ThrowsExactly<ArgumentException>(() => StandInChocolateyEngine.Build(List, new string('x', 0x81)));
        Assert.ThrowsExactly<ArgumentException>(() => StandInChocolateyEngine.Build("git\t1.2.0", Outdated));
        Assert.ThrowsExactly<ArgumentException>(() => StandInChocolateyEngine.Build(List, "git|1.2.0|1.3.0|fälse"));
    }

    private static string Name(ReadOnlySpan<byte> data, int offset)
    {
        int length = data[offset..].IndexOf((byte)0);
        Assert.IsTrue(length > 0);
        return Encoding.ASCII.GetString(data.Slice(offset, length));
    }
}
