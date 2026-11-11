using System.Text.RegularExpressions;

namespace PCSteward.ArchitectureTests;

/// <summary>
/// ADR-034 §4 and "Follow-up controls and tests": the release path of the Windows capability reader. Only the release
/// manifest in the fixed install root selects it; a release install beside the ADR-022 state root is a mismatch; the pure
/// policy reads nothing itself; the production reader checks the executables with the compiled pins only; and the
/// substitute pin check is a fixture seam that no production code uses.
/// </summary>
[TestClass]
public sealed class ReleaseCapabilityReaderBoundaryTests
{
    [TestMethod]
    public void OnlyTheProtectedReleaseManifestSelectsTheReleasePathAndBothRecordsAreAMismatch()
    {
        string reader = File.ReadAllText(Path.Combine(Execution(), "OwnerOnlyDeploymentCapabilityReader.cs"));
        StringAssert.Contains(reader, "string releaseManifestPath = Path.Combine(installRoot, ReleaseManifestPolicy.FileName);");
        StringAssert.Contains(reader, "if (releasePresent && Directory.Exists(stateRoot)) return HelperProtocolCapabilityStatus.Mismatch;");
        StringAssert.Contains(reader, "if (!protection(installRoot, [releaseManifestPath])) return HelperProtocolCapabilityStatus.Unreadable;");
        StringAssert.Contains(reader, "byte[]? manifest = ReadWhole(releaseManifestPath, ReleaseManifestPolicy.MaximumManifestBytes);");
        // The layout check (process path inside the install root) runs before either record is looked at.
        Assert.IsLessThan(reader.IndexOf("string releaseManifestPath", StringComparison.Ordinal),
            reader.IndexOf("Path.Combine(installRoot, ExpectedProcessLeaf(role))", StringComparison.Ordinal));
        // Both paths converge on the single binding creation point.
        Assert.AreEqual(1, Regex.Count(reader, @"HeldHelperBinding\.TryCreate\("));
        StringAssert.Contains(reader, "ReleaseCapabilityInputs complete = inputs with { ObservedHashes = observed, ExecutablesCarrySamePin = samePin };");
        // ADR-032 revision 2 §6 step 2: a route evaluation, or (no route) PC Steward's own update, which needs the release path.
        StringAssert.Contains(reader, "return (route is { } named ? ReleaseManifestPolicy.Evaluate(complete, named) : ReleaseManifestPolicy.EvaluateInstalledRelease(complete)).Status;");
        StringAssert.Contains(reader, ": HelperProtocolCapabilityStatus.Absent;");
    }

    [TestMethod]
    public void TheProductionReaderChecksTheExecutablesWithTheCompiledPinsOnly()
    {
        string reader = File.ReadAllText(Path.Combine(Execution(), "OwnerOnlyDeploymentCapabilityReader.cs"));
        StringAssert.Contains(reader, ": this(role, resolveLayout, protection, ExecutablesCarrySameCompiledPin)");
        Assert.IsTrue(Regex.IsMatch(reader,
            @"internal static bool ExecutablesCarrySameCompiledPin\(string desktopPath, string helperPath\) =>\s+AuthenticodeIdentity\.HaveSamePinnedSigner\(desktopPath, helperPath, ReleaseSigningPins\.Compiled\);"));
        // No production source uses the four-argument fixture constructor or names the substitute.
        string src = Path.Combine(FindRepositoryRoot(), "src");
        foreach (string path in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj")))
        {
            if (Path.GetFileName(path) == "OwnerOnlyDeploymentCapabilityReader.cs") continue;
            string source = File.ReadAllText(path);
            Assert.IsFalse(source.Contains("executablesCarrySamePin", StringComparison.Ordinal), $"{path} supplies a pin-check substitute");
            Assert.IsFalse(source.Contains("ExecutablesCarrySameCompiledPin", StringComparison.Ordinal), $"{path} names the reader's pin check");
        }
    }

    [TestMethod]
    public void ThePolicyIsPureAndBounded()
    {
        string policy = File.ReadAllText(Path.Combine(Execution(), "ReleaseManifestPolicy.cs"));
        foreach (string forbidden in new[]
        {
            "File.", "Directory.", "SafeFileHandle", "Registry", "Environment.", "HttpClient", "Process", "Sqlite", "AppContext",
            "MemoryCache", "Lazy<", "WinVerifyTrust", "AuthenticodeIdentity",
        })
            Assert.IsFalse(policy.Contains(forbidden, StringComparison.Ordinal), $"ReleaseManifestPolicy.cs contains {forbidden}");
        StringAssert.Contains(policy, "internal const int MaximumManifestBytes = 1024 * 1024;");
        StringAssert.Contains(policy, "internal const int MaximumFiles = 4096;");
        StringAssert.Contains(policy, "return inputs.ExecutablesCarrySamePin ? HelperProtocolCapabilityStatus.Supported : HelperProtocolCapabilityStatus.Mismatch;");
    }

    private static string Execution() => Path.Combine(FindRepositoryRoot(), "src", "PCSteward.Execution");

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "PCSteward.slnx"))) return current.FullName;
        throw new InvalidOperationException("Repository root was not found.");
    }
}
