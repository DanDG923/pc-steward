using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCSteward.Application;
using PCSteward.Domain;
using PCSteward.Provider.Chocolatey;
using PCSteward.Provider.Chocolatey.Native;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// FR-UPD-15, FR-UPD-01, TM-03/04/07/13, AC-UPD-07; Task 50 design rev 6 §7 and §8 N5 (Windows CI): the desktop producers
/// over the real Desktop-role observer on synthetic <c>ProgramData</c> trees (<see cref="N2ProfileFixture"/>), evaluated with
/// the derived token through the observer's test seam. No real Chocolatey client, no runner ProgramData, no elevation.
/// Every test is Windows-only and is reported as failed elsewhere.
/// </summary>
[TestClass]
public sealed class ChocolateyDesktopProducersNativeTests
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(60);

    [TestMethod]
    public async Task FreshEvidenceOverARealObservationCarriesThatObservationsFingerprint()
    {
        N1NativeFixture.RequireWindows("N5 producers over the native observer");
        using RequesterToken token = N1NativeFixture.DerivedToken();
        using N2ProfileFixture fixture = new N2ProfileFixture().Build().Lock();
        string fingerprint;
        ChocolateyClientStateData direct;
        using (ChocolateyProfileObservation observation = Observed(Observe(token, fixture, N2ProfileFixture.Target, "1.2.0")))
            (fingerprint, direct) = (observation.Fingerprint, observation.ClientState);
        ReviewedUpdateOperation operation = Operation(token.Sid, "1.2.0", fingerprint);
        ChocolateyDispatchBaselines baselines = new();

        FreshUpdateDispatchEvidence? evidence = await Source(token, fixture, baselines).ObserveAsync(operation, Owner(token.Sid), CancellationToken.None);

        Assert.IsNotNull(evidence);
        Assert.AreEqual(ReviewedUpdateOperation.ProfileEvidencePrefix + fingerprint, evidence.EvidenceId);
        Assert.AreEqual(token.Sid, evidence.RequestingUserSid);
        Assert.IsTrue(evidence.ProfileEvidenceMatches(operation));
        Assert.IsTrue(baselines.TryTake(operation, out ChocolateyDispatchBaseline? baseline));
        Assert.AreEqual(direct with { EngineSha256 = ChocolateyClientReleasePolicy.EngineSha256 }, baseline.ClientState,
            "the baseline is the fresh observation's client state (engine hash projected, see FixtureObserve)");
    }

    [TestMethod]
    public async Task ARealInstalledMismatchGivesNoEvidence()
    {
        N1NativeFixture.RequireWindows("N5 producers over the native observer");
        using RequesterToken token = N1NativeFixture.DerivedToken();
        using N2ProfileFixture fixture = new N2ProfileFixture().Build().Lock();
        ChocolateyDispatchBaselines baselines = new();
        ReviewedUpdateOperation operation = Operation(token.Sid, "1.1.0", new string('A', 64));
        Assert.IsNull(await Source(token, fixture, baselines).ObserveAsync(operation, Owner(token.Sid), CancellationToken.None));
        Assert.IsFalse(baselines.TryTake(operation, out _));
    }

    [TestMethod]
    public async Task AnUpdatedTargetWithAnUnchangedClientIsVerified()
    {
        N1NativeFixture.RequireWindows("N5 verifier over the native observer");
        using RequesterToken token = N1NativeFixture.DerivedToken();
        using N2ProfileFixture fixture = new N2ProfileFixture().Build().Lock();
        (ReviewedUpdateOperation operation, ChocolateyDispatchBaselines baselines) = await DispatchedAsync(token, fixture);
        SimulateUpdate(fixture);

        PostUpdateInventoryResult result = await Verifier(token, fixture, baselines).VerifyAsync(operation, Owner(token.Sid), CancellationToken.None);

        Assert.AreEqual(new PostUpdateInventoryResult(true, false), result);
    }

    [TestMethod]
    public async Task AChangedHookAfterTheUpdateIsClientChangedNeverSuccess()
    {
        N1NativeFixture.RequireWindows("N5 verifier over the native observer");
        using RequesterToken token = N1NativeFixture.DerivedToken();
        using N2ProfileFixture fixture = new N2ProfileFixture().Build().Lock();
        (ReviewedUpdateOperation operation, ChocolateyDispatchBaselines baselines) = await DispatchedAsync(token, fixture);
        SimulateUpdate(fixture);
        RewriteSameLength(fixture.RootPath(@"hooks\pre-install-all.ps1"), "HOOK");

        PostUpdateInventoryResult result = await Verifier(token, fixture, baselines).VerifyAsync(operation, Owner(token.Sid), CancellationToken.None);

        Assert.AreEqual(new PostUpdateInventoryResult(false, false, PostUpdateVerificationDetail.ClientChanged), result);
    }

    [TestMethod]
    public async Task ATargetThatWasNotUpdatedIsNeverSuccess()
    {
        N1NativeFixture.RequireWindows("N5 verifier over the native observer");
        using RequesterToken token = N1NativeFixture.DerivedToken();
        using N2ProfileFixture fixture = new N2ProfileFixture().Build().Lock();
        (ReviewedUpdateOperation operation, ChocolateyDispatchBaselines baselines) = await DispatchedAsync(token, fixture);

        PostUpdateInventoryResult result = await Verifier(token, fixture, baselines).VerifyAsync(operation, Owner(token.Sid), CancellationToken.None);

        // The file capture observes at the expected version, so the unchanged 1.2.0 registration refuses it (step 8a).
        Assert.IsFalse(result.ExpectedVersionDetected);
        Assert.IsFalse(result.RestartPending);
        Assert.AreEqual(PostUpdateVerificationDetail.ClientUnverifiable, result.Detail);
    }

    // ---- Helpers ------------------------------------------------------------------------------------------------------

    /// <summary>F′ for 1.2.0 → 1.3.0 over the fixture, which records the client-state baseline the verifier takes.</summary>
    private static async Task<(ReviewedUpdateOperation, ChocolateyDispatchBaselines)> DispatchedAsync(RequesterToken token, N2ProfileFixture fixture)
    {
        string fingerprint;
        using (ChocolateyProfileObservation direct = Observed(Observe(token, fixture, N2ProfileFixture.Target, "1.2.0")))
            fingerprint = direct.Fingerprint;
        ReviewedUpdateOperation operation = Operation(token.Sid, "1.2.0", fingerprint);
        ChocolateyDispatchBaselines baselines = new();
        Assert.IsNotNull(await Source(token, fixture, baselines).ObserveAsync(operation, Owner(token.Sid), CancellationToken.None));
        return (operation, baselines);
    }

    /// <summary>
    /// What a completed upgrade leaves in the fixture library: the target archive now names 1.3.0 and the store directory
    /// carries the new version. Both nodes keep their locked DACLs (the file is rewritten in place and the directory renamed
    /// through the test process's logon-SID access); no client file changes.
    /// </summary>
    private static void SimulateUpdate(N2ProfileFixture fixture)
    {
        fixture.WritePackage(@"lib\git\git.nupkg", N2ProfileFixture.Target, "1.3.0");
        Directory.Move(fixture.RootPath(@".chocolatey\git.1.2.0"), fixture.RootPath(@".chocolatey\git.1.3.0"));
    }

    private static void RewriteSameLength(string path, string content)
    {
        Assert.AreEqual(new FileInfo(path).Length, Encoding.UTF8.GetByteCount(content));
        using FileStream stream = new(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
        stream.SetLength(0);
        stream.Write(Encoding.UTF8.GetBytes(content));
    }

    /// <summary>
    /// The real Desktop-role observer over the fixture. A fixture cannot hold the signed 2.7.5 engine, and the file-first
    /// comparison accepts only the pinned engine SHA-256, so the observed engine hash (which must be the fixture engine's)
    /// is projected onto the pinned value; every other fact (code digest, both registration digests, target, version,
    /// fingerprint) is the native observation's own.
    /// </summary>
    private static ChocolateyDesktopObserve FixtureObserve(RequesterToken token, N2ProfileFixture fixture) =>
        (target, frozen, remaining, cancellationToken) =>
        {
            ChocolateyProfileResult result = ChocolateyProfileObserver.ObserveForTests(token, ChocolateyObserverRole.Desktop, target, frozen,
                expectedFingerprint: null, remaining, fixture.Seam(), cancellationToken);
            if (result is not ChocolateyProfileResult.Observed observed) return result;
            using ChocolateyProfileObservation real = observed.Observation;
            Assert.IsNull(real.HeldClient, "the Desktop role holds nothing");
            Assert.AreEqual(fixture.Release.Single(file => file.Path == "choco.exe").Sha256, real.ClientState.EngineSha256);
            return ChocolateyProfileResult.Observed.Create(ChocolateyProfileObservation.Create(real.Common, real.Fingerprint,
                real.ClientState with { EngineSha256 = ChocolateyClientReleasePolicy.EngineSha256 }, real.InstalledVersion, real.TargetLibBadResidue,
                heldClient: null));
        };

    private static ChocolateyProfileEvidenceSource Source(RequesterToken token, N2ProfileFixture fixture, ChocolateyDispatchBaselines baselines) =>
        ChocolateyProfileEvidenceSource.CreateForTests(FixtureObserve(token, fixture), baselines, TimeProvider.System, Budget);

    private static ChocolateyReviewedUpdateVerifier Verifier(RequesterToken token, N2ProfileFixture fixture, ChocolateyDispatchBaselines baselines) =>
        ChocolateyReviewedUpdateVerifier.CreateForTests(FixtureObserve(token, fixture), baselines, TimeProvider.System, Budget);

    private static ChocolateyProfileResult Observe(RequesterToken token, N2ProfileFixture fixture, string target, string frozen) =>
        ChocolateyProfileObserver.ObserveForTests(token, ChocolateyObserverRole.Desktop, target, N2ProfileFixture.Version(frozen), null, Budget,
            fixture.Seam(), CancellationToken.None);

    private static ChocolateyProfileObservation Observed(ChocolateyProfileResult result) =>
        result is ChocolateyProfileResult.Observed observed ? observed.Observation : throw new AssertFailedException($"expected Observed, got {result}");

    private static OperationOwnership Owner(string sid) => new(sid, OperationGateContract.NamePrefix + sid, false, reconciled: true);

    private static ReviewedUpdateOperation Operation(string sid, string installed, string fingerprint)
    {
        DateTimeOffset reviewedAt = DateTimeOffset.UtcNow.AddSeconds(-5);
        ImmutableUpdateJob job = new UpdateJobReview(Guid.NewGuid(), Guid.NewGuid(), new UpdateJobSelection("choco:" + N2ProfileFixture.Target,
            UpdateProvider.Chocolatey, UpdateSource.ChocolateyCommunity, N2ProfileFixture.Target, installed, "1.3.0", UpdateScope.Machine,
            UpdateArchitecture.Neutral, UpdateChannel.Stable, "mapping-v2")).Confirm(reviewedAt);
        return new(job, sid, "choco:" + N2ProfileFixture.Target, "machine:choco", ReviewedUpdateOperation.ProfileEvidencePrefix + fingerprint, reviewedAt,
            UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3, ReviewedInstallerMode.NotControllable(UncontrolledInstallerModeReason.ProviderScriptControlled),
            ReviewedUpdateArchitecture.ProviderScriptSelected());
    }
}
