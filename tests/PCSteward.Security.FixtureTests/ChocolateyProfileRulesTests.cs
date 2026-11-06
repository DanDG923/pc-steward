using System.Buffers.Binary;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCSteward.Provider.Chocolatey;
using PCSteward.Provider.Chocolatey.Native;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// FR-UPD-15, TM-03/04/07/13; Task 50 design rev 6 §3 and Profile V2 (portable): the pure rules the observer applies to
/// facts it read through held handles. Each rule fails closed on anything it does not positively recognise.
/// </summary>
[TestClass]
public sealed class ChocolateyProfileRulesTests
{
    private const string MinimalMachineConfig = "<?xml version=\"1.0\" encoding=\"utf-8\"?><configuration><configSections>" +
        "<section name=\"appSettings\" type=\"System.Configuration.AppSettingsSection, System.Configuration\"/></configSections>" +
        "<runtime><assemblyBinding xmlns=\"urn:schemas-microsoft-com:asm.v1\"><dependentAssembly>" +
        "<assemblyIdentity name=\"System.Web.Extensions\" publicKeyToken=\"31bf3856ad364e35\"/>" +
        "<bindingRedirect oldVersion=\"1.0.0.0-1.1.0.0\" newVersion=\"4.0.0.0\"/></dependentAssembly></assemblyBinding></runtime></configuration>";

    private static readonly string[] Git12Alternatives = ["git.1.2.0", "git.1.2", "git.1.2.0.0"];

    [TestMethod]
    public void ReleasePathsAreCanonicalRootFilesOrHelpersAndToolsPaths()
    {
        foreach (string path in new[] { "choco.exe", "helpers/functions/Get-WebFile.ps1", "tools/7z.exe.ignore" })
            Assert.IsTrue(ChocolateyProfileRules.IsReleasePath(path), path);
        foreach (string? path in new[] { null, "", "bin/choco.exe", "helpers\\a.ps1", "helpers//a.ps1", "helpers/../choco.exe", "tools/a.",
                     "tools/NUL", "/choco.exe", "tools/é.exe", "tools/a:b", "lib/x.nupkg" })
            Assert.IsFalse(ChocolateyProfileRules.IsReleasePath(path), path ?? "null");
    }

    [TestMethod]
    public void FileIdentityTextKeepsTheFileIdBytesInReturnedOrder()
    {
        // FILE_ID_INFO returns FileIdLow then FileIdHigh; the Profile V2 L3 golden encodes the 16 bytes in that order.
        NativeFileIdentity identity = new(0x0123456789ABCDEF, new UInt128(0x0F0E0D0C0B0A0908, 0x0706050403020100));
        Assert.AreEqual("0123456789ABCDEF:000102030405060708090A0B0C0D0E0F", ChocolateyProfileRules.FormatIdentity(identity));
        Assert.AreEqual(ChocolateyCommonProfileFingerprint.FormatFileIdentity(1, Enumerable.Repeat((byte)7, 16).ToArray()),
            ChocolateyProfileRules.FormatIdentity(new NativeFileIdentity(1, new UInt128(0x0707070707070707, 0x0707070707070707))));
    }

    [TestMethod]
    public void TheRequiredEffectiveFeaturesHoldOnDefaultsAndEachExplicitViolationRefuses()
    {
        Assert.IsTrue(ChocolateyProfileRules.HasRequiredFeatures(Config("")));
        foreach ((string name, bool value) in ChocolateyProfileRules.RequiredFeatures)
        {
            string flipped = value ? "false" : "true";
            Assert.IsFalse(ChocolateyProfileRules.HasRequiredFeatures(
                Config($"<features><feature name='{name}' enabled='{flipped}' setExplicitly='true'/></features>")), name);
            // Not set explicitly: the effective value is the default, which satisfies the policy.
            Assert.IsTrue(ChocolateyProfileRules.HasRequiredFeatures(
                Config($"<features><feature name='{name}' enabled='{flipped}' setExplicitly='false'/></features>")), name);
        }
        // Observed-only features never refuse.
        Assert.IsTrue(ChocolateyProfileRules.HasRequiredFeatures(
            Config("<features><feature name='useHttpCache' enabled='false' setExplicitly='true'/></features>")));
        Assert.AreEqual(6, ChocolateyProfileRules.RequiredFeatures.Count);
    }

    [TestMethod]
    public void NegativeSourcePrioritiesAreUnsupported()
    {
        Assert.IsTrue(ChocolateyProfileRules.HasSupportedSources(Config("<sources><source id='c' value='https://community.chocolatey.org/api/v2/' priority='0'/></sources>")));
        Assert.IsTrue(ChocolateyProfileRules.HasSupportedSources(Config("<sources><source id='c' value='https://example.invalid/' priority='3'/></sources>")));
        Assert.IsFalse(ChocolateyProfileRules.HasSupportedSources(Config("<sources><source id='c' value='https://example.invalid/' priority='-1'/></sources>")));
    }

    [TestMethod]
    public void CacheLocationMustBeACanonicalAbsolutePathWithoutExpansion()
    {
        Assert.IsTrue(ChocolateyProfileRules.IsSupportedCacheLocation(@"C:\ProgramData\ChocolateyCache"));
        foreach (string? value in new[] { null, "", @"C:\", "C:", @"%TEMP%\chocolatey", @"C:\%x%", "cache", @"C:\cache\", @"\\server\share\cache",
                     "C:/cache", @"C:\a\..\cache", @"C:\a\.\cache", @"C:\NUL\cache", "C:\\a\u0001" })
            Assert.IsFalse(ChocolateyProfileRules.IsSupportedCacheLocation(value), value ?? "null");
    }

    [TestMethod]
    public void ARealShape275ConfigurationWithTheTemplateLegacyKeyPassesEveryStep6Rule()
    {
        // Profile V2 revision 2.1: the owner-host shape (12 keys, 30 features, one source, no API keys) with the template
        // value of containsLegacyPackageInstalls and a protected cacheLocation format passes every portable step-6 rule.
        const string cache = @"C:\ProgramData\ChocolateyCache";
        ChocolateyConfigurationObservation configuration = RealShape(cache, "true");
        Assert.AreEqual(cache, configuration.CacheLocation);
        Assert.AreEqual(30, configuration.ObservedFeatures.Count);
        Assert.IsTrue(ChocolateyProfileRules.HasRequiredFeatures(configuration));
        Assert.IsTrue(ChocolateyProfileRules.HasSupportedSources(configuration));
        Assert.IsTrue(ChocolateyProfileRules.IsSupportedCacheLocation(configuration.CacheLocation));
        Assert.IsTrue(ChocolateySourceResolution.TryResolve(configuration, out ChocolateyConfiguredSource? source));
        Assert.AreEqual(Chocolatey275ConfigurationFixture.CommunitySource, source!.Value);
        Assert.AreEqual(2700, configuration.CommandExecutionTimeoutSeconds);
        Assert.AreEqual(30, configuration.WebRequestTimeoutSeconds);
        Assert.IsFalse(configuration.ProxyConfigured);
    }

    [TestMethod]
    [DataRow("false")]
    [DataRow("True")]
    [DataRow("")]
    [DataRow(" true")]
    [DataRow("1")]
    public void ARealShape275ConfigurationWithAnyOtherLegacyKeyValueRefusesAtTheParser(string value) =>
        Assert.IsFalse(ChocolateyConfigurationParser.TryRead(Chocolatey275ConfigurationFixture.Bytes(@"C:\ProgramData\ChocolateyCache", value), out _), value);

    [TestMethod]
    public void ARealShape275ConfigurationWithoutTheLegacyKeyAlsoPassesEveryPortableStep6Rule()
    {
        // Independent review (Info): the key may be absent; that shape parses and meets the same portable rules.
        const string cache = @"C:\ProgramData\ChocolateyCache";
        ChocolateyConfigurationObservation configuration = RealShape(cache, null);
        Assert.IsTrue(ChocolateyProfileRules.HasRequiredFeatures(configuration));
        Assert.IsTrue(ChocolateyProfileRules.HasSupportedSources(configuration));
        Assert.IsTrue(ChocolateyProfileRules.IsSupportedCacheLocation(configuration.CacheLocation));
        Assert.AreEqual(RealShape(cache, "true").Fingerprint.Length, configuration.Fingerprint.Length);
    }

    [TestMethod]
    public void ARealShape275ConfigurationKeepsTheEmptyCacheLocationRefusal()
    {
        // Unchanged by revision 2.1: the native default (empty) cacheLocation parses but fails the cacheLocation rule.
        ChocolateyConfigurationObservation configuration = RealShape("", "true");
        Assert.AreEqual(string.Empty, configuration.CacheLocation);
        Assert.IsTrue(ChocolateyProfileRules.HasRequiredFeatures(configuration));
        Assert.IsFalse(ChocolateyProfileRules.IsSupportedCacheLocation(configuration.CacheLocation));
    }

    [TestMethod]
    public void AShippedShapeMachineConfigIsSupported()
    {
        Assert.IsTrue(ChocolateyProfileRules.IsSupportedMachineConfig(Encoding.UTF8.GetBytes(MinimalMachineConfig)));
        Assert.IsTrue(ChocolateyProfileRules.IsSupportedMachineConfig([.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(MinimalMachineConfig)]),
            "a UTF-8 BOM is accepted");
        Assert.IsTrue(ChocolateyProfileRules.IsSupportedMachineConfig(Encoding.UTF8.GetBytes("<configuration/>")));
    }

    [TestMethod]
    [DataRow("<runtime><appDomainManagerAssembly value='Evil, Version=1.0.0.0'/></runtime>")]
    [DataRow("<runtime><appDomainManagerType value='Evil.Manager'/></runtime>")]
    [DataRow("<runtime><AppDomainManagerType value='Evil.Manager'/></runtime>")]
    [DataRow("<runtime><assemblyBinding xmlns='urn:schemas-microsoft-com:asm.v1'><dependentAssembly><assemblyIdentity name='x'/><codeBase version='1.0.0.0' href='file:///C:/x.dll'/></dependentAssembly></assemblyBinding></runtime>")]
    [DataRow("<runtime><assemblyBinding xmlns='urn:schemas-microsoft-com:asm.v1'><probing privatePath='bin;x'/></assemblyBinding></runtime>")]
    [DataRow("<runtime><developmentMode developerInstallation='true'/></runtime>")]
    [DataRow("<runtime><assemblyBinding xmlns='urn:schemas-microsoft-com:asm.v1'><linkedConfiguration href='file://C:/x.config'/></assemblyBinding></runtime>")]
    [DataRow("<runtime configSource='runtime.config'/>")]
    [DataRow("<runtime><assemblyBinding xmlns='urn:schemas-microsoft-com:asm.v1'><dependentAssembly><assemblyIdentity name='chocolatey.licensed'/></dependentAssembly></assemblyBinding></runtime>")]
    [DataRow("<runtime><assemblyBinding xmlns='urn:schemas-microsoft-com:asm.v1'><dependentAssembly><assemblyIdentity name='Chocolatey.Licensed' publicKeyToken='x'/><bindingRedirect oldVersion='0.0.0.0-9.0.0.0' newVersion='1.0.0.0'/></dependentAssembly></assemblyBinding></runtime>")]
    [DataRow("<runtime><assemblyBinding xmlns='urn:schemas-microsoft-com:asm.v1'><qualifyAssembly partialName='chocolatey.licensed' fullName='chocolatey.licensed, Version=1.0.0.0'/></assemblyBinding></runtime>")]
    [DataRow("<appSettings><add key='x' value='y'/></appSettings><x>chocolatey.licensed</x>")]
    public void EveryUnsupportedLoaderSettingRefuses(string body) =>
        Assert.IsFalse(ChocolateyProfileRules.IsSupportedMachineConfig(Encoding.UTF8.GetBytes("<configuration>" + body + "</configuration>")), body);

    [TestMethod]
    public void MalformedUnboundedOrForeignMachineConfigsRefuse()
    {
        Assert.IsFalse(ChocolateyProfileRules.IsSupportedMachineConfig([]));
        Assert.IsFalse(ChocolateyProfileRules.IsSupportedMachineConfig(Encoding.UTF8.GetBytes("<configuration>")), "unterminated");
        Assert.IsFalse(ChocolateyProfileRules.IsSupportedMachineConfig(Encoding.UTF8.GetBytes("<settings/>")), "wrong root");
        Assert.IsFalse(ChocolateyProfileRules.IsSupportedMachineConfig(Encoding.UTF8.GetBytes("<configuration xmlns='urn:x'/>")), "namespaced root");
        Assert.IsFalse(ChocolateyProfileRules.IsSupportedMachineConfig(Encoding.UTF8.GetBytes(
            "<!DOCTYPE configuration [<!ENTITY e 'chocolatey.licensed'>]><configuration>&e;</configuration>")), "DTD");
        string deep = string.Concat(Enumerable.Repeat("<a>", 40)) + string.Concat(Enumerable.Repeat("</a>", 40));
        Assert.IsFalse(ChocolateyProfileRules.IsSupportedMachineConfig(Encoding.UTF8.GetBytes("<configuration>" + deep + "</configuration>")), "depth");
        string wide = string.Concat(Enumerable.Repeat("<a/>", ChocolateyProfileRules.MachineConfigMaximumNodes + 1));
        Assert.IsFalse(ChocolateyProfileRules.IsSupportedMachineConfig(Encoding.UTF8.GetBytes("<configuration>" + wide + "</configuration>")), "nodes");
        byte[] large = Encoding.UTF8.GetBytes("<configuration><!--" + new string('x', ChocolateyProfileRules.MachineConfigMaximumBytes) + "--></configuration>");
        Assert.IsFalse(ChocolateyProfileRules.IsSupportedMachineConfig(large), "bytes");
        Assert.IsFalse(ChocolateyProfileRules.IsSupportedMachineConfig([0xC3, 0x28]), "invalid UTF-8");
    }

    [TestMethod]
    public void LicensedGacEntriesAndProbedAssemblyNamesAreRecognised()
    {
        foreach (string name in new[] { "chocolatey.licensed", "CHOCOLATEY.LICENSED", "policy.1.0.chocolatey.licensed", "Policy.2.7.Chocolatey.Licensed" })
            Assert.IsTrue(ChocolateyProfileRules.IsLicensedGacEntry(name), name);
        foreach (string name in new[] { "chocolatey", "chocolatey.licensed.resources", "policy.1.0.chocolatey", "xchocolatey.licensed", "System.Web" })
            Assert.IsFalse(ChocolateyProfileRules.IsLicensedGacEntry(name), name);
        Assert.IsTrue(ChocolateyProfileRules.IsLicensedAssemblyNode("Chocolatey.Licensed.dll", directory: false));
        Assert.IsTrue(ChocolateyProfileRules.IsLicensedAssemblyNode("chocolatey.licensed.exe", directory: false));
        Assert.IsTrue(ChocolateyProfileRules.IsLicensedAssemblyNode("chocolatey.licensed", directory: true));
        Assert.IsFalse(ChocolateyProfileRules.IsLicensedAssemblyNode("chocolatey.licensed", directory: false));
        Assert.IsFalse(ChocolateyProfileRules.IsLicensedAssemblyNode("chocolatey.dll", directory: false));
    }

    [TestMethod]
    public void PendingAndStoreOverrideMarkersAreRecognised()
    {
        Assert.IsTrue(ChocolateyProfileRules.IsPendingMarker(".chocolateyPending"));
        Assert.IsTrue(ChocolateyProfileRules.IsPendingMarker(".CHOCOLATEYPENDINGSKIP"));
        Assert.IsFalse(ChocolateyProfileRules.IsPendingMarker("chocolateyPending"));
        foreach (string marker in new[] { ".version", ".SXS", ".pin" })
            Assert.IsTrue(ChocolateyProfileRules.IsStoreOverrideMarker(marker), marker);
        foreach (string marker in new[] { ".arguments", ".files", ".extra", ".sourceInstalledFrom", "version" })
            Assert.IsFalse(ChocolateyProfileRules.IsStoreOverrideMarker(marker), marker);
    }

    [TestMethod]
    public void AtMostOneStoreAlternativeMayExistAndNoSuffixIsGuessed()
    {
        Assert.IsTrue(ChocolateyNativeVersion.TryParse("1.2", out ChocolateyNativeVersion? version));
        IReadOnlyList<string> alternatives = version!.GetStoreDirectoryNames("git");
        CollectionAssert.AreEqual(Git12Alternatives, alternatives.ToArray());

        Assert.IsTrue(ChocolateyProfileRules.TrySelectStore(alternatives, ["git.install.1.2.0", "other.1.0"], out string? none));
        Assert.IsNull(none);
        Assert.IsTrue(ChocolateyProfileRules.TrySelectStore(alternatives, ["GIT.1.2.0", "git.1.2.0-beta", "git.1.2.0.1"], out string? one));
        Assert.AreEqual("GIT.1.2.0", one);
        Assert.IsFalse(ChocolateyProfileRules.TrySelectStore(alternatives, ["git.1.2.0", "git.1.2"], out _));
        Assert.IsFalse(ChocolateyProfileRules.TrySelectStore(alternatives, ["git.1.2", "git.1.2.0.0"], out _));
    }

    [TestMethod]
    public void TheLibBackupGlobMatchesLongAndShortNamePrefixesCaseInsensitively()
    {
        Assert.IsTrue(ChocolateyProfileRules.IsLibBackupConflict("git", "", "git"));
        Assert.IsTrue(ChocolateyProfileRules.IsLibBackupConflict("git.install", "GIT~1.INS", "git"));
        Assert.IsTrue(ChocolateyProfileRules.IsLibBackupConflict("GIT-old", "", "git"));
        Assert.IsTrue(ChocolateyProfileRules.IsLibBackupConflict("g it backup", "GITBAC~1", "git"), "an 8.3 alias matches the glob");
        Assert.IsFalse(ChocolateyProfileRules.IsLibBackupConflict("gi", "", "git"));
        Assert.IsFalse(ChocolateyProfileRules.IsLibBackupConflict("legit", "LEGIT", "git"));
        Assert.ThrowsExactly<ArgumentException>(() => ChocolateyProfileRules.IsLibBackupConflict("x", "", ""));
    }

    [TestMethod]
    public void TheRegistrationDigestIsAbsentWhenEmptyAndBindsEveryRecordField()
    {
        Assert.IsNull(ChocolateyProfileRules.ComputeRegistrationDigest([]));
        ChocolateyRegistrationRecord a = Record(@"lib\chocolatey\chocolatey.nupkg", "chocolatey", "2.7.5", 1, 100, 'A');
        ChocolateyRegistrationRecord b = Record(@"lib\other\chocolatey.nupkg", "Chocolatey", "2.7.4", 2, 200, 'B');
        string digest = ChocolateyProfileRules.ComputeRegistrationDigest([a, b])!;
        Assert.AreEqual(64, digest.Length);
        Assert.AreEqual(digest, ChocolateyProfileRules.ComputeRegistrationDigest([b, a]), "order independent");
        ChocolateyRegistrationRecord[] changed =
        [
            a with { RelativePath = @"lib\chocolatey2\chocolatey.nupkg" }, a with { Id = "chocolatey.extension" }, Record(a.RelativePath, a.Id, "2.7.6", 1, 100, 'A'),
            a with { ArchiveIdentity = Identity(9) }, a with { Length = 101 }, a with { Sha256 = new string('C', 64) },
        ];
        foreach (ChocolateyRegistrationRecord record in changed)
            Assert.AreNotEqual(digest, ChocolateyProfileRules.ComputeRegistrationDigest([record, b]), record.ToString());
        Assert.AreNotEqual(digest, ChocolateyProfileRules.ComputeRegistrationDigest([a]));
    }

    /// <summary>
    /// N2/N5 obligation (composition record, N2 Info): an independent golden vector for <c>pc-steward-choco-client-registration:1</c>.
    /// The canonical input is spelled out field by field here (each field: UTF-8 bytes preceded by their length as a 32-bit
    /// little-endian integer) and hashed by the test itself; the expected digests were also computed outside .NET (Python
    /// <c>hashlib</c>/<c>struct</c>) and are pinned as literals. Records sort by the upper-cased path with <c>\</c>, so
    /// <c>lib\chocolatey0</c> precedes <c>lib\chocolatey</c> (<c>0</c> &lt; <c>\</c>), while the hashed paths use <c>/</c>
    /// (where the order would be reversed); IDs are upper-cased; both the original and normalized versions are bound.
    /// </summary>
    [TestMethod]
    public void TheRegistrationDigestMatchesTheIndependentGoldenVector()
    {
        const string GoldenTwo = "BBA747F4FDDDD62B6A0DB70313769A6EB2C87B31E57547E132CF09DC1ADBDF04";
        const string GoldenOne = "9B33C178099488BBAF96FFE29DC32DB3BD345B322DE8A6F90D31CBE129BA572D";
        ChocolateyRegistrationRecord a = Record(@"lib\chocolatey\chocolatey.nupkg", "chocolatey", "2.7.5", 1, 100, 'A');
        ChocolateyRegistrationRecord b = Record(@"lib\chocolatey0\chocolatey.nupkg", "Chocolatey", "2.7.4.0", 2, 200, 'B');
        // The premises of the spelled-out input below.
        Assert.AreEqual(("2.7.4.0", "2.7.4"), (b.Version.Original, b.Version.Normalized));
        Assert.AreEqual("0000000000000001:" + string.Concat(Enumerable.Repeat("02", 16)), b.ArchiveIdentity);

        string[] two =
        [
            "pc-steward-choco-client-registration:1", "2",
            "LIB/CHOCOLATEY0/CHOCOLATEY.NUPKG", "CHOCOLATEY", "2.7.4.0", "2.7.4", "0000000000000001:" + string.Concat(Enumerable.Repeat("02", 16)), "200", new string('B', 64),
            "LIB/CHOCOLATEY/CHOCOLATEY.NUPKG", "CHOCOLATEY", "2.7.5", "2.7.5", "0000000000000001:" + string.Concat(Enumerable.Repeat("01", 16)), "100", new string('A', 64),
        ];
        string[] one =
        [
            "pc-steward-choco-client-registration:1", "1",
            "LIB/CHOCOLATEY/CHOCOLATEY.NUPKG", "CHOCOLATEY", "2.7.5", "2.7.5", "0000000000000001:" + string.Concat(Enumerable.Repeat("01", 16)), "100", new string('A', 64),
        ];
        Assert.AreEqual(GoldenTwo, LengthPrefixedSha256(two), "the spelled-out input reproduces the external golden");
        Assert.AreEqual(GoldenOne, LengthPrefixedSha256(one));
        Assert.AreEqual(GoldenTwo, ChocolateyProfileRules.ComputeRegistrationDigest([a, b]));
        Assert.AreEqual(GoldenTwo, ChocolateyProfileRules.ComputeRegistrationDigest([b, a]));
        Assert.AreEqual(GoldenOne, ChocolateyProfileRules.ComputeRegistrationDigest([a]));
    }

    private static string LengthPrefixedSha256(IEnumerable<string> fields)
    {
        using MemoryStream canonical = new();
        Span<byte> length = stackalloc byte[4];
        foreach (string field in fields)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(field);
            BinaryPrimitives.WriteUInt32LittleEndian(length, (uint)bytes.Length);
            canonical.Write(length);
            canonical.Write(bytes);
        }
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(canonical.ToArray()));
    }

    [TestMethod]
    public void TheEntryParserReadsShortNamesAndRefusesMalformedShortNameLengths()
    {
        byte[] buffer = Entry("git backup", "GITBAC~1", 0x10, 5);
        List<NativeDirectoryEntry> entries = [];
        Assert.IsTrue(ProtectedTreeWalker.TryParseEntries(buffer, entries));
        Assert.AreEqual(new NativeDirectoryEntry("git backup", 0x10, 5, "GITBAC~1"), entries.Single());

        byte[] odd = (byte[])buffer.Clone();
        odd[68] = 3;
        Assert.IsFalse(ProtectedTreeWalker.TryParseEntries(odd, []));
        byte[] large = (byte[])buffer.Clone();
        large[68] = 26;
        Assert.IsFalse(ProtectedTreeWalker.TryParseEntries(large, []));
    }

    private static byte[] Entry(string name, string shortName, uint attributes, ulong fileId)
    {
        byte[] nameBytes = Encoding.Unicode.GetBytes(name);
        byte[] shortBytes = Encoding.Unicode.GetBytes(shortName);
        byte[] entry = new byte[(ProtectedTreeWalker.EntryHeaderBytes + nameBytes.Length + 7) / 8 * 8];
        BinaryPrimitives.WriteUInt32LittleEndian(entry.AsSpan(56), attributes);
        BinaryPrimitives.WriteUInt32LittleEndian(entry.AsSpan(60), (uint)nameBytes.Length);
        entry[68] = (byte)shortBytes.Length;
        shortBytes.CopyTo(entry, 70);
        BinaryPrimitives.WriteUInt64LittleEndian(entry.AsSpan(96), fileId);
        nameBytes.CopyTo(entry, ProtectedTreeWalker.EntryHeaderBytes);
        return entry;
    }

    private static ChocolateyRegistrationRecord Record(string path, string id, string version, byte identity, long length, char hash)
    {
        Assert.IsTrue(ChocolateyNativeVersion.TryParse(version, out ChocolateyNativeVersion? parsed));
        return new ChocolateyRegistrationRecord(path, id, parsed!, Identity(identity), length, new string(hash, 64));
    }

    private static string Identity(byte value) => ChocolateyCommonProfileFingerprint.FormatFileIdentity(1, Enumerable.Repeat(value, 16).ToArray());

    private static ChocolateyConfigurationObservation Config(string body)
    {
        Assert.IsTrue(ChocolateyConfigurationParser.TryRead(Encoding.UTF8.GetBytes("<chocolatey>" + body + "</chocolatey>"), out ChocolateyConfigurationObservation? config));
        return config!;
    }

    private static ChocolateyConfigurationObservation RealShape(string cache, string? legacyValue)
    {
        Assert.IsTrue(ChocolateyConfigurationParser.TryRead(Chocolatey275ConfigurationFixture.Bytes(cache, legacyValue), out ChocolateyConfigurationObservation? config));
        return config!;
    }
}
