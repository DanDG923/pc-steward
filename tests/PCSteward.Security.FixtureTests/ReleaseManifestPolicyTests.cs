using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PCSteward.Domain;
using PCSteward.Execution;
using Status = PCSteward.Execution.HelperProtocolCapabilityStatus;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// FR-UPD-15, TM-03/04/07/13; ADR-034 §4 and "Follow-up controls and tests": the pure release-manifest policy. Strict
/// parsing (oversized, duplicate-key, unknown-key, bad-path and bad-version manifests are refused), every binding-set leaf
/// listed exactly once, and the statement, binding-set hashes and same-pin result checked against the manifest
/// (missing-file and wrong-hash cases). No file access: these run on every platform.
/// </summary>
[TestClass]
public sealed class ReleaseManifestPolicyTests
{
    private static readonly UpdateRouteContract Route = DeploymentCapabilityPolicy.CapabilityOperationAllowlist[0];

    [TestMethod]
    public void AConsistentReleaseInstallIsSupportedForEveryAllowListedRoute()
    {
        Release release = Release.Create();
        foreach (UpdateRouteContract route in DeploymentCapabilityPolicy.CapabilityOperationAllowlist)
            Assert.AreEqual(Status.Supported, ReleaseManifestPolicy.Evaluate(release.Inputs(), route).Status, route.ToString());
        Assert.IsTrue(ReleaseManifestPolicy.TryRead(release.ManifestBytes, out ReleaseManifest? manifest));
        Assert.AreEqual("0.1.0-private-beta", manifest!.ProductVersion);
        Assert.HasCount(DeploymentCapabilityPolicy.BindingSet.Count + 2, manifest.Files);
    }

    [TestMethod]
    public void NoManifestIsAbsentAndAnUnreadableManifestIsUnreadable()
    {
        Release release = Release.Create();
        Assert.AreEqual(Status.Absent, ReleaseManifestPolicy.Evaluate(release.Inputs() with { ManifestBytes = null }, Route).Status);
        Assert.AreEqual(Status.Unreadable, ReleaseManifestPolicy.Evaluate(release.Inputs() with { ManifestBytes = "{"u8.ToArray() }, Route).Status);
    }

    [TestMethod]
    public void BothExecutablesMustCarryTheSamePin() =>
        Assert.AreEqual(Status.Mismatch, ReleaseManifestPolicy.Evaluate(Release.Create().Inputs() with { ExecutablesCarrySamePin = false }, Route).Status);

    [TestMethod]
    public void EveryBindingLeafMustBeListedExactlyOnceAtTheRootWithItsExactName()
    {
        foreach (string leaf in DeploymentCapabilityPolicy.BindingSet)
        {
            Release missing = Release.Create(files => files.RemoveAll(file => file.Path == leaf));
            Assert.AreEqual(Status.Mismatch, ReleaseManifestPolicy.Evaluate(missing.Inputs(), Route).Status, $"missing {leaf}");
            Release nested = Release.Create(files => files.ForEach(file => file.Path = file.Path == leaf ? "sub\\" + leaf : file.Path));
            Assert.AreEqual(Status.Mismatch, ReleaseManifestPolicy.Evaluate(nested.Inputs(), Route).Status, $"nested {leaf}");
            Release cased = Release.Create(files => files.ForEach(file => file.Path = file.Path == leaf ? leaf.ToUpperInvariant() : file.Path));
            Assert.AreEqual(Status.Mismatch, ReleaseManifestPolicy.Evaluate(cased.Inputs(), Route).Status, $"case of {leaf}");
        }
    }

    [TestMethod]
    public void TheStatementMustMatchItsManifestEntryAndAllowTheRoute()
    {
        Release release = Release.Create();
        byte[] changed = [.. release.Capability];
        changed[^2] ^= 1;
        Assert.AreEqual(Status.Mismatch, ReleaseManifestPolicy.Evaluate(release.Inputs() with { CapabilityBytes = changed }, Route).Status);
        Assert.AreEqual(Status.Mismatch, ReleaseManifestPolicy.Evaluate(release.Inputs() with { CapabilityBytes = null }, Route).Status);
        Assert.AreEqual(Status.Mismatch, ReleaseManifestPolicy.Evaluate(release.Inputs(), (UpdateRouteContract)int.MaxValue).Status);
    }

    [TestMethod]
    public void EveryBindingHashAndLengthMustEqualTheManifest()
    {
        Release release = Release.Create();
        foreach (string leaf in DeploymentCapabilityPolicy.BindingSet)
        {
            Dictionary<string, DeploymentObservedFile> wrongHash = new(release.Observed) { [leaf] = release.Observed[leaf] with { Sha256 = new string('0', 64) } };
            Assert.AreEqual(Status.Mismatch, ReleaseManifestPolicy.Evaluate(release.Inputs() with { ObservedHashes = wrongHash }, Route).Status, leaf);
            Dictionary<string, DeploymentObservedFile> wrongLength = new(release.Observed) { [leaf] = release.Observed[leaf] with { Length = release.Observed[leaf].Length + 1 } };
            Assert.AreEqual(Status.Mismatch, ReleaseManifestPolicy.Evaluate(release.Inputs() with { ObservedHashes = wrongLength }, Route).Status, leaf);
            Dictionary<string, DeploymentObservedFile> missing = new(release.Observed);
            missing.Remove(leaf);
            Assert.AreEqual(Status.Unreadable, ReleaseManifestPolicy.Evaluate(release.Inputs() with { ObservedHashes = missing }, Route).Status, leaf);
        }
        Dictionary<string, DeploymentObservedFile> oversized = new(release.Observed)
        {
            [DeploymentCapabilityPolicy.BindingSet[0]] = new(DeploymentCapabilityPolicy.MaximumBindingFileBytes + 1, new string('A', 64)),
        };
        Assert.AreEqual(Status.Unreadable, ReleaseManifestPolicy.Evaluate(release.Inputs() with { ObservedHashes = oversized }, Route).Status);
    }

    [TestMethod]
    public void TheStagesStopBeforeLaterInputs()
    {
        Release release = Release.Create();
        ReleaseCapabilityInputs manifestOnly = new(release.ManifestBytes, null, new Dictionary<string, DeploymentObservedFile>(), false);
        Assert.IsTrue(ReleaseManifestPolicy.PassesThrough(manifestOnly, Route, DeploymentCapabilityStage.Ledger, out _));
        Assert.IsFalse(ReleaseManifestPolicy.PassesThrough(manifestOnly, Route, DeploymentCapabilityStage.Statement, out Status failure));
        Assert.AreEqual(Status.Mismatch, failure);
        Assert.IsTrue(ReleaseManifestPolicy.PassesThrough(manifestOnly with { CapabilityBytes = release.Capability }, Route, DeploymentCapabilityStage.Statement, out _));
        Assert.IsFalse(ReleaseManifestPolicy.PassesThrough(manifestOnly, Route, DeploymentCapabilityStage.BindingSet, out failure));
        Assert.AreEqual(Status.Unreadable, failure);
        // Passing a stage never means Supported overall: the pin and hashes are still required.
        Assert.AreNotEqual(Status.Supported, ReleaseManifestPolicy.Evaluate(manifestOnly with { CapabilityBytes = release.Capability }, Route).Status);
    }

    [TestMethod]
    public void TheParserRefusesEveryMalformedManifest()
    {
        Release release = Release.Create();
        string valid = Encoding.UTF8.GetString(release.ManifestBytes);
        Assert.IsTrue(ReleaseManifestPolicy.TryRead(release.ManifestBytes, out _));
        string[] refused =
        [
            "",
            "[]",
            "null",
            valid.Replace("\"format\":\"pc-steward-release-manifest:1\"", "\"format\":\"pc-steward-release-manifest:2\"", StringComparison.Ordinal),
            valid.Replace("{\"format\"", "{\"extra\":1,\"format\"", StringComparison.Ordinal),
            valid.Replace("{\"format\"", "{\"Format\":\"x\",\"format\"", StringComparison.Ordinal),
            valid.Replace(",\"productVersion\":\"0.1.0-private-beta\"", string.Empty, StringComparison.Ordinal),
            valid.Replace("0.1.0-private-beta", "01.1.0", StringComparison.Ordinal),
            valid.Replace("0.1.0-private-beta", "0.1", StringComparison.Ordinal),
            valid.Replace("0.1.0-private-beta", "0.1.0-", StringComparison.Ordinal),
            valid.Replace("0.1.0-private-beta", "0.1.0+build", StringComparison.Ordinal),
            valid.Replace("\"files\":[", "\"files\":[1,", StringComparison.Ordinal),
            valid.Replace("\"files\":[", "\"files\":[{\"path\":\"x.dll\",\"bytes\":1,\"sha256\":\"" + new string('A', 64) + "\",\"more\":1},", StringComparison.Ordinal),
            valid.Replace("\"files\":[", "\"files\":[{\"path\":\"x.dll\",\"bytes\":-1,\"sha256\":\"" + new string('A', 64) + "\"},", StringComparison.Ordinal),
            valid.Replace("\"files\":[", "\"files\":[{\"path\":\"x.dll\",\"bytes\":1.5,\"sha256\":\"" + new string('A', 64) + "\"},", StringComparison.Ordinal),
            valid.Replace("\"files\":[", "\"files\":[{\"path\":\"x.dll\",\"bytes\":1,\"sha256\":\"" + new string('G', 64) + "\"},", StringComparison.Ordinal),
            valid.Replace("\"files\":[", "\"files\":[{\"path\":\"x.dll\",\"bytes\":1,\"sha256\":\"ABC\"},", StringComparison.Ordinal),
            valid.Replace("\"files\":[", "\"files\":[{\"path\":\"pcsteward.desktop.EXE\",\"bytes\":1,\"sha256\":\"" + new string('A', 64) + "\"},", StringComparison.Ordinal),
            valid.Replace("\"files\":[", "\"files\":[{\"path\":\"" + ReleaseManifestPolicy.FileName + "\",\"bytes\":1,\"sha256\":\"" + new string('A', 64) + "\"},", StringComparison.Ordinal),
            valid + " {}",
            valid[..^1] + ",}",
        ];
        foreach (string text in refused)
            Assert.IsFalse(ReleaseManifestPolicy.TryRead(Encoding.UTF8.GetBytes(text), out _), text.Length > 120 ? text[..120] : text);

        Assert.IsFalse(ReleaseManifestPolicy.TryRead([0xEF, 0xBB, 0xBF, .. release.ManifestBytes], out _), "BOM");
        Assert.IsFalse(ReleaseManifestPolicy.TryRead(Encoding.UTF8.GetBytes(valid.Replace("PCSteward.Desktop.dll", "PCSteward.Desktop\u00e9.dll", StringComparison.Ordinal)), out _), "non-ASCII path");
        Assert.IsFalse(ReleaseManifestPolicy.TryRead([.. release.ManifestBytes[..^1], 0xFF, (byte)'}'], out _), "invalid UTF-8");
        Assert.IsFalse(ReleaseManifestPolicy.TryRead(new byte[ReleaseManifestPolicy.MaximumManifestBytes + 1], out _), "oversized");
        Release empty = Release.Create(files => files.Clear());
        Assert.IsFalse(ReleaseManifestPolicy.TryRead(empty.ManifestBytes, out _), "no files");
        Release tooMany = Release.Create(files => files.AddRange(Enumerable.Range(0, ReleaseManifestPolicy.MaximumFiles).Select(index =>
            new Release.Entry($"extra\\file{index}.dll", 1, new string('A', 64)))));
        Assert.IsFalse(ReleaseManifestPolicy.TryRead(tooMany.ManifestBytes, out _), "too many files");
        Release duplicate = Release.Create(files => files.Add(new("Assets\\Logo.png", 1, new string('A', 64))));
        Assert.IsFalse(ReleaseManifestPolicy.TryRead(duplicate.ManifestBytes, out _), "duplicate path");
        Release cased = Release.Create(files => files.Add(new("assets\\logo.PNG", 1, new string('A', 64))));
        Assert.IsFalse(ReleaseManifestPolicy.TryRead(cased.ManifestBytes, out _), "duplicate path by case");
    }

    [TestMethod]
    [DataRow("PCSteward.Desktop.exe", true)]
    [DataRow("Assets\\Logo.png", true)]
    [DataRow("a\\b\\c.dll", true)]
    [DataRow("", false)]
    [DataRow("\\root.dll", false)]
    [DataRow("trailing\\", false)]
    [DataRow("a\\\\b.dll", false)]
    [DataRow("..\\escape.dll", false)]
    [DataRow("a\\..\\b.dll", false)]
    [DataRow(".\\here.dll", false)]
    [DataRow("C:\\Windows\\x.dll", false)]
    [DataRow("file.dll:stream", false)]
    [DataRow("a/b.dll", false)]
    [DataRow("wild*.dll", false)]
    [DataRow("dot.", false)]
    [DataRow("space ", false)]
    [DataRow("tab\t.dll", false)]
    public void InstallPathsStayBelowTheRoot(string path, bool expected) =>
        Assert.AreEqual(expected, ReleaseManifestPolicy.IsRelativeInstallPath(path));

    /// <summary>A consistent release: every binding leaf with synthetic content plus two extra installed files.</summary>
    internal sealed class Release
    {
        internal sealed class Entry(string path, long bytes, string sha256)
        {
            internal string Path { get; set; } = path;
            internal long Bytes { get; } = bytes;
            internal string Sha256 { get; } = sha256;
        }

        internal byte[] ManifestBytes { get; private init; } = [];
        internal byte[] Capability { get; } = DeploymentCapabilityPolicy.CanonicalCapabilityBytes();
        internal Dictionary<string, DeploymentObservedFile> Observed { get; } = new(StringComparer.Ordinal);
        internal Dictionary<string, byte[]> Contents { get; } = new(StringComparer.Ordinal);

        internal ReleaseCapabilityInputs Inputs() => new(ManifestBytes, Capability, Observed, true);

        internal static Release Create(Action<List<Entry>>? edit = null)
        {
            Release release = new();
            List<Entry> entries = [];
            foreach (string leaf in DeploymentCapabilityPolicy.BindingSet)
            {
                byte[] bytes = leaf == DeploymentCapabilityPolicy.CapabilityFileName ? release.Capability : Encoding.UTF8.GetBytes("release " + leaf);
                release.Contents[leaf] = bytes;
                string hash = Convert.ToHexString(SHA256.HashData(bytes));
                release.Observed[leaf] = new(bytes.Length, hash);
                entries.Add(new(leaf, bytes.Length, hash));
            }
            foreach (string extra in new[] { "Assets\\Logo.png", "Microsoft.WindowsAppRuntime.Bootstrap.dll" })
            {
                byte[] bytes = Encoding.UTF8.GetBytes("extra " + extra);
                release.Contents[extra] = bytes;
                entries.Add(new(extra, bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()));
            }
            edit?.Invoke(entries);
            return new Release(release, Render(entries));
        }

        private Release()
        {
        }

        private Release(Release source, byte[] manifest)
        {
            ManifestBytes = manifest;
            Capability = source.Capability;
            Observed = source.Observed;
            Contents = source.Contents;
        }

        internal static byte[] Render(IEnumerable<Entry> entries)
        {
            using MemoryStream stream = new();
            using (Utf8JsonWriter writer = new(stream))
            {
                writer.WriteStartObject();
                writer.WriteString("format", ReleaseManifestPolicy.Format);
                writer.WriteString("productVersion", "0.1.0-private-beta");
                writer.WriteStartArray("files");
                foreach (Entry entry in entries)
                {
                    writer.WriteStartObject();
                    writer.WriteString("path", entry.Path);
                    writer.WriteNumber("bytes", entry.Bytes);
                    writer.WriteString("sha256", entry.Sha256);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            return stream.ToArray();
        }
    }
}
