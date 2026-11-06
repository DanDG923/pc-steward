using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace PCSteward.Provider.Chocolatey;

/// <summary>Bounded archive identity data only. No extraction, signature, registration or candidate authority.</summary>
internal static class ChocolateyPackageArchiveIdentityReader
{
    internal static bool TryRead(ReadOnlySpan<byte> input, out ChocolateyNuspecIdentity? identity)
    {
        identity = null;
        if (input.Length is < 22 or > 64 * 1024 * 1024) return false;
        byte[] bytes = input.ToArray();
        try
        {
            int end = FindEnd(bytes);
            if (end < 0 || U16(bytes, end + 4) != 0 || U16(bytes, end + 6) != 0) return false;
            int count = U16(bytes, end + 10);
            uint directorySize = U32(bytes, end + 12), directoryOffset = U32(bytes, end + 16);
            if (count is 0 or > 4096 || U16(bytes, end + 8) != count || directorySize > 1048576 ||
                (long)directoryOffset + directorySize != end) return false;
            int position = checked((int)directoryOffset);
            List<Entry> entries = new(count);
            HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
            Entry? nuspec = null;
            for (int index = 0; index < count; index++)
            {
                if (position > end - 46 || U32(bytes, position) != 0x02014b50 || U16(bytes, position + 6) > 20 ||
                    U16(bytes, position + 34) != 0) return false;
                ushort flags = U16(bytes, position + 8), method = U16(bytes, position + 10);
                uint crc = U32(bytes, position + 16), compressed = U32(bytes, position + 20), length = U32(bytes, position + 24);
                int nameLength = U16(bytes, position + 28), extraLength = U16(bytes, position + 30), commentLength = U16(bytes, position + 32);
                int next = checked(position + 46 + nameLength + extraLength + commentLength);
                if ((flags & ~0x0808) != 0 || method is not (0 or 8) || next > end ||
                    compressed == uint.MaxValue || length == uint.MaxValue ||
                    (method == 0 && compressed != length) ||
                    !TryName(bytes.AsSpan(position + 46, nameLength), out string name) ||
                    !ValidExtra(bytes.AsSpan(position + 46 + nameLength, extraLength))) return false;
                if (!names.Add(name)) return false;
                Entry entry = new(U32(bytes, position + 42), flags, method, crc, compressed, length,
                    bytes.AsSpan(position + 46, nameLength).ToArray());
                entries.Add(entry);
                if (!name.Contains('/') && name.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase))
                {
                    if (nuspec is not null || length is 0 or > 65536) return false;
                    nuspec = entry;
                }
                position = next;
            }
            if (position != end || nuspec is null) return false;
            entries.Sort((left, right) => left.Offset.CompareTo(right.Offset));
            if (entries[0].Offset != 0) return false;
            for (int index = 0; index < entries.Count; index++)
            {
                uint boundary = index + 1 < entries.Count ? entries[index + 1].Offset : directoryOffset;
                if (!ValidateLocal(bytes, entries[index], boundary)) return false;
            }

            using MemoryStream compressedData = new(bytes, nuspec.DataOffset, checked((int)nuspec.Compressed), writable: false);
            using Stream data = nuspec.Method == 0 ? compressedData : new DeflateStream(compressedData, CompressionMode.Decompress, leaveOpen: true);
            byte[] content = new byte[65537];
            int actual = 0;
            while (actual < content.Length)
            {
                int read = data.Read(content, actual, content.Length - actual);
                if (read == 0) break;
                actual += read;
            }
            if (actual > 65536 || actual != nuspec.Length || Crc32(content.AsSpan(0, actual)) != nuspec.Crc) return false;
            return ChocolateyNuspecIdentityReader.TryRead(content.AsSpan(0, actual), out identity);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidDataException or IOException or OverflowException)
        {
            identity = null;
            return false;
        }
    }

    private static int FindEnd(byte[] bytes)
    {
        int minimum = Math.Max(0, bytes.Length - 22 - ushort.MaxValue);
        for (int position = bytes.Length - 22; position >= minimum; position--)
            if (U32(bytes, position) == 0x06054b50)
                return position + 22 + U16(bytes, position + 20) == bytes.Length ? position : -1;
        return -1;
    }

    private static bool ValidateLocal(byte[] bytes, Entry entry, uint boundary)
    {
        if (boundary > bytes.Length || entry.Offset > boundary || (long)entry.Offset + 30 > boundary) return false;
        int start = checked((int)entry.Offset);
        if (U32(bytes, start) != 0x04034b50 || U16(bytes, start + 4) > 20 ||
            U16(bytes, start + 6) != entry.Flags || U16(bytes, start + 8) != entry.Method) return false;
        int nameLength = U16(bytes, start + 26), extraLength = U16(bytes, start + 28);
        long payload = (long)start + 30 + nameLength + extraLength;
        if (payload + entry.Compressed > boundary || nameLength != entry.Name.Length ||
            !bytes.AsSpan(start + 30, nameLength).SequenceEqual(entry.Name) ||
            !ValidExtra(bytes.AsSpan(start + 30 + nameLength, extraLength))) return false;
        entry.DataOffset = checked((int)payload);
        uint crc = U32(bytes, start + 14), compressed = U32(bytes, start + 18), length = U32(bytes, start + 22);
        if ((entry.Flags & 8) == 0)
            return crc == entry.Crc && compressed == entry.Compressed && length == entry.Length && payload + compressed == boundary;
        if ((crc != 0 && crc != entry.Crc) || (compressed != 0 && compressed != entry.Compressed) ||
            (length != 0 && length != entry.Length)) return false;
        int descriptor = checked(entry.DataOffset + (int)entry.Compressed);
        long descriptorLength = (long)boundary - descriptor;
        if (descriptorLength == 16)
        {
            if (U32(bytes, descriptor) != 0x08074b50) return false;
            descriptor += 4;
        }
        else if (descriptorLength != 12) return false;
        return U32(bytes, descriptor) == entry.Crc && U32(bytes, descriptor + 4) == entry.Compressed &&
            U32(bytes, descriptor + 8) == entry.Length;
    }

    private static bool TryName(ReadOnlySpan<byte> bytes, out string name)
    {
        name = string.Empty;
        if (bytes.Length is 0 or > 4096) return false;
        foreach (byte value in bytes)
            if (value is < 0x20 or > 0x7e or (byte)'\\' or (byte)':' or (byte)'"') return false;
        string raw = Encoding.ASCII.GetString(bytes);
        string decoded = raw;
        if (raw.Contains('%'))
        {
            // The pinned native reader unescapes once. Only nested, non-manifest ASCII payload
            // escapes are supported here; raw bytes still bind the local/central headers.
            if (!raw.Contains('/')) return false;
            StringBuilder builder = new(raw.Length);
            for (int index = 0; index < raw.Length; index++)
            {
                char character = raw[index];
                if (character == '%')
                {
                    if (index + 2 >= raw.Length || !IsHex(raw[index + 1]) || !IsHex(raw[index + 2])) return false;
                    character = (char)((Hex(raw[index + 1]) << 4) | Hex(raw[index + 2]));
                    if (character is < ' ' or > '~' or '/' or '\\' or '%' or ':' or '"') return false;
                    index += 2;
                }
                builder.Append(character);
            }
            decoded = builder.ToString();
            if (decoded.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase)) return false;
        }
        string[] segments = decoded.Split('/');
        for (int index = 0; index < segments.Length; index++)
        {
            string part = segments[index];
            if (part.Length == 0 && index == segments.Length - 1 && index > 0) continue;
            if (part.Length == 0 || part is "." or ".." || part.EndsWith('.') || part.EndsWith(' ')) return false;
        }
        name = decoded;
        return true;
    }

    private static bool IsHex(char value) => value is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
    private static int Hex(char value) => value <= '9' ? value - '0' : char.ToUpperInvariant(value) - 'A' + 10;

    private static bool ValidExtra(ReadOnlySpan<byte> bytes)
    {
        int offset = 0;
        while (offset < bytes.Length)
        {
            if (bytes.Length - offset < 4) return false;
            ushort tag = BinaryPrimitives.ReadUInt16LittleEndian(bytes[offset..]);
            int length = BinaryPrimitives.ReadUInt16LittleEndian(bytes[(offset + 2)..]);
            if (tag is 0x0001 or 0x7075 || length > bytes.Length - offset - 4) return false;
            offset += 4 + length;
        }
        return true;
    }

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        uint value = uint.MaxValue;
        foreach (byte item in data)
        {
            value ^= item;
            for (int bit = 0; bit < 8; bit++) value = (value >> 1) ^ ((value & 1) == 0 ? 0 : 0xedb88320);
        }
        return ~value;
    }

    private static ushort U16(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset, 2));
    private static uint U32(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4));

    private sealed class Entry(uint offset, ushort flags, ushort method, uint crc, uint compressed, uint length, byte[] name)
    {
        internal uint Offset { get; } = offset;
        internal ushort Flags { get; } = flags;
        internal ushort Method { get; } = method;
        internal uint Crc { get; } = crc;
        internal uint Compressed { get; } = compressed;
        internal uint Length { get; } = length;
        internal byte[] Name { get; } = name;
        internal int DataOffset { get; set; }
    }
}
