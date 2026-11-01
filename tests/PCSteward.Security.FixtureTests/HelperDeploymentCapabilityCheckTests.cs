using System.Security.AccessControl;
using PCSteward.Domain;
using PCSteward.Execution;
using PCSteward.Provider.Chocolatey.Native;
using Status = PCSteward.Execution.HelperProtocolCapabilityStatus;
using Tree = PCSteward.Security.FixtureTests.DeploymentCapabilityReaderTests.DeploymentTree;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// FR-UPD-15, TM-03/04/07/13; Task 50 design rev 6 §5.2 A3, §6 (G1, H1, L6) and §8 N4b: the Helper-role deployment-capability
/// reader and <see cref="HelperDeploymentCapabilityCheck"/>. Portable: the role's process leaf, the peer-image rule and the
/// fixed-layout refusal on a test host. Windows-only (reported as failed elsewhere): the reader over an N1 fixture tree with
/// explicit ACLs, protected by the native item's <c>RequesterCannotModify</c> evaluated with the derived requester token
/// (ancestry from the fixture root, as the N2 observer seam does), so the protection principal is the requester and never
/// the test process.
/// </summary>
[TestClass]
public sealed class HelperDeploymentCapabilityCheckTests
{
    private const UpdateRouteContract Route = UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3;
    private const string Sid = DeploymentCapabilityPolicyTests.Sid;
    private const string InstallRoot = @"C:\Program Files\PC Steward";

    // ---- Portable ----

    [TestMethod]
    public void TheRoleSetsTheExpectedProcessLeaf()
    {
        Assert.AreEqual("PCSteward.Desktop.exe", OwnerOnlyDeploymentCapabilityReader.ExpectedProcessLeaf(DeploymentCapabilityRole.Desktop));
        Assert.AreEqual("PCSteward.ElevatedHelper.exe", OwnerOnlyDeploymentCapabilityReader.ExpectedProcessLeaf(DeploymentCapabilityRole.Helper));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new OwnerOnlyDeploymentCapabilityReader((DeploymentCapabilityRole)7, () => null, (_, _) => true));
    }

    [TestMethod]
    [DataRow(@"C:\Program Files\PC Steward\PCSteward.ElevatedHelper.exe")]
    [DataRow(@"C:\Users\u\PCSteward.Desktop.exe")]
    [DataRow(@"PCSteward.Desktop.exe")]
    [DataRow("")]
    public void APeerImageOtherThanTheInstalledDesktopIsRefusedBeforeAnyRead(string peerImage)
    {
        int protections = 0;
        HelperDeploymentCapabilityCheck check = new(() => new DeploymentCapabilityLayout(InstallRoot, @"C:\Program Files\State",
            InstallRoot + @"\PCSteward.ElevatedHelper.exe", Sid));

        HelperProtocolCapabilityResult result = check.Evaluate(Route, (_, _) => { protections++; return true; }, peerImage);

        Assert.AreEqual(Status.Unreadable, result.Status);
        Assert.AreEqual(0, protections);
    }

    [TestMethod]
    public void AMissingOrThrowingLayoutIsUnreadable()
    {
        Assert.AreEqual(Status.Unreadable, new HelperDeploymentCapabilityCheck(() => null).Evaluate(Route, (_, _) => true, InstallRoot + @"\PCSteward.Desktop.exe").Status);
        Assert.AreEqual(Status.Unreadable, new HelperDeploymentCapabilityCheck(() => throw new IOException("synthetic"))
            .Evaluate(Route, (_, _) => true, InstallRoot + @"\PCSteward.Desktop.exe").Status);
    }

    [TestMethod]
    public void TheFixedHelperLayoutResolvesOnlyFromTheInstalledHelper()
    {
        // The test host does not run as <Program Files>\PC Steward\PCSteward.ElevatedHelper.exe (§6 L6).
        Assert.IsNull(OwnerOnlyDeploymentCapabilityReader.ResolveHelperLayout());
        Assert.AreEqual(Status.Unreadable, new HelperDeploymentCapabilityCheck().Evaluate(Route, (_, _) => true, InstallRoot + @"\PCSteward.Desktop.exe").Status);
    }

    // ---- Windows: the Helper-role reader over N1 fixture ACLs with the requester token ----

    [TestMethod]
    public void AProtectedDeploymentIsSupportedWithTheRequesterAsProtectionPrincipal()
    {
        using G1Fixture fixture = new();
        int calls = 0;
        HelperProtocolCapabilityResult result = fixture.Check().Evaluate(Route, (root, files) =>
        {
            calls++;
            return fixture.Item.RequesterCannotModify(root, files);
        }, fixture.Tree.Leaf(InstalledHelperLaunchTrust.DesktopExecutableName));

        Assert.AreEqual(Status.Supported, result.Status, result.Diagnostic);
        Assert.AreEqual(2, calls, "the state root (ledger, manifest) and the install root (every binding-set leaf)");
    }

    [TestMethod]
    public void TheDesktopProcessLeafIsRefusedInTheHelperRoleAndTheReverse()
    {
        using G1Fixture fixture = new();
        DeploymentCapabilityLayout desktopLayout = fixture.Layout with { ProcessPath = fixture.Tree.Leaf(InstalledHelperLaunchTrust.DesktopExecutableName) };
        OwnerOnlyDeploymentCapabilityReader helperRole = new(DeploymentCapabilityRole.Helper, () => desktopLayout, fixture.Item.RequesterCannotModify);
        Assert.AreEqual(Status.Unreadable, helperRole.Evaluate(Route).Status);
        OwnerOnlyDeploymentCapabilityReader desktopRole = new(DeploymentCapabilityRole.Desktop, () => fixture.Layout, (_, _) => true);
        Assert.AreEqual(Status.Unreadable, desktopRole.Evaluate(Route).Status, "the desktop role refuses the helper process path");
    }

    [TestMethod]
    [DataRow("binding-leaf-writable")]
    [DataRow("helper-image-writable")]
    [DataRow("ledger-writable")]
    [DataRow("state-root-delete-child")]
    [DataRow("install-root-add-file")]
    [DataRow("fixture-root-delete-child")]
    public void AnythingTheRequesterCanModifyIsUnreadable(string scenario)
    {
        using G1Fixture fixture = new();
        const FileSystemRights Read = FileSystemRights.ReadAndExecute;
        switch (scenario)
        {
            case "binding-leaf-writable": fixture.Native.Protect(fixture.Tree.Leaf("PCSteward.Execution.dll"), true, Read | FileSystemRights.WriteData); break;
            case "helper-image-writable": fixture.Native.Protect(fixture.Tree.Leaf(InstalledHelperLaunchTrust.HelperExecutableName), true, Read | FileSystemRights.AppendData); break;
            case "ledger-writable": fixture.Native.Protect(fixture.Tree.LedgerPath, true, Read | FileSystemRights.WriteAttributes); break;
            case "state-root-delete-child": fixture.Native.Protect(fixture.Tree.StateRoot, true, Read | FileSystemRights.DeleteSubdirectoriesAndFiles); break;
            case "install-root-add-file": fixture.Native.Protect(fixture.Tree.InstallRoot, true, Read | FileSystemRights.CreateFiles); break;
            default: fixture.Native.Protect(fixture.Native.Root, true, Read | FileSystemRights.DeleteSubdirectoriesAndFiles); break;
        }

        HelperProtocolCapabilityResult result = fixture.Check().Evaluate(Route, fixture.Item.RequesterCannotModify,
            fixture.Tree.Leaf(InstalledHelperLaunchTrust.DesktopExecutableName));

        Assert.AreEqual(Status.Unreadable, result.Status, scenario);
    }

    [TestMethod]
    public void ARequesterOwnedNodeWithoutOwnerRightsIsUnreadable()
    {
        using G1Fixture fixture = new();
        // Owner = requester and no OWNER RIGHTS ACE: the owner's implicit WRITE_DAC is credited to the requester token.
        fixture.Native.Protect(fixture.Tree.Leaf("PCSteward.Domain.dll"), ownerRights: false, FileSystemRights.ReadAndExecute);

        Assert.AreEqual(Status.Unreadable, fixture.Check().Evaluate(Route, fixture.Item.RequesterCannotModify,
            fixture.Tree.Leaf(InstalledHelperLaunchTrust.DesktopExecutableName)).Status);
    }

    [TestMethod]
    public void AnImageWhoseHashDiffersFromTheLedgerIsAMismatch()
    {
        using G1Fixture fixture = new(beforeLock: tree => File.WriteAllText(tree.Leaf(InstalledHelperLaunchTrust.DesktopExecutableName), "replaced desktop image"));

        Assert.AreEqual(Status.Mismatch, fixture.Check().Evaluate(Route, fixture.Item.RequesterCannotModify,
            fixture.Tree.Leaf(InstalledHelperLaunchTrust.DesktopExecutableName)).Status);
    }

    [TestMethod]
    public void TheG1CheckIsRefusedOnceStageBHasStarted()
    {
        using G1Fixture fixture = new();
        // Stage B (its fixture observer throws, which ends the sequence): afterwards the item no longer answers the G1 check.
        Assert.ThrowsExactly<InvalidOperationException>(() => fixture.Item.ObserveProfile(EndpointFixture.Operation(), EndpointFixture.Fingerprint,
            TimeSpan.FromMinutes(1), CancellationToken.None));
        Assert.IsFalse(fixture.Item.RequesterCannotModify(fixture.Tree.InstallRoot, [fixture.Tree.Leaf("PCSteward.Domain.dll")]));
    }

    [TestMethod]
    public void TheRequesterCheckRefusesFilesOutsideTheRootAndNonCanonicalRoots()
    {
        using G1Fixture fixture = new();
        Assert.IsTrue(fixture.Item.RequesterCannotModify(fixture.Tree.InstallRoot, [fixture.Tree.Leaf("PCSteward.Domain.dll")]));
        Assert.IsFalse(fixture.Item.RequesterCannotModify(fixture.Tree.InstallRoot, [fixture.Tree.LedgerPath]), "a file of another root");
        Assert.IsFalse(fixture.Item.RequesterCannotModify(fixture.Tree.InstallRoot + @"\", []), "a non-canonical root");
        Assert.IsFalse(fixture.Item.RequesterCannotModify(fixture.Tree.InstallRoot, [fixture.Tree.Leaf(@"missing.dll")]), "a missing file");
        Assert.IsFalse(fixture.Item.RequesterCannotModify("C:", []), "a drive root is never a protected root");
    }

    /// <summary>
    /// A v2 deployment tree under an N1 fixture root, locked children-first, and a native item over the derived requester
    /// token whose G1 ancestry starts at the fixture root. Its stage-B observer refuses (no Chocolatey inputs are involved).
    /// </summary>
    private sealed class G1Fixture : IDisposable
    {
        internal G1Fixture(Action<Tree>? beforeLock = null)
        {
            Native = new N1NativeFixture();
            try
            {
                Tree = Tree.Create(Native.Root);
                beforeLock?.Invoke(Tree);
                Native.LockTree(Native.Root);
                Layout = new DeploymentCapabilityLayout(Tree.InstallRoot, Tree.StateRoot, Tree.Leaf(InstalledHelperLaunchTrust.HelperExecutableName), Sid);
                Item = NativeChocolateyHelperItem.CreateOverFixtureForTests(N1NativeFixture.DerivedToken(),
                    static (_, _, _, _, _, _) => throw new InvalidOperationException("no Chocolatey observation in the G1 fixture"),
                    Native.Root, stageCForTests: null);
            }
            catch
            {
                Native.Dispose();
                throw;
            }
        }

        internal N1NativeFixture Native { get; }
        internal Tree Tree { get; } = null!;
        internal DeploymentCapabilityLayout Layout { get; } = null!;
        internal NativeChocolateyHelperItem Item { get; } = null!;

        internal HelperDeploymentCapabilityCheck Check() => new(() => Layout);

        public void Dispose()
        {
            Item?.Dispose();
            Native.Dispose();
        }
    }
}
