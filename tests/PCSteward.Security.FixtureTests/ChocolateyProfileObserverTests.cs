using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Win32.SafeHandles;
using PCSteward.Provider.Chocolatey;
using PCSteward.Provider.Chocolatey.Native;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// FR-UPD-15, TM-03/04/07/13; Task 50 design rev 6 §3 and §8 N2: the static profile observer over synthetic trees with
/// explicit ACLs, evaluated with the derived token. One positive per role, then one negative per §3 rule in step order
/// (1–9 with 8a), the rev 3 precedence between settled reasons, and the TimedOut/Cancelled arms (never a settled refusal on
/// an interrupt). Every test is Windows-only and is reported as failed elsewhere.
/// </summary>
[TestClass]
public sealed class ChocolateyProfileObserverTests
{
    private const string GoldenProfile = "C179F864C37EA8029917E62CB60DACE19533E5E4DA9E20A4656EC729B580EE77";
    private const string AbcHash = "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD";
    private static readonly TimeSpan Budget = TimeSpan.FromMinutes(2);
    private static readonly string[] CodePaths = [@"extensions\fixture\fixture.psm1", @"hooks\pre-install-all.ps1"];
    private static readonly string[] OptionalTrees = ["hooks", "extensions", "license", "lib-bkp", "lib-bad", "logs"];

    // ---- Positives ----------------------------------------------------------------------------------------------------

    [TestMethod]
    public void TheDesktopRoleObservesASupportedTreeWithItsFingerprintBaselineAndNoHeldFiles()
    {
        using RequesterToken token = N1NativeFixture.DerivedToken();
        using N2ProfileFixture fixture = new N2ProfileFixture().Build().Lock();
        string? trustedPath = null;
        ChocolateyProfileResult result = Observe(token, fixture, ChocolateyObserverRole.Desktop, engineTrust: (handle, path) =>
        {
            trustedPath = path;
            return !handle.IsInvalid && !handle.IsClosed;
        });

        using ChocolateyProfileObservation observation = Observed(result);
        Assert.AreEqual(fixture.RootPath("choco.exe"), trustedPath, "engine trust runs on the root engine's held handle, never the bin shim");
        Assert.IsNull(observation.HeldClient, "the Desktop role holds nothing");
        Assert.AreEqual("1.2.0", observation.InstalledVersion.Normalized);
        Assert.IsFalse(observation.TargetLibBadResidue);
        ChocolateyCommonProfileData common = observation.Common;
        Assert.AreEqual(token.Sid, common.RequestingUserSid);
        Assert.AreEqual(Identity(fixture.Root, directory: true), common.RootIdentity);
        Assert.AreEqual(Identity(fixture.Cache, directory: true), common.CacheIdentity);
        Assert.AreEqual(Identity(fixture.RootPath(@"lib\git\git.nupkg"), directory: false), common.ArchiveIdentity);
        Assert.AreEqual(Sha256(fixture.RootPath(@"lib\git\git.nupkg")), common.ArchiveSha256);
        Assert.AreEqual(Identity(fixture.RootPath(@".chocolatey\git.1.2.0"), directory: true), common.StoreIdentity);
        Assert.AreEqual(N2ProfileFixture.Target, common.TargetId);
        CollectionAssert.AreEquivalent(CodePaths, common.CodeFiles.Select(file => file.RelativePath).ToArray());
        Assert.AreEqual(ChocolateyCommonProfileFingerprint.Compute(common), observation.Fingerprint);

        ChocolateyClientStateData state = observation.ClientState;
        Assert.AreEqual(fixture.Release.Single(file => file.Path == "choco.exe").Sha256, state.EngineSha256);
        Assert.AreEqual(ChocolateyCommonProfileFingerprint.ComputeCodeList(common.CodeFiles), state.CodeDigest);
        Assert.IsNotNull(state.ClientRegistrationDigest, "lib\\chocolatey registration observed");
        Assert.IsNull(state.LicensedExtensionRegistrationDigest, "chocolatey.extension observed absent");
    }

    [TestMethod]
    public void TheObservedConfigurationProjectsOntoTheIndependentGoldenFingerprint()
    {
        using RequesterToken token = N1NativeFixture.DerivedToken();
        using N2ProfileFixture fixture = new N2ProfileFixture().Build().Lock();
        using ChocolateyProfileObservation observation = Observed(Observe(token, fixture, ChocolateyObserverRole.Desktop));
        // Native identities cannot be reproduced on a fixture volume, so the golden snapshot's identity, target and code
        // fields replace the observed ones; the observed configuration (defaults, timeouts, proxy, no matching source)
        // must then reproduce the independent PowerShell golden exactly.
        ChocolateyCommonProfileData projected = observation.Common with
        {
            RequestingUserSid = "S-1-5-21-11-22-33-1001", RootIdentity = GoldenIdentity(1), CacheIdentity = GoldenIdentity(2),
            CodeFiles = [new ChocolateyCodeFileData("helpers/a.ps1", GoldenIdentity(3), 3, AbcHash)], TargetId = "git.install",
            InstalledVersion = N2ProfileFixture.Version("1.0"), ArchiveIdentity = GoldenIdentity(4), ArchiveSha256 = new string('B', 64),
            StoreIdentity = null,
        };
        Assert.AreEqual(GoldenProfile, ChocolateyCommonProfileFingerprint.Compute(projected));
    }

    [TestMethod]
    public void TheHelperRoleChecksTheHttpCacheAndHoldsTheEngineReleaseAndCodeFilesUntilDisposed()
    {
        using RequesterToken token = N1NativeFixture.DerivedToken();
        using N2ProfileFixture fixture = new N2ProfileFixture().Build().Lock();
        // A standard-user runner cannot create a tree it does not own, so the helper cache is an existing protected system
        // directory owned by another principal (the seam permits this one substitution; ancestry runs from the drive root).
        fixture.HttpCache = SystemCache();
        string engine = fixture.RootPath("choco.exe");

        ChocolateyProfileResult result = Observe(token, fixture, ChocolateyObserverRole.Helper);
        using (ChocolateyProfileObservation observation = Observed(result))
        {
            ChocolateyHeldClientBinding held = observation.HeldClient ?? throw new AssertFailedException("the helper holds its files");
            Assert.AreEqual(engine, held.Engine.Node.FullPath);
            // N3: the binding launches the held root engine from the root, with TEMP/TMP = the observed protected cache.
            Assert.AreEqual(engine, held.EnginePath);
            Assert.AreEqual(fixture.Root, held.LaunchPaths.ChocolateyRoot);
            Assert.AreEqual(fixture.Cache, held.LaunchPaths.ProtectedCache);
            Assert.AreEqual(N2ProfileFixture.Target, held.TargetId);
            Assert.IsTrue(held.CanLaunch && held.CodeHeld);
            Assert.AreEqual(N2ProfileFixture.ReleasePaths.Length, held.ReleaseFiles.Count);
            Assert.AreEqual(2, held.CodeFiles.Count);
            Assert.ThrowsExactly<IOException>(() => OpenForWrite(engine), "a held release file cannot be opened for write");
            Assert.ThrowsExactly<IOException>(() => OpenForWrite(fixture.RootPath(@"hooks\pre-install-all.ps1")), "a held code file cannot be opened for write");
        }
        OpenForWrite(engine).Dispose();
    }

    [TestMethod]
    public void AbsentOptionalTreesAndAnAbsentGacRootUnderAProtectedParentAreSupported()
    {
        using RequesterToken token = N1NativeFixture.DerivedToken();
        using N2ProfileFixture fixture = new N2ProfileFixture().Build();
        foreach (string tree in OptionalTrees)
            System.IO.Directory.Delete(fixture.RootPath(tree), recursive: true);
        System.IO.Directory.Delete(fixture.Windows + @"\assembly\GAC_64");
        fixture.Lock();

        using ChocolateyProfileObservation observation = Observed(Observe(token, fixture, ChocolateyObserverRole.Desktop));
        Assert.AreEqual(0, observation.Common.CodeFiles.Count);
    }

    [TestMethod]
    public void LibBadResidueIsDisclosedAndAMatchingFingerprintIsAccepted()
    {
        using RequesterToken token = N1NativeFixture.DerivedToken();
        using N2ProfileFixture fixture = new N2ProfileFixture().Build();
        System.IO.Directory.CreateDirectory(fixture.RootPath(@"lib-bad\git"));
        fixture.Lock();
        string fingerprint;
        using (ChocolateyProfileObservation first = Observed(Observe(token, fixture, ChocolateyObserverRole.Desktop)))
        {
            Assert.IsTrue(first.TargetLibBadResidue);
            fingerprint = first.Fingerprint;
        }
        using ChocolateyProfileObservation second = Observed(Observe(token, fixture, ChocolateyObserverRole.Desktop, expected: fingerprint));
        Assert.AreEqual(fingerprint, second.Fingerprint);
    }

    // ---- Step 1: roots and coverage -----------------------------------------------------------------------------------

    [TestMethod]
    public void Step1AReplaceableAncestorRefusesBeforeTheRootIsWalked() =>
        AssertUnsupported(after: fixture => fixture.Native.Protect(fixture.ProgramData, ownerRights: true, FileSystemRights.ReadAndExecute | FileSystemRights.Delete));

    [TestMethod]
    public void Step1ARequesterModifiableNodeAnywhereInTheRootRefuses() =>
        AssertUnsupported(after: fixture => fixture.Native.Protect(fixture.RootPath(@"bin\choco.exe"), ownerRights: true,
            FileSystemRights.ReadAndExecute | FileSystemRights.Write));

    [TestMethod]
    public void Step1AMissingRequiredTreeRefuses() =>
        AssertUnsupported(before: fixture => System.IO.Directory.Delete(fixture.RootPath("bin"), recursive: true));

    [TestMethod]
    public void Step1AJunctionInsideTheRootRefuses() =>
        AssertUnsupported(after: fixture => N1NativeFixture.Junction(fixture.RootPath(@"lib\link"), fixture.Cache));

    [TestMethod]
    public void Step1AnOptionalTreeThatIsAFileRefuses() =>
        AssertUnsupported(before: fixture =>
        {
            System.IO.Directory.Delete(fixture.RootPath("license"), recursive: true);
            System.IO.File.WriteAllText(fixture.RootPath("license"), "x");
        });

    // ---- Step 2: release tree -----------------------------------------------------------------------------------------

    [TestMethod]
    public void Step2AChangedReleaseFileOfTheSameLengthRefuses() =>
        AssertUnsupported(after: fixture => Rewrite(fixture.RootPath(@"helpers\chocolateyInstaller.psm1"), "fixture:helpers/chocolateyInstaller.psX1"));

    [TestMethod]
    public void Step2AMissingReleaseFileRefuses() =>
        AssertUnsupported(before: fixture => System.IO.File.Delete(fixture.RootPath(@"tools\checksum.exe")));

    [TestMethod]
    public void Step2AnExtraToolsFileRefuses() =>
        AssertUnsupported(before: fixture => System.IO.File.WriteAllText(fixture.RootPath(@"tools\extra.dll"), "x"));

    [TestMethod]
    public void Step2AnExtraHelpersDirectoryRefuses() =>
        AssertUnsupported(before: fixture => System.IO.Directory.CreateDirectory(fixture.RootPath(@"helpers\extra")));

    [TestMethod]
    public void Step2AnUntrustedEngineRefuses()
    {
        using RequesterToken token = N1NativeFixture.DerivedToken();
        using N2ProfileFixture fixture = new N2ProfileFixture().Build().Lock();
        AssertRefused(Observe(token, fixture, ChocolateyObserverRole.Desktop, engineTrust: (_, _) => false), ChocolateyProfileRefusal.MachineInputsUnsupported);
    }

    // ---- Step 3: code identities --------------------------------------------------------------------------------------

    [TestMethod]
    public void Step3ACodeFileOutsideTheSupportedPathGrammarRefuses() =>
        AssertUnsupported(before: fixture => System.IO.File.WriteAllText(fixture.RootPath(@"hooks\é.ps1"), "x"));

    // ---- Step 4: helper HTTP cache ------------------------------------------------------------------------------------

    [TestMethod]
    public void Step4AnAbsentHelperHttpCacheRefusesTheHelperOnly()
    {
        using RequesterToken token = N1NativeFixture.DerivedToken();
        using N2ProfileFixture fixture = new N2ProfileFixture().Build().Lock();
        AssertRefused(Observe(token, fixture, ChocolateyObserverRole.Helper), ChocolateyProfileRefusal.MachineInputsUnsupported);
        using ChocolateyProfileObservation desktop = Observed(Observe(token, fixture, ChocolateyObserverRole.Desktop));
    }

    [TestMethod]
    public void Step4ARequesterOwnedHelperHttpCacheRefuses()
    {
        using RequesterToken token = N1NativeFixture.DerivedToken();
        using N2ProfileFixture fixture = new N2ProfileFixture().Build();
        System.IO.Directory.CreateDirectory(fixture.HttpCache + @"\metadata");
        System.IO.File.WriteAllText(fixture.HttpCache + @"\metadata\feed.dat", "cached");
        fixture.Lock();
        // Every node is requester-unmodifiable (locked) but owned by the requester (L4).
        AssertRefused(Observe(token, fixture, ChocolateyObserverRole.Helper), ChocolateyProfileRefusal.MachineInputsUnsupported);
    }

    // ---- Step 5: loader and licensing ---------------------------------------------------------------------------------

    [TestMethod]
    public void Step5AnAppDomainManagerInMachineConfigRefuses() =>
        AssertUnsupported(before: fixture => System.IO.File.WriteAllText(fixture.Windows + @"\Microsoft.NET\Framework64\v4.0.30319\Config\machine.config",
            "<configuration><runtime><appDomainManagerType value='Evil'/></runtime></configuration>"));

    [TestMethod]
    public void Step5AMissingFramework64MachineConfigRefuses() =>
        AssertUnsupported(before: fixture => System.IO.File.Delete(fixture.Windows + @"\Microsoft.NET\Framework64\v4.0.30319\Config\machine.config"));

    [TestMethod]
    public void Step5ARequesterModifiableConfigDirectoryRefuses() =>
        AssertUnsupported(after: fixture => fixture.Native.Protect(fixture.Windows + @"\Microsoft.NET\Framework\v4.0.30319\Config", ownerRights: true,
            FileSystemRights.ReadAndExecute | FileSystemRights.CreateFiles));

    [TestMethod]
    public void Step5TheLicensedAssemblyInAGacRootRefuses() =>
        AssertUnsupported(before: fixture => System.IO.Directory.CreateDirectory(fixture.Windows + @"\Microsoft.NET\assembly\GAC_MSIL\chocolatey.licensed"));

    [TestMethod]
    public void Step5ALicensedPublisherPolicyInALegacyGacRootRefuses() =>
        AssertUnsupported(before: fixture => System.IO.Directory.CreateDirectory(fixture.Windows + @"\assembly\GAC_MSIL\policy.1.0.chocolatey.licensed"));

    [TestMethod]
    public void Step5ARequesterModifiableGacRootRefuses() =>
        AssertUnsupported(after: fixture => fixture.Native.Protect(fixture.Windows + @"\assembly\GAC_32", ownerRights: true,
            FileSystemRights.ReadAndExecute | FileSystemRights.CreateDirectories));

    [TestMethod]
    public void Step5TheLicensedAssemblyUnderExtensionsRefuses() =>
        AssertUnsupported(before: fixture => System.IO.File.WriteAllText(fixture.RootPath(@"extensions\fixture\chocolatey.licensed.dll"), "x"));

    [TestMethod]
    public void Step5ARootChocoExeConfigRefuses() =>
        AssertUnsupported(before: fixture => System.IO.File.WriteAllText(fixture.RootPath("choco.exe.config"), "<configuration/>"));

    // ---- Step 6: configuration ----------------------------------------------------------------------------------------

    [TestMethod]
    public void Step6ARequiredFeatureViolationRefuses() =>
        AssertUnsupported(before: fixture => fixture.WriteConfiguration("<features><feature name='checksumFiles' enabled='false' setExplicitly='true'/></features>"));

    [TestMethod]
    public void Step6AMalformedConfigurationRefuses() =>
        AssertUnsupported(before: fixture => System.IO.File.WriteAllText(fixture.RootPath(@"config\chocolatey.config"), "<chocolatey><unknown/></chocolatey>"));

    [TestMethod]
    public void Step6AMissingCacheLocationRefuses() =>
        AssertUnsupported(before: fixture => System.IO.Directory.Delete(fixture.Cache));

    [TestMethod]
    public void Step6ARequesterModifiableCacheLocationRefuses() =>
        AssertUnsupported(after: fixture => fixture.Native.Protect(fixture.Cache, ownerRights: true, FileSystemRights.ReadAndExecute | FileSystemRights.Write));

    // ---- Step 7: library and store ------------------------------------------------------------------------------------

    [TestMethod]
    public void Step7AnAbsentTargetRefuses()
    {
        using RequesterToken token = N1NativeFixture.DerivedToken();
        using N2ProfileFixture fixture = new N2ProfileFixture().Build().Lock();
        AssertRefused(Observe(token, fixture, ChocolateyObserverRole.Desktop, target: "missing"), ChocolateyProfileRefusal.MachineInputsUnsupported);
    }

    [TestMethod]
    public void Step7AClientTargetRefuses()
    {
        using RequesterToken token = N1NativeFixture.DerivedToken();
        using N2ProfileFixture fixture = new N2ProfileFixture().Build().Lock();
        AssertRefused(Observe(token, fixture, ChocolateyObserverRole.Desktop, target: "Chocolatey", frozen: "2.7.5"), ChocolateyProfileRefusal.MachineInputsUnsupported);
    }

    [TestMethod]
    public void Step7ADuplicateTargetIdentityRefuses() =>
        AssertUnsupported(before: fixture => fixture.WritePackage(@"lib\git-copy\git-copy.nupkg", "GIT", "1.2.0"));

    [TestMethod]
    public void Step7ACorruptNonTargetArchiveRefuses() =>
        AssertUnsupported(before: fixture =>
        {
            System.IO.Directory.CreateDirectory(fixture.RootPath(@"lib\other"));
            System.IO.File.WriteAllText(fixture.RootPath(@"lib\other\other.nupkg"), "not a zip archive at all");
        });

    [TestMethod]
    public void Step7TwoStoreAlternativesRefuse() =>
        AssertUnsupported(before: fixture => System.IO.Directory.CreateDirectory(fixture.RootPath(@".chocolatey\git.1.2.0.0")));

    [TestMethod]
    public void Step7AStorePinMarkerRefuses() =>
        AssertUnsupported(before: fixture => System.IO.File.WriteAllText(fixture.RootPath(@".chocolatey\git.1.2.0\.pin"), ""));

    [TestMethod]
    public void Step7APendingMarkerRefuses() =>
        AssertUnsupported(before: fixture => System.IO.File.WriteAllText(fixture.RootPath(@"lib\git\.chocolateyPending"), ""));

    [TestMethod]
    public void Step7ALibBackupEntryStartingWithTheTargetRefuses() =>
        AssertUnsupported(before: fixture => System.IO.Directory.CreateDirectory(fixture.RootPath(@"lib-bkp\GIT.backup")));

    // ---- Steps 8, 8a, 9 and precedence --------------------------------------------------------------------------------

    private const string NameOnlyCommunitySource = "<sources><source id='https://community.chocolatey.org/api/v2/' value='https://example.invalid/'/></sources>";

    [TestMethod]
    public void Step8ANameOnlyRedirectionIsSourceNotCommunityWithNoValues()
    {
        using RequesterToken token = N1NativeFixture.DerivedToken();
        using N2ProfileFixture fixture = new N2ProfileFixture().Build(NameOnlyCommunitySource).Lock();
        AssertRefused(Observe(token, fixture, ChocolateyObserverRole.Desktop, frozen: "1.1.0", expected: new string('0', 64)),
            ChocolateyProfileRefusal.SourceNotCommunity);
        // Precedence: a library failure (MachineInputsUnsupported) outranks the source.
        AssertRefused(Observe(token, fixture, ChocolateyObserverRole.Desktop, target: "missing"), ChocolateyProfileRefusal.MachineInputsUnsupported);
    }

    [TestMethod]
    public void Step8aDifferentFrozenInstalledVersionIsInstalledMismatchWithTheObservedValue()
    {
        using RequesterToken token = N1NativeFixture.DerivedToken();
        using N2ProfileFixture fixture = new N2ProfileFixture().Build().Lock();
        ChocolateyProfileResult.Refused refused = AssertRefused(
            Observe(token, fixture, ChocolateyObserverRole.Desktop, frozen: "1.1.0", expected: new string('0', 64)), ChocolateyProfileRefusal.InstalledMismatch);
        Assert.AreEqual("1.2.0", refused.ObservedInstalled);
        Assert.IsNull(refused.ObservedFingerprint);
    }

    [TestMethod]
    public void Step9ADifferentExpectedFingerprintIsProfileMismatchWithTheObservedValuesAndReleasesHelperHolds()
    {
        using RequesterToken token = N1NativeFixture.DerivedToken();
        using N2ProfileFixture fixture = new N2ProfileFixture().Build().Lock();
        fixture.HttpCache = SystemCache();
        string observed;
        using (ChocolateyProfileObservation baseline = Observed(Observe(token, fixture, ChocolateyObserverRole.Helper)))
            observed = baseline.Fingerprint;

        string different = observed[..63] + (observed[63] == '0' ? '1' : '0');
        ChocolateyProfileResult.Refused refused = AssertRefused(
            Observe(token, fixture, ChocolateyObserverRole.Helper, expected: different), ChocolateyProfileRefusal.ProfileMismatch);
        Assert.AreEqual("1.2.0", refused.ObservedInstalled);
        Assert.AreEqual(observed, refused.ObservedFingerprint);
        AssertRefused(Observe(token, fixture, ChocolateyObserverRole.Helper, expected: observed.ToLowerInvariant()), ChocolateyProfileRefusal.ProfileMismatch);
        // A refused helper observation holds nothing.
        OpenForWrite(fixture.RootPath("choco.exe")).Dispose();
    }

    // ---- Interrupts ---------------------------------------------------------------------------------------------------

    [TestMethod]
    public void AnExhaustedBudgetOrACancelledTokenIsAnInterruptBeforeAnyRead()
    {
        using RequesterToken token = N1NativeFixture.DerivedToken();
        using N2ProfileFixture fixture = new N2ProfileFixture().Build().Lock();
        Assert.IsInstanceOfType<ChocolateyProfileResult.TimedOut>(Observe(token, fixture, ChocolateyObserverRole.Helper, remaining: TimeSpan.Zero));
        Assert.IsInstanceOfType<ChocolateyProfileResult.TimedOut>(Observe(token, fixture, ChocolateyObserverRole.Helper, remaining: TimeSpan.FromSeconds(-1)));
        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();
        Assert.IsInstanceOfType<ChocolateyProfileResult.Cancelled>(Observe(token, fixture, ChocolateyObserverRole.Helper, cancellationToken: cancelled.Token));
    }

    [TestMethod]
    public void ACancellationDuringObservationIsCancelledNeverASettledRefusal()
    {
        using RequesterToken token = N1NativeFixture.DerivedToken();
        using N2ProfileFixture fixture = new N2ProfileFixture().Build().Lock();
        using CancellationTokenSource source = new();
        // The engine check cancels and also fails: the failure would be a settled refusal without the interrupt rule.
        ChocolateyProfileResult result = Observe(token, fixture, ChocolateyObserverRole.Desktop, cancellationToken: source.Token,
            engineTrust: (_, _) =>
            {
                source.Cancel();
                return false;
            });
        Assert.IsInstanceOfType<ChocolateyProfileResult.Cancelled>(result);
    }

    [TestMethod]
    public void ABudgetThatRunsOutDuringObservationIsTimedOutNeverASettledRefusal()
    {
        using RequesterToken token = N1NativeFixture.DerivedToken();
        using N2ProfileFixture fixture = new N2ProfileFixture().Build().Lock();
        ChocolateyProfileResult result = Observe(token, fixture, ChocolateyObserverRole.Desktop, remaining: TimeSpan.FromMilliseconds(300),
            engineTrust: (_, _) =>
            {
                Thread.Sleep(TimeSpan.FromMilliseconds(600));
                return false;
            });
        Assert.IsInstanceOfType<ChocolateyProfileResult.TimedOut>(result);
    }

    // ---- Helpers ------------------------------------------------------------------------------------------------------

    private static ChocolateyProfileResult Observe(RequesterToken token, N2ProfileFixture fixture, ChocolateyObserverRole role,
        string target = N2ProfileFixture.Target, string frozen = "1.2.0", string? expected = null, TimeSpan? remaining = null,
        Func<SafeFileHandle, string, bool>? engineTrust = null, CancellationToken cancellationToken = default) =>
        ChocolateyProfileObserver.ObserveForTests(token, role, target, N2ProfileFixture.Version(frozen), expected, remaining ?? Budget,
            fixture.Seam(engineTrust), cancellationToken);

    /// <summary>Builds a supported fixture, applies <paramref name="before"/> before locking and <paramref name="after"/> after, and requires MachineInputsUnsupported for both roles' common steps.</summary>
    private static void AssertUnsupported(Action<N2ProfileFixture>? before = null, Action<N2ProfileFixture>? after = null)
    {
        using RequesterToken token = N1NativeFixture.DerivedToken();
        using N2ProfileFixture fixture = new N2ProfileFixture().Build();
        before?.Invoke(fixture);
        fixture.Lock();
        after?.Invoke(fixture);
        AssertRefused(Observe(token, fixture, ChocolateyObserverRole.Desktop), ChocolateyProfileRefusal.MachineInputsUnsupported);
    }

    private static ChocolateyProfileResult.Refused AssertRefused(ChocolateyProfileResult result, ChocolateyProfileRefusal reason)
    {
        if (result is ChocolateyProfileResult.Observed observed) observed.Observation.Dispose();
        ChocolateyProfileResult.Refused refused = result as ChocolateyProfileResult.Refused
            ?? throw new AssertFailedException($"expected Refused({reason}), got {result.GetType().Name}");
        Assert.AreEqual(reason, refused.Reason);
        if (reason is ChocolateyProfileRefusal.MachineInputsUnsupported or ChocolateyProfileRefusal.SourceNotCommunity)
        {
            Assert.IsNull(refused.ObservedInstalled);
            Assert.IsNull(refused.ObservedFingerprint);
        }
        return refused;
    }

    private static ChocolateyProfileObservation Observed(ChocolateyProfileResult result) =>
        result is ChocolateyProfileResult.Observed observed ? observed.Observation
            : throw new AssertFailedException($"expected Observed, got {result}");

    private static string Identity(string path, bool directory)
    {
        using SafeFileHandle handle = N1NativeFixture.Open(path, directory, out NativeNode node);
        return ChocolateyProfileRules.FormatIdentity(node.Identity);
    }

    private static string Sha256(string path) => Convert.ToHexString(SHA256.HashData(System.IO.File.ReadAllBytes(path)));

    private static string GoldenIdentity(byte value) => ChocolateyCommonProfileFingerprint.FormatFileIdentity(1, Enumerable.Repeat(value, 16).ToArray());

    private static FileStream OpenForWrite(string path) => new(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);

    /// <summary>Rewrites a locked fixture file through the test process's logon-SID access (same length, different bytes).</summary>
    private static void Rewrite(string path, string content)
    {
        Assert.AreEqual(new FileInfo(path).Length, Encoding.UTF8.GetByteCount(content));
        using FileStream stream = OpenForWrite(path);
        stream.SetLength(0);
        stream.Write(Encoding.UTF8.GetBytes(content));
    }

    private static string SystemCache() =>
        N1NativeFixture.FinalPathOf(Path.Combine(Environment.SystemDirectory, "drivers", "etc"), directory: true);
}
