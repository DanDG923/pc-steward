using System.Buffers.Binary;
using System.Text;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// FR-UPD-15, TM-03/04/13; N4b review M1 (test only): a minimal Windows x64 console image that stands in for the root engine of a fixture profile and
/// prints fixed <c>--limit-output</c> records, so the native stage-C queries can parse a real child's output. It imports only
/// <c>GetCommandLineW</c>, <c>GetStdHandle</c>, <c>WriteFile</c> and <c>ExitProcess</c> from <c>KERNEL32.dll</c>, reads no
/// file, environment variable or registry value, and branches only on the verb after the quoted image path (the launcher
/// always quotes argv[0]): <c>list</c> writes <c>listOutput</c>, <c>outdated</c> writes <c>outdatedOutput</c>, anything else
/// (the upgrade) writes nothing. It exits 0, or 1 when the write fails or is short. The image is built here in the fixture
/// temp tree; no tool or package is involved at test time.
/// <para>
/// Layout: headers in the first 0x200 file bytes; one RWX <c>.text</c> section at RVA 0x1000 (file offset 0x200, 0x400 bytes)
/// holding the code at +0x000, the two output lengths at +0x100/+0x104, the two outputs at +0x110/+0x180, the import address
/// table at +0x200, the lookup table at +0x230, the import descriptors at +0x260, the DLL name at +0x290 and the hint/name
/// entries from +0x2A0 (0x20 apart). No base relocation is needed (every address is RIP-relative), so the image is marked
/// relocations-stripped without dynamic base. The code bytes were assembled with NASM 2.16 (<c>bits 64</c>,
/// <c>default rel</c>, <c>org 0x1000</c>, data labels at the offsets above) from:
/// </para>
/// <code>
///     sub rsp, 0x38
///     call [IatGetCommandLineW]
///     cmp word [rax], 0x22          ; argv[0] is always quoted
///     jne silent
///     lea rcx, [rax + 2]
/// find:
///     movzx edx, word [rcx]
///     test edx, edx
///     jz silent
///     add rcx, 2
///     cmp edx, 0x22
///     jne find
///     cmp word [rcx], 0x20          ; one space, then the verb
///     jne silent
///     movzx edx, word [rcx + 2]
///     cmp edx, 'l'
///     je list
///     cmp edx, 'o'
///     jne silent
///     lea rbx, [OutdatedText]
///     mov edi, [OutdatedLength]
///     jmp write
/// list:
///     lea rbx, [ListText]
///     mov edi, [ListLength]
/// write:
///     mov ecx, -11                  ; STD_OUTPUT_HANDLE
///     call [IatGetStdHandle]
///     mov rcx, rax
///     mov rdx, rbx
///     mov r8d, edi
///     lea r9, [rsp + 0x28]
///     mov qword [rsp + 0x20], 0
///     call [IatWriteFile]
///     test eax, eax
///     jz failed
///     cmp edi, [rsp + 0x28]
///     jne failed
/// silent:
///     xor ecx, ecx
///     call [IatExitProcess]
/// failed:
///     mov ecx, 1
///     call [IatExitProcess]
///     int3
/// </code>
/// </summary>
internal static class StandInChocolateyEngine
{
    internal const int TextRva = 0x1000;
    internal const int TextFileOffset = 0x200;
    internal const int TextSize = 0x400;
    internal const int ListLengthAt = 0x100;
    internal const int OutdatedLengthAt = 0x104;
    internal const int ListTextAt = 0x110;
    internal const int OutdatedTextAt = 0x180;
    internal const int IatAt = 0x200;
    internal const int LookupAt = 0x230;
    internal const int DescriptorsAt = 0x260;
    internal const int DllNameAt = 0x290;
    internal const int HintNamesAt = 0x2A0;
    internal const string Dll = "KERNEL32.dll";

    /// <summary>The import order is the IAT order the code calls through (+0x200, +0x208, +0x210, +0x218).</summary>
    internal static readonly string[] Imports = ["GetCommandLineW", "GetStdHandle", "WriteFile", "ExitProcess"];

    private const int PeHeaderAt = 0x40;
    private const int OptionalHeaderAt = PeHeaderAt + 4 + 20;
    private const int OptionalHeaderSize = 0xF0;
    private const int SectionHeaderAt = OptionalHeaderAt + OptionalHeaderSize;

    private static readonly byte[] Code =
    [
        0x48, 0x83, 0xEC, 0x38, 0xFF, 0x15, 0xF6, 0x01, 0x00, 0x00, 0x66, 0x83, 0x38, 0x22, 0x75, 0x76,
        0x48, 0x8D, 0x48, 0x02, 0x0F, 0xB7, 0x11, 0x85, 0xD2, 0x74, 0x6B, 0x48, 0x83, 0xC1, 0x02, 0x83,
        0xFA, 0x22, 0x75, 0xF0, 0x66, 0x83, 0x39, 0x20, 0x75, 0x5C, 0x0F, 0xB7, 0x51, 0x02, 0x83, 0xFA,
        0x6C, 0x74, 0x14, 0x83, 0xFA, 0x6F, 0x75, 0x4E, 0x48, 0x8D, 0x1D, 0x41, 0x01, 0x00, 0x00, 0x8B,
        0x3D, 0xBF, 0x00, 0x00, 0x00, 0xEB, 0x0D, 0x48, 0x8D, 0x1D, 0xC2, 0x00, 0x00, 0x00, 0x8B, 0x3D,
        0xAC, 0x00, 0x00, 0x00, 0xB9, 0xF5, 0xFF, 0xFF, 0xFF, 0xFF, 0x15, 0xA9, 0x01, 0x00, 0x00, 0x48,
        0x89, 0xC1, 0x48, 0x89, 0xDA, 0x41, 0x89, 0xF8, 0x4C, 0x8D, 0x4C, 0x24, 0x28, 0x48, 0xC7, 0x44,
        0x24, 0x20, 0x00, 0x00, 0x00, 0x00, 0xFF, 0x15, 0x94, 0x01, 0x00, 0x00, 0x85, 0xC0, 0x74, 0x0E,
        0x3B, 0x7C, 0x24, 0x28, 0x75, 0x08, 0x31, 0xC9, 0xFF, 0x15, 0x8A, 0x01, 0x00, 0x00, 0xB9, 0x01,
        0x00, 0x00, 0x00, 0xFF, 0x15, 0x7F, 0x01, 0x00, 0x00, 0xCC,
    ];

    /// <summary>The image bytes; each output is printable ASCII (CR/LF allowed) and fits its slot.</summary>
    internal static byte[] Build(string listOutput, string outdatedOutput)
    {
        byte[] list = Ascii(listOutput, OutdatedTextAt - ListTextAt);
        byte[] outdated = Ascii(outdatedOutput, IatAt - OutdatedTextAt);
        byte[] image = new byte[TextFileOffset + TextSize];
        Span<byte> span = image;

        // DOS header: the signature and e_lfanew only.
        span[0] = (byte)'M';
        span[1] = (byte)'Z';
        U32(span, 0x3C, PeHeaderAt);

        // PE signature and COFF file header: AMD64, one section, executable, large-address aware, relocations stripped.
        "PE\0\0"u8.CopyTo(span[PeHeaderAt..]);
        U16(span, PeHeaderAt + 4, 0x8664);
        U16(span, PeHeaderAt + 6, 1);
        U16(span, PeHeaderAt + 20, OptionalHeaderSize);
        U16(span, PeHeaderAt + 22, 0x0001 | 0x0002 | 0x0020);

        // PE32+ optional header.
        int o = OptionalHeaderAt;
        U16(span, o, 0x20B);
        span[o + 2] = 14;
        U32(span, o + 4, TextSize);                    // SizeOfCode
        U32(span, o + 16, TextRva);                    // AddressOfEntryPoint (code at +0x000)
        U32(span, o + 20, TextRva);                    // BaseOfCode
        U64(span, o + 24, 0x1_4000_0000);              // ImageBase
        U32(span, o + 32, 0x1000);                     // SectionAlignment
        U32(span, o + 36, 0x200);                      // FileAlignment
        U16(span, o + 40, 6);                          // MajorOperatingSystemVersion
        U16(span, o + 48, 6);                          // MajorSubsystemVersion
        U32(span, o + 56, 0x2000);                     // SizeOfImage
        U32(span, o + 60, TextFileOffset);             // SizeOfHeaders
        U16(span, o + 68, 3);                          // IMAGE_SUBSYSTEM_WINDOWS_CUI
        U16(span, o + 70, 0x0100 | 0x8000);            // NX compatible, terminal-server aware; no dynamic base
        U64(span, o + 72, 0x10_0000);                  // SizeOfStackReserve
        U64(span, o + 80, 0x1000);                     // SizeOfStackCommit
        U64(span, o + 88, 0x10_0000);                  // SizeOfHeapReserve
        U64(span, o + 96, 0x1000);                     // SizeOfHeapCommit
        U32(span, o + 108, 16);                        // NumberOfRvaAndSizes
        U32(span, o + 112 + 8 * 1, TextRva + DescriptorsAt);   // import directory
        U32(span, o + 112 + 8 * 1 + 4, 2 * 20);
        U32(span, o + 112 + 8 * 12, TextRva + IatAt);           // import address table directory
        U32(span, o + 112 + 8 * 12 + 4, (Imports.Length + 1) * 8);

        // The one section: code, initialized data, execute, read, write (the loader binds the IAT in place).
        ".text"u8.CopyTo(span[SectionHeaderAt..]);
        U32(span, SectionHeaderAt + 8, TextSize);      // VirtualSize
        U32(span, SectionHeaderAt + 12, TextRva);      // VirtualAddress
        U32(span, SectionHeaderAt + 16, TextSize);     // SizeOfRawData
        U32(span, SectionHeaderAt + 20, TextFileOffset); // PointerToRawData
        U32(span, SectionHeaderAt + 36, unchecked((int)0xE000_0060));

        Span<byte> text = span.Slice(TextFileOffset, TextSize);
        Code.CopyTo(text);
        U32(text, ListLengthAt, list.Length);
        U32(text, OutdatedLengthAt, outdated.Length);
        list.CopyTo(text[ListTextAt..]);
        outdated.CopyTo(text[OutdatedTextAt..]);
        for (int index = 0; index < Imports.Length; index++)
        {
            int hintName = HintNamesAt + 0x20 * index;
            Encoding.ASCII.GetBytes(Imports[index]).CopyTo(text[(hintName + 2)..]); // hint 0, then the NUL-terminated name
            U64(text, IatAt + 8 * index, TextRva + hintName);
            U64(text, LookupAt + 8 * index, TextRva + hintName);
        }
        U32(text, DescriptorsAt, TextRva + LookupAt);      // OriginalFirstThunk
        U32(text, DescriptorsAt + 12, TextRva + DllNameAt); // Name
        U32(text, DescriptorsAt + 16, TextRva + IatAt);     // FirstThunk; the second descriptor stays zero
        Encoding.ASCII.GetBytes(Dll).CopyTo(text[DllNameAt..]);
        return image;
    }

    private static byte[] Ascii(string value, int capacity)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length > capacity || value.Any(character => character is not ('\r' or '\n') && character is < ' ' or > '~'))
            throw new ArgumentException("The stand-in output must be short printable ASCII.", nameof(value));
        return Encoding.ASCII.GetBytes(value);
    }

    private static void U16(Span<byte> span, int offset, int value) => BinaryPrimitives.WriteUInt16LittleEndian(span[offset..], checked((ushort)value));

    private static void U32(Span<byte> span, int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(span[offset..], value);

    private static void U64(Span<byte> span, int offset, long value) => BinaryPrimitives.WriteInt64LittleEndian(span[offset..], value);
}
