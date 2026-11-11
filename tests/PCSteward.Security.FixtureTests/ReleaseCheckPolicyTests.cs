using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using PCSteward.Execution;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// FR-UPD-04/15, TM-03/04/13; ADR-032 revision 2 §3 and §6 and "Follow-up controls and tests": the release check record and
/// its artefact zip are parsed strictly (malformed, oversized, unknown-key, duplicate-key, wrong-repository, bad-path and
/// version-mismatch records are refused), and a signed executable matches its CI original only when signing added exactly
/// one padded certificate record. Pure: these run on every platform.
/// </summary>
[TestClass]
public sealed class ReleaseCheckPolicyTests
{
    [TestMethod]
    public void TheRecordTheWorkflowWritesIsAccepted()
    {
        Assert.IsTrue(ReleaseCheckPolicy.TryRead(ReleaseCheckFixture.Record(), out ReleaseCheckRecord? record));
        Assert.AreEqual(4242, record!.RunId);
        Assert.AreEqual("0.1.1", record.MsiVersion);
        Assert.AreEqual("0.1.1-private-beta", record.ProductVersion);
        Assert.AreEqual(ReleaseCheckFixture.Commit, record.SourceCommit);
        Assert.HasCount(4, record.Files);
        Assert.HasCount(2, record.Executables);
        Assert.IsTrue(ReleaseCheckPolicy.NamesPins(record, ReleaseCheckFixture.Pins));
        Assert.IsFalse(ReleaseCheckPolicy.NamesPins(record, [ReleaseCheckFixture.Pins[1], ReleaseCheckFixture.Pins[0]]));
        Assert.IsFalse(ReleaseCheckPolicy.NamesPins(record, [ReleaseCheckFixture.Pins[0]]));
    }

    [TestMethod]
    public void MalformedRecordsAreRefused()
    {
        string valid = Encoding.ASCII.GetString(ReleaseCheckFixture.Record());
        foreach ((string from, string to) in new[]
        {
            ("\"schema\":\"pc-steward-release-check:1\"", "\"schema\":\"pc-steward-release-check:2\""),
            ("\"repository\":\"DanDG923/pc-steward\"", "\"repository\":\"someone/pc-steward\""),
            ("\"runId\":4242", "\"runId\":0"),
            ("\"runId\":4242", "\"runId\":\"4242\""),
            ("\"sourceCommit\":\"" + ReleaseCheckFixture.Commit + "\"", "\"sourceCommit\":\"" + ReleaseCheckFixture.Commit.ToUpperInvariant() + "\""),
            ("\"msiVersion\":\"0.1.1\"", "\"msiVersion\":\"0.1.2\""),
            ("\"msiVersion\":\"0.1.1\"", "\"msiVersion\":\"0.1.1-private-beta\""),
            ("\"productVersion\":\"0.1.1-private-beta\"", "\"productVersion\":\"0.1.1-Private\""),
            ("\"primary\":\"" + ReleaseCheckFixture.Pins[0] + "\"", "\"primary\":\"" + ReleaseCheckFixture.Pins[0].ToLowerInvariant() + "\""),
            ("\"backup\":\"" + ReleaseCheckFixture.Pins[1] + "\"", "\"backup\":\"" + ReleaseCheckFixture.Pins[0] + "\""),
            ("\"msiTablesSha256\":", "\"msiTablesSha256\":\"\",\"x\":"),
            ("\"files\":[", "\"files\":[{\"path\":\"..\\\\evil.dll\",\"bytes\":1,\"sha256\":\"" + new string('A', 64) + "\"},"),
            ("\"files\":[", "\"files\":[{\"path\":\"C:\\\\evil.dll\",\"bytes\":1,\"sha256\":\"" + new string('A', 64) + "\"},"),
            ("\"files\":[", "\"files\":[{\"path\":\"app.xbf\",\"bytes\":1,\"sha256\":\"" + new string('A', 64) + "\"},"),
            ("\"files\":[", "\"files\":[{\"path\":\"PCSteward.ReleaseManifest.json\",\"bytes\":1,\"sha256\":\"" + new string('A', 64) + "\"},"),
            ("\"files\":[", "\"files\":[{\"path\":\"x.dll\",\"bytes\":-1,\"sha256\":\"" + new string('A', 64) + "\"},"),
            ("\"files\":[", "\"files\":[{\"path\":\"x.dll\",\"bytes\":1,\"sha256\":\"" + new string('A', 64) + "\",\"extra\":1},"),
            ("{\"path\":\"PCSteward.ElevatedHelper.exe\",\"originalChecksum\"", "{\"path\":\"PCSteward.Desktop.exe\",\"originalChecksum\""),
            ("\"originalChecksum\":4660", "\"originalChecksum\":-1"),
            ("\"schema\"", "\"SCHEMA\":\"x\",\"schema\""),
        })
        {
            Assert.Contains(from, valid, from);
            Assert.IsFalse(ReleaseCheckPolicy.TryRead(Encoding.ASCII.GetBytes(valid.Replace(from, to, StringComparison.Ordinal)), out _), to);
        }
        Assert.IsFalse(ReleaseCheckPolicy.TryRead([0xEF, 0xBB, 0xBF, .. ReleaseCheckFixture.Record()], out _), "BOM");
        Assert.IsFalse(ReleaseCheckPolicy.TryRead("[]"u8, out _));
        Assert.IsFalse(ReleaseCheckPolicy.TryRead("{"u8, out _));
        Assert.IsFalse(ReleaseCheckPolicy.TryRead([], out _));
        Assert.IsFalse(ReleaseCheckPolicy.TryRead(new byte[ReleaseCheckPolicy.MaximumRecordBytes + 1], out _));
    }

    [TestMethod]
    public void TheArtefactZipMustHoldExactlyTheRecord()
    {
        byte[] record = ReleaseCheckFixture.Record();
        Assert.IsTrue(ReleaseCheckPolicy.TryReadZip(ReleaseCheckFixture.Zip(record), out byte[]? read));
        CollectionAssert.AreEqual(record, read);

        Assert.IsFalse(ReleaseCheckPolicy.TryReadZip(ReleaseCheckFixture.Zip(record, "other.json"), out _));
        Assert.IsFalse(ReleaseCheckPolicy.TryReadZip(ReleaseCheckFixture.Zip(record, "dir/release-check.json"), out _));
        Assert.IsFalse(ReleaseCheckPolicy.TryReadZip(ReleaseCheckFixture.Zip(record, ReleaseCheckPolicy.EntryName, ("extra.txt", "x"u8.ToArray())), out _));
        Assert.IsFalse(ReleaseCheckPolicy.TryReadZip(ReleaseCheckFixture.Zip(new byte[ReleaseCheckPolicy.MaximumRecordBytes + 1]), out _), "inflates past the bound");
        Assert.IsFalse(ReleaseCheckPolicy.TryReadZip("not a zip"u8, out _));
        Assert.IsFalse(ReleaseCheckPolicy.TryReadZip([], out _));
    }

    [TestMethod]
    public void ASignedExecutableMatchesItsUnsignedOriginalOnlyWithOneCertificateRecordAdded()
    {
        byte[] original = ReleaseCheckFixture.Pe(checksum: 0x1234);
        string sha = Convert.ToHexString(SHA256.HashData(original));
        byte[] signed = ReleaseCheckFixture.Sign(original, recordLength: 1001, newChecksum: 0xBEEF);
        Assert.IsTrue(ReleaseImageComparison.MatchesUnsignedOriginal(signed, original.Length, sha, 0x1234));

        Assert.IsFalse(ReleaseImageComparison.MatchesUnsignedOriginal(signed, original.Length, sha, 0x1235), "other checksum");
        Assert.IsFalse(ReleaseImageComparison.MatchesUnsignedOriginal(signed, original.Length, new string('0', 64), 0x1234), "other original");
        Assert.IsFalse(ReleaseImageComparison.MatchesUnsignedOriginal(original, original.Length, sha, 0x1234), "unsigned");

        byte[] changed = (byte[])signed.Clone();
        changed[0x300] ^= 1;
        Assert.IsFalse(ReleaseImageComparison.MatchesUnsignedOriginal(changed, original.Length, sha, 0x1234), "changed code");

        byte[] padding = (byte[])signed.Clone();
        padding[^1] = 1;
        Assert.IsFalse(ReleaseImageComparison.MatchesUnsignedOriginal(padding, original.Length, sha, 0x1234), "non-zero padding");

        byte[] overlay = [.. signed, .. new byte[8]];
        Assert.IsFalse(ReleaseImageComparison.MatchesUnsignedOriginal(overlay, original.Length, sha, 0x1234), "data after the certificate table");

        byte[] otherType = (byte[])signed.Clone();
        BinaryPrimitives.WriteUInt16LittleEndian(otherType.AsSpan(original.Length + 6), 1);
        Assert.IsFalse(ReleaseImageComparison.MatchesUnsignedOriginal(otherType, original.Length, sha, 0x1234), "not PKCS signed data");
    }
}

/// <summary>Builds release check records, artefact zips and minimal PE32+ images for the update tests.</summary>
internal static class ReleaseCheckFixture
{
    internal const string Commit = "0123456789abcdef0123456789abcdef01234567";
    internal static readonly string[] Pins = [new string('A', 64), new string('B', 64)];

    internal static byte[] Record(string msiVersion = "0.1.1", long runId = 4242, string commit = Commit, string[]? pins = null, string tables = "")
    {
        pins ??= Pins;
        if (tables.Length == 0) tables = new string('C', 64);
        string file(string path, int bytes, char hash) => "{\"path\":\"" + path.Replace("\\", "\\\\", StringComparison.Ordinal) + "\",\"bytes\":" + bytes + ",\"sha256\":\"" + new string(hash, 64) + "\"}";
        return Encoding.ASCII.GetBytes(
            "{\"schema\":\"pc-steward-release-check:1\",\"repository\":\"DanDG923/pc-steward\",\"runId\":" + runId +
            ",\"sourceCommit\":\"" + commit + "\",\"productVersion\":\"" + msiVersion + "-private-beta\",\"msiVersion\":\"" + msiVersion +
            "\",\"pins\":{\"primary\":\"" + pins[0] + "\",\"backup\":\"" + pins[1] + "\"},\"candidateManifestSha256\":\"" + new string('D', 64) +
            "\",\"msiTablesSha256\":\"" + tables + "\",\"files\":[" +
            string.Join(",", file("App.xbf", 1, '1'), file("PCSteward.Desktop.exe", 1024, '2'), file("PCSteward.ElevatedHelper.exe", 1024, '3'), file("Resources\\A.xbf", 2, '4')) +
            "],\"executables\":[{\"path\":\"PCSteward.Desktop.exe\",\"originalChecksum\":4660},{\"path\":\"PCSteward.ElevatedHelper.exe\",\"originalChecksum\":48879}]}");
    }

    internal static byte[] Zip(byte[] record, string name = ReleaseCheckPolicy.EntryName, params (string Name, byte[] Bytes)[] extra)
    {
        using MemoryStream stream = new();
        using (ZipArchive archive = new(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string entryName, byte[] bytes) in new[] { (name, record) }.Concat(extra))
            {
                using Stream entry = archive.CreateEntry(entryName, CompressionLevel.Optimal).Open();
                entry.Write(bytes);
            }
        }
        return stream.ToArray();
    }

    /// <summary>A minimal unsigned PE32+ x64 image: headers to 0x200, one 0x200-byte section.</summary>
    internal static byte[] Pe(uint checksum)
    {
        byte[] data = new byte[0x400];
        data[0] = (byte)'M'; data[1] = (byte)'Z';
        const int header = 0x80, optional = header + 24;
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x3C), header);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(header), 0x4550);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(header + 4), 0x8664);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(header + 6), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(header + 20), 240);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(optional), 0x20B);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(optional + 36), 0x200);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(optional + 60), 0x200);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(optional + 64), checksum);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(optional + 108), 16);
        int section = optional + 240;
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(section + 16), 0x200);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(section + 20), 0x200);
        for (int index = 0x200; index < data.Length; index++) data[index] = (byte)(index * 7);
        return data;
    }

    /// <summary>What Authenticode signing does to the image: a new checksum, a certificate-table entry and one padded record.</summary>
    internal static byte[] Sign(byte[] original, int recordLength, uint newChecksum)
    {
        int padded = (recordLength + 7) & ~7;
        byte[] signed = [.. original, .. new byte[padded]];
        const int optional = 0x80 + 24;
        BinaryPrimitives.WriteUInt32LittleEndian(signed.AsSpan(optional + 64), newChecksum);
        BinaryPrimitives.WriteUInt32LittleEndian(signed.AsSpan(optional + 144), (uint)original.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(signed.AsSpan(optional + 148), (uint)padded);
        BinaryPrimitives.WriteUInt32LittleEndian(signed.AsSpan(original.Length), (uint)recordLength);
        BinaryPrimitives.WriteUInt16LittleEndian(signed.AsSpan(original.Length + 4), 0x200);
        BinaryPrimitives.WriteUInt16LittleEndian(signed.AsSpan(original.Length + 6), 2);
        for (int index = original.Length + 8; index < original.Length + recordLength; index++) signed[index] = 0x30;
        return signed;
    }
}
