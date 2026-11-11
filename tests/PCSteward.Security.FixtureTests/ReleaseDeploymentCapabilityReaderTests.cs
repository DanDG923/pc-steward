using PCSteward.Domain;
using PCSteward.Execution;
using Status = PCSteward.Execution.HelperProtocolCapabilityStatus;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// FR-UPD-15, TM-03/04/07/13; ADR-034 §4: the Windows capability reader on an MSI release install, over real files and
/// handles. The release manifest (and never the ADR-022 state root) selects the release path; every binding-set leaf is held
/// share-read and hashed against the manifest; both executables must carry the same compiled pin; a release install beside
/// the ADR-022 state root is a mismatch; and the production pin check refuses unsigned fixture executables. Windows-native:
/// on other platforms each test reports <see cref="PlatformNotSupportedException"/> (never silently skipped).
/// </summary>
[TestClass]
public sealed class ReleaseDeploymentCapabilityReaderTests
{
    private static readonly UpdateRouteContract Route = DeploymentCapabilityPolicy.CapabilityOperationAllowlist[0];

    [TestMethod]
    public void AConsistentPinnedReleaseInstallIsSupportedAndHoldsTheBindingSet()
    {
        RequireWindows();
        using DeploymentCapabilityReaderTests.Fixture fixture = new();
        ReleaseTree tree = ReleaseTree.Create(fixture.Root);
        List<(string Desktop, string Helper)> pinChecks = [];
        OwnerOnlyDeploymentCapabilityReader reader = new(DeploymentCapabilityRole.Desktop, () => tree.Layout, (_, _) => true,
            (desktop, helper) => { pinChecks.Add((desktop, helper)); return true; });

        HelperProtocolCapabilityResult result = reader.TryHold(Route, out HeldHelperBinding? binding);

        Assert.AreEqual(Status.Supported, result.Status);
        Assert.IsNotNull(binding);
        using (binding)
        {
            Assert.IsTrue(binding.IsHolding);
            Assert.IsNull(binding.LedgerSha256, "a release install has no ledger");
            CollectionAssert.AreEquivalent(DeploymentCapabilityPolicy.BindingSet.ToArray(), binding.VerifiedHashes.Keys.ToArray());
            // Held share-read: no leaf can be replaced while held.
            Assert.ThrowsExactly<IOException>(() => File.WriteAllText(tree.Leaf(InstalledHelperLaunchTrust.HelperExecutableName), "replaced"));
        }
        Assert.HasCount(1, pinChecks);
        Assert.AreEqual(tree.Leaf(InstalledHelperLaunchTrust.DesktopExecutableName), pinChecks[0].Desktop);
        Assert.AreEqual(tree.Leaf(InstalledHelperLaunchTrust.HelperExecutableName), pinChecks[0].Helper);
        Assert.AreEqual(Status.Supported, reader.Evaluate(Route).Status);
    }

    [TestMethod]
    public void TheHelperRoleReadsTheSameReleaseInstall()
    {
        RequireWindows();
        using DeploymentCapabilityReaderTests.Fixture fixture = new();
        ReleaseTree tree = ReleaseTree.Create(fixture.Root);
        DeploymentCapabilityLayout helperLayout = tree.Layout with { ProcessPath = tree.Leaf(InstalledHelperLaunchTrust.HelperExecutableName) };
        OwnerOnlyDeploymentCapabilityReader helper = new(DeploymentCapabilityRole.Helper, () => helperLayout, (_, _) => true, (_, _) => true);
        Assert.AreEqual(Status.Supported, helper.Evaluate(Route).Status);
        OwnerOnlyDeploymentCapabilityReader wrongProcess = new(DeploymentCapabilityRole.Helper, () => tree.Layout, (_, _) => true, (_, _) => true);
        Assert.AreEqual(Status.Unreadable, wrongProcess.Evaluate(Route).Status);
    }

    [TestMethod]
    public void WithoutTheSamePinTheReleaseIsAMismatchAndTheProductionCheckRefusesUnsignedFiles()
    {
        RequireWindows();
        using DeploymentCapabilityReaderTests.Fixture fixture = new();
        ReleaseTree tree = ReleaseTree.Create(fixture.Root);
        OwnerOnlyDeploymentCapabilityReader unpinned = new(DeploymentCapabilityRole.Desktop, () => tree.Layout, (_, _) => true, (_, _) => false);
        Assert.AreEqual(Status.Mismatch, unpinned.Evaluate(Route).Status);
        // The production constructor uses the compiled pins, which no unsigned fixture can carry.
        OwnerOnlyDeploymentCapabilityReader production = new(DeploymentCapabilityRole.Desktop, () => tree.Layout, (_, _) => true);
        Assert.AreEqual(Status.Mismatch, production.TryHold(Route, out HeldHelperBinding? binding).Status);
        Assert.IsNull(binding);
    }

    [TestMethod]
    public void AReleaseInstallBesideTheOwnerOnlyStateRootIsAMismatch()
    {
        RequireWindows();
        using DeploymentCapabilityReaderTests.Fixture fixture = new();
        ReleaseTree tree = ReleaseTree.Create(fixture.Root);
        Directory.CreateDirectory(tree.Layout.StateRoot);
        OwnerOnlyDeploymentCapabilityReader reader = new(DeploymentCapabilityRole.Desktop, () => tree.Layout, (_, _) => true, (_, _) => true);
        Assert.AreEqual(Status.Mismatch, reader.Evaluate(Route).Status);
    }

    [TestMethod]
    public void AnUnprotectedManifestOrLeafIsUnreadable()
    {
        RequireWindows();
        using DeploymentCapabilityReaderTests.Fixture fixture = new();
        ReleaseTree tree = ReleaseTree.Create(fixture.Root);
        OwnerOnlyDeploymentCapabilityReader manifestUnprotected = new(DeploymentCapabilityRole.Desktop, () => tree.Layout,
            (_, files) => !files.Any(file => file.EndsWith(ReleaseManifestPolicy.FileName, StringComparison.Ordinal)), (_, _) => true);
        Assert.AreEqual(Status.Unreadable, manifestUnprotected.Evaluate(Route).Status);
        OwnerOnlyDeploymentCapabilityReader leafUnprotected = new(DeploymentCapabilityRole.Desktop, () => tree.Layout,
            (_, files) => !files.Any(file => file.EndsWith(InstalledHelperLaunchTrust.HelperExecutableName, StringComparison.Ordinal)), (_, _) => true);
        Assert.AreEqual(Status.Unreadable, leafUnprotected.Evaluate(Route).Status);
    }

    [TestMethod]
    public void AChangedMissingOrUnlistedFileOrABadManifestFailsClosed()
    {
        RequireWindows();
        foreach ((string name, Action<ReleaseTree> change, Status expected) in new (string, Action<ReleaseTree>, Status)[]
        {
            ("changed helper", tree => File.WriteAllText(tree.Leaf(InstalledHelperLaunchTrust.HelperExecutableName), "other helper"), Status.Mismatch),
            ("changed statement", tree => File.WriteAllText(tree.Leaf(DeploymentCapabilityPolicy.CapabilityFileName), "pc-steward-deployment-capability:1\n"), Status.Mismatch),
            ("missing assembly", tree => File.Delete(tree.Leaf("PCSteward.Domain.dll")), Status.Unreadable),
            ("missing statement", tree => File.Delete(tree.Leaf(DeploymentCapabilityPolicy.CapabilityFileName)), Status.Mismatch),
            ("unlisted leaf", tree => tree.WriteManifest(entries => entries.RemoveAll(entry => entry.Path == "PCSteward.Execution.dll")), Status.Mismatch),
            ("malformed manifest", tree => File.WriteAllText(tree.ManifestPath, "{}"), Status.Unreadable),
            ("oversized manifest", tree => File.WriteAllBytes(tree.ManifestPath, new byte[ReleaseManifestPolicy.MaximumManifestBytes + 1]), Status.Unreadable),
        })
        {
            using DeploymentCapabilityReaderTests.Fixture fixture = new();
            ReleaseTree tree = ReleaseTree.Create(fixture.Root);
            change(tree);
            OwnerOnlyDeploymentCapabilityReader reader = new(DeploymentCapabilityRole.Desktop, () => tree.Layout, (_, _) => true, (_, _) => true);
            Assert.AreEqual(expected, reader.TryHold(Route, out HeldHelperBinding? binding).Status, name);
            Assert.IsNull(binding, name);
        }
    }

    [TestMethod]
    public void WithNeitherInstallRecordTheCapabilityIsAbsent()
    {
        RequireWindows();
        using DeploymentCapabilityReaderTests.Fixture fixture = new();
        ReleaseTree tree = ReleaseTree.Create(fixture.Root);
        File.Delete(tree.ManifestPath);
        OwnerOnlyDeploymentCapabilityReader reader = new(DeploymentCapabilityRole.Desktop, () => tree.Layout, (_, _) => true, (_, _) => true);
        Assert.AreEqual(Status.Absent, reader.Evaluate(Route).Status);
    }

    private static void RequireWindows()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows-native test (ADR-034 §4 release reader): it runs on Windows CI and is reported as failed on other platforms.");
    }

    /// <summary>A release install on disk: the binding set and two extra files under the install root, and the release manifest.</summary>
    private sealed class ReleaseTree
    {
        private ReleaseTree(string root)
        {
            InstallRoot = Path.Combine(root, "PC Steward");
            StateRoot = Path.Combine(root, OwnerOnlyDeploymentCapabilityReader.StateDirectoryName);
        }

        internal string InstallRoot { get; }
        private string StateRoot { get; }
        internal string ManifestPath => Path.Combine(InstallRoot, ReleaseManifestPolicy.FileName);
        internal DeploymentCapabilityLayout Layout =>
            new(InstallRoot, StateRoot, Leaf(InstalledHelperLaunchTrust.DesktopExecutableName), DeploymentCapabilityPolicyTests.Sid);

        internal string Leaf(string leaf) => Path.Combine(InstallRoot, leaf);

        internal static ReleaseTree Create(string root)
        {
            ReleaseTree tree = new(root);
            ReleaseManifestPolicyTests.Release release = ReleaseManifestPolicyTests.Release.Create();
            foreach ((string path, byte[] bytes) in release.Contents)
            {
                string target = tree.Leaf(path);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllBytes(target, bytes);
            }
            File.WriteAllBytes(tree.ManifestPath, release.ManifestBytes);
            return tree;
        }

        internal void WriteManifest(Action<List<ReleaseManifestPolicyTests.Release.Entry>> edit) =>
            File.WriteAllBytes(ManifestPath, ReleaseManifestPolicyTests.Release.Create(edit).ManifestBytes);
    }
}
