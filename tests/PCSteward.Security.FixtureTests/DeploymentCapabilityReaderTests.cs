using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;
using PCSteward.Domain;
using PCSteward.Execution;
using Status = PCSteward.Execution.HelperProtocolCapabilityStatus;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// FR-UPD-15, TM-03/04/07/13; Task 50 adapter gates Part A §A.2, §A.4 steps 2–6 and 3.1/3.4/3.5, §A.7 "Ledger", "Binding",
/// "Phases" (no cache) and "Peer identity", §R A2-L3/A2-L5: the Windows deployment-capability reader, the held binding and the
/// <c>FileIdInfo</c> identity. Fixture trees live in GUID-named temp directories.
/// <para>
/// Tests that call <see cref="RequireWindows"/> exercise real Windows handles and are Windows-CI evidence. On other platforms
/// they fail with <see cref="PlatformNotSupportedException"/> (reported, never silently skipped), like the repository's other
/// native tests. The remaining tests are portable.
/// </para>
/// </summary>
[TestClass]
public sealed class DeploymentCapabilityReaderTests
{
    private const string Sid = DeploymentCapabilityPolicyTests.Sid;
    private const UpdateRouteContract Route = UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3;
    private const int ErrorAccessDenied = 5;
    private const int ErrorSharingViolation = 32;

    // ---- Portable ----

    [TestMethod]
    public void RouteOutsideTheCompiledAllowlistIsMismatchWithoutAnyRead()
    {
        int resolutions = 0, protections = 0;
        OwnerOnlyDeploymentCapabilityReader reader = new(() => { resolutions++; return null; }, (_, _) => { protections++; return true; });
        foreach (UpdateRouteContract route in Enum.GetValues<UpdateRouteContract>().Where(route => route != Route))
        {
            Assert.AreEqual(Status.Mismatch, reader.Evaluate(route).Status, route.ToString());
            Assert.AreEqual(Status.Mismatch, reader.TryHold(route, out HeldHelperBinding? binding).Status, route.ToString());
            Assert.IsNull(binding);
        }
        Assert.AreEqual(0, resolutions);
        Assert.AreEqual(0, protections);
    }

    [TestMethod]
    public void UnresolvableLayoutIsUnreadableAndNonWindowsReadsNothing()
    {
        int resolutions = 0;
        OwnerOnlyDeploymentCapabilityReader reader = new(() => { resolutions++; return null; }, (_, _) => true);
        HelperProtocolCapabilityResult result = reader.TryHold(Route, out HeldHelperBinding? binding);
        Assert.AreEqual(Status.Unreadable, result.Status);
        Assert.AreEqual("helper-capability-unreadable", result.Diagnostic);
        Assert.IsNull(binding);
        Assert.AreEqual(OperatingSystem.IsWindows() ? 1 : 0, resolutions);
    }

    [TestMethod]
    public void ThrowingLayoutOrProtectionFailsClosed()
    {
        OwnerOnlyDeploymentCapabilityReader throwingLayout = new(() => throw new InvalidOperationException("boom"), (_, _) => true);
        Assert.AreEqual(Status.Unreadable, throwingLayout.Evaluate(Route).Status);
        Assert.AreEqual(Status.Unreadable, throwingLayout.TryHold(Route, out HeldHelperBinding? binding).Status);
        Assert.IsNull(binding);
    }

    [TestMethod]
    public void HeldBindingRefusesAnIncompleteHandleSet()
    {
        Assert.IsNull(HeldHelperBinding.TryCreate(
            new Dictionary<string, SafeFileHandle>(), new Dictionary<string, string>(), new Dictionary<string, DeploymentObservedFile>(),
            InstalledHelperLaunchTrust.HelperExecutableName));
    }

    // ---- Windows-native: share-read hold and FileIdInfo (A-M1, A-M2, A-Q1 partial evidence) ----

    [TestMethod]
    public void ShareReadHoldDeniesWriteDeleteRenameAndReplacementButAllowsReads()
    {
        RequireWindows("A-M2 share-read hold");
        using Fixture fixture = new();
        string held = fixture.WriteFile("held.dll", "held bytes");
        string other = fixture.WriteFile("other.dll", "replacement bytes");
        string elsewhere = Path.Combine(fixture.Root, "renamed.dll");
        using (SafeFileHandle? handle = DeploymentFileAccess.TryOpenShareRead(held))
        {
            Assert.IsNotNull(handle);
            AssertRefused(() => File.Delete(held));
            AssertRefused(() => File.Move(held, elsewhere));
            AssertRefused(() => File.Move(other, held, overwrite: true));
            AssertRefused(() => File.WriteAllText(held, "tampered"));
            AssertRefused(() => File.OpenHandle(held, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete).Dispose());
            // Reads stay possible, including an image-loader-style open (read access, read and delete sharing).
            Assert.AreEqual("held bytes", File.ReadAllText(held));
            using (SafeFileHandle loaderStyle = File.OpenHandle(held, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
                Assert.IsFalse(loaderStyle.IsInvalid);
            Assert.AreEqual("held bytes", Encoding.UTF8.GetString(DeploymentFileAccess.TryReadBounded(handle, 1024)!));
            Assert.IsTrue(File.Exists(other), "the refused replacement must leave its source in place");
        }
        File.Move(other, held, overwrite: true);
        Assert.AreEqual("replacement bytes", File.ReadAllText(held));
        File.Delete(held);
    }

    [TestMethod]
    public void FileIdInfoIsStableForOneFileAndDiffersAfterReplacementOrCopy()
    {
        RequireWindows("A-M1 FileIdInfo");
        using Fixture fixture = new();
        string target = fixture.WriteFile("PCSteward.ElevatedHelper.exe", "helper image");
        string replacement = fixture.WriteFile("replacement.exe", "helper image");
        string copy = Path.Combine(fixture.Root, "copy.exe");
        File.Copy(target, copy);

        Assert.IsTrue(DeploymentFileAccess.TryReadIdentity(target, out WindowsFileIdentity first));
        Assert.IsTrue(DeploymentFileAccess.TryReadIdentity(target, out WindowsFileIdentity second));
        Assert.AreEqual(first, second, "two opens of one file give one identity");
        using (SafeFileHandle? handle = DeploymentFileAccess.TryOpenShareRead(target))
        {
            Assert.IsTrue(DeploymentFileAccess.TryReadIdentity(handle!, out WindowsFileIdentity throughHandle));
            Assert.AreEqual(first, throughHandle);
        }
        Assert.IsTrue(DeploymentFileAccess.TryReadIdentity(copy, out WindowsFileIdentity copied));
        Assert.AreNotEqual(first, copied, "equal bytes at another path are another file");
        Assert.AreEqual(first.VolumeSerialNumber, copied.VolumeSerialNumber);

        Assert.IsTrue(DeploymentFileAccess.TryReadIdentity(replacement, out WindowsFileIdentity replacementIdentity));
        File.Move(replacement, target, overwrite: true);
        Assert.IsTrue(DeploymentFileAccess.TryReadIdentity(target, out WindowsFileIdentity afterReplace));
        Assert.AreNotEqual(first, afterReplace, "same path and bytes, replaced file record");
        Assert.AreEqual(replacementIdentity, afterReplace, "a same-volume move keeps the moved file's identity");

        Assert.IsFalse(DeploymentFileAccess.TryReadIdentity(Path.Combine(fixture.Root, "missing.exe"), out _));
    }

    [TestMethod]
    public void TrustProbeStillReportsAccessDeniedWhileTheShareReadHoldIsOpen()
    {
        // §R A2-L3: an access-check failure precedes the sharing check, so a protected file stays ACCESS_DENIED while held;
        // a caller-writable held file reports a sharing violation, which TryProbeRights already treats as fail closed.
        RequireWindows("A2-L3 probe while held");
        using Fixture fixture = new();
        string protectedFile = fixture.WriteFile("protected.dll", "protected");
        string writable = fixture.WriteFile("writable.dll", "writable");
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        SecurityIdentifier user = identity.User!;
        FileInfo info = new(protectedFile);
        FileSecurity security = info.GetAccessControl();
        FileSystemAccessRule deny = new(user, FileSystemRights.WriteData, AccessControlType.Deny);
        security.AddAccessRule(deny);
        info.SetAccessControl(security);
        try
        {
            using SafeFileHandle? protectedHold = DeploymentFileAccess.TryOpenShareRead(protectedFile);
            using SafeFileHandle? writableHold = DeploymentFileAccess.TryOpenShareRead(writable);
            Assert.IsNotNull(protectedHold);
            Assert.IsNotNull(writableHold);
            Assert.AreEqual(ErrorAccessDenied, ProbeWriteData(protectedFile));
            Assert.AreEqual(ErrorSharingViolation, ProbeWriteData(writable));
            Assert.IsFalse(ProtectedLaunchLayout.CallerCannotModify(fixture.Root, writable), "a held caller-writable file is never reported protected");
        }
        finally
        {
            security = info.GetAccessControl();
            security.RemoveAccessRuleSpecific(deny);
            info.SetAccessControl(security);
        }
    }

    // ---- Windows-native: reader end to end on fixture trees ----

    [TestMethod]
    public void ConsistentFixtureTreeIsSupportedAndTheBindingSetStaysHeldUntilDisposed()
    {
        RequireWindows("A.4 reader and hold");
        using Fixture fixture = new();
        DeploymentTree tree = DeploymentTree.Create(fixture);
        int protections = 0;
        OwnerOnlyDeploymentCapabilityReader reader = new(() => tree.Layout, (_, _) => { protections++; return true; });

        Assert.AreEqual(Status.Supported, reader.Evaluate(Route).Status);
        HelperProtocolCapabilityResult result = reader.TryHold(Route, out HeldHelperBinding? binding);
        Assert.AreEqual(Status.Supported, result.Status);
        Assert.AreEqual("helper-capability-supported", result.Diagnostic);
        Assert.IsNotNull(binding);
        string helper = tree.Leaf(InstalledHelperLaunchTrust.HelperExecutableName);
        string lazyClosure = tree.Leaf("PCSteward.Provider.Chocolatey.dll");
        try
        {
            CollectionAssert.AreEquivalent(DeploymentCapabilityPolicy.BindingSet.ToArray(), binding.VerifiedHashes.Keys.ToArray());
            foreach (string leaf in DeploymentCapabilityPolicy.BindingSet)
                Assert.AreEqual(tree.Hashes[leaf], binding.VerifiedHashes[leaf].Sha256, leaf);
            Assert.AreEqual(helper, binding.HelperPath);
            Assert.IsTrue(DeploymentFileAccess.TryReadIdentity(helper, out WindowsFileIdentity helperIdentity));
            Assert.AreEqual(helperIdentity, binding.HelperIdentity);

            // Every leaf, including a lazily loaded closure file and the statement, is protected from replacement; reads work.
            foreach (string leaf in DeploymentCapabilityPolicy.BindingSet)
            {
                AssertRefused(() => File.Delete(tree.Leaf(leaf)));
                AssertRefused(() => File.WriteAllText(tree.Leaf(leaf), "tampered"));
            }
            string impostor = fixture.WriteFile("impostor.dll", "content PCSteward.Provider.Chocolatey.dll");
            AssertRefused(() => File.Move(impostor, lazyClosure, overwrite: true));
            Assert.AreEqual("content PCSteward.Provider.Chocolatey.dll", File.ReadAllText(lazyClosure));

            // A-M1: the peer image identity must equal the held helper identity; equal bytes elsewhere do not.
            string helperCopy = Path.Combine(fixture.Root, "helper-copy.exe");
            File.Copy(helper, helperCopy);
            Assert.IsTrue(binding.PeerImageMatchesHelper(helper));
            Assert.IsFalse(binding.PeerImageMatchesHelper(helperCopy));
            Assert.IsFalse(binding.PeerImageMatchesHelper(Path.Combine(fixture.Root, "missing.exe")));
            Assert.IsFalse(binding.PeerImageMatchesHelper(string.Empty));

            // A2-L5 fallback: re-hash by path plus the pre-launch reference identity.
            Assert.IsTrue(binding.RevalidateAfterPeer(helper));
            Assert.IsFalse(binding.RevalidateAfterPeer(helperCopy));

            // A second, overlapping evaluation still reads (share-read handles are compatible with each other).
            Assert.AreEqual(Status.Supported, reader.Evaluate(Route).Status);
        }
        finally { binding.Dispose(); }

        Assert.IsTrue(binding.IsDisposed);
        Assert.IsFalse(binding.PeerImageMatchesHelper(helper), "a disposed binding never verifies");
        Assert.IsFalse(binding.RevalidateAfterPeer(helper));
        binding.Dispose();
        File.Delete(lazyClosure);
        Assert.IsFalse(File.Exists(lazyClosure), "disposal releases every handle");
        Assert.IsTrue(protections > 0);
    }

    [TestMethod]
    public void NothingIsCachedBetweenEvaluations()
    {
        RequireWindows("A.7 Phases no cache");
        using Fixture fixture = new();
        DeploymentTree tree = DeploymentTree.Create(fixture);
        int resolutions = 0, protections = 0;
        OwnerOnlyDeploymentCapabilityReader reader = new(() => { resolutions++; return tree.Layout; }, (_, _) => { protections++; return true; });
        Assert.AreEqual(Status.Supported, reader.Evaluate(Route).Status);
        Assert.AreEqual(Status.Supported, reader.Evaluate(Route).Status);
        Assert.AreEqual(2, resolutions);
        Assert.AreEqual(4, protections, "state root and install root are checked on every evaluation");

        // Valid before, then the helper changes: the next evaluation sees it.
        File.WriteAllText(tree.Leaf(InstalledHelperLaunchTrust.HelperExecutableName), "replaced helper");
        Assert.AreEqual(Status.Mismatch, reader.Evaluate(Route).Status);
        // Then the capability statement disappears.
        File.Delete(tree.Leaf(DeploymentCapabilityPolicy.CapabilityFileName));
        Assert.AreEqual(Status.Mismatch, reader.Evaluate(Route).Status);
    }

    public static IEnumerable<object[]> FixtureCases() =>
    [
        ["state-root-missing", "Absent"],
        ["ledger-missing", "Absent"],
        ["ledger-pinned-v1", "Absent"],
        ["ledger-legacy-v6", "Absent"],
        ["ledger-pending", "Incomplete"],
        ["ledger-rollback", "Incomplete"],
        ["ledger-unknown-origin", "Unreadable"],
        ["ledger-over-1-mib", "Unreadable"],
        ["ledger-bom", "Unreadable"],
        ["sid-differs", "Mismatch"],
        ["install-root-differs", "Mismatch"],
        ["manifest-missing", "Mismatch"],
        ["manifest-hash-differs", "Mismatch"],
        ["capability-missing", "Mismatch"],
        ["capability-protocol-1-consistently-pinned", "Mismatch"],
        ["capability-crlf-consistently-pinned", "Mismatch"],
        ["capability-over-1024-bytes", "Mismatch"],
        ["helper-exe-differs", "Mismatch"],
        ["desktop-dll-differs", "Mismatch"],
        ["closure-dll-differs", "Mismatch"],
        ["binding-leaf-missing", "Unreadable"],
        ["binding-leaf-over-32-mib", "Unreadable"],
        ["process-path-not-fixed", "Unreadable"],
        ["state-protection-refused", "Unreadable"],
        ["install-protection-refused", "Unreadable"],
    ];

    [TestMethod]
    [DynamicData(nameof(FixtureCases))]
    public void FixtureTreesEvaluateAsSpecified(string name, string expected)
    {
        RequireWindows("A.7 Ledger/Manifest/Binding on disk");
        using Fixture fixture = new();
        DeploymentTree tree = DeploymentTree.Create(fixture, name switch
        {
            "capability-protocol-1-consistently-pinned" => Encoding.ASCII.GetBytes(
                Encoding.ASCII.GetString(DeploymentCapabilityPolicy.CanonicalCapabilityBytes()).Replace("helper-protocol:2", "helper-protocol:1", StringComparison.Ordinal)),
            "capability-crlf-consistently-pinned" => Encoding.ASCII.GetBytes(
                Encoding.ASCII.GetString(DeploymentCapabilityPolicy.CanonicalCapabilityBytes()).Replace("\n", "\r\n", StringComparison.Ordinal)),
            "capability-over-1024-bytes" => [.. DeploymentCapabilityPolicy.CanonicalCapabilityBytes(), .. Enumerable.Repeat((byte)'x', 1024), (byte)'\n'],
            _ => null,
        });
        DeploymentCapabilityLayout layout = tree.Layout;
        bool stateProtection = true, installProtection = true;
        switch (name)
        {
            case "state-root-missing": Directory.Delete(tree.StateRoot, recursive: true); break;
            case "ledger-missing": File.Delete(tree.LedgerPath); break;
            case "ledger-pinned-v1": tree.WriteLedger(DeploymentCapabilityPolicyTests.Ledgers.Completed(DeploymentCapabilityPolicy.PayloadOriginV1, hashes: tree.Hashes)); break;
            case "ledger-legacy-v6": tree.WriteLedger(DeploymentCapabilityPolicyTests.Ledgers.Completed(null, hashes: tree.Hashes)); break;
            case "ledger-pending": tree.WriteLedger(DeploymentCapabilityPolicyTests.Ledgers.Pending(null, ["My"])); break;
            case "ledger-rollback": tree.WriteLedger(DeploymentCapabilityPolicyTests.Ledgers.Rollback(null, null, null)); break;
            case "ledger-unknown-origin": tree.WriteLedger(DeploymentCapabilityPolicyTests.Ledgers.Completed("pinned-unsigned-v3", hashes: tree.Hashes, manifestSha256: tree.ManifestSha256)); break;
            case "ledger-over-1-mib":
                File.WriteAllBytes(tree.LedgerPath, [.. File.ReadAllBytes(tree.LedgerPath), .. Enumerable.Repeat((byte)' ', DeploymentCapabilityPolicy.MaximumLedgerBytes)]);
                break;
            case "ledger-bom": File.WriteAllBytes(tree.LedgerPath, [0xEF, 0xBB, 0xBF, .. File.ReadAllBytes(tree.LedgerPath)]); break;
            case "sid-differs": layout = layout with { CurrentUserSid = "S-1-5-21-11-22-33-1002" }; break;
            case "install-root-differs": tree.WriteLedger(tree.CompletedV2Ledger(), installRoot: @"C:\Program Files\PC Steward"); break;
            case "manifest-missing": File.Delete(tree.ManifestPath); break;
            case "manifest-hash-differs": File.AppendAllText(tree.ManifestPath, " "); break;
            case "capability-missing": File.Delete(tree.Leaf(DeploymentCapabilityPolicy.CapabilityFileName)); break;
            case "helper-exe-differs": File.WriteAllText(tree.Leaf(InstalledHelperLaunchTrust.HelperExecutableName), "old helper"); break;
            case "desktop-dll-differs": File.WriteAllText(tree.Leaf("PCSteward.Desktop.dll"), "foreign desktop"); break;
            case "closure-dll-differs": File.WriteAllText(tree.Leaf("PCSteward.Provider.Chocolatey.dll"), "partial copy"); break;
            case "binding-leaf-missing": File.Delete(tree.Leaf("PCSteward.Domain.dll")); break;
            case "binding-leaf-over-32-mib":
                using (FileStream stream = new(tree.Leaf("PCSteward.Execution.dll"), FileMode.Open, FileAccess.Write))
                    stream.SetLength(DeploymentCapabilityPolicy.MaximumBindingFileBytes + 1);
                break;
            case "process-path-not-fixed": layout = layout with { ProcessPath = Path.Combine(fixture.Root, InstalledHelperLaunchTrust.DesktopExecutableName) }; break;
            case "state-protection-refused": stateProtection = false; break;
            case "install-protection-refused": installProtection = false; break;
        }
        List<string> protectedRoots = [];
        OwnerOnlyDeploymentCapabilityReader reader = new(() => layout, (root, files) =>
        {
            protectedRoots.Add(root);
            foreach (string file in files) Assert.IsTrue(file.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), file);
            return root == tree.StateRoot ? stateProtection : installProtection;
        });
        Status status = reader.TryHold(Route, out HeldHelperBinding? binding).Status;
        Assert.AreEqual(Enum.Parse<Status>(expected), status, name);
        Assert.IsNull(binding, name);
        Assert.AreEqual(Enum.Parse<Status>(expected), reader.Evaluate(Route).Status, name);
        // The first failing stage ends evaluation: ledger-stage failures never inspect the install root.
        if (name.StartsWith("ledger-", StringComparison.Ordinal) || name is "state-root-missing" or "sid-differs" or "install-root-differs" or "state-protection-refused")
            Assert.IsFalse(protectedRoots.Contains(tree.InstallRoot), name);
        // Nothing stays held after a refusal: a write open (fully sharing, so a scanner's handle cannot interfere) succeeds.
        foreach (string leaf in DeploymentCapabilityPolicy.BindingSet.Where(leaf => File.Exists(tree.Leaf(leaf))))
            using (File.OpenHandle(tree.Leaf(leaf), FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete)) { }
    }

    [TestMethod]
    public void ProductionProtectionRefusesACallerWritableFixtureTree()
    {
        RequireWindows("A.7 Binding caller-writable");
        using Fixture fixture = new();
        DeploymentTree tree = DeploymentTree.Create(fixture);
        Assert.IsFalse(OwnerOnlyDeploymentCapabilityReader.IsProtectedLayout(tree.StateRoot, [tree.LedgerPath, tree.ManifestPath]));
        OwnerOnlyDeploymentCapabilityReader reader = new(() => tree.Layout, OwnerOnlyDeploymentCapabilityReader.IsProtectedLayout);
        Assert.AreEqual(Status.Unreadable, reader.Evaluate(Route).Status);
        Assert.AreEqual(Status.Unreadable, reader.TryHold(Route, out HeldHelperBinding? binding).Status);
        Assert.IsNull(binding);
    }

    [TestMethod]
    public void SymbolicLinkLeafIsRefusedEvenWhenTheProtectionCheckIsBypassed()
    {
        RequireWindows("A.7 Binding reparse");
        using Fixture fixture = new();
        DeploymentTree tree = DeploymentTree.Create(fixture);
        string leaf = tree.Leaf("PCSteward.Application.dll");
        string outside = fixture.WriteFile("outside.dll", "content PCSteward.Application.dll");
        File.Delete(leaf);
        try { File.CreateSymbolicLink(leaf, outside); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Assert.Inconclusive("Creating a symbolic link needs SeCreateSymbolicLinkPrivilege or Developer Mode on this Windows host.");
        }
        // The link resolves to identical bytes, so only the reparse defences can refuse it.
        OwnerOnlyDeploymentCapabilityReader noReparseOnly = new(() => tree.Layout,
            (root, files) => ProtectedLaunchLayout.HasNoReparseComponents(root, [.. files]));
        Assert.AreEqual(Status.Unreadable, noReparseOnly.Evaluate(Route).Status);
        OwnerOnlyDeploymentCapabilityReader bypassed = new(() => tree.Layout, (_, _) => true);
        Assert.AreEqual(Status.Unreadable, bypassed.Evaluate(Route).Status, "the final-path check refuses a followed link");
        Assert.IsNull(DeploymentFileAccess.TryOpenShareRead(leaf));
    }

    // ---- Helpers ----

    private static void RequireWindows(string evidence)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException($"Windows-native test ({evidence}): it runs on Windows CI and is reported as failed on other platforms.");
    }

    private static void AssertRefused(Action action)
    {
        try { action(); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return; }
        Assert.Fail("The operation succeeded while the share-read hold was open.");
    }

    private static int ProbeWriteData(string path)
    {
        using SafeFileHandle handle = NativeMethods.CreateFileForTrustProbe(path, NativeMethods.FileWriteData,
            NativeMethods.FileShareRead | NativeMethods.FileShareWrite | NativeMethods.FileShareDelete, IntPtr.Zero,
            NativeMethods.OpenExisting, NativeMethods.FileFlagOpenReparsePoint, IntPtr.Zero);
        int error = Marshal.GetLastWin32Error();
        Assert.IsTrue(handle.IsInvalid, "the write probe must not open");
        return error;
    }

    /// <summary>A GUID-named temp directory, canonicalized to its final path so short (8.3) temp names cannot differ from it.</summary>
    private sealed class Fixture : IDisposable
    {
        internal Fixture()
        {
            string created = Path.Combine(Path.GetTempPath(), $"pcsteward-capability-reader-{Guid.NewGuid():N}");
            Directory.CreateDirectory(created);
            string probe = Path.Combine(created, "probe");
            File.WriteAllText(probe, "probe");
            using (SafeFileHandle handle = File.OpenHandle(probe, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Assert.IsTrue(DeploymentFileAccess.TryGetFinalPath(handle, out string final));
                Root = Path.GetDirectoryName(final)!;
            }
            File.Delete(probe);
        }

        internal string Root { get; }

        internal string WriteFile(string name, string content)
        {
            string path = Path.Combine(Root, name);
            File.WriteAllText(path, content);
            return path;
        }

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>
    /// A consistent v2 deployment on disk: binding-set leaves with synthetic content, the statement's real bytes, a pinned
    /// candidate manifest and a ledger rendered from the real-writer key lists of <see cref="DeploymentCapabilityPolicyTests.Ledgers"/>.
    /// </summary>
    private sealed class DeploymentTree
    {
        private DeploymentTree(string root)
        {
            InstallRoot = Path.Combine(root, "PC Steward");
            StateRoot = Path.Combine(root, OwnerOnlyDeploymentCapabilityReader.StateDirectoryName);
        }

        internal string InstallRoot { get; }
        internal string StateRoot { get; }
        internal string LedgerPath => Path.Combine(StateRoot, OwnerOnlyDeploymentCapabilityReader.LedgerFileName);
        internal string ManifestPath => Path.Combine(StateRoot, OwnerOnlyDeploymentCapabilityReader.CandidateManifestFileName);
        internal Dictionary<string, string> Hashes { get; } = new(StringComparer.Ordinal);
        internal string ManifestSha256 { get; private set; } = string.Empty;
        internal DeploymentCapabilityLayout Layout => new(InstallRoot, StateRoot, Leaf(InstalledHelperLaunchTrust.DesktopExecutableName), Sid);

        internal string Leaf(string leaf) => Path.Combine(InstallRoot, leaf);

        internal static DeploymentTree Create(Fixture fixture, byte[]? capability = null)
        {
            DeploymentTree tree = new(fixture.Root);
            Directory.CreateDirectory(tree.InstallRoot);
            Directory.CreateDirectory(tree.StateRoot);
            capability ??= DeploymentCapabilityPolicy.CanonicalCapabilityBytes();
            List<object?> manifest = [];
            foreach (string leaf in DeploymentCapabilityPolicy.BindingSet)
            {
                byte[] bytes = leaf == DeploymentCapabilityPolicy.CapabilityFileName ? capability : Encoding.UTF8.GetBytes("content " + leaf);
                File.WriteAllBytes(tree.Leaf(leaf), bytes);
                tree.Hashes[leaf] = Convert.ToHexString(SHA256.HashData(bytes));
                manifest.Add(DeploymentCapabilityPolicyTests.Obj(("bytes", (long)bytes.Length), ("path", leaf), ("sha256", tree.Hashes[leaf].ToLowerInvariant())));
            }
            byte[] manifestBytes = Encoding.UTF8.GetBytes(DeploymentCapabilityPolicyTests.Ps7Json.RenderValue(manifest.ToArray()));
            File.WriteAllBytes(tree.ManifestPath, manifestBytes);
            tree.ManifestSha256 = Convert.ToHexString(SHA256.HashData(manifestBytes));
            tree.WriteLedger(tree.CompletedV2Ledger());
            return tree;
        }

        internal IReadOnlyList<(string, object?)> CompletedV2Ledger() =>
            DeploymentCapabilityPolicyTests.Ledgers.Completed(DeploymentCapabilityPolicy.PayloadOriginV2, hashes: Hashes, manifestSha256: ManifestSha256);

        internal void WriteLedger(IReadOnlyList<(string Key, object? Value)> ledger, string? installRoot = null)
        {
            (string, object?)[] fields = [.. ledger.Select(field => field.Key == "installRoot" ? (field.Key, (object?)(installRoot ?? InstallRoot)) : (field.Key, field.Value))];
            File.WriteAllText(LedgerPath, DeploymentCapabilityPolicyTests.Ps7Json.Render(fields), new UTF8Encoding(false));
        }
    }
}
