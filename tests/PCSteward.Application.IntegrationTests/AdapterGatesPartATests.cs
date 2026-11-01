using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using PCSteward.Domain;
using PCSteward.Persistence.Sqlite;
using PCSteward.Provider.Chocolatey;

namespace PCSteward.Application.IntegrationTests;

/// <summary>
/// FR-UPD-15, FR-UPD-01, TM-03/04/07/13. Task 50 adapter gates Part A, portable slice: the pre-claim <c>Supports</c> wrapper
/// (§A.2), the route-confined fresh-evidence rule (§A.6.1, §R A2-L6) and the stored-row behaviour of the O2 EvidenceId
/// enforcement (§A.6.2).
/// </summary>
[TestClass]
public sealed class AdapterGatesPartATests
{
    private const string User = "S-1-5-21-11-22-33-1001";
    private static readonly DateTimeOffset ReviewedAt = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly string F = Convert.ToHexString(SHA256.HashData("adapter gates profile F"u8));
    private static readonly string OtherF = Convert.ToHexString(SHA256.HashData("adapter gates profile F-prime"u8));

    // ---- §A.2: Supports wrapper ----

    [TestMethod]
    public async Task ThrowingPreClaimSupportsIsNotStartedWithoutGateClaimOrDispatch()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        ReviewedUpdateDraft draft = await fixture.SaveWinGetAsync();
        CountingDispatcher dispatcher = new(_ => throw new IOException("capability ledger unreadable"));
        ReviewedUpdateDispatchResult result = await fixture.Workflow(dispatcher).RunAsync(fixture.Confirm(draft), CancellationToken.None);
        Assert.AreEqual(ReviewedUpdateDispatchDisposition.NotStarted, result.Disposition);
        Assert.IsNull(result.Outcome);
        Assert.AreEqual("This reviewed provider, mode or route is unavailable. No update was started.", result.Message);
        Assert.AreEqual(1, dispatcher.SupportsCalls);
        Assert.AreEqual(0, dispatcher.Dispatches);
        Assert.AreEqual(0, fixture.GateCalls);
        Assert.IsNull(await fixture.Store.GetDispatchStateAsync(draft.Operation.JobId, CancellationToken.None));
        Assert.IsFalse(await fixture.Store.HasUnresolvedDispatchesAsync(Owner(), CancellationToken.None));
    }

    [TestMethod]
    public async Task SupportsIsEvaluatedAgainBeforeClaimAndAfterPermitWithoutCaching()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        ReviewedUpdateDraft draft = await fixture.SaveWinGetAsync();
        CountingDispatcher dispatcher = new(_ => true);
        ReviewedUpdateDispatchResult result = await fixture.Workflow(dispatcher).RunAsync(fixture.Confirm(draft), CancellationToken.None);
        Assert.AreEqual(ReviewedUpdateDispatchDisposition.Finalized, result.Disposition);
        Assert.AreEqual(2, dispatcher.SupportsCalls);
        Assert.AreEqual(1, dispatcher.Dispatches);
    }

    [TestMethod]
    public async Task CapabilityRemovedAfterTheClaimIsCancelledNotInvokedAndNeverDispatched()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        ReviewedUpdateDraft draft = await fixture.SaveWinGetAsync();
        CountingDispatcher dispatcher = new(call => call == 1);
        ReviewedUpdateDispatchResult result = await fixture.Workflow(dispatcher).RunAsync(fixture.Confirm(draft), CancellationToken.None);
        Assert.AreEqual(ReviewedUpdateDispatchDisposition.Finalized, result.Disposition);
        Assert.AreEqual(UpdateDispatchOutcome.Cancelled, result.Outcome);
        Assert.AreEqual(0, dispatcher.Dispatches);
        Assert.AreEqual(UpdateDispatchState.Final, await fixture.Store.GetDispatchStateAsync(draft.Operation.JobId, CancellationToken.None));
    }

    // ---- §A.6.1 / A2-L6: fresh evidence for the runtime-limit route ----

    [TestMethod]
    [DataRow("fresh-1", false)]
    [DataRow("lowercase-digest", false)]
    [DataRow("digits-63", false)]
    [DataRow("digits-65", false)]
    [DataRow("different-f-prime", false)]
    [DataRow("profile-v1-prefix", false)]
    [DataRow("equal-f-prime", true)]
    public void RuntimeLimitFreshEvidenceMustCarryTheFrozenProfileDigest(string change, bool expected)
    {
        ReviewedUpdateOperation op = RuntimeLimits();
        FreshUpdateDispatchEvidence fresh = Fresh(op, change switch
        {
            "fresh-1" => "fresh:1",
            "lowercase-digest" => ReviewedUpdateOperation.ProfileEvidencePrefix + F.ToLowerInvariant(),
            "digits-63" => ReviewedUpdateOperation.ProfileEvidencePrefix + F[..63],
            "digits-65" => ReviewedUpdateOperation.ProfileEvidencePrefix + F + "0",
            "different-f-prime" => ReviewedUpdateOperation.ProfileEvidencePrefix + OtherF,
            "profile-v1-prefix" => "choco-profile-v1:" + F,
            _ => ReviewedUpdateOperation.ProfileEvidencePrefix + F,
        });
        Assert.AreEqual(expected, fresh.ProfileEvidenceMatches(op), change);
        // The route is not admitted yet (HasSupportedMode), so Matches is false either way; the profile rule is an extra conjunct.
        Assert.IsFalse(ReviewedUpdateReview.HasSupportedMode(op));
        Assert.IsFalse(fresh.Matches(op, ReviewedAt.AddMinutes(1)));
    }

    [TestMethod]
    public void WinGetFreshEvidenceKeepsItsOwnIdentityWithoutGlobalEquality()
    {
        ReviewedUpdateOperation op = WinGet();
        foreach (string evidenceId in new[] { "fresh:1", ReviewedUpdateOperation.ProfileEvidencePrefix + OtherF, "review:1" })
        {
            FreshUpdateDispatchEvidence fresh = Fresh(op, evidenceId);
            Assert.IsTrue(fresh.ProfileEvidenceMatches(op), evidenceId);
            Assert.IsTrue(fresh.Matches(op, ReviewedAt.AddMinutes(1)), evidenceId);
        }
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task AllMatchesSitesAgreeForTheRuntimeLimitRoute(bool sameProfile)
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        ReviewedUpdateDraft draft = await fixture.Store.SaveReviewedOperationAsync(RuntimeLimits(), CancellationToken.None);
        FreshUpdateDispatchEvidence fresh = Fresh(draft.Operation, ReviewedUpdateOperation.ProfileEvidencePrefix + (sameProfile ? F : OtherF));
        bool first = fresh.Matches(draft.Operation, ReviewedAt.AddMinutes(1));
        // Pre-claim site (ClaimAsync uses Matches before the store) and the store claim CAS site agree; TryConsume uses the same
        // pure Matches over the same immutable evidence and operation, so it cannot differ.
        Assert.AreEqual(first, fresh.Matches(draft.Operation, ReviewedAt.AddMinutes(1)));
        UpdateDispatchClaim? claim = await fixture.Store.TryClaimAsync(draft, fresh, Owner(), ReviewedAt.AddMinutes(1), CancellationToken.None);
        Assert.IsFalse(first);
        Assert.IsNull(claim);
        Assert.IsNull(await fixture.Store.GetDispatchStateAsync(draft.Operation.JobId, CancellationToken.None));
    }

    [TestMethod]
    public async Task WinGetClaimAndPermitSitesStillAcceptAFreshIdentity()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        ReviewedUpdateDraft draft = await fixture.SaveWinGetAsync();
        LiveApprovedUpdate approval = fixture.Confirm(draft);
        OperationOwnership owner = Owner();
        UpdateDispatchAdmissionResult admitted = await new UpdateDispatchAdmission(fixture.Store, fixture.Clock)
            .ClaimAsync(approval, Fresh(draft.Operation, "fresh:1"), owner, CancellationToken.None);
        Assert.IsNotNull(admitted.Permit);
        Assert.IsTrue(admitted.Permit.TryConsume(approval, owner));
    }

    // ---- §A.6.2: stored rows whose runtime-limit EvidenceId no longer conforms (forgery or old fixtures only) ----

    [TestMethod]
    public async Task NonConformingStoredDraftIsCorruptAndNeverRestored()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        ReviewedUpdateDraft draft = await fixture.Store.SaveReviewedOperationAsync(RuntimeLimits(), CancellationToken.None);
        await fixture.ForgeEvidenceAsync(draft.Operation, includeClaim: false);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.Store.GetReviewedOperationAsync(draft.Operation.JobId, CancellationToken.None));
    }

    [TestMethod]
    public async Task NonConformingDispatchClaimedRowRemainsABarrier()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        UpdateDispatchClaim claim = await fixture.InsertRuntimeClaimAsync();
        await fixture.ForgeEvidenceAsync(claim.Draft.Operation, includeClaim: true);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.Store.GetDispatchStateAsync(claim.Draft.Operation.JobId, CancellationToken.None));
        Assert.IsTrue(await fixture.Store.HasUnresolvedDispatchesAsync(Owner(), CancellationToken.None));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            fixture.Store.RecoverUnfinishedDispatchesAsync(Owner(), ReviewedAt.AddMinutes(5), CancellationToken.None));
        ReviewedUpdateHistoryRecord record = (await fixture.Store.GetReviewedUpdateHistoryAsync(10, CancellationToken.None)).Single();
        Assert.IsFalse(record.IntegrityVerified);
        Assert.IsTrue(ReviewedUpdateHistoryPresentation.Present(record).BlocksOtherUpdates);
        Assert.IsTrue(await fixture.Store.HasUnresolvedDispatchesAsync(Owner(), CancellationToken.None));
    }

    [TestMethod]
    [DataRow("Uncertain", true)]
    [DataRow("Final", false)]
    public async Task NonConformingTerminalRowIsUntrustedAndBlocksOnlyWhenNotFinal(string state, bool blocks)
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        UpdateDispatchClaim claim = await fixture.InsertRuntimeClaimAsync();
        OperationOwnership owner = Owner();
        UpdateDispatchTerminal terminal = state == "Final"
            ? new(UpdateDispatchOutcome.Cancelled, ReviewedAt.AddMinutes(2), null, "fixture")
            : new(UpdateDispatchOutcome.Uncertain, ReviewedAt.AddMinutes(2), null, "fixture");
        UpdateDispatchSettlement settlement = state == "Final"
            ? UpdateDispatchSettlement.BeforeInvocation(claim, owner)
            : UpdateDispatchSettlement.FromObservedWork(claim, owner, Task.CompletedTask, false, false, false, false, true);
        await fixture.SetTerminalAsync(claim, state, terminal.GetReservedPayload(settlement));
        ReviewedUpdateHistoryRecord before = (await fixture.Store.GetReviewedUpdateHistoryAsync(10, CancellationToken.None)).Single();
        Assert.IsTrue(before.IntegrityVerified, "baseline row must be readable before forging");

        await fixture.ForgeEvidenceAsync(claim.Draft.Operation, includeClaim: true);
        ReviewedUpdateHistoryRecord record = (await fixture.Store.GetReviewedUpdateHistoryAsync(10, CancellationToken.None)).Single();
        ReviewedUpdateHistoryView view = ReviewedUpdateHistoryPresentation.Present(record);
        Assert.IsFalse(record.IntegrityVerified);
        Assert.AreEqual(blocks, view.BlocksOtherUpdates);
        Assert.AreEqual(blocks, await fixture.Store.HasUnresolvedDispatchesAsync(Owner(), CancellationToken.None));
        if (!blocks) StringAssert.Contains(view.Status, "cannot be trusted");
        Assert.IsNull(view.HelperReport);
    }

    private static OperationOwnership Owner() => new(User, OperationGateContract.NamePrefix + User, false, reconciled: true);

    private static ReviewedUpdateOperation RuntimeLimits()
    {
        ImmutableUpdateJob job = new UpdateJobReview(Guid.NewGuid(), Guid.NewGuid(), new UpdateJobSelection("choco:example", UpdateProvider.Chocolatey,
            UpdateSource.ChocolateyCommunity, "example", "1.2.3", "1.2.4", UpdateScope.Machine, UpdateArchitecture.Neutral, UpdateChannel.Stable,
            "mapping-v2")).Confirm(ReviewedAt);
        return new(job, User, "choco:example", "machine:choco", ReviewedUpdateOperation.ProfileEvidencePrefix + F, ReviewedAt.AddSeconds(-1),
            UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3, ReviewedInstallerMode.NotControllable(UncontrolledInstallerModeReason.ProviderScriptControlled),
            ReviewedUpdateArchitecture.ProviderScriptSelected());
    }

    private static ReviewedUpdateOperation WinGet()
    {
        ImmutableUpdateJob job = new UpdateJobReview(Guid.NewGuid(), Guid.NewGuid(), new("winget:contoso.widget", UpdateProvider.WinGet, UpdateSource.WinGet,
            "Contoso.Widget", "1.0", "1.1", UpdateScope.User, UpdateArchitecture.X64, UpdateChannel.Stable, "mapping-v1")).Confirm(ReviewedAt);
        return new(job, User, "native:1", "context:1", "review:1", ReviewedAt.AddSeconds(-1), UpdateRouteContract.WinGetUserExactV3,
            ReviewedInstallerMode.Selected(InstallerMode.Silent));
    }

    private static FreshUpdateDispatchEvidence Fresh(ReviewedUpdateOperation op, string evidenceId) => new(op.Selection, op.RequestingUserSid,
        op.NativeInstanceId, op.InstallationContextId, evidenceId, ReviewedAt.AddMinutes(1), op.RouteContract, op.Mode, true, true);

    private static ReviewedUpdateDispatchObservation Settled() =>
        new(ReviewedUpdateInvocation.Started, ReviewedUpdateProviderStatus.Completed, 0, "fixture", Task.CompletedTask, true, true, true, false, false);

    /// <summary>Counts every Supports call; the predicate receives the 1-based call number (or throws).</summary>
    private sealed class CountingDispatcher(Func<int, bool> supports) : IReviewedUpdateRouteDispatcher
    {
        internal int SupportsCalls;
        internal int Dispatches;
        public bool Supports(ReviewedUpdateOperation operation) => supports(++SupportsCalls);
        public Task<ReviewedUpdateDispatchObservation> DispatchAsync(ReviewedUpdateDispatchTicket ticket, CancellationToken cancellationToken)
        {
            Dispatches++;
            return Task.FromResult(Settled());
        }
    }

    private sealed class FreshEvidence : IReviewedUpdateDispatchEvidenceSource
    {
        public Task<FreshUpdateDispatchEvidence?> ObserveAsync(ReviewedUpdateOperation operation, OperationOwnership ownership, CancellationToken cancellationToken) =>
            Task.FromResult<FreshUpdateDispatchEvidence?>(Fresh(operation, "fresh:1"));
    }

    private sealed class Verifier : IReviewedUpdateVerifier
    {
        public Task<PostUpdateInventoryResult> VerifyAsync(ReviewedUpdateOperation operation, OperationOwnership ownership, CancellationToken cancellationToken) =>
            Task.FromResult(new PostUpdateInventoryResult(true, false));
    }

    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => ReviewedAt.AddMinutes(1);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string path = Path.Combine(Path.GetTempPath(), $"pcsteward-task50-adapter-a-{Guid.NewGuid():N}.db");
        private Fixture() => Store = new(path, new ChocolateyHelperVersionGrammar());
        internal SqliteDatabase Store { get; }
        internal Clock Clock { get; } = new();
        internal int GateCalls;

        internal static async Task<Fixture> CreateAsync()
        {
            Fixture result = new();
            await result.Store.MigrateAsync(CancellationToken.None);
            return result;
        }

        internal Task<ReviewedUpdateDraft> SaveWinGetAsync() => Store.SaveReviewedOperationAsync(WinGet(), CancellationToken.None);

        internal LiveApprovedUpdate Confirm(ReviewedUpdateDraft draft) => new ReviewedUpdateReview(draft, Clock).Confirm();

        internal ReviewedUpdateDispatchWorkflow Workflow(IReviewedUpdateRouteDispatcher dispatcher) =>
            new((work, _, ct) => { GateCalls++; return work(Owner(), ct); }, new FreshEvidence(), dispatcher, new Verifier(), Store, Clock);

        /// <summary>The runtime-limit route is not admitted, so this inserts exactly the row TryClaimAsync would have written.</summary>
        internal async Task<UpdateDispatchClaim> InsertRuntimeClaimAsync()
        {
            ReviewedUpdateDraft draft = await Store.SaveReviewedOperationAsync(RuntimeLimits(), CancellationToken.None);
            ReviewedUpdateOperation op = draft.Operation;
            UpdateDispatchClaim claim = new(Guid.NewGuid(), Guid.NewGuid(), draft, Fresh(op, ReviewedUpdateOperation.ProfileEvidencePrefix + F), ReviewedAt.AddMinutes(1));
            await ExecuteAsync("""
                INSERT INTO update_dispatch_claims(job_id,claim_id,terminal_event_id,draft_generation,canonical_payload,canonical_digest,job_digest,fresh_evidence,claimed_at,state,terminal_payload)
                VALUES($job,$claim,$event,$generation,$payload,$digest,$jobDigest,$evidence,$at,'DispatchClaimed',zeroblob(16384));
                """,
                ("$job", op.JobId.ToString("D")), ("$claim", claim.ClaimId.ToString("D")), ("$event", claim.TerminalEventId.ToString("D")),
                ("$generation", draft.Generation.ToString("D")), ("$payload", op.GetCanonicalBytes()), ("$digest", op.Digest),
                ("$jobDigest", op.LegacyJobDigest), ("$evidence", claim.Evidence.GetCanonicalBytes()),
                ("$at", claim.ClaimedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)));
            return claim;
        }

        internal Task SetTerminalAsync(UpdateDispatchClaim claim, string state, byte[] payload) => ExecuteAsync(
            "UPDATE update_dispatch_claims SET state=$state, terminal_payload=$p, terminal_digest=$d WHERE job_id=$job;",
            ("$state", state), ("$p", payload), ("$d", Convert.ToHexString(SHA256.HashData(payload))), ("$job", claim.Draft.Operation.JobId.ToString("D")));

        /// <summary>
        /// Rewrites the stored canonical operation with a non-conforming runtime-limit EvidenceId and a recomputed digest, as a
        /// forger or a pre-O2 fixture would have stored it.
        /// </summary>
        internal async Task ForgeEvidenceAsync(ReviewedUpdateOperation op, bool includeClaim)
        {
            string[] lines = Encoding.UTF8.GetString(op.GetCanonicalBytes()).Split('\n');
            int field = Array.IndexOf(lines, op.EvidenceId);
            Assert.IsTrue(field > 0);
            lines[field] = "evidence:1";
            byte[] forged = Encoding.UTF8.GetBytes(string.Join('\n', lines));
            string digest = Convert.ToHexString(SHA256.HashData(forged));
            Assert.IsFalse(ReviewedUpdateOperation.TryRestore(forged, digest, out _));
            (string, object)[] parameters = [("$p", forged), ("$d", digest), ("$job", op.JobId.ToString("D"))];
            await ExecuteAsync("UPDATE reviewed_update_operations SET canonical_payload=$p, canonical_digest=$d WHERE job_id=$job;", parameters);
            if (includeClaim)
                await ExecuteAsync("UPDATE update_dispatch_claims SET canonical_payload=$p, canonical_digest=$d WHERE job_id=$job;", parameters);
        }

        private async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
        {
            await using SqliteConnection connection = new(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
            await connection.OpenAsync();
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = sql;
            foreach ((string name, object value) in parameters) command.Parameters.AddWithValue(name, value);
            Assert.AreEqual(1, await command.ExecuteNonQueryAsync());
        }

        public ValueTask DisposeAsync()
        {
            foreach (string file in new[] { path, path + "-wal", path + "-shm" }) File.Delete(file);
            return ValueTask.CompletedTask;
        }
    }
}
