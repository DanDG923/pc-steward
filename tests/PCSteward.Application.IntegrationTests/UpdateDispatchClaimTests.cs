using Microsoft.Data.Sqlite;
using PCSteward.Domain;
using PCSteward.Persistence.Sqlite;

namespace PCSteward.Application.IntegrationTests;

[TestClass]
public sealed class UpdateDispatchClaimTests
{
    private const string User = "S-1-5-21-11-22-33-1001";
    private static readonly DateTimeOffset ReviewedAt = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task CommittedClaimReservesTerminalCapacityAndIssuesOneUsePermit()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        ReviewedUpdateDraft draft = await fixture.SaveAsync();
        LiveApprovedUpdate approval = new ReviewedUpdateReview(draft, fixture.Clock).Confirm();
        OperationOwnership owner = Owner();
        UpdateDispatchPermit? permit = await new UpdateDispatchAdmission(fixture.Store, fixture.Clock).TryClaimAsync(approval, Fresh(draft.Operation), owner, CancellationToken.None);
        Assert.IsNotNull(permit);
        Assert.AreEqual(UpdateDispatchState.DispatchClaimed, await fixture.Store.GetDispatchStateAsync(draft.Operation.JobId, CancellationToken.None));
        Assert.AreEqual(16384L, await fixture.ScalarAsync("SELECT length(terminal_payload) FROM update_dispatch_claims;"));
        Assert.IsTrue(permit.TryConsume(approval, owner));
        Assert.IsFalse(permit.TryConsume(approval, owner));
        Assert.IsNull(await new UpdateDispatchAdmission(fixture.Store, fixture.Clock).TryClaimAsync(approval, Fresh(draft.Operation), owner, CancellationToken.None));
        Assert.IsEmpty(typeof(UpdateDispatchPermit).GetConstructors());
    }

    [TestMethod]
    [DataRow("missing")]
    [DataRow("recreated")]
    [DataRow("bytes")]
    [DataRow("generation")]
    public async Task ClaimMatchesExistingGenerationAndExactCanonicalBytes(string change)
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        ReviewedUpdateDraft draft = await fixture.SaveAsync();
        if (change is "missing" or "recreated") await fixture.ExecuteAsync("DELETE FROM reviewed_update_operations;");
        if (change == "recreated") await fixture.Store.SaveReviewedOperationAsync(draft.Operation, CancellationToken.None);
        if (change == "generation") await fixture.ExecuteAsync("UPDATE reviewed_update_operations SET draft_generation='11111111-1111-1111-1111-111111111111';");
        if (change == "bytes") await fixture.ExecuteAsync("UPDATE reviewed_update_operations SET canonical_payload=zeroblob(100);");
        Assert.IsNull(await fixture.Store.TryClaimAsync(draft, Fresh(draft.Operation), Owner(), fixture.Clock.GetUtcNow(), CancellationToken.None));
        Assert.AreEqual(0L, await fixture.ScalarAsync("SELECT COUNT(*) FROM update_dispatch_claims;"));
    }

    [TestMethod]
    [DataRow("native")]
    [DataRow("context")]
    [DataRow("selection")]
    [DataRow("sid")]
    [DataRow("mode")]
    [DataRow("route")]
    [DataRow("stale")]
    [DataRow("future")]
    [DataRow("not-actionable")]
    [DataRow("source")]
    public async Task FreshDispatchEvidenceCannotChangeTheFrozenTargetOrCapability(string change)
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        ReviewedUpdateDraft draft = await fixture.SaveAsync();
        FreshUpdateDispatchEvidence evidence = Fresh(draft.Operation, change);
        Assert.IsFalse(evidence.Matches(draft.Operation, fixture.Clock.GetUtcNow()));
        Assert.IsNull(await fixture.Store.TryClaimAsync(draft, evidence, Owner(), fixture.Clock.GetUtcNow(), CancellationToken.None));
    }

    [TestMethod]
    public async Task FailedClaimTransactionConsumesApprovalAndRollsBackReservation()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        ReviewedUpdateDraft draft = await fixture.SaveAsync();
        LiveApprovedUpdate approval = new ReviewedUpdateReview(draft, fixture.Clock).Confirm();
        await fixture.ExecuteAsync("CREATE TRIGGER reject_claim BEFORE INSERT ON update_dispatch_claims BEGIN SELECT RAISE(ABORT,'fixture claim failure'); END;");
        UpdateDispatchAdmission admission = new(fixture.Store, fixture.Clock);
        await Assert.ThrowsExactlyAsync<SqliteException>(() => admission.TryClaimAsync(approval, Fresh(draft.Operation), Owner(), CancellationToken.None));
        Assert.AreEqual(0L, await fixture.ScalarAsync("SELECT COUNT(*) FROM update_dispatch_claims;"));
        await fixture.ExecuteAsync("DROP TRIGGER reject_claim;");
        Assert.IsNull(await admission.TryClaimAsync(approval, Fresh(draft.Operation), Owner(), CancellationToken.None));
    }

    [TestMethod]
    public async Task UniqueClaimPreventsConcurrentOrRepeatedDispatch()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        ReviewedUpdateDraft draft = await fixture.SaveAsync();
        Task<UpdateDispatchClaim?>[] attempts = Enumerable.Range(0, 8).Select(_ => Task.Run(() => fixture.Store.TryClaimAsync(draft, Fresh(draft.Operation), Owner(), fixture.Clock.GetUtcNow(), CancellationToken.None))).ToArray();
        UpdateDispatchClaim?[] claims = await Task.WhenAll(attempts);
        Assert.AreEqual(1, claims.Count(c => c is not null));
        Assert.AreEqual(1L, await fixture.ScalarAsync("SELECT COUNT(*) FROM update_dispatch_claims;"));
    }

    [TestMethod]
    public async Task RecoveryRecordsUncertainWithoutReplayAndHistoryClearKeepsTheBarrier()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        ReviewedUpdateDraft draft = await fixture.SaveAsync();
        Assert.IsNotNull(await fixture.ClaimAsync(draft));
        OperationOwnership recoveryOwner = Owner(recovery: true);
        Assert.AreEqual(1, await fixture.Store.RecoverUnfinishedDispatchesAsync(recoveryOwner, fixture.Clock.GetUtcNow(), CancellationToken.None));
        Assert.AreEqual(UpdateDispatchState.Uncertain, await fixture.Store.GetDispatchStateAsync(draft.Operation.JobId, CancellationToken.None));
        Assert.AreEqual(0, await fixture.Store.RecoverUnfinishedDispatchesAsync(recoveryOwner, fixture.Clock.GetUtcNow(), CancellationToken.None));
        await fixture.Store.ClearUpdateHistoryAsync(CancellationToken.None);
        Assert.AreEqual(UpdateDispatchState.Uncertain, await fixture.Store.GetDispatchStateAsync(draft.Operation.JobId, CancellationToken.None));
        Assert.IsNull(await fixture.ClaimAsync(await fixture.SaveAsync()));
        Assert.IsTrue(recoveryOwner.RecoveryRequired);
    }

    [TestMethod]
    public async Task FinalizationRequiresExactClaimAndWritesTheReservedSlotOnlyOnce()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        ReviewedUpdateDraft draft = await fixture.SaveAsync();
        UpdateDispatchClaim claim = (await fixture.ClaimAsync(draft))!;
        UpdateDispatchTerminal terminal = new(UpdateDispatchOutcome.Failed, fixture.Clock.GetUtcNow(), -1, new string('\u0800', 4096));
        Assert.HasCount(UpdateDispatchTerminal.ReservedBytes, terminal.GetReservedPayload());
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.Store.RecordFinalAsync(claim with { ClaimId = Guid.NewGuid() }, Owner(), terminal, CancellationToken.None));
        await fixture.Store.RecordFinalAsync(claim, Owner(), terminal, CancellationToken.None);
        Assert.AreEqual(UpdateDispatchState.Final, await fixture.Store.GetDispatchStateAsync(draft.Operation.JobId, CancellationToken.None));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.Store.RecordFinalAsync(claim, Owner(), terminal, CancellationToken.None));
        Assert.IsNotNull(await fixture.ClaimAsync(await fixture.SaveAsync()));
    }

    [TestMethod]
    public async Task ReleasedOrRecoveryOnlyOwnershipCannotClaimOrConsumeAPermit()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        ReviewedUpdateDraft draft = await fixture.SaveAsync();
        Assert.IsNull(await fixture.Store.TryClaimAsync(draft, Fresh(draft.Operation), Owner(recovery: true), fixture.Clock.GetUtcNow(), CancellationToken.None));
        LiveApprovedUpdate approval = new ReviewedUpdateReview(draft, fixture.Clock).Confirm();
        OperationOwnership owner = Owner();
        UpdateDispatchPermit permit = (await new UpdateDispatchAdmission(fixture.Store, fixture.Clock).TryClaimAsync(approval, Fresh(draft.Operation), owner, CancellationToken.None))!;
        owner.Release();
        Assert.IsFalse(permit.TryConsume(approval, owner));
        Assert.IsFalse(permit.TryConsume(approval, Owner()));
    }

    private static OperationOwnership Owner(bool recovery = false) => new(User, OperationGateContract.NamePrefix + User, recovery);
    [TestMethod]
    public async Task LostCommitAcknowledgmentCannotProduceAPermitOrRetry()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        ReviewedUpdateDraft draft = await fixture.SaveAsync();
        LiveApprovedUpdate approval = new ReviewedUpdateReview(draft, fixture.Clock).Confirm();
        ClaimBoundaryStore store = new(fixture.Store, () => throw new IOException("fixture lost commit acknowledgment"));
        UpdateDispatchAdmission admission = new(store, fixture.Clock);
        await Assert.ThrowsExactlyAsync<IOException>(() => admission.TryClaimAsync(approval, Fresh(draft.Operation), Owner(), CancellationToken.None));
        Assert.AreEqual(UpdateDispatchState.DispatchClaimed, await fixture.Store.GetDispatchStateAsync(draft.Operation.JobId, CancellationToken.None));
        Assert.IsNull(await admission.TryClaimAsync(approval, Fresh(draft.Operation), Owner(), CancellationToken.None));
        Assert.AreEqual(1, await fixture.Store.RecoverUnfinishedDispatchesAsync(Owner(recovery: true), fixture.Clock.GetUtcNow(), CancellationToken.None));
    }

    [TestMethod]
    public async Task ActualCommitConstraintFailureRollsBackClaimAndReservation()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        ReviewedUpdateDraft draft = await fixture.SaveAsync();
        await fixture.ExecuteAsync("CREATE TABLE commit_parent(id INTEGER PRIMARY KEY); CREATE TABLE commit_guard(id INTEGER REFERENCES commit_parent(id) DEFERRABLE INITIALLY DEFERRED); CREATE TRIGGER fail_commit AFTER INSERT ON update_dispatch_claims BEGIN INSERT INTO commit_guard VALUES(1); END;");
        await Assert.ThrowsExactlyAsync<SqliteException>(() => fixture.ClaimAsync(draft));
        Assert.AreEqual(0L, await fixture.ScalarAsync("SELECT COUNT(*) FROM update_dispatch_claims;"));
    }

    [TestMethod]
    [DataRow("expired")]
    [DataRow("cancelled")]
    public async Task ExpiryOrCancellationAfterCommitLeavesClaimWithoutAPermit(string change)
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        ReviewedUpdateDraft draft = await fixture.SaveAsync();
        using CancellationTokenSource cancellation = new();
        LiveApprovedUpdate approval = new ReviewedUpdateReview(draft, fixture.Clock, cancellation.Token).Confirm();
        ClaimBoundaryStore store = new(fixture.Store, () =>
        {
            if (change == "expired") fixture.Clock.Now = ReviewedAt.AddMinutes(31);
            else cancellation.Cancel();
        });
        Assert.IsNull(await new UpdateDispatchAdmission(store, fixture.Clock).TryClaimAsync(approval, Fresh(draft.Operation), Owner(), CancellationToken.None));
        Assert.AreEqual(UpdateDispatchState.DispatchClaimed, await fixture.Store.GetDispatchStateAsync(draft.Operation.JobId, CancellationToken.None));
    }

    [TestMethod]
    public async Task ClaimTriggerCannotReplaceTheOriginalDraft()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        ReviewedUpdateDraft draft = await fixture.SaveAsync();
        await fixture.ExecuteAsync("CREATE TRIGGER replace_draft AFTER INSERT ON update_dispatch_claims BEGIN UPDATE reviewed_update_operations SET draft_generation='11111111-1111-1111-1111-111111111111' WHERE job_id=NEW.job_id; END;");
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.ClaimAsync(draft));
        Assert.AreEqual(0L, await fixture.ScalarAsync("SELECT COUNT(*) FROM update_dispatch_claims;"));
        Assert.IsTrue(draft.HasSameDraftIdentity(await fixture.Store.GetReviewedOperationDraftAsync(draft.Operation.JobId, CancellationToken.None)));
    }

    [TestMethod]
    public async Task TerminalWriteFailureRetainsClaimAndRecoveryUsesUncertain()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        UpdateDispatchClaim claim = (await fixture.ClaimAsync(await fixture.SaveAsync()))!;
        await fixture.ExecuteAsync("CREATE TRIGGER reject_final BEFORE UPDATE ON update_dispatch_claims BEGIN SELECT RAISE(ABORT,'fixture final persistence failure'); END;");
        await Assert.ThrowsExactlyAsync<SqliteException>(() => fixture.Store.RecordFinalAsync(claim, Owner(), new(UpdateDispatchOutcome.Success, fixture.Clock.GetUtcNow(), 0, "fixture verified"), CancellationToken.None));
        Assert.AreEqual(UpdateDispatchState.DispatchClaimed, await fixture.Store.GetDispatchStateAsync(claim.Draft.Operation.JobId, CancellationToken.None));
        await fixture.ExecuteAsync("DROP TRIGGER reject_final;");
        Assert.AreEqual(1, await fixture.Store.RecoverUnfinishedDispatchesAsync(Owner(), fixture.Clock.GetUtcNow(), CancellationToken.None));
        Assert.AreEqual(UpdateDispatchState.Uncertain, await fixture.Store.GetDispatchStateAsync(claim.Draft.Operation.JobId, CancellationToken.None));
        await fixture.ExecuteAsync("DELETE FROM reviewed_update_operations;");
        Assert.IsFalse(await fixture.Store.IsFreshResetStoreAsync(CancellationToken.None));
    }

    [TestMethod]
    public async Task ClaimMigrationFromTenIsTransactionalAndPreservesTheOriginalDraft()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        ReviewedUpdateDraft draft = await fixture.SaveAsync();
        await fixture.ExecuteAsync("DROP TABLE update_dispatch_claims; DELETE FROM schema_migrations WHERE version=11; CREATE TRIGGER reject_eleven BEFORE INSERT ON schema_migrations WHEN NEW.version=11 BEGIN SELECT RAISE(ABORT,'fixture migration fault'); END;");
        await Assert.ThrowsExactlyAsync<SqliteException>(() => fixture.Store.MigrateAsync(CancellationToken.None));
        Assert.AreEqual(10L, await fixture.ScalarAsync("SELECT MAX(version) FROM schema_migrations;"));
        Assert.AreEqual(0L, await fixture.ScalarAsync("SELECT COUNT(*) FROM sqlite_master WHERE name IN ('update_dispatch_claims','ix_update_dispatch_unresolved');"));
        Assert.IsTrue(draft.HasSameDraftIdentity(await fixture.Store.GetReviewedOperationDraftAsync(draft.Operation.JobId, CancellationToken.None)));
        await fixture.ExecuteAsync("DROP TRIGGER reject_eleven;");
        await fixture.Store.MigrateAsync(CancellationToken.None);
        Assert.IsNotNull(await fixture.ClaimAsync(draft));
    }

    [TestMethod]
    [DataRow("missing")]
    [DataRow("partial")]
    [DataRow("corrupt")]
    public async Task IncompleteClaimSchemaOrCorruptStateFailsClosed(string change)
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        ReviewedUpdateDraft draft = await fixture.SaveAsync();
        if (change == "missing") await fixture.ExecuteAsync("DROP TABLE update_dispatch_claims;");
        else if (change == "partial") await fixture.ExecuteAsync("DELETE FROM schema_migrations WHERE version=11;");
        else
        {
            await fixture.ClaimAsync(draft);
            await fixture.ExecuteAsync("PRAGMA ignore_check_constraints=ON; UPDATE update_dispatch_claims SET canonical_payload=zeroblob(16385);");
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.Store.GetDispatchStateAsync(draft.Operation.JobId, CancellationToken.None));
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.Store.RecoverUnfinishedDispatchesAsync(Owner(recovery: true), fixture.Clock.GetUtcNow(), CancellationToken.None));
            return;
        }
        await Assert.ThrowsExactlyAsync<SqliteException>(() => fixture.Store.MigrateAsync(CancellationToken.None));
    }

    private sealed class ClaimBoundaryStore(IUpdateDispatchStore inner, Action afterCommit) : IUpdateDispatchStore
    {
        public async Task<UpdateDispatchClaim?> TryClaimAsync(ReviewedUpdateDraft draft, FreshUpdateDispatchEvidence evidence, OperationOwnership ownership, DateTimeOffset at, CancellationToken ct)
        {
            UpdateDispatchClaim? claim = await inner.TryClaimAsync(draft, evidence, ownership, at, ct);
            afterCommit();
            return claim;
        }
        public Task RecordFinalAsync(UpdateDispatchClaim claim, OperationOwnership owner, UpdateDispatchTerminal terminal, CancellationToken ct) => inner.RecordFinalAsync(claim, owner, terminal, ct);
        public Task<int> RecoverUnfinishedDispatchesAsync(OperationOwnership owner, DateTimeOffset now, CancellationToken ct) => inner.RecoverUnfinishedDispatchesAsync(owner, now, ct);
        public Task<UpdateDispatchState?> GetDispatchStateAsync(Guid job, CancellationToken ct) => inner.GetDispatchStateAsync(job, ct);
    }
    private static FreshUpdateDispatchEvidence Fresh(ReviewedUpdateOperation op, string? change = null) => new(
        change == "selection" ? new(op.Selection.StableProductKey, op.Selection.Provider, op.Selection.Source, op.Selection.PackageId, "0.9", "1.1", op.Selection.Scope, op.Selection.Architecture, op.Selection.Channel, op.Selection.MappingEvidenceVersion) : op.Selection,
        change == "sid" ? "S-1-5-21-1" : op.RequestingUserSid, change == "native" ? "other:1" : op.NativeInstanceId,
        change == "context" ? "other:1" : op.InstallationContextId, "fresh:1",
        change == "stale" ? ReviewedAt.AddMinutes(-3) : change == "future" ? ReviewedAt.AddMinutes(2) : ReviewedAt.AddMinutes(1),
        change == "route" ? UpdateRouteContract.ChocolateyMachineRegistrationV2 : op.RouteContract,
        change == "mode" ? ReviewedInstallerMode.Selected(InstallerMode.Interactive) : op.Mode, change != "not-actionable", change != "source");

    private sealed class Clock : TimeProvider
    {
        internal DateTimeOffset Now = ReviewedAt.AddMinutes(1);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string path = Path.Combine(Path.GetTempPath(), $"pcsteward-task50-claims-{Guid.NewGuid():N}.db");
        internal SqliteDatabase Store { get; }
        internal Clock Clock { get; } = new();
        private Fixture() => Store = new(path);
        internal static async Task<Fixture> CreateAsync() { Fixture result = new(); await result.Store.MigrateAsync(CancellationToken.None); return result; }
        internal Task<ReviewedUpdateDraft> SaveAsync()
        {
            ImmutableUpdateJob job = new UpdateJobReview(Guid.NewGuid(), Guid.NewGuid(), new("winget:contoso.widget", UpdateProvider.WinGet, UpdateSource.WinGet,
                "Contoso.Widget", "1.0", "1.1", UpdateScope.User, UpdateArchitecture.X64, UpdateChannel.Stable, "mapping-v1")).Confirm(ReviewedAt);
            return Store.SaveReviewedOperationAsync(new(job, User, "native:1", "context:1", "review:1", ReviewedAt.AddSeconds(-1),
                UpdateRouteContract.WinGetUserExactV2, ReviewedInstallerMode.Selected(InstallerMode.Silent)), CancellationToken.None);
        }
        internal Task<UpdateDispatchClaim?> ClaimAsync(ReviewedUpdateDraft draft) => Store.TryClaimAsync(draft, Fresh(draft.Operation), Owner(), Clock.GetUtcNow(), CancellationToken.None);
        internal async Task ExecuteAsync(string sql)
        {
            await using SqliteConnection connection = new(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
            await connection.OpenAsync(); await using SqliteCommand command = connection.CreateCommand(); command.CommandText = sql; await command.ExecuteNonQueryAsync();
        }
        internal async Task<object?> ScalarAsync(string sql)
        {
            await using SqliteConnection connection = new(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
            await connection.OpenAsync(); await using SqliteCommand command = connection.CreateCommand(); command.CommandText = sql; return await command.ExecuteScalarAsync();
        }
        public ValueTask DisposeAsync() { foreach (string file in new[] { path, path + "-wal", path + "-shm" }) File.Delete(file); return ValueTask.CompletedTask; }
    }
}
