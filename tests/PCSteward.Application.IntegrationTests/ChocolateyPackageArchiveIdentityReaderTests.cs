using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using PCSteward.Provider.Chocolatey;

namespace PCSteward.Application.IntegrationTests;

/// <summary>FR-UPD-15/01; TM-07/13: bounded native-library archive identity, no extraction or execution.</summary>
[TestClass]
public sealed class ChocolateyPackageArchiveIdentityReaderTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void StoredOrDeflatedSingleRootNuspecProducesIdentity(bool compressed)
    {
        byte[] archive = Archive(compressed, "git.NUSPEC", Xml);
        Assert.IsTrue(ChocolateyPackageArchiveIdentityReader.TryRead(archive, out ChocolateyNuspecIdentity? identity));
        Assert.AreEqual("git", identity!.Id);
        Assert.AreEqual("1.2.0", identity.Version.Normalized);
    }

    [TestMethod]
    [DataRow("directory/git.nuspec")]
    [DataRow("git%2enuspec")]
    [DataRow("../git.nuspec")]
    [DataRow("/git.nuspec")]
    [DataRow("directory\\git.nuspec")]
    [DataRow("gít.nuspec")]
    public void AmbiguousEncodedOrNonRootNuspecRefuses(string path) => Refuses(Archive(true, path, Xml));

    [TestMethod]
    public void DuplicateRootIdentitiesAndCaseAliasedEntriesRefuse()
    {
        Refuses(Archive(true, "git.nuspec", Xml, "other.nuspec"));
        Refuses(Archive(true, "git.nuspec", Xml, "GIT.NUSPEC"));
    }

    [TestMethod]
    public void OpcPackageWithEscapedPayloadSpaceKeepsItsSingleRootIdentity()
    {
        using MemoryStream stream = new();
        using (ZipArchive zip = new(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (string path in new[] { "git.nuspec", "[Content_Types].xml", "_rels/.rels", "tools/a%20file.txt" })
            {
                using Stream data = zip.CreateEntry(path).Open();
                data.Write(Encoding.UTF8.GetBytes(path == "git.nuspec" ? Xml : "opaque payload"));
            }
        }
        Assert.IsTrue(ChocolateyPackageArchiveIdentityReader.TryRead(stream.ToArray(), out ChocolateyNuspecIdentity? identity));
        Assert.AreEqual("git", identity!.Id);
    }

    [TestMethod]
    public void DecodedPayloadAliasesRefuseEvenWhenRawEntryNamesDiffer()
    {
        byte[] archive = Archive(true, "git.nuspec", Xml, "tools/a%20file.txt");
        using MemoryStream buffer = new();
        buffer.Write(archive);
        using (ZipArchive zip = new(buffer, ZipArchiveMode.Update, leaveOpen: true)) zip.CreateEntry("TOOLS/a file.txt");
        Refuses(buffer.ToArray());
    }

    [TestMethod]
    [DataRow("tools/a%2fb.txt")]
    [DataRow("tools/a%5cb.txt")]
    [DataRow("tools/a%2enuspec")]
    [DataRow("tools/%2e%2e/a.txt")]
    [DataRow("tools/a%2520b.txt")]
    [DataRow("tools/a%00b.txt")]
    [DataRow("tools/a%GGb.txt")]
    public void EscapesCannotIntroduceManifestOrPathAmbiguity(string path) => Refuses(Archive(true, "git.nuspec", Xml, path));

    [TestMethod]
    [DataRow("eocd-count")]
    [DataRow("disk")]
    [DataRow("central-size")]
    [DataRow("central-offset")]
    [DataRow("central-signature")]
    [DataRow("method")]
    [DataRow("encryption")]
    [DataRow("local-name")]
    [DataRow("local-size")]
    [DataRow("local-offset")]
    [DataRow("crc")]
    [DataRow("declared-output")]
    public void ContradictoryOrUnsupportedZipStructureReturnsNoIdentity(string mutation)
    {
        byte[] archive = Archive(true, "git.nuspec", Xml);
        int end = archive.Length - 22;
        int central = checked((int)Read32(archive, end + 16));
        switch (mutation)
        {
            case "eocd-count": Write16(archive, end + 10, 4097); break;
            case "disk": Write16(archive, end + 4, 1); break;
            case "central-size": Write32(archive, end + 12, 1048577); break;
            case "central-offset": Write32(archive, end + 16, uint.MaxValue); break;
            case "central-signature": archive[central] = 0; break;
            case "method": Write16(archive, central + 10, 99); break;
            case "encryption": Write16(archive, central + 8, 1); break;
            case "local-name": archive[30] = (byte)'x'; break;
            case "local-size": Write32(archive, 18, uint.MaxValue); break;
            case "local-offset": Write32(archive, central + 42, uint.MaxValue); break;
            case "crc": archive[central + 16] ^= 1; archive[14] ^= 1; break;
            case "declared-output": Write32(archive, central + 24, 65537); break;
        }
        Refuses(archive);
    }

    [TestMethod]
    public void MalformedXmlActualOutputLimitTruncationAndOversizeArchiveRefuse()
    {
        Refuses(Archive(true, "git.nuspec", "<package/>"));
        byte[] bomb = Archive(true, "git.nuspec", Xml.PadRight(65537));
        int central = checked((int)Read32(bomb, bomb.Length - 6));
        Write32(bomb, central + 24, 100);
        Write32(bomb, 22, 100);
        Refuses(bomb);
        byte[] valid = Archive(true, "git.nuspec", Xml);
        Refuses(valid[..^1]);
        Refuses([]);
        Refuses(new byte[64 * 1024 * 1024 + 1]);
    }

    [TestMethod]
    public void NonseekableWriterDataDescriptorIsValidatedBeforeReadingIdentity()
    {
        using MemoryStream buffer = new();
        using (ZipArchive zip = new(new NonSeekingWriter(buffer), ZipArchiveMode.Create, leaveOpen: true))
        {
            using Stream data = zip.CreateEntry("git.nuspec", CompressionLevel.Optimal).Open();
            data.Write(Encoding.UTF8.GetBytes(Xml));
        }
        byte[] archive = buffer.ToArray();
        Assert.IsTrue(ChocolateyPackageArchiveIdentityReader.TryRead(archive, out _));
        int central = checked((int)Read32(archive, archive.Length - 6));
        archive[central - 5] ^= 1;
        Refuses(archive);
    }

    [TestMethod]
    public void EntryCountAndRootIdentityRemainBoundedAcrossOtherwiseValidArchives()
    {
        using MemoryStream stream = new();
        using (ZipArchive zip = new(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (Stream data = zip.CreateEntry("git.nuspec").Open()) data.Write(Encoding.UTF8.GetBytes(Xml));
            for (int index = 1; index < 4096; index++) zip.CreateEntry("tools/" + index);
        }
        Assert.IsTrue(ChocolateyPackageArchiveIdentityReader.TryRead(stream.ToArray(), out _));
        using (ZipArchive zip = new(stream, ZipArchiveMode.Update, leaveOpen: true)) zip.CreateEntry("tools/overflow");
        Refuses(stream.ToArray());
    }

    [TestMethod]
    public void MalformedDeflatePayloadAndForgedEndSignatureInsideCommentRefuse()
    {
        byte[] invalid = Archive(true, "git.nuspec", Xml);
        invalid[30 + "git.nuspec".Length] = 0x07; // Reserved DEFLATE block type.
        Refuses(invalid);

        byte[] valid = Archive(true, "git.nuspec", Xml);
        byte[] comment = new byte[24];
        Write32(comment, 0, 0x06054b50);
        Write16(valid, valid.Length - 2, (ushort)comment.Length);
        Refuses(valid.Concat(comment).ToArray());
    }

    [TestMethod]
    [DataRow("nonzero-first")]
    [DataRow("duplicate-offset")]
    [DataRow("overlap")]
    [DataRow("gap")]
    [DataRow("stored-length")]
    public void LocalLayoutAndStoredPayloadLengthsAreStructurallyConsistent(string mutation)
    {
        byte[] archive = Archive(false, "git.nuspec", Xml, "tools/a.txt");
        int central = checked((int)Read32(archive, archive.Length - 6));
        int second = central + 46 + Read16(archive, central + 28) + Read16(archive, central + 30) + Read16(archive, central + 32);
        switch (mutation)
        {
            case "nonzero-first": Write32(archive, central + 42, 1); break;
            case "duplicate-offset": Write32(archive, second + 42, 0); break;
            case "overlap": Write32(archive, second + 42, 1); break;
            case "gap": Write32(archive, second + 42, Read32(archive, second + 42) + 1); break;
            case "stored-length":
                int local = checked((int)Read32(archive, second + 42));
                Write32(archive, second + 24, Read32(archive, second + 24) + 1);
                Write32(archive, local + 22, Read32(archive, local + 22) + 1);
                break;
        }
        Refuses(archive);
    }

    [TestMethod]
    [DataRow(0x0001)]
    [DataRow(0x7075)]
    public void UnsupportedZip64OrUnicodePathExtraFieldsRefuse(int tag)
    {
        byte[] archive = Archive(true, "git.nuspec", Xml);
        int end = archive.Length - 22, central = checked((int)Read32(archive, end + 16));
        int insert = central + 46 + Read16(archive, central + 28);
        byte[] extra = new byte[4];
        Write16(extra, 0, (ushort)tag);
        byte[] changed = archive[..insert].Concat(extra).Concat(archive[insert..]).ToArray();
        Write16(changed, central + 30, 4);
        Write32(changed, end + 4 + 12, Read32(archive, end + 12) + 4);
        Refuses(changed);
    }

    [TestMethod]
    public void PrependedStubIsRefusedEvenWithConsistentAdjustedOffsets()
    {
        byte[] archive = Archive(true, "git.nuspec", Xml);
        int end = archive.Length - 22, central = checked((int)Read32(archive, end + 16));
        byte[] changed = new byte[archive.Length + 1];
        archive.CopyTo(changed, 1);
        Write32(changed, end + 1 + 16, (uint)central + 1);
        Write32(changed, central + 1 + 42, 1);
        Refuses(changed);
    }

    [TestMethod]
    public void UnsignedDataDescriptorRetainsExactHeaderAndPayloadAgreement()
    {
        using MemoryStream buffer = new();
        using (ZipArchive zip = new(new NonSeekingWriter(buffer), ZipArchiveMode.Create, leaveOpen: true))
        {
            using Stream data = zip.CreateEntry("git.nuspec").Open();
            data.Write(Encoding.UTF8.GetBytes(Xml));
        }
        byte[] archive = buffer.ToArray();
        int end = archive.Length - 22, central = checked((int)Read32(archive, end + 16));
        Assert.AreEqual(0x08074b50u, Read32(archive, central - 16));
        byte[] changed = archive[..(central - 16)].Concat(archive[(central - 12)..]).ToArray();
        Write32(changed, end - 4 + 16, (uint)central - 4);
        Assert.IsTrue(ChocolateyPackageArchiveIdentityReader.TryRead(changed, out _));
    }

    private const string Xml = "<package><metadata><id>git</id><version>1.2.0</version></metadata></package>";

    private static byte[] Archive(bool compressed, string path, string xml, string? secondPath = null)
    {
        using MemoryStream stream = new();
        using (ZipArchive zip = new(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (Stream entry = zip.CreateEntry(path, compressed ? CompressionLevel.Optimal : CompressionLevel.NoCompression).Open())
                entry.Write(Encoding.UTF8.GetBytes(xml));
            if (secondPath is not null)
            {
                using Stream entry = zip.CreateEntry(secondPath, compressed ? CompressionLevel.Optimal : CompressionLevel.NoCompression).Open();
                entry.Write(Encoding.UTF8.GetBytes(xml));
            }
        }
        return stream.ToArray();
    }

    private static void Refuses(byte[] archive)
    {
        Assert.IsFalse(ChocolateyPackageArchiveIdentityReader.TryRead(archive, out ChocolateyNuspecIdentity? identity));
        Assert.IsNull(identity);
    }
    private static uint Read32(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4));
    private static ushort Read16(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset, 2));
    private static void Write32(byte[] bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset, 4), value);
    private static void Write16(byte[] bytes, int offset, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset, 2), value);

    private sealed class NonSeekingWriter(Stream destination) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => destination.Flush();
        public override void Write(byte[] buffer, int offset, int count) => destination.Write(buffer, offset, count);
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
