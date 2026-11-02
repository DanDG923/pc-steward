using System.Buffers.Binary;
using System.Reflection.PortableExecutable;
using System.Text;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// FR-UPD-15, TM-03/04/07/13; ADR-031 design §8.6 admission gate 2 (portable): the client stand-in engine used by the
/// Windows-only end-to-end gate is a well-formed PE32+ console image, read back with the BCL PE reader: AMD64, one RWX
/// section holding the entry point, a <c>KERNEL32.dll</c> import of exactly the six documented functions, no relocation; its
/// list output, sleeps and move table (UTF-16 paths in the pool) are where the code reads them; and every RIP-relative operand
/// of the assembled code resolves to the documented slot.
/// </summary>
[TestClass]
public sealed class StandInClientUpgradeEngineTests
{
    private const string List = "chocolatey|2.7.5\r\n";

    /// <summary>(end offset of an instruction with a RIP-relative disp32, the section offset it must address), from the NASM listing.</summary>
    private static readonly (int End, int Target)[] RipOperands =
    [
        (0x00A, StandInClientUpgradeEngine.IatAt + 0x00),  // call [GetCommandLineW]
        (0x040, StandInClientUpgradeEngine.IatAt + 0x18),  // call [ExitProcess] (silent)
        (0x04B, StandInClientUpgradeEngine.IatAt + 0x08),  // call [GetStdHandle]
        (0x055, StandInClientUpgradeEngine.ListTextAt),    // lea rdx, [ListText]
        (0x05C, StandInClientUpgradeEngine.ListLengthAt),  // mov r8d, [ListLength]
        (0x070, StandInClientUpgradeEngine.IatAt + 0x10),  // call [WriteFile]
        (0x07A, StandInClientUpgradeEngine.ListLengthAt),  // mov eax, [ListLength]
        (0x088, StandInClientUpgradeEngine.PreSleepAt),    // mov ecx, [PreSleepMs]
        (0x08E, StandInClientUpgradeEngine.IatAt + 0x28),  // call [Sleep]
        (0x095, 0x000),                                     // lea rdi, [start]
        (0x09C, StandInClientUpgradeEngine.MoveTableAt),   // lea rbx, [OpTable]
        (0x0A2, StandInClientUpgradeEngine.MoveCountAt),   // mov esi, [OpCount]
        (0x0BD, StandInClientUpgradeEngine.IatAt + 0x20),  // call [MoveFileExW]
        (0x0CF, StandInClientUpgradeEngine.PostSleepAt),   // mov ecx, [PostSleepMs]
        (0x0D5, StandInClientUpgradeEngine.IatAt + 0x28),  // call [Sleep]
        (0x0E5, StandInClientUpgradeEngine.IatAt + 0x18),  // call [ExitProcess] (move failed)
        (0x0F0, StandInClientUpgradeEngine.IatAt + 0x18),  // call [ExitProcess] (write failed)
    ];

    [TestMethod]
    public void TheStandInIsAWellFormedConsoleImageImportingOnlyTheSixKernel32Functions()
    {
        (string, string)[] moves = [(Fixture("a", "choco.exe"), Fixture("b", "choco.exe.old")), (Fixture("c", "chocolatey.nupkg"), Fixture("d", "x.nupkg"))];
        byte[] image = StandInClientUpgradeEngine.Build(List, moves, 1500, 250);
        using PEReader reader = new(new MemoryStream(image));
        PEHeaders headers = reader.PEHeaders;

        Assert.AreEqual(Machine.Amd64, headers.CoffHeader.Machine);
        Assert.IsTrue(headers.IsExe);
        Assert.AreEqual(PEMagic.PE32Plus, headers.PEHeader!.Magic);
        Assert.AreEqual(Subsystem.WindowsCui, headers.PEHeader.Subsystem);
        Assert.IsTrue(headers.CoffHeader.Characteristics.HasFlag(Characteristics.RelocsStripped));
        Assert.IsFalse(headers.PEHeader.DllCharacteristics.HasFlag(DllCharacteristics.DynamicBase));
        Assert.AreEqual(0, headers.PEHeader.BaseRelocationTableDirectory.Size);
        Assert.AreEqual(1, headers.SectionHeaders.Length);
        SectionHeader text = headers.SectionHeaders[0];
        Assert.AreEqual(".text", text.Name);
        Assert.AreEqual(StandInClientUpgradeEngine.TextRva, headers.PEHeader.AddressOfEntryPoint);
        Assert.AreEqual(headers.PEHeader.SizeOfImage, text.VirtualAddress + StandInClientUpgradeEngine.TextSize);
        Assert.AreEqual(image.Length, text.PointerToRawData + text.SizeOfRawData);

        ReadOnlySpan<byte> data = reader.GetSectionData(StandInClientUpgradeEngine.TextRva).GetContent().AsSpan();
        DirectoryEntry imports = headers.PEHeader.ImportTableDirectory;
        int descriptor = imports.RelativeVirtualAddress - StandInClientUpgradeEngine.TextRva;
        Assert.AreEqual(StandInClientUpgradeEngine.DescriptorsAt, descriptor);
        Assert.AreEqual(StandInClientUpgradeEngine.Dll, Ascii(data, BinaryPrimitives.ReadInt32LittleEndian(data[(descriptor + 12)..]) - StandInClientUpgradeEngine.TextRva));
        Assert.IsTrue(data.Slice(descriptor + 20, 20).IndexOfAnyExcept((byte)0) < 0, "one descriptor, then the terminator");
        int lookup = BinaryPrimitives.ReadInt32LittleEndian(data[descriptor..]) - StandInClientUpgradeEngine.TextRva;
        int iat = BinaryPrimitives.ReadInt32LittleEndian(data[(descriptor + 16)..]) - StandInClientUpgradeEngine.TextRva;
        Assert.AreEqual(StandInClientUpgradeEngine.IatAt, iat);
        Assert.AreEqual(headers.PEHeader.ImportAddressTableDirectory.RelativeVirtualAddress, StandInClientUpgradeEngine.TextRva + iat);
        List<string> names = [];
        for (int index = 0; ; index++)
        {
            long entry = BinaryPrimitives.ReadInt64LittleEndian(data[(lookup + 8 * index)..]);
            Assert.AreEqual(entry, BinaryPrimitives.ReadInt64LittleEndian(data[(iat + 8 * index)..]), "the unbound IAT equals the lookup table");
            if (entry == 0) break;
            Assert.IsTrue(entry is > 0 and < int.MaxValue, "import by name, never by ordinal");
            names.Add(Ascii(data, (int)entry - StandInClientUpgradeEngine.TextRva + 2));
        }
        CollectionAssert.AreEqual(StandInClientUpgradeEngine.Imports, names);

        Assert.AreEqual(List, Encoding.ASCII.GetString(data.Slice(StandInClientUpgradeEngine.ListTextAt,
            BinaryPrimitives.ReadInt32LittleEndian(data[StandInClientUpgradeEngine.ListLengthAt..]))));
        Assert.AreEqual(1500, BinaryPrimitives.ReadInt32LittleEndian(data[StandInClientUpgradeEngine.PreSleepAt..]));
        Assert.AreEqual(250, BinaryPrimitives.ReadInt32LittleEndian(data[StandInClientUpgradeEngine.PostSleepAt..]));
        Assert.AreEqual(moves.Length, BinaryPrimitives.ReadInt32LittleEndian(data[StandInClientUpgradeEngine.MoveCountAt..]));
        for (int index = 0; index < moves.Length; index++)
        {
            int from = BinaryPrimitives.ReadInt32LittleEndian(data[(StandInClientUpgradeEngine.MoveTableAt + 8 * index)..]);
            int to = BinaryPrimitives.ReadInt32LittleEndian(data[(StandInClientUpgradeEngine.MoveTableAt + 8 * index + 4)..]);
            Assert.IsTrue(from >= StandInClientUpgradeEngine.PoolAt && from % 2 == 0 && to > from && to % 2 == 0);
            Assert.AreEqual(moves[index].Item1, Utf16(data, from));
            Assert.AreEqual(moves[index].Item2, Utf16(data, to));
        }
        Assert.AreEqual(0, BinaryPrimitives.ReadInt64LittleEndian(data[(StandInClientUpgradeEngine.MoveTableAt + 8 * moves.Length)..]), "no further move");
    }

    [TestMethod]
    public void EveryRipRelativeOperandOfTheCodeAddressesItsDocumentedSlot()
    {
        byte[] code = StandInClientUpgradeEngine.Code;
        Assert.IsTrue(code.Length <= StandInClientUpgradeEngine.ListLengthAt, "the code ends before the data");
        foreach ((int end, int target) in RipOperands)
            Assert.AreEqual(target, end + BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(end - 4)), $"operand ending at 0x{end:X3}");
        Assert.AreEqual(0xCC, code[^1], "the code ends with int3");
    }

    [TestMethod]
    public void AnOutputMoveOrSleepThatDoesNotFitIsRefused()
    {
        string path = Fixture("a", "choco.exe");
        Assert.ThrowsExactly<ArgumentException>(() => StandInClientUpgradeEngine.Build(new string('x', 0x71), [], 0, 0));
        Assert.ThrowsExactly<ArgumentException>(() => StandInClientUpgradeEngine.Build("chocolatey\t2.7.5", [], 0, 0));
        Assert.ThrowsExactly<ArgumentException>(() => StandInClientUpgradeEngine.Build(List, [("relative\\choco.exe", path)], 0, 0));
        Assert.ThrowsExactly<ArgumentException>(() => StandInClientUpgradeEngine.Build(List, [(path, path + "\0x")], 0, 0));
        Assert.ThrowsExactly<ArgumentException>(() => StandInClientUpgradeEngine.Build(List,
            [.. Enumerable.Repeat((path, path), StandInClientUpgradeEngine.MaximumMoves + 1)], 0, 0));
        string longPath = Fixture(new string('p', 2000), "choco.exe");
        Assert.ThrowsExactly<ArgumentException>(() => StandInClientUpgradeEngine.Build(List, [(longPath, longPath), (longPath, longPath)], 0, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => StandInClientUpgradeEngine.Build(List, [], -1, 0));
    }

    private static string Fixture(string directory, string name) => Path.Combine(Path.GetTempPath(), "pcsteward-standin", directory, name);

    private static string Ascii(ReadOnlySpan<byte> data, int offset)
    {
        int length = data[offset..].IndexOf((byte)0);
        Assert.IsTrue(length > 0);
        return Encoding.ASCII.GetString(data.Slice(offset, length));
    }

    private static string Utf16(ReadOnlySpan<byte> data, int offset)
    {
        int index = offset;
        while (BinaryPrimitives.ReadUInt16LittleEndian(data[index..]) != 0) index += 2;
        return Encoding.Unicode.GetString(data[offset..index]);
    }
}
