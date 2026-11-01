using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCSteward.Application;
using PCSteward.Domain;
using PCSteward.Provider.Chocolatey;
using PCSteward.Provider.Chocolatey.Native;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// FR-UPD-15, FR-UPD-01, TM-03/04/07/13, AC-UPD-07; Task 50 design rev 6 §7 and §8 N5, adapter gates §A.6.1 (A2-L6), fact
/// extension rev 3 §4.3 (L5), Task 90 M2: the desktop producers over scripted observer outcomes (portable; no token, no
/// file, no process). F′ is null for every refusal kind and carries the fresh fingerprint otherwise; the review intent is
/// UserCacheIntent with the fixed disclosure; the verifier compares files first, reaches the registration re-observation only
/// when unchanged, and is Success only for the proven expected version, never restart-pending.
/// </summary>
[TestClass]
public sealed class ChocolateyDesktopProducersTests
{
    private const string User = "S-1-5-21-11-22-33-1001";
    private const string Target = "git.install";
    private static readonly DateTimeOffset ReviewedAt = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(10);

    // ---- F′ (IReviewedUpdateDispatchEvidenceSource) ---------------------------------------------------------------------

    [TestMethod]
    public async Task FreshEvidenceCarriesTheFreshFingerprintAndTheFreshRequesterAndRecordsTheBaseline()
    {
        ChocolateyProfileObservation observation = Observation();
        string fresh = observation.Fingerprint;
        ReviewedUpdateOperation operation = Operation(frozenFingerprint: fresh);
        ScriptedObserver observer = new(_ => Observed(observation));
        ChocolateyDispatchBaselines baselines = new();
        Clock clock = new(ReviewedAt.AddSeconds(30));

        FreshUpdateDispatchEvidence? evidence = await Source(observer, baselines, clock).ObserveAsync(operation, Owner(), CancellationToken.None);

        Assert.IsNotNull(evidence);
        Assert.AreEqual(ReviewedUpdateOperation.ProfileEvidencePrefix + fresh, evidence.EvidenceId);
        Assert.AreEqual(User, evidence.RequestingUserSid);
        Assert.AreEqual(clock.GetUtcNow(), evidence.ObservedAt);
        Assert.IsTrue(evidence.Actionable && evidence.SourceIsFresh);
        Assert.IsTrue(evidence.ProfileEvidenceMatches(operation), "fresh F′ equal to the frozen F passes the §A.6.1 conjunct");
        Assert.IsFalse(evidence.Matches(operation, clock.GetUtcNow()), "the route is still not admitted (HasSupportedMode), so nothing can dispatch");
        Assert.AreEqual(1, observer.Calls.Count);
        Assert.AreEqual((Target, "1.0.0", Budget), observer.Calls[0], "observed for the frozen ID and installed version, within the bound");
        Assert.IsTrue(baselines.TryTake(operation, out ChocolateyDispatchBaseline? baseline));
        Assert.AreEqual(observation.ClientState, baseline.ClientState);
        Assert.AreEqual(User, baseline.RequestingUserSid);
    }

    [TestMethod]
    public async Task TheEvidenceIdentityComesFromTheFreshObservationNeverFromTheFrozenOperation()
    {
        ChocolateyProfileObservation observation = Observation();
        ReviewedUpdateOperation operation = Operation(frozenFingerprint: new string('A', 64));
        Assert.AreNotEqual(new string('A', 64), observation.Fingerprint);
        Clock clock = new(ReviewedAt.AddSeconds(30));
        FreshUpdateDispatchEvidence? evidence = await Source(new(_ => Observed(observation)), new(), clock)
            .ObserveAsync(operation, Owner(), CancellationToken.None);
        Assert.IsNotNull(evidence);
        Assert.AreEqual(ReviewedUpdateOperation.ProfileEvidencePrefix + observation.Fingerprint, evidence.EvidenceId);
        Assert.IsFalse(evidence.ProfileEvidenceMatches(operation), "a different fresh F′ never passes the §A.6.1 conjunct");
    }

    [TestMethod]
    [DataRow("machine-inputs")]
    [DataRow("source")]
    [DataRow("installed")]
    [DataRow("profile")]
    [DataRow("timed-out")]
    [DataRow("cancelled")]
    [DataRow("token-refused")]
    [DataRow("throws")]
    public async Task EveryObserverRefusalKindGivesNoEvidenceAndNoBaseline(string kind)
    {
        ReviewedUpdateOperation operation = Operation();
        ChocolateyDispatchBaselines baselines = new();
        FreshUpdateDispatchEvidence? evidence = await Source(new(_ => NotObserved(kind)), baselines).ObserveAsync(operation, Owner(), CancellationToken.None);
        Assert.IsNull(evidence);
        Assert.IsFalse(baselines.TryTake(operation, out _));
    }

    [TestMethod]
    [DataRow("target")]
    [DataRow("installed")]
    [DataRow("requester")]
    [DataRow("fingerprint")]
    public async Task AnObservationInconsistentWithTheRequestGivesNoEvidence(string change)
    {
        ChocolateyProfileObservation observation = change switch
        {
            "target" => Observation(target: "git"),
            "installed" => Observation(installed: "1.0.1"),
            "requester" => Observation(sid: "S-1-5-21-11-22-33-1002"),
            _ => Observation(fingerprintOverride: new string('F', 64)),
        };
        ChocolateyDispatchBaselines baselines = new();
        Assert.IsNull(await Source(new(_ => Observed(observation)), baselines).ObserveAsync(Operation(), Owner(), CancellationToken.None));
        Assert.IsFalse(baselines.TryTake(Operation(), out _));
    }

    [TestMethod]
    [DataRow("winget")]
    [DataRow("provisional")]
    [DataRow("unreconciled")]
    [DataRow("other-user")]
    public async Task AnOperationOrOwnershipOutsideTheRouteIsNeverObserved(string change)
    {
        ReviewedUpdateOperation operation = change switch
        {
            "winget" => WinGetOperation(),
            "provisional" => Operation(route: UpdateRouteContract.ChocolateyMachineProvisionalV3),
            _ => Operation(),
        };
        OperationOwnership owner = change switch
        {
            "unreconciled" => new OperationOwnership(User, OperationGateContract.NamePrefix + User, false, reconciled: false),
            "other-user" => new OperationOwnership("S-1-5-21-11-22-33-1002", OperationGateContract.NamePrefix + "S-1-5-21-11-22-33-1002", false, reconciled: true),
            _ => Owner(),
        };
        ScriptedObserver observer = new(_ => Observed(Observation()));
        Assert.IsNull(await Source(observer).ObserveAsync(operation, owner, CancellationToken.None));
        Assert.AreEqual(0, observer.Calls.Count, "nothing is observed for an operation the route does not cover");
    }

    [TestMethod]
    public async Task CancellationBeforeOrDuringTheObservationGivesNoEvidence()
    {
        using CancellationTokenSource before = new();
        before.Cancel();
        ScriptedObserver idle = new(_ => Observed(Observation()));
        Assert.IsNull(await Source(idle).ObserveAsync(Operation(), Owner(), before.Token));
        Assert.AreEqual(0, idle.Calls.Count);

        using CancellationTokenSource during = new();
        ScriptedObserver blocking = new(token =>
        {
            during.Cancel();
            token.WaitHandle.WaitOne(TimeSpan.FromSeconds(10));
            return Observed(Observation());
        });
        Assert.IsNull(await Source(blocking).ObserveAsync(Operation(), Owner(), during.Token));
    }

    [TestMethod]
    public async Task AnObservationThatOverrunsItsBoundIsAbandoned()
    {
        using ManualResetEventSlim release = new();
        ScriptedObserver hung = new(_ =>
        {
            release.Wait(TimeSpan.FromSeconds(30), CancellationToken.None);
            return Observed(Observation());
        });
        ChocolateyProfileEvidenceSource source = ChocolateyProfileEvidenceSource.CreateForTests(hung.Observe, new(), new Clock(ReviewedAt.AddSeconds(30)),
            TimeSpan.FromMilliseconds(100));
        Task<FreshUpdateDispatchEvidence?> pending = source.ObserveAsync(Operation(), Owner(), CancellationToken.None);
        Task finished = await Task.WhenAny(pending, Task.Delay(TimeSpan.FromSeconds(20)));
        release.Set();
        Assert.AreSame(pending, finished, "the real-time cap (bound + margin) ended the wait");
        Assert.IsNull(await pending);
    }

    // ---- Review intent ------------------------------------------------------------------------------------------------

    [TestMethod]
    public async Task TheReviewIntentIsUserCacheIntentWithTheFixedDisclosureAndTheFreshProfile()
    {
        ChocolateyProfileObservation observation = Observation(libBad: true);
        ScriptedObserver observer = new(_ => Observed(observation));
        Clock clock = new(ReviewedAt);
        ChocolateyReviewIntent? intent = await Source(observer, clock: clock).ObserveReviewIntentAsync(
            new StructuredUpdateCandidateEvidence(ChocolateyUpdateSourceConnector.CommunitySourceId, Target, "1.0.0", "2.0.0"), CancellationToken.None);
        Assert.IsNotNull(intent);
        Assert.AreEqual(ReviewedCandidateProvenance.UserCacheIntent, intent.CandidateProvenance);
        Assert.AreEqual("Candidate shown from your user cache; PC Steward confirms the exact version with protected machine data before updating.",
            intent.Disclosure);
        Assert.AreEqual((Target, "1.0.0", "2.0.0", User), (intent.PackageId, intent.InstalledVersion, intent.CandidateVersion, intent.RequestingUserSid));
        Assert.AreEqual(ReviewedUpdateOperation.ProfileEvidencePrefix + observation.Fingerprint, intent.ProfileEvidenceId);
        Assert.IsTrue(intent.TargetLibBadResidue, "lib-bad residue is carried for disclosure");
        Assert.AreEqual(ReviewedAt, intent.ObservedAt);
        Assert.AreEqual((Target, "1.0.0", Budget), observer.Calls.Single(), "the installed version the observation must find is the intent's");
    }

    [TestMethod]
    [DataRow("source")]
    [DataRow("client")]
    [DataRow("id")]
    [DataRow("installed-not-normalized")]
    [DataRow("candidate-not-normalized")]
    [DataRow("prerelease")]
    [DataRow("not-newer")]
    [DataRow("equal")]
    public async Task AnUnsupportedIntentIsRefusedWithoutObserving(string change)
    {
        StructuredUpdateCandidateEvidence intent = change switch
        {
            "source" => new("chocolatey-other", Target, "1.0.0", "2.0.0"),
            "client" => new(ChocolateyUpdateSourceConnector.CommunitySourceId, "chocolatey", "2.7.4", "2.7.5"),
            "id" => new(ChocolateyUpdateSourceConnector.CommunitySourceId, "git install", "1.0.0", "2.0.0"),
            "installed-not-normalized" => new(ChocolateyUpdateSourceConnector.CommunitySourceId, Target, "1.0", "2.0.0"),
            "candidate-not-normalized" => new(ChocolateyUpdateSourceConnector.CommunitySourceId, Target, "1.0.0", "2.0"),
            "prerelease" => new(ChocolateyUpdateSourceConnector.CommunitySourceId, Target, "1.0.0", "2.0.0-beta"),
            "not-newer" => new(ChocolateyUpdateSourceConnector.CommunitySourceId, Target, "2.0.0", "1.0.0"),
            _ => new(ChocolateyUpdateSourceConnector.CommunitySourceId, Target, "1.0.0", "1.0.0"),
        };
        ScriptedObserver observer = new(_ => Observed(Observation()));
        Assert.IsNull(await Source(observer).ObserveReviewIntentAsync(intent, CancellationToken.None));
        Assert.AreEqual(0, observer.Calls.Count);
    }

    [TestMethod]
    [DataRow("machine-inputs")]
    [DataRow("installed")]
    [DataRow("timed-out")]
    [DataRow("cancelled")]
    [DataRow("token-refused")]
    public async Task AReviewIntentWhoseObservationIsRefusedIsNull(string kind) =>
        Assert.IsNull(await Source(new(_ => NotObserved(kind))).ObserveReviewIntentAsync(
            new StructuredUpdateCandidateEvidence(ChocolateyUpdateSourceConnector.CommunitySourceId, Target, "1.0.0", "2.0.0"), CancellationToken.None));

    // ---- Verifier -----------------------------------------------------------------------------------------------------

    [TestMethod]
    public async Task OnlyAnUnchangedClientAndTheProvenExpectedVersionAreSuccess()
    {
        ReviewedUpdateOperation operation = Operation();
        ChocolateyDispatchBaselines baselines = Baselines(operation);
        ScriptedObserver observer = new(_ => Observed(Observation(installed: "2.0.0")));
        PostUpdateInventoryResult result = await Verifier(observer, baselines).VerifyAsync(operation, Owner(), CancellationToken.None);
        Assert.AreEqual(new PostUpdateInventoryResult(true, false), result);
        Assert.AreEqual(2, observer.Calls.Count, "the file-only capture, then the registration re-observation");
        Assert.IsTrue(observer.Calls.All(call => call == (Target, "2.0.0", Budget)), "both observations require the expected version");
        Assert.IsFalse(baselines.TryTake(operation, out _), "the baseline is one-use");
        Assert.AreEqual(PostUpdateVerificationDetail.ClientUnverifiable,
            (await Verifier(observer, baselines).VerifyAsync(operation, Owner(), CancellationToken.None)).Detail, "a second verification has no baseline");
    }

    [TestMethod]
    [DataRow("engine", PostUpdateVerificationDetail.ClientChanged)]
    [DataRow("code", PostUpdateVerificationDetail.ClientChanged)]
    [DataRow("client-registration", PostUpdateVerificationDetail.ClientChanged)]
    [DataRow("extension-registration", PostUpdateVerificationDetail.ClientChanged)]
    [DataRow("malformed", PostUpdateVerificationDetail.ClientUnverifiable)]
    public async Task AChangedOrUnreadableClientStopsBeforeTheRegistrationReobservation(string change, PostUpdateVerificationDetail detail)
    {
        ReviewedUpdateOperation operation = Operation();
        ChocolateyClientStateData before = State();
        ChocolateyClientStateData after = change switch
        {
            "engine" => before with { EngineSha256 = new string('E', 64) },
            "code" => before with { CodeDigest = new string('C', 64) },
            "client-registration" => before with { ClientRegistrationDigest = new string('D', 64) },
            "extension-registration" => before with { LicensedExtensionRegistrationDigest = new string('9', 64) },
            _ => before with { CodeDigest = "not-a-digest" },
        };
        ScriptedObserver observer = new(_ => Observed(Observation(installed: "2.0.0", state: after)));
        PostUpdateInventoryResult result = await Verifier(observer, Baselines(operation, before)).VerifyAsync(operation, Owner(), CancellationToken.None);
        Assert.AreEqual(new PostUpdateInventoryResult(false, false, detail), result);
        Assert.AreEqual(1, observer.Calls.Count, "a changed client is never re-observed for the target");
    }

    [TestMethod]
    [DataRow("machine-inputs")]
    [DataRow("source")]
    [DataRow("installed")]
    [DataRow("profile")]
    [DataRow("timed-out")]
    [DataRow("cancelled")]
    [DataRow("token-refused")]
    [DataRow("throws")]
    [DataRow("other-requester")]
    public async Task AnUnobservableFileCaptureIsUnverifiable(string kind)
    {
        ReviewedUpdateOperation operation = Operation();
        ScriptedObserver observer = new(_ => kind == "other-requester"
            ? Observed(Observation(installed: "2.0.0", sid: "S-1-5-21-11-22-33-1002"))
            : NotObserved(kind, installed: "2.0.0"));
        PostUpdateInventoryResult result = await Verifier(observer, Baselines(operation)).VerifyAsync(operation, Owner(), CancellationToken.None);
        Assert.AreEqual(new PostUpdateInventoryResult(false, false, PostUpdateVerificationDetail.ClientUnverifiable), result);
        Assert.AreEqual(1, observer.Calls.Count);
    }

    [TestMethod]
    [DataRow("missing-or-duplicate-target")]
    [DataRow("wrong-version")]
    [DataRow("pending-or-residue")]
    [DataRow("timed-out")]
    [DataRow("other-requester")]
    [DataRow("drift-between-observations")]
    public async Task AnUnchangedClientWithoutTheUniqueExactTargetIsNotSuccess(string kind)
    {
        ReviewedUpdateOperation operation = Operation();
        ScriptedObserver observer = new(_ => Observed(Observation(installed: "2.0.0")));
        observer.Then(_ => kind switch
        {
            // The observer refuses a missing/duplicate target, pending markers and lib-bkp residue (step 7), and a different
            // installed version (step 8a), before any Observed.
            "missing-or-duplicate-target" or "pending-or-residue" => NotObserved("machine-inputs", installed: "2.0.0"),
            "wrong-version" => NotObserved("installed", installed: "2.0.0"),
            "timed-out" => NotObserved("timed-out"),
            "other-requester" => Observed(Observation(installed: "2.0.0", sid: "S-1-5-21-11-22-33-1002")),
            _ => Observed(Observation(installed: "2.0.0", state: State() with { CodeDigest = new string('C', 64) })),
        });
        PostUpdateInventoryResult result = await Verifier(observer, Baselines(operation)).VerifyAsync(operation, Owner(), CancellationToken.None);
        Assert.IsFalse(result.ExpectedVersionDetected);
        Assert.IsFalse(result.RestartPending);
        Assert.AreEqual(PostUpdateVerificationDetail.None, result.Detail, "the client was unchanged; the target was not proven");
        Assert.AreEqual(2, observer.Calls.Count);
    }

    [TestMethod]
    [DataRow("no-baseline")]
    [DataRow("other-operation")]
    [DataRow("unreconciled")]
    [DataRow("winget")]
    public async Task WithoutTheBaselineOrTheRouteNothingIsObservedAndTheResultIsUnverifiable(string change)
    {
        ReviewedUpdateOperation operation = change == "winget" ? WinGetOperation() : Operation();
        ChocolateyDispatchBaselines baselines = change switch
        {
            "no-baseline" => new(),
            "other-operation" => Baselines(Operation()),
            _ => Baselines(operation),
        };
        OperationOwnership owner = change == "unreconciled"
            ? new OperationOwnership(User, OperationGateContract.NamePrefix + User, false, reconciled: false)
            : Owner();
        ScriptedObserver observer = new(_ => Observed(Observation(installed: "2.0.0")));
        PostUpdateInventoryResult result = await Verifier(observer, baselines).VerifyAsync(operation, owner, CancellationToken.None);
        Assert.AreEqual(new PostUpdateInventoryResult(false, false, PostUpdateVerificationDetail.ClientUnverifiable), result);
        Assert.AreEqual(0, observer.Calls.Count);
    }

    [TestMethod]
    public async Task CancellationPropagatesAndIsNeverSuccess()
    {
        ReviewedUpdateOperation operation = Operation();
        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            Verifier(new(_ => Observed(Observation(installed: "2.0.0"))), Baselines(operation)).VerifyAsync(operation, Owner(), cancelled.Token));

        using CancellationTokenSource during = new();
        ScriptedObserver observer = new(_ =>
        {
            during.Cancel();
            return Observed(Observation(installed: "2.0.0"));
        });
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            Verifier(observer, Baselines(operation)).VerifyAsync(operation, Owner(), during.Token));
        Assert.AreEqual(1, observer.Calls.Count, "a cancelled capture never reaches the re-observation");
    }

    [TestMethod]
    public async Task TheVerifierNeverReportsARestart()
    {
        ReviewedUpdateOperation operation = Operation();
        foreach (Func<CancellationToken, ChocolateyProfileResult?> script in new Func<CancellationToken, ChocolateyProfileResult?>[]
                 {
                     _ => Observed(Observation(installed: "2.0.0")), _ => NotObserved("machine-inputs", installed: "2.0.0"),
                     _ => Observed(Observation(installed: "2.0.0", state: State() with { EngineSha256 = new string('E', 64) })),
                 })
            Assert.IsFalse((await Verifier(new(script), Baselines(operation)).VerifyAsync(operation, Owner(), CancellationToken.None)).RestartPending);
    }

    // ---- Helpers ------------------------------------------------------------------------------------------------------

    private static ChocolateyProfileEvidenceSource Source(ScriptedObserver observer, ChocolateyDispatchBaselines? baselines = null, Clock? clock = null) =>
        ChocolateyProfileEvidenceSource.CreateForTests(observer.Observe, baselines ?? new(), clock ?? new Clock(ReviewedAt.AddSeconds(30)), Budget);

    private static ChocolateyReviewedUpdateVerifier Verifier(ScriptedObserver observer, ChocolateyDispatchBaselines baselines) =>
        ChocolateyReviewedUpdateVerifier.CreateForTests(observer.Observe, baselines, new Clock(ReviewedAt.AddMinutes(1)), Budget);

    private static ChocolateyDispatchBaselines Baselines(ReviewedUpdateOperation operation, ChocolateyClientStateData? state = null)
    {
        ChocolateyDispatchBaselines baselines = new();
        baselines.Record(operation, new ChocolateyDispatchBaseline(Target, User, state ?? State()));
        return baselines;
    }

    private static OperationOwnership Owner() => new(User, OperationGateContract.NamePrefix + User, false, reconciled: true);

    private static ReviewedUpdateOperation Operation(string? frozenFingerprint = null, UpdateRouteContract route = UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3)
    {
        ImmutableUpdateJob job = new UpdateJobReview(Guid.NewGuid(), Guid.NewGuid(), new UpdateJobSelection("choco:" + Target, UpdateProvider.Chocolatey,
            UpdateSource.ChocolateyCommunity, Target, "1.0.0", "2.0.0", UpdateScope.Machine, UpdateArchitecture.Neutral, UpdateChannel.Stable,
            "mapping-v2")).Confirm(ReviewedAt);
        return new(job, User, "choco:" + Target, "machine:choco",
            route == UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3 ? ReviewedUpdateOperation.ProfileEvidencePrefix + (frozenFingerprint ?? new string('A', 64)) : "evidence:1",
            ReviewedAt, route, ReviewedInstallerMode.NotControllable(UncontrolledInstallerModeReason.ProviderScriptControlled),
            ReviewedUpdateArchitecture.ProviderScriptSelected());
    }

    private static ReviewedUpdateOperation WinGetOperation()
    {
        ImmutableUpdateJob job = new UpdateJobReview(Guid.NewGuid(), Guid.NewGuid(), new UpdateJobSelection("winget:contoso.widget", UpdateProvider.WinGet,
            UpdateSource.WinGet, "Contoso.Widget", "1.0", "1.1", UpdateScope.User, UpdateArchitecture.X64, UpdateChannel.Stable, "mapping-v1")).Confirm(ReviewedAt);
        return new(job, User, "native:1", "context:1", "review:1", ReviewedAt, UpdateRouteContract.WinGetUserExactV3,
            ReviewedInstallerMode.Selected(InstallerMode.Silent));
    }

    private static ChocolateyClientStateData State() =>
        new(ChocolateyClientReleasePolicy.EngineSha256, new string('A', 64), new string('B', 64), null);

    private static ChocolateyProfileObservation Observation(string target = Target, string installed = "1.0.0", string sid = User,
        ChocolateyClientStateData? state = null, bool libBad = false, string? fingerprintOverride = null)
    {
        Assert.IsTrue(ChocolateyConfigurationParser.TryRead(Encoding.UTF8.GetBytes("<chocolatey></chocolatey>"), out ChocolateyConfigurationObservation? configuration));
        ChocolateyNativeVersion version = Version(installed);
        ChocolateyCommonProfileData common = new(sid, Identity(1), [new ChocolateyCodeFileData("hooks/a.ps1", Identity(3), 3, new string('C', 64))],
            Identity(2), configuration!, target, version, Identity(4), new string('B', 64), Identity(5));
        return ChocolateyProfileObservation.Create(common, fingerprintOverride ?? ChocolateyCommonProfileFingerprint.Compute(common), state ?? State(),
            version, libBad, heldClient: null);
    }

    private static ChocolateyProfileResult.Observed Observed(ChocolateyProfileObservation observation) => ChocolateyProfileResult.Observed.Create(observation);

    /// <summary>Every non-Observed outcome: the four settled reasons, both interrupts, a token refusal (null) and a throwing observer.</summary>
    private static ChocolateyProfileResult? NotObserved(string kind, string installed = "1.0.0") => kind switch
    {
        "machine-inputs" => ChocolateyProfileResult.Refused.Create(ChocolateyProfileRefusal.MachineInputsUnsupported, null, null),
        "source" => ChocolateyProfileResult.Refused.Create(ChocolateyProfileRefusal.SourceNotCommunity, null, null),
        "installed" => ChocolateyProfileResult.Refused.Create(ChocolateyProfileRefusal.InstalledMismatch, installed == "1.0.0" ? "0.9.0" : "1.0.0", null),
        "profile" => ChocolateyProfileResult.Refused.Create(ChocolateyProfileRefusal.ProfileMismatch, installed, new string('D', 64)),
        "timed-out" => ChocolateyProfileResult.TimedOut.Create(),
        "cancelled" => ChocolateyProfileResult.Cancelled.Create(),
        "token-refused" => null,
        "throws" => throw new InvalidOperationException("synthetic observer fault"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static ChocolateyNativeVersion Version(string text) =>
        ChocolateyNativeVersion.TryParse(text, out ChocolateyNativeVersion? version) ? version! : throw new ArgumentException(text);

    private static string Identity(byte value) => ChocolateyCommonProfileFingerprint.FormatFileIdentity(1, Enumerable.Repeat(value, 16).ToArray());

    /// <summary>Scripted observer outcomes; records each call's target, frozen installed version and bound.</summary>
    private sealed class ScriptedObserver(Func<CancellationToken, ChocolateyProfileResult?> first)
    {
        private readonly Queue<Func<CancellationToken, ChocolateyProfileResult?>> next = new();
        private Func<CancellationToken, ChocolateyProfileResult?> current = first;
        private readonly Lock gate = new();

        internal List<(string Target, string Installed, TimeSpan Remaining)> Calls { get; } = [];

        internal void Then(Func<CancellationToken, ChocolateyProfileResult?> script) => next.Enqueue(script);

        internal ChocolateyProfileResult? Observe(string targetId, ChocolateyNativeVersion frozenInstalled, TimeSpan remaining, CancellationToken cancellationToken)
        {
            Func<CancellationToken, ChocolateyProfileResult?> script;
            lock (gate)
            {
                Calls.Add((targetId, frozenInstalled.Normalized, remaining));
                script = current;
                if (next.Count > 0) current = next.Dequeue();
            }
            return script(cancellationToken);
        }
    }

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
