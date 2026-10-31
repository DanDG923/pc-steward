using System.Text;
using PCSteward.Provider.Chocolatey;

namespace PCSteward.Application.IntegrationTests;

/// <summary>FR-UPD-15/01; TM-07/13: exact comparison bytes, no native evidence or authority.</summary>
[TestClass]
public sealed class ChocolateyCommonProfileFingerprintTests
{
    private const string AbcHash = "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD";

    [TestMethod]
    public void IndependentPowerShellGoldenPinsScalarOrderingAndFileIdByteOrder()
    {
        Assert.AreEqual("0123456789ABCDEF:000102030405060708090A0B0C0D0E0F",
            ChocolateyCommonProfileFingerprint.FormatFileIdentity(0x0123456789ABCDEF, Enumerable.Range(0, 16).Select(value => (byte)value).ToArray()));
        Assert.AreEqual("3E8129A28F774E7B906A7A9011B90F70C994E289077973F2EE046C9A833E9823",
            ChocolateyCommonProfileFingerprint.ComputeCodeList(Data().CodeFiles));
        Assert.AreEqual("C179F864C37EA8029917E62CB60DACE19533E5E4DA9E20A4656EC729B580EE77",
            ChocolateyCommonProfileFingerprint.Compute(Data()));
    }

    [TestMethod]
    public void EquivalentCodePathCaseSeparatorsAndEnumerationOrderHaveOneEncoding()
    {
        ChocolateyCodeFileData first = Data().CodeFiles[0];
        ChocolateyCodeFileData second = first with { RelativePath = "tools/b.exe", Identity = Identity(5) };
        ChocolateyCodeFileData[] a = [first, second];
        ChocolateyCodeFileData[] b = [second with { RelativePath = "TOOLS\\B.EXE" }, first with { RelativePath = "HELPERS\\A.PS1" }];
        Assert.AreEqual(ChocolateyCommonProfileFingerprint.ComputeCodeList(a), ChocolateyCommonProfileFingerprint.ComputeCodeList(b));
        Assert.ThrowsExactly<ArgumentException>(() => ChocolateyCommonProfileFingerprint.ComputeCodeList([first, first with { RelativePath = "HELPERS\\a.ps1" }]));
    }

    [TestMethod]
    public void SemanticDefaultsDoNotBindXmlRepresentationOrRawCredentialValues()
    {
        ChocolateyConfigurationObservation a = Config("<features><feature name='checksumFiles' enabled='false' setExplicitly='false'/></features>");
        ChocolateyConfigurationObservation b = Config("");
        Assert.AreNotEqual(a.Fingerprint, b.Fingerprint);
        Assert.AreEqual(ChocolateyCommonProfileFingerprint.Compute(Data() with { Configuration = a }),
            ChocolateyCommonProfileFingerprint.Compute(Data() with { Configuration = b }));
    }

    [TestMethod]
    [DataRow("sid")]
    [DataRow("root")]
    [DataRow("cache")]
    [DataRow("code")]
    [DataRow("archive")]
    [DataRow("archive-hash")]
    [DataRow("installed")]
    [DataRow("store")]
    [DataRow("feature")]
    [DataRow("source")]
    public void RelevantObservationChangesCannotKeepTheBaselineDigest(string field)
    {
        ChocolateyCommonProfileData baseline = Data();
        ChocolateyCommonProfileData changed = field switch
        {
            "sid" => baseline with { RequestingUserSid = "S-1-5-21-11-22-33-1002" },
            "root" => baseline with { RootIdentity = Identity(6) },
            "cache" => baseline with { CacheIdentity = Identity(6) },
            "code" => baseline with { CodeFiles = [baseline.CodeFiles[0] with { Length = 4 }] },
            "archive" => baseline with { ArchiveIdentity = Identity(6) },
            "archive-hash" => baseline with { ArchiveSha256 = new string('C', 64) },
            "installed" => baseline with { InstalledVersion = Version("1.0.1") },
            "store" => baseline with { StoreIdentity = Identity(6) },
            "feature" => baseline with { Configuration = Config("<features><feature name='alwaysIncludeHeaders' enabled='true' setExplicitly='true'/></features>") },
            "source" => baseline with { Configuration = Config("<sources><source id='default' value='https://community.chocolatey.org/api/v2/' certificate='configured-cert'/></sources>") },
            _ => throw new InvalidOperationException()
        };
        Assert.AreNotEqual(ChocolateyCommonProfileFingerprint.Compute(baseline), ChocolateyCommonProfileFingerprint.Compute(changed));
    }

    [TestMethod]
    [DataRow("../a")]
    [DataRow("C:/a")]
    [DataRow("/a")]
    [DataRow("a//b")]
    [DataRow("a./b")]
    [DataRow("a/b ")]
    [DataRow("a/é.ps1")]
    public void UnsupportedCodePathCannotEnterTheCanonicalList(string path) =>
        Assert.ThrowsExactly<ArgumentException>(() => ChocolateyCommonProfileFingerprint.ComputeCodeList([Data().CodeFiles[0] with { RelativePath = path }]));

    [TestMethod]
    public void MalformedIdentityHashLengthAndSourceHaveNoFingerprint()
    {
        Assert.ThrowsExactly<ArgumentException>(() => ChocolateyCommonProfileFingerprint.FormatFileIdentity(1, new byte[15]));
        Assert.ThrowsExactly<ArgumentException>(() => ChocolateyCommonProfileFingerprint.Compute(Data() with { RootIdentity = "1:03" }));
        Assert.ThrowsExactly<ArgumentException>(() => ChocolateyCommonProfileFingerprint.Compute(Data() with { RequestingUserSid = "S-1-5-01" }));
        Assert.ThrowsExactly<ArgumentException>(() => ChocolateyCommonProfileFingerprint.Compute(Data() with { ArchiveSha256 = "not-a-hash" }));
        Assert.ThrowsExactly<ArgumentException>(() => ChocolateyCommonProfileFingerprint.ComputeCodeList([Data().CodeFiles[0] with { Length = -1 }]));
        Assert.ThrowsExactly<ArgumentException>(() => ChocolateyCommonProfileFingerprint.ComputeCodeList([Data().CodeFiles[0] with { Length = 64 * 1024 * 1024 + 1 }]));
        Assert.ThrowsExactly<ArgumentException>(() => ChocolateyCommonProfileFingerprint.Compute(Data() with
        {
            Configuration = Config("<sources><source id='https://community.chocolatey.org/api/v2/' value='https://example.invalid'/></sources>")
        }));
    }

    [TestMethod]
    public void BoundedCodeListIsCopiedByIndexWithoutEnumeratingAnUnboundedSequence()
    {
        Assert.AreEqual(ChocolateyCommonProfileFingerprint.ComputeCodeList(Data().CodeFiles),
            ChocolateyCommonProfileFingerprint.ComputeCodeList(new IndexOnlyCodeList(Data().CodeFiles[0])));
    }

    private static ChocolateyCommonProfileData Data() => new("S-1-5-21-11-22-33-1001", Identity(1),
        [new("helpers/a.ps1", Identity(3), 3, AbcHash)], Identity(2), Config(""), "git.install", Version("1.0"), Identity(4), new string('B', 64), null);
    private static string Identity(byte repeated) => ChocolateyCommonProfileFingerprint.FormatFileIdentity(1, Enumerable.Repeat(repeated, 16).ToArray());
    private static ChocolateyNativeVersion Version(string text)
    {
        Assert.IsTrue(ChocolateyNativeVersion.TryParse(text, out ChocolateyNativeVersion? version));
        return version!;
    }
    private static ChocolateyConfigurationObservation Config(string body)
    {
        Assert.IsTrue(ChocolateyConfigurationParser.TryRead(Encoding.UTF8.GetBytes("<chocolatey>" + body + "</chocolatey>"), out ChocolateyConfigurationObservation? config));
        return config!;
    }

    private sealed class IndexOnlyCodeList(ChocolateyCodeFileData item) : IReadOnlyList<ChocolateyCodeFileData>
    {
        public int Count => 1;
        public ChocolateyCodeFileData this[int index] => index == 0 ? item : throw new ArgumentOutOfRangeException(nameof(index));
        public IEnumerator<ChocolateyCodeFileData> GetEnumerator() => throw new InvalidOperationException("Only the bounded index surface may be read.");
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
