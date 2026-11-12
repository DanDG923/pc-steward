using System.Buffers.Binary;
using System.Security.Cryptography;

namespace PCSteward.Execution;

/// <summary>
/// FR-UPD-15, TM-04/13; ADR-034 security review H1 and ADR-032 revision 2 §6: is a signed PC Steward executable the CI
/// build's unsigned executable with exactly one Authenticode signature appended? This is the C# form of
/// <c>PayloadIntegrity.AssertSignedImage</c> in <c>scripts/OwnerOnlyPayloadIntegrity.ps1</c>, which the first install used:
/// <list type="number">
/// <item>the image is a standard PE32+ x64 image whose headers and sections end exactly at the original length;</item>
/// <item>the certificate table starts there, holds one WIN_CERT_TYPE_PKCS_SIGNED_DATA revision 2 record padded with zeros to
/// eight bytes, and ends the file;</item>
/// <item>putting back the original PE checksum and clearing the certificate-table directory entry gives the original bytes,
/// whose SHA-256 must equal the CI hash.</item>
/// </list>
/// Whether the signature itself is valid and pinned is <see cref="AuthenticodeIdentity"/>'s job; this only proves that
/// signing added nothing else. Pure: it works on bytes the caller has already read through a held handle.
/// </summary>
internal static class ReleaseImageComparison
{
    internal const int MaximumImageBytes = 16 * 1024 * 1024;

    internal static bool MatchesUnsignedOriginal(ReadOnlySpan<byte> signed, long originalBytes, string originalSha256, uint originalChecksum)
    {
        ArgumentNullException.ThrowIfNull(originalSha256);
        if (originalBytes is < 512 or > MaximumImageBytes || signed.Length > MaximumImageBytes || !DeploymentCapabilityPolicy.IsHex(originalSha256, 64)) return false;
        if (!TryParse(signed, out Layout layout)) return false;
        if (originalBytes % layout.FileAlignment != 0 || layout.ImageEnd != originalBytes || layout.CertificateOffset != originalBytes ||
            layout.CertificateSize is < 8 or > 65536 || (long)layout.CertificateOffset + layout.CertificateSize != signed.Length)
            return false;
        int offset = checked((int)layout.CertificateOffset);
        uint recordLength = BinaryPrimitives.ReadUInt32LittleEndian(signed[offset..]);
        if (recordLength < 8 || recordLength > layout.CertificateSize || ((recordLength + 7L) & ~7L) != layout.CertificateSize ||
            BinaryPrimitives.ReadUInt16LittleEndian(signed[(offset + 4)..]) != 0x200 || BinaryPrimitives.ReadUInt16LittleEndian(signed[(offset + 6)..]) != 2)
            return false;
        if (signed[(offset + (int)recordLength)..].ContainsAnyExcept((byte)0)) return false;

        byte[] original = signed[..(int)originalBytes].ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(original.AsSpan(layout.ChecksumOffset), originalChecksum);
        original.AsSpan(layout.SecurityOffset, 8).Clear();
        return CryptographicOperations.FixedTimeEquals(SHA256.HashData(original), Convert.FromHexString(originalSha256));
    }

    private readonly record struct Layout(int ChecksumOffset, int SecurityOffset, long ImageEnd, uint CertificateOffset, uint CertificateSize, uint FileAlignment);

    private static bool TryParse(ReadOnlySpan<byte> data, out Layout layout)
    {
        layout = default;
        if (data.Length < 512 || BinaryPrimitives.ReadUInt16LittleEndian(data) != 0x5A4D) return false;
        long header = BinaryPrimitives.ReadUInt32LittleEndian(data[60..]);
        long optional = header + 24;
        if (optional + 240 > data.Length || BinaryPrimitives.ReadUInt32LittleEndian(data[(int)header..]) != 0x4550 ||
            BinaryPrimitives.ReadUInt16LittleEndian(data[(int)(header + 4)..]) != 0x8664 || BinaryPrimitives.ReadUInt16LittleEndian(data[(int)(header + 20)..]) != 240 ||
            BinaryPrimitives.ReadUInt16LittleEndian(data[(int)optional..]) != 0x20B || BinaryPrimitives.ReadUInt32LittleEndian(data[(int)(optional + 108)..]) < 5)
            return false;
        int count = BinaryPrimitives.ReadUInt16LittleEndian(data[(int)(header + 6)..]);
        if (count is 0 or > 96) return false;
        uint alignment = BinaryPrimitives.ReadUInt32LittleEndian(data[(int)(optional + 36)..]);
        if (alignment is < 512 or > 65536 || (alignment & (alignment - 1)) != 0) return false;
        long end = BinaryPrimitives.ReadUInt32LittleEndian(data[(int)(optional + 60)..]);
        long sectionTable = optional + 240;
        if (end < sectionTable + count * 40L || end > data.Length || end % alignment != 0) return false;
        List<(long Start, long Stop)> sections = [];
        for (int index = 0; index < count; index++)
        {
            int section = (int)(sectionTable + index * 40L);
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(data[(section + 16)..]), start = BinaryPrimitives.ReadUInt32LittleEndian(data[(section + 20)..]);
            if (size == 0) continue;
            long stop = (long)start + size;
            if (start < end || stop > data.Length || start % alignment != 0 || size % alignment != 0) return false;
            sections.Add((start, stop));
        }
        sections.Sort(static (left, right) => left.Start.CompareTo(right.Start));
        long last = end;
        foreach ((long start, long stop) in sections)
        {
            if (start < last) return false;
            last = stop;
        }
        int security = (int)(optional + 144);
        layout = new((int)(optional + 64), security, last, BinaryPrimitives.ReadUInt32LittleEndian(data[security..]),
            BinaryPrimitives.ReadUInt32LittleEndian(data[(security + 4)..]), alignment);
        return true;
    }
}
