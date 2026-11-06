using System.Buffers.Binary;
using System.Text;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// FR-UPD-15, TM-03/04/07/13; ADR-031 design §8.6 admission gate 2 (test only): a minimal Windows x64 console image that
/// stands in for the root engine of a client self-update fixture. It branches only on the verb after the quoted image path
/// (the launcher always quotes argv[0]): <c>list</c> writes the fixed <c>listOutput</c> (the client's own
/// <c>chocolatey|&lt;version&gt;</c> record for stage C); <c>upgrade</c> sleeps <c>preSleep</c> milliseconds (so the
/// launcher's release of every hold after resume has happened), then performs a fixed table of
/// <c>MoveFileExW(from, to, MOVEFILE_REPLACE_EXISTING)</c> operations whose absolute paths are baked into the image by the
/// fixture (exit 2 at the first failure), then sleeps <c>postSleep</c> milliseconds and exits 0; anything else exits 0.
/// It moves the running engine out of the root and prepared target files in, the shape of pinned
/// <c>chocolateysetup.psm1</c>'s rename-then-copy of <c>choco.exe</c>. It reads no environment variable, registry value or
/// file content and touches no path that is not in its table. It imports only <c>GetCommandLineW</c>, <c>GetStdHandle</c>,
/// <c>WriteFile</c>, <c>ExitProcess</c>, <c>MoveFileExW</c> and <c>Sleep</c> from <c>KERNEL32.dll</c>.
/// <para>
/// Layout: headers in the first 0x200 file bytes; one RWX <c>.text</c> section at RVA 0x1000 (file offset 0x200, 0x2000
/// bytes): code at +0x000; list length, move count, pre- and post-sleep at +0x180/+0x184/+0x188/+0x18C; the list output at
/// +0x190; the import address table at +0x200, the lookup table at +0x240, the import descriptors at +0x280, the DLL name at
/// +0x2B0 and the hint/name entries from +0x2C0 (0x20 apart); the move table (pairs of section offsets) at +0x380; the
/// UTF-16 path pool from +0x400. Every address is RIP-relative (no relocation). The code bytes were assembled with NASM 2.16
/// (<c>bits 64</c>, <c>default rel</c>, <c>org 0x1000</c>, each data label <c>equ start + offset</c>) from:
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
///     cmp edx, 'u'
///     je upgrade
/// silent:
///     xor ecx, ecx
///     call [IatExitProcess]
/// list:
///     mov ecx, -11                  ; STD_OUTPUT_HANDLE
///     call [IatGetStdHandle]
///     mov rcx, rax
///     lea rdx, [ListText]
///     mov r8d, [ListLength]
///     lea r9, [rsp + 0x28]
///     mov qword [rsp + 0x20], 0
///     call [IatWriteFile]
///     test eax, eax
///     jz failed
///     mov eax, [ListLength]
///     cmp eax, [rsp + 0x28]
///     jne failed
///     jmp silent
/// upgrade:
///     mov ecx, [PreSleepMs]
///     call [IatSleep]
///     lea rdi, [start]              ; runtime address of the section start
///     lea rbx, [OpTable]
///     mov esi, [OpCount]
/// next:
///     test esi, esi
///     jz moved
///     mov ecx, [rbx]
///     add rcx, rdi                  ; existing file name (section offset to address)
///     mov edx, [rbx + 4]
///     add rdx, rdi                  ; new file name
///     mov r8d, 1                    ; MOVEFILE_REPLACE_EXISTING
///     call [IatMoveFileExW]
///     test eax, eax
///     jz movefailed
///     add rbx, 8
///     dec esi
///     jmp next
/// moved:
///     mov ecx, [PostSleepMs]
///     call [IatSleep]
///     jmp silent
/// movefailed:
///     mov ecx, 2
///     call [IatExitProcess]
/// failed:
///     mov ecx, 1
///     call [IatExitProcess]
///     int3
/// </code>
/// </summary>
internal static class StandInClientUpgradeEngine
{
    internal const int TextRva = 0x1000;
    internal const int TextFileOffset = 0x200;
    internal const int TextSize = 0x2000;
    internal const int ListLengthAt = 0x180;
    internal const int MoveCountAt = 0x184;
    internal const int PreSleepAt = 0x188;
    internal const int PostSleepAt = 0x18C;
    internal const int ListTextAt = 0x190;
    internal const int IatAt = 0x200;
    internal const int LookupAt = 0x240;
    internal const int DescriptorsAt = 0x280;
    internal const int DllNameAt = 0x2B0;
    internal const int HintNamesAt = 0x2C0;
    internal const int MoveTableAt = 0x380;
    internal const int PoolAt = 0x400;
    internal const int MaximumMoves = (PoolAt - MoveTableAt) / 8;
    internal const string Dll = "KERNEL32.dll";

    /// <summary>The import order is the IAT order the code calls through (+0x200 … +0x228).</summary>
    internal static readonly string[] Imports = ["GetCommandLineW", "GetStdHandle", "WriteFile", "ExitProcess", "MoveFileExW", "Sleep"];

    private const int PeHeaderAt = 0x40;
    private const int OptionalHeaderAt = PeHeaderAt + 4 + 20;
    private const int OptionalHeaderSize = 0xF0;
    private const int SectionHeaderAt = OptionalHeaderAt + OptionalHeaderSize;

    internal static readonly byte[] Code =
    [
        0x48, 0x83, 0xEC, 0x38, 0xFF, 0x15, 0xF6, 0x01, 0x00, 0x00, 0x66, 0x83, 0x38, 0x22, 0x75, 0x28,
        0x48, 0x8D, 0x48, 0x02, 0x0F, 0xB7, 0x11, 0x85, 0xD2, 0x74, 0x1D, 0x48, 0x83, 0xC1, 0x02, 0x83,
        0xFA, 0x22, 0x75, 0xF0, 0x66, 0x83, 0x39, 0x20, 0x75, 0x0E, 0x0F, 0xB7, 0x51, 0x02, 0x83, 0xFA,
        0x6C, 0x74, 0x0D, 0x83, 0xFA, 0x75, 0x74, 0x4A, 0x31, 0xC9, 0xFF, 0x15, 0xD8, 0x01, 0x00, 0x00,
        0xB9, 0xF5, 0xFF, 0xFF, 0xFF, 0xFF, 0x15, 0xBD, 0x01, 0x00, 0x00, 0x48, 0x89, 0xC1, 0x48, 0x8D,
        0x15, 0x3B, 0x01, 0x00, 0x00, 0x44, 0x8B, 0x05, 0x24, 0x01, 0x00, 0x00, 0x4C, 0x8D, 0x4C, 0x24,
        0x28, 0x48, 0xC7, 0x44, 0x24, 0x20, 0x00, 0x00, 0x00, 0x00, 0xFF, 0x15, 0xA0, 0x01, 0x00, 0x00,
        0x85, 0xC0, 0x74, 0x71, 0x8B, 0x05, 0x06, 0x01, 0x00, 0x00, 0x3B, 0x44, 0x24, 0x28, 0x75, 0x65,
        0xEB, 0xB6, 0x8B, 0x0D, 0x00, 0x01, 0x00, 0x00, 0xFF, 0x15, 0x9A, 0x01, 0x00, 0x00, 0x48, 0x8D,
        0x3D, 0x6B, 0xFF, 0xFF, 0xFF, 0x48, 0x8D, 0x1D, 0xE4, 0x02, 0x00, 0x00, 0x8B, 0x35, 0xE2, 0x00,
        0x00, 0x00, 0x85, 0xF6, 0x74, 0x23, 0x8B, 0x0B, 0x48, 0x01, 0xF9, 0x8B, 0x53, 0x04, 0x48, 0x01,
        0xFA, 0x41, 0xB8, 0x01, 0x00, 0x00, 0x00, 0xFF, 0x15, 0x63, 0x01, 0x00, 0x00, 0x85, 0xC0, 0x74,
        0x19, 0x48, 0x83, 0xC3, 0x08, 0xFF, 0xCE, 0xEB, 0xD9, 0x8B, 0x0D, 0xBD, 0x00, 0x00, 0x00, 0xFF,
        0x15, 0x53, 0x01, 0x00, 0x00, 0xE9, 0x5E, 0xFF, 0xFF, 0xFF, 0xB9, 0x02, 0x00, 0x00, 0x00, 0xFF,
        0x15, 0x33, 0x01, 0x00, 0x00, 0xB9, 0x01, 0x00, 0x00, 0x00, 0xFF, 0x15, 0x28, 0x01, 0x00, 0x00,
        0xCC,
    ];

    /// <summary>
    /// The image bytes. <paramref name="listOutput"/> is printable ASCII (CR/LF allowed) that fits its slot; every move path is
    /// fully qualified, free of NUL and fits the pool; at most <see cref="MaximumMoves"/> moves; sleeps are non-negative.
    /// </summary>
    internal static byte[] Build(string listOutput, IReadOnlyList<(string From, string To)> moves, int preSleepMilliseconds, int postSleepMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(moves);
        byte[] list = Ascii(listOutput, IatAt - ListTextAt);
        if (moves.Count > MaximumMoves) throw new ArgumentException("Too many moves.", nameof(moves));
        ArgumentOutOfRangeException.ThrowIfNegative(preSleepMilliseconds);
        ArgumentOutOfRangeException.ThrowIfNegative(postSleepMilliseconds);
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
        U32(span, o + 56, TextRva + TextSize);         // SizeOfImage
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
        U32(text, MoveCountAt, moves.Count);
        U32(text, PreSleepAt, preSleepMilliseconds);
        U32(text, PostSleepAt, postSleepMilliseconds);
        list.CopyTo(text[ListTextAt..]);
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

        int pool = PoolAt;
        for (int index = 0; index < moves.Count; index++)
        {
            U32(text, MoveTableAt + 8 * index, pool);
            pool = Path16(text, pool, moves[index].From);
            U32(text, MoveTableAt + 8 * index + 4, pool);
            pool = Path16(text, pool, moves[index].To);
        }
        return image;
    }

    /// <summary>Writes a NUL-terminated UTF-16LE path at <paramref name="at"/>; returns the next (2-byte aligned) offset.</summary>
    private static int Path16(Span<byte> text, int at, string path)
    {
        if (string.IsNullOrEmpty(path) || !Path.IsPathFullyQualified(path) || path.Contains('\0', StringComparison.Ordinal))
            throw new ArgumentException("A move path must be fully qualified.", nameof(path));
        byte[] bytes = Encoding.Unicode.GetBytes(path + "\0");
        if (at + bytes.Length > text.Length) throw new ArgumentException("The move paths do not fit the stand-in's pool.", nameof(path));
        bytes.CopyTo(text[at..]);
        return at + bytes.Length;
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
