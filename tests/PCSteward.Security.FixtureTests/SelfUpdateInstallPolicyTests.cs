using System.Security.Cryptography;
using System.Text;
using PCSteward.Execution;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// FR-UPD-15, TM-12, TM-20, TM-23, TM-24; ADR-032 revision 2 §6–§8 and "Follow-up controls and tests": the pure rules of
/// the elevated self-update operation. The request carries job values only and is parsed strictly; the extracted MSI tree must
/// be exactly the CI payload plus its release manifest, with executables equal to CI apart from one added signature; the
/// installed tree must be exactly the expected one; the expected tree round-trips through its canonical file; and the MSI
/// structure rule refuses code, services, chainers and custom actions other than an error message. Pure: these run on every
/// platform.
/// </summary>
[TestClass]
public sealed class SelfUpdateInstallPolicyTests
{
    private static readonly Guid Job = new("6f1c2d3e-4b5a-4978-8c6d-5e4f3a2b1c0d");
    private static readonly string ZipSha = new('E', 64);
    private static readonly string MsiSha = new('F', 64);
    private static readonly string Nonce = new('9', 64);

    [TestMethod]
    public void OnlyANewerReleaseThanTheInstalledOneIsAccepted()
    {
        Assert.IsTrue(SelfUpdateInstallPolicy.IsNewerVersion("0.1.2", "0.1.1"));
        Assert.IsTrue(SelfUpdateInstallPolicy.IsNewerVersion("0.1.1", "0.1.0-private-beta"));
        Assert.IsTrue(SelfUpdateInstallPolicy.IsNewerVersion("0.10.0", "0.9.9"));
        Assert.IsTrue(SelfUpdateInstallPolicy.IsNewerVersion("1.0.0", "0.99.99"));
        Assert.IsFalse(SelfUpdateInstallPolicy.IsNewerVersion("0.1.1", "0.1.1"));
        Assert.IsFalse(SelfUpdateInstallPolicy.IsNewerVersion("0.1.1", "0.1.1-private-beta"));
        Assert.IsFalse(SelfUpdateInstallPolicy.IsNewerVersion("0.1.0", "0.1.1"));
        Assert.IsFalse(SelfUpdateInstallPolicy.IsNewerVersion("0.2", "0.1.1"));
        Assert.IsFalse(SelfUpdateInstallPolicy.IsNewerVersion("0.1.2", "garbage"));
        Assert.IsFalse(SelfUpdateInstallPolicy.IsNewerVersion("0.1.2", ""));
    }

    [TestMethod]
    public void TheRequestRoundTripsAndCarriesNoPath()
    {
        SelfUpdateRequest request = new(Nonce, Job, "0.1.2", 4242, ReleaseCheckFixture.Commit, ZipSha, MsiSha);
        byte[] bytes = SelfUpdateInstallPolicy.WriteRequest(request);
        Assert.IsTrue(SelfUpdateInstallPolicy.TryReadRequest(bytes, out SelfUpdateRequest? read));
        Assert.AreEqual(request, read);
        Assert.AreEqual(MsiSha + ".msi", SelfUpdateInstallPolicy.MsiFileName(read!.MsiSha256));
        Assert.AreEqual(ZipSha + ".zip", SelfUpdateInstallPolicy.CheckZipFileName(read.CheckZipSha256));
        Assert.AreEqual("PC Steward Update 6f1c2d3e4b5a49788c6d5e4f3a2b1c0d", SelfUpdateInstallPolicy.WorkFolderName(Job));
        Assert.IsTrue(SelfUpdateInstallPolicy.IsWorkFolderName(SelfUpdateInstallPolicy.WorkFolderName(Job)));
        foreach (string name in new[] { "PC Steward Update ..", "PC Steward Update 6F1C2D3E4B5A49788C6D5E4F3A2B1C0D", "PC Steward", "PC Steward Update 6f1c2d3e4b5a49788c6d5e4f3a2b1c0d\\x" })
            Assert.IsFalse(SelfUpdateInstallPolicy.IsWorkFolderName(name), name);
    }

    [TestMethod]
    public void AMalformedRequestIsRefused()
    {
        string good = Encoding.ASCII.GetString(SelfUpdateInstallPolicy.WriteRequest(new(Nonce, Job, "0.1.2", 4242, ReleaseCheckFixture.Commit, ZipSha, MsiSha)));
        string[] bad =
        [
            good.Replace("}", ",\"path\":\"C:\\\\Windows\"}", StringComparison.Ordinal),
            good.Replace("\"toVersion\":\"0.1.2\"", "\"toVersion\":\"0.1.2-beta\"", StringComparison.Ordinal),
            good.Replace("\"runId\":4242", "\"runId\":0", StringComparison.Ordinal),
            good.Replace("\"runId\":4242", "\"runId\":\"4242\"", StringComparison.Ordinal),
            good.Replace(ReleaseCheckFixture.Commit, ReleaseCheckFixture.Commit.ToUpperInvariant(), StringComparison.Ordinal),
            good.Replace(MsiSha, MsiSha.ToLowerInvariant(), StringComparison.Ordinal),
            good.Replace(ZipSha, "..\\..\\x", StringComparison.Ordinal),
            good.Replace(Job.ToString("D"), Guid.Empty.ToString("D"), StringComparison.Ordinal),
            good.Replace(Job.ToString("D"), Job.ToString("D").ToUpperInvariant(), StringComparison.Ordinal),
            good.Replace("\"msiSha256\"", "\"MsiSha256\"", StringComparison.Ordinal),
            good.Replace(Nonce, Nonce.ToLowerInvariant().Replace('9', 'a'), StringComparison.Ordinal),
            good.Replace("\"nonce\":\"" + Nonce + "\",", "", StringComparison.Ordinal),
            good + " ",
            good.Replace("{", "{\"jobId\":\"" + Job.ToString("D") + "\",", StringComparison.Ordinal),
        ];
        foreach (string text in bad)
            Assert.IsFalse(SelfUpdateInstallPolicy.TryReadRequest(Encoding.UTF8.GetBytes(text), out _), text);
        Assert.IsFalse(SelfUpdateInstallPolicy.TryReadRequest(new byte[SelfUpdateInstallPolicy.MaximumRequestBytes + 1], out _));
    }

    [TestMethod]
    public void TheConfirmedRecordMustBeTheApprovedOne()
    {
        Assert.IsTrue(ReleaseCheckPolicy.TryRead(ReleaseCheckFixture.Record(msiVersion: "0.1.2"), out ReleaseCheckRecord? record));
        SelfUpdateRequest request = new(Nonce, Job, "0.1.2", 4242, ReleaseCheckFixture.Commit, ZipSha, MsiSha);
        Assert.IsTrue(SelfUpdateInstallPolicy.RecordMatchesRequest(record!, request, ReleaseCheckFixture.Pins));
        Assert.IsFalse(SelfUpdateInstallPolicy.RecordMatchesRequest(record!, request with { RunId = 4243 }, ReleaseCheckFixture.Pins));
        Assert.IsFalse(SelfUpdateInstallPolicy.RecordMatchesRequest(record!, request with { ToVersion = "0.1.3" }, ReleaseCheckFixture.Pins));
        Assert.IsFalse(SelfUpdateInstallPolicy.RecordMatchesRequest(record!, request with { SourceCommit = new string('9', 40) }, ReleaseCheckFixture.Pins));
        Assert.IsFalse(SelfUpdateInstallPolicy.RecordMatchesRequest(record!, request, [ReleaseCheckFixture.Pins[1], ReleaseCheckFixture.Pins[0]]));
    }

    [TestMethod]
    public void TheExtractedCiBuildMatchesAndYieldsTheExpectedTree()
    {
        Build build = Build.Create();
        SelfUpdateTreeVerdict verdict = build.Check(out SelfUpdateExpectedInstall? expected);
        Assert.AreEqual(SelfUpdateTreeVerdict.Matches, verdict);
        Assert.HasCount(5, expected!.Files);
        Assert.AreEqual("0.1.2", expected.ToVersion);
        Assert.AreEqual(Job, expected.JobId);
        Assert.AreEqual(Sha(build.Manifest), expected.ManifestSha256);
        Assert.AreEqual(SelfUpdateTreeVerdict.Matches, SelfUpdateInstallPolicy.CheckInstalled(expected, build.Tree()));
    }

    [TestMethod]
    public void AnyDifferenceInTheExtractedTreeIsRefused()
    {
        Assert.AreEqual(SelfUpdateTreeVerdict.ManifestMissing, Build.Create().CheckWithManifest(null));
        Assert.AreEqual(SelfUpdateTreeVerdict.ManifestUnreadable, Build.Create().CheckWithManifest(Encoding.ASCII.GetBytes("{}")));

        // An extra file, a missing file, and a changed data file.
        Build extra = Build.Create();
        extra.Files["Extra.dll"] = [1, 2, 3];
        Assert.AreEqual(SelfUpdateTreeVerdict.FileSetDiffers, extra.Check(out _));
        Build missing = Build.Create();
        Assert.AreEqual(SelfUpdateTreeVerdict.FileSetDiffers, missing.CheckTree(missing.Tree().Where(file => file.Path != "App.xbf").ToList()));
        Build changed = Build.Create(changeDataFile: true);
        Assert.AreEqual(SelfUpdateTreeVerdict.FileDiffers, changed.Check(out _));

        // A signed executable whose code differs from CI (manifest updated to match it, so only the CI comparison catches it).
        Build tampered = Build.Create(tamperExecutable: true);
        Assert.AreEqual(SelfUpdateTreeVerdict.ExecutableDiffers, tampered.Check(out _));

        // The extracted manifest must be the bytes listed in the tree, and name the record's product version.
        Build version = Build.Create(productVersion: "0.1.3-private-beta");
        Assert.AreEqual(SelfUpdateTreeVerdict.VersionMismatch, version.Check(out _));
        Build swapped = Build.Create();
        Assert.AreEqual(SelfUpdateTreeVerdict.ManifestUnreadable,
            swapped.CheckTree(swapped.Tree().Select(file => file.Path == ReleaseManifestPolicy.FileName ? file with { Sha256 = new string('0', 64) } : file).ToList()));

        // Duplicate paths differing only in case are refused.
        Build duplicate = Build.Create();
        Assert.AreEqual(SelfUpdateTreeVerdict.FileSetDiffers, duplicate.CheckTree([.. duplicate.Tree(), duplicate.Tree()[0] with { Path = duplicate.Tree()[0].Path.ToUpperInvariant() }]));
    }

    [TestMethod]
    public void TheInstalledTreeMustBeExactlyTheExpectedOne()
    {
        Build build = Build.Create();
        Assert.AreEqual(SelfUpdateTreeVerdict.Matches, build.Check(out SelfUpdateExpectedInstall? expected));
        List<SelfUpdateTreeFile> installed = build.Tree();
        Assert.AreEqual(SelfUpdateTreeVerdict.FileSetDiffers, SelfUpdateInstallPolicy.CheckInstalled(expected!, [.. installed, new("Extra.dll", 3, new string('1', 64))]));
        Assert.AreEqual(SelfUpdateTreeVerdict.FileSetDiffers, SelfUpdateInstallPolicy.CheckInstalled(expected!, installed.Skip(1).ToList()));
        Assert.AreEqual(SelfUpdateTreeVerdict.FileDiffers, SelfUpdateInstallPolicy.CheckInstalled(expected!,
            installed.Select(file => file.Path == "PCSteward.Desktop.exe" ? file with { Sha256 = new string('1', 64) } : file).ToList()));
        Assert.AreEqual(SelfUpdateTreeVerdict.FileDiffers, SelfUpdateInstallPolicy.CheckInstalled(expected!,
            installed.Select(file => file.Path == "App.xbf" ? file with { Bytes = file.Bytes + 1 } : file).ToList()));
    }

    [TestMethod]
    public void TheExpectedTreeRoundTripsAndIsReadStrictly()
    {
        Build build = Build.Create();
        Assert.AreEqual(SelfUpdateTreeVerdict.Matches, build.Check(out SelfUpdateExpectedInstall? expected));
        byte[] bytes = SelfUpdateInstallPolicy.WriteExpected(expected!);
        Assert.IsTrue(SelfUpdateInstallPolicy.TryReadExpected(bytes, out SelfUpdateExpectedInstall? read));
        Assert.AreEqual(expected!.JobId, read!.JobId);
        Assert.AreEqual(expected.ManifestSha256, read.ManifestSha256);
        CollectionAssert.AreEqual(expected.Files.ToList(), read.Files.ToList());
        CollectionAssert.AreEqual(bytes, SelfUpdateInstallPolicy.WriteExpected(read));

        string text = Encoding.ASCII.GetString(bytes);
        string[] bad =
        [
            text.Replace("pc-steward-expected-install:1", "pc-steward-expected-install:2", StringComparison.Ordinal),
            text.Replace("\"files\":[", "\"files\":[{\"path\":\"..\\\\x\",\"bytes\":1,\"sha256\":\"" + new string('1', 64) + "\"},", StringComparison.Ordinal),
            text.Replace("\"files\":[", "\"files\":[{\"path\":\"App.XBF\",\"bytes\":1,\"sha256\":\"" + new string('1', 64) + "\"},", StringComparison.Ordinal),
            text.Replace(expected.ManifestSha256, expected.ManifestSha256.ToLowerInvariant(), StringComparison.Ordinal),
            text.Replace("{\"schema\"", "{\"extra\":1,\"schema\"", StringComparison.Ordinal),
            text.Replace("\"" + ReleaseManifestPolicy.FileName + "\"", "\"Other.json\"", StringComparison.Ordinal),
        ];
        foreach (string candidate in bad)
            Assert.IsFalse(SelfUpdateInstallPolicy.TryReadExpected(Encoding.ASCII.GetBytes(candidate), out _), candidate);
    }

    [TestMethod]
    public void TheStructureRuleAllowsOnlyAnErrorMessageCustomAction()
    {
        static MsiTable Table(string name, string[] columns, params string?[][] rows) => new(name, columns, [.. rows.Select(row => (IReadOnlyList<string?>)row)]);
        string[] actionColumns = ["Action", "Type", "Source", "Target", "ExtendedType"];
        Dictionary<string, MsiTable> tables = new(StringComparer.Ordinal)
        {
            ["File"] = Table("File", ["File", "FileName"], ["f1", "App.xbf"]),
            ["CustomAction"] = Table("CustomAction", actionColumns, ["WixDowngrade", "19", null, "A newer version is installed.", null]),
            ["Binary"] = Table("Binary", ["Name", "Data"]),
        };
        Assert.AreEqual(MsiStructureVerdict.Allowed, MsiStructurePolicy.Check(tables, out _));

        foreach (string?[] action in new[]
        {
            new string?[] { "Run", "34", "INSTALLFOLDER", "cmd.exe /c x", null },
            ["Script", "3094", "Binary1", "Main", null],
            ["ErrorWithSource", "19", "Property1", "x", null],
            ["ErrorWithFlags", "1043", null, "x", null],
            ["NotANumber", "x19", null, "x", null],
        })
        {
            Dictionary<string, MsiTable> withAction = new(tables) { ["CustomAction"] = Table("CustomAction", actionColumns, action) };
            Assert.AreEqual(MsiStructureVerdict.CustomActionNotAllowed, MsiStructurePolicy.Check(withAction, out string detail), action[0]);
            Assert.AreEqual(action[0], detail);
        }

        // Security review R5: an allow-list. Every table outside it is refused once it has a row, including ones that run no code
        // but make privileged changes (registry, COM, file moves and removals, permissions on other objects).
        foreach (string forbidden in new[]
        {
            "Binary", "ServiceInstall", "ServiceControl", "MsiServiceConfig", "MsiEmbeddedChainer", "MsiEmbeddedUI", "SelfReg", "ODBCDriver",
            "Environment", "Registry", "RemoveRegistry", "Class", "ProgId", "Verb", "Extension", "TypeLib", "AppId", "MoveFile", "DuplicateFile",
            "RemoveFile", "LockPermissions", "IniFile", "Font", "PublishComponent", "IsolatedComponent", "MsiAssembly", "SomeFutureTable",
        })
        {
            Dictionary<string, MsiTable> withRow = new(tables) { [forbidden] = Table(forbidden, ["Key"], ["x"]) };
            Assert.AreEqual(MsiStructureVerdict.ForbiddenTable, MsiStructurePolicy.Check(withRow, out string detail), forbidden);
            Assert.AreEqual(forbidden, detail);
        }
        foreach (string allowed in MsiStructurePolicy.AllowedTables.Where(name => name != "CustomAction"))
        {
            Dictionary<string, MsiTable> withRow = new(tables) { [allowed] = Table(allowed, ["Key"], ["x"]) };
            Assert.AreEqual(MsiStructureVerdict.Allowed, MsiStructurePolicy.Check(withRow, out _), allowed);
        }
        Dictionary<string, MsiTable> noType = new(tables) { ["CustomAction"] = Table("CustomAction", ["Action", "Source"], ["x", null]) };
        Assert.AreEqual(MsiStructureVerdict.UnreadableTable, MsiStructurePolicy.Check(noType, out _));
    }

    [TestMethod]
    public void WindowsInstallerRegistrationKeysUseThePackedProductCode()
    {
        // Packed form: each of the first three groups reversed, then the last eight bytes with their two digits swapped.
        Assert.AreEqual("03371DA25DABEB644A900218B46276C3", MsiProductRegistration.Pack(MsiProductRegistration.UpgradeCode));
        Assert.AreEqual(MsiProductRegistration.UpgradeCode, MsiProductRegistration.Unpack("03371DA25DABEB644A900218B46276C3"));
        Assert.AreEqual(@"SOFTWARE\Classes\Installer\UpgradeCodes\03371DA25DABEB644A900218B46276C3", MsiProductRegistration.MachineUpgradeCodeKey);
        const string product = "{0123ABCD-4567-89EF-0123-456789ABCDEF}";
        Assert.AreEqual(product, MsiProductRegistration.Unpack(MsiProductRegistration.Pack(product)));
        Assert.AreEqual(
            @"Software\Microsoft\Installer\Products\DCBA32107654FE981032547698BADCFE|" +
            @"Software\Microsoft\Installer\Features\DCBA32107654FE981032547698BADCFE|" +
            @"Software\Microsoft\Installer\UpgradeCodes\03371DA25DABEB644A900218B46276C3",
            string.Join('|', MsiProductRegistration.PerUserKeys(product)));
        foreach (string bad in new[] { "", "0123ABCD-4567-89EF-0123-456789ABCDEF", "{0123abcd-4567-89ef-0123-456789abcdef}", "{0123ABCD-4567-89EF-0123-456789ABCDE}" })
            Assert.ThrowsExactly<ArgumentException>(() => MsiProductRegistration.Pack(bad), bad);
        Assert.IsNull(MsiProductRegistration.Unpack("03371da25dabeb644a900218b46276c3"));
        Assert.IsNull(MsiProductRegistration.Unpack("..\\03371DA25DABEB644A900218B46276C3"));

        static MsiTable Properties(params string?[][] rows) => new("Property", ["Property", "Value"], [.. rows.Select(row => (IReadOnlyList<string?>)row)]);
        Dictionary<string, MsiTable> tables = new(StringComparer.Ordinal) { ["Property"] = Properties(["ProductName", "PC Steward"], ["ProductCode", product]) };
        Assert.AreEqual(product, MsiProductRegistration.ProductCode(tables));
        Assert.IsNull(MsiProductRegistration.ProductCode(new Dictionary<string, MsiTable>(StringComparer.Ordinal)));
        Assert.IsNull(MsiProductRegistration.ProductCode(new Dictionary<string, MsiTable>(StringComparer.Ordinal) { ["Property"] = Properties(["ProductCode", "{0123abcd-4567-89ef-0123-456789abcdef}"]) }));
        Assert.IsNull(MsiProductRegistration.ProductCode(new Dictionary<string, MsiTable>(StringComparer.Ordinal) { ["Property"] = Properties(["ProductCode", product], ["ProductCode", product]) }));
    }

    private static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    /// <summary>A CI payload (unsigned), the signed tree an MSI would unpack, its release manifest and the CI check record.</summary>
    /// <summary>A CI build, its signed extraction and release manifest (shared with the session tests).</summary>
    internal sealed class Build
    {
        internal Dictionary<string, byte[]> Files { get; } = new(StringComparer.Ordinal);
        internal byte[] Manifest { get; private set; } = [];
        internal ReleaseCheckRecord Record { get; private set; } = null!;

        /// <summary>The record's JSON bytes, as the check zip carries them.</summary>
        internal byte[] RecordBytes { get; private set; } = [];

        internal static Build Create(bool changeDataFile = false, bool tamperExecutable = false, string productVersion = "0.1.2-private-beta",
            IReadOnlyList<string>? pins = null)
        {
            byte[] app = Encoding.ASCII.GetBytes("app resource");
            byte[] resource = Encoding.ASCII.GetBytes("nested resource");
            byte[] desktop = ReleaseCheckFixture.Pe(0x1234), helper = ReleaseCheckFixture.Pe(0xBEEF);
            Build build = new();
            (build.Record, build.RecordBytes) = RecordAndBytes(pins ?? ReleaseCheckFixture.Pins, ("App.xbf", app, null), ("PCSteward.Desktop.exe", desktop, 0x1234u),
                ("PCSteward.ElevatedHelper.exe", helper, 0xBEEFu), ("Resources\\A.xbf", resource, null));
            byte[] signedDesktop = ReleaseCheckFixture.Sign(desktop, 1001, 0x5555);
            if (tamperExecutable) signedDesktop[0x250] ^= 0xFF;
            build.Files["App.xbf"] = changeDataFile ? Encoding.ASCII.GetBytes("app resourcE") : app;
            build.Files["PCSteward.Desktop.exe"] = signedDesktop;
            build.Files["PCSteward.ElevatedHelper.exe"] = ReleaseCheckFixture.Sign(helper, 1001, 0x6666);
            build.Files["Resources\\A.xbf"] = resource;
            build.Manifest = ManifestFor(productVersion, build.Files);
            return build;
        }

        internal List<SelfUpdateTreeFile> Tree() =>
        [
            .. Files.Select(pair => new SelfUpdateTreeFile(pair.Key, pair.Value.LongLength, Sha(pair.Value))),
            new(ReleaseManifestPolicy.FileName, Manifest.LongLength, Sha(Manifest)),
        ];

        internal SelfUpdateTreeVerdict Check(out SelfUpdateExpectedInstall? expected) =>
            SelfUpdateInstallPolicy.CheckExtracted(Record, Job, Tree(), Manifest, path => Files[path], out expected);

        internal SelfUpdateTreeVerdict CheckTree(List<SelfUpdateTreeFile> tree) =>
            SelfUpdateInstallPolicy.CheckExtracted(Record, Job, tree, Manifest, path => Files[path], out _);

        internal SelfUpdateTreeVerdict CheckWithManifest(byte[]? manifest) =>
            SelfUpdateInstallPolicy.CheckExtracted(Record, Job, Tree(), manifest, path => Files[path], out _);

        private static byte[] ManifestFor(string productVersion, Dictionary<string, byte[]> files) => Encoding.ASCII.GetBytes(
            "{\"format\":\"pc-steward-release-manifest:1\",\"productVersion\":\"" + productVersion + "\",\"files\":[" +
            string.Join(",", files.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair =>
                "{\"path\":\"" + pair.Key.Replace("\\", "\\\\", StringComparison.Ordinal) + "\",\"bytes\":" + pair.Value.Length + ",\"sha256\":\"" + Sha(pair.Value) + "\"}")) + "]}");

        internal static (ReleaseCheckRecord Record, byte[] Bytes) RecordAndBytes(IReadOnlyList<string> pins, params (string Path, byte[] Bytes, uint? Checksum)[] files)
        {
            string json = "{\"schema\":\"pc-steward-release-check:1\",\"repository\":\"DanDG923/pc-steward\",\"runId\":4242,\"sourceCommit\":\"" +
                ReleaseCheckFixture.Commit + "\",\"productVersion\":\"0.1.2-private-beta\",\"msiVersion\":\"0.1.2\",\"pins\":{\"primary\":\"" +
                pins[0] + "\",\"backup\":\"" + pins[1] + "\"},\"candidateManifestSha256\":\"" + new string('D', 64) +
                "\",\"msiTablesSha256\":\"" + new string('C', 64) + "\",\"files\":[" +
                string.Join(",", files.OrderBy(file => file.Path, StringComparer.Ordinal).Select(file =>
                    "{\"path\":\"" + file.Path.Replace("\\", "\\\\", StringComparison.Ordinal) + "\",\"bytes\":" + file.Bytes.Length + ",\"sha256\":\"" + Sha(file.Bytes) + "\"}")) +
                "],\"executables\":[" + string.Join(",", files.Where(file => file.Checksum is not null).Select(file =>
                    "{\"path\":\"" + file.Path + "\",\"originalChecksum\":" + file.Checksum + "}")) + "]}";
            Assert.IsTrue(ReleaseCheckPolicy.TryRead(Encoding.ASCII.GetBytes(json), out ReleaseCheckRecord? record), json);
            return (record!, Encoding.ASCII.GetBytes(json));
        }
    }
}
