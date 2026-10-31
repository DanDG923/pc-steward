using Microsoft.Data.Sqlite;
using PCSteward.Domain;
using PCSteward.Persistence.Sqlite;

namespace PCSteward.Application.IntegrationTests;

/// <summary>FR-HIS-01; FR-UPD-15; TM-07/13: reviewed mode/route/guard/disclosure survive into history without authority or false qualification.</summary>
[TestClass]
public sealed class ReviewedUpdateHistoryTests
{
    private const string User = "S-1-5-21-11-22-33-1001";
    private static readonly DateTimeOffset ReviewedAt = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task FinalizedDispatchKeepsTheReviewedModeAndVerifiedOutcome()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        ReviewedUpdateDraft draft = await fixture.SaveWinGetAsync();
        UpdateDispatchClaim claim = (await fixture.ClaimAsync(draft))!;
        OperationOwnership owner = Owner();
        await fixture.Store.RecordFinalAsync(claim, owner, new(UpdateDispatchOutcome.Success, ReviewedAt.AddMinutes(2), 0, "verified"),
            UpdateDispatchSettlement.FromObservedWork(claim, owner, Task.CompletedTask, true, true, true, false, false), CancellationToken.None);

        ReviewedUpdateHistoryRecord record = (await fixture.Store.GetReviewedUpdateHistoryAsync(10, CancellationToken.None)).Single();
        Assert.IsTrue(record.IntegrityVerified);
        Assert.AreEqual(UpdateDispatchState.Final, record.State);
        Assert.IsTrue(draft.Operation.HasSameIdentity(record.Operation));
        Assert.AreEqual(UpdateDispatchOutcome.Success, record.Terminal!.Outcome);
        Assert.AreEqual(UpdateDispatchSettlementState.OwnedWorkStopped, record.Terminal.Settlement);
        Assert.AreEqual(0, record.Terminal.ExitCode);

        ReviewedUpdateHistoryView view = ReviewedUpdateHistoryPresentation.Present(record);
        StringAssert.Contains(view.InstallerMode, "Silent");
        StringAssert.StartsWith(view.Status, "Updated");
        Assert.IsFalse(view.BlocksOtherUpdates);
        Assert.IsNull(view.Disclosure);
        AssertNeverClaimsHelperQualification(view);
    }

    [TestMethod]
    public async Task ClaimedAndRecoveredUncertainRowsArePresentedAsBlockingAndNeverAsResults()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        ReviewedUpdateDraft draft = await fixture.SaveWinGetAsync();
        _ = await fixture.ClaimAsync(draft);
        ReviewedUpdateHistoryView claimed = ReviewedUpdateHistoryPresentation.Present((await fixture.Store.GetReviewedUpdateHistoryAsync(10, CancellationToken.None)).Single());
        Assert.IsTrue(claimed.BlocksOtherUpdates);
        StringAssert.Contains(claimed.Status, "no result recorded");

        await fixture.Store.RecoverUnfinishedDispatchesAsync(Owner(), ReviewedAt.AddMinutes(5), CancellationToken.None);
        ReviewedUpdateHistoryRecord recovered = (await fixture.Store.GetReviewedUpdateHistoryAsync(10, CancellationToken.None)).Single();
        Assert.IsTrue(recovered.IntegrityVerified);
        Assert.AreEqual(UpdateDispatchState.Uncertain, recovered.State);
        Assert.AreEqual(UpdateDispatchSettlementState.InvocationUnknown, recovered.Terminal!.Settlement);
        ReviewedUpdateHistoryView view = ReviewedUpdateHistoryPresentation.Present(recovered);
        Assert.IsTrue(view.BlocksOtherUpdates);
        StringAssert.StartsWith(view.Status, "Uncertain");
        StringAssert.Contains(view.Status, "never retried automatically");
    }

    [TestMethod]
    public void RuntimeLimitRouteDisclosureAndProvisionalIntentAppearOnlyForTheNewContract()
    {
        ReviewedUpdateOperation current = Chocolatey(UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3);
        ReviewedUpdateHistoryView view = ReviewedUpdateHistoryPresentation.Present(new(current.JobId, current, UpdateDispatchState.Final, ReviewedAt,
            new(UpdateDispatchOutcome.Failed, ReviewedAt.AddMinutes(1), null, UpdateDispatchSettlementState.NotInvoked, "helper-refused"), true));
        Assert.AreEqual(ReviewedUpdateProviderDisclosure.ChocolateyCommunityScriptRuntimeLimits, view.Disclosure);
        StringAssert.Contains(view.InstallerMode, "package's own scripts");
        StringAssert.Contains(view.CandidateEvidence, "provisional intent");
        StringAssert.Contains(view.CandidateEvidence, "must independently confirm");
        AssertNeverClaimsHelperQualification(view);

        ReviewedUpdateOperation older = Chocolatey(UpdateRouteContract.ChocolateyMachineProvisionalV3);
        Assert.IsNull(ReviewedUpdateHistoryPresentation.Present(new(older.JobId, older, UpdateDispatchState.Final, ReviewedAt,
            new(UpdateDispatchOutcome.Failed, ReviewedAt.AddMinutes(1), null, UpdateDispatchSettlementState.NotInvoked, "x"), true)).Disclosure);
    }

    [TestMethod]
    [DataRow("payload")]
    [DataRow("digest")]
    [DataRow("terminal")]
    [DataRow("terminal-digest")]
    [DataRow("state")]
    [DataRow("claimed-terminal")]
    public async Task TamperedOrCorruptRowsAreUnreadableAndBlockingNeverTrusted(string change)
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        ReviewedUpdateDraft draft = await fixture.SaveWinGetAsync();
        UpdateDispatchClaim claim = (await fixture.ClaimAsync(draft))!;
        if (change != "claimed-terminal")
        {
            OperationOwnership owner = Owner();
            await fixture.Store.RecordFinalAsync(claim, owner, new(UpdateDispatchOutcome.Success, ReviewedAt.AddMinutes(2), 0, "verified"),
                UpdateDispatchSettlement.FromObservedWork(claim, owner, Task.CompletedTask, true, true, true, false, false), CancellationToken.None);
        }
        await fixture.ExecuteAsync("PRAGMA ignore_check_constraints=ON;" + change switch
        {
            "payload" => "UPDATE update_dispatch_claims SET canonical_payload=x'00';",
            "digest" => "UPDATE update_dispatch_claims SET canonical_digest=upper(hex(randomblob(32)));",
            "terminal" => $"UPDATE update_dispatch_claims SET terminal_payload=zeroblob(16384), terminal_digest='{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(new byte[16384]))}';",
            "terminal-digest" => "UPDATE update_dispatch_claims SET terminal_digest=upper(hex(randomblob(32)));",
            "state" => "UPDATE update_dispatch_claims SET state='Bogus';",
            _ => "UPDATE update_dispatch_claims SET terminal_payload=randomblob(16384);",
        });
        ReviewedUpdateHistoryRecord record = (await fixture.Store.GetReviewedUpdateHistoryAsync(10, CancellationToken.None)).Single();
        Assert.IsFalse(record.IntegrityVerified);
        ReviewedUpdateHistoryView view = ReviewedUpdateHistoryPresentation.Present(record);
        Assert.IsTrue(view.BlocksOtherUpdates);
        Assert.IsFalse(view.Status.StartsWith("Updated", StringComparison.Ordinal));
        AssertNeverClaimsHelperQualification(view);
    }

    [TestMethod]
    [DataRow(nameof(UpdateDispatchOutcome.Success), "not-invoked")]
    [DataRow(nameof(UpdateDispatchOutcome.RestartRequired), "not-invoked")]
    [DataRow(nameof(UpdateDispatchOutcome.Unverified), "not-invoked")]
    [DataRow(nameof(UpdateDispatchOutcome.Failed), "unsettled")]
    [DataRow(nameof(UpdateDispatchOutcome.Cancelled), "unknown")]
    public async Task RowsTheStoreCouldNeverWriteAreCorruptionNotHistory(string outcomeName, string settlement)
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        ReviewedUpdateDraft draft = await fixture.SaveWinGetAsync();
        UpdateDispatchClaim claim = (await fixture.ClaimAsync(draft))!;
        OperationOwnership owner = Owner();
        UpdateDispatchSettlement? forged = settlement switch
        {
            "not-invoked" => UpdateDispatchSettlement.BeforeInvocation(claim, owner),
            "unsettled" => UpdateDispatchSettlement.FromObservedWork(claim, owner, Task.CompletedTask, false, true, true, false, false),
            _ => null,
        };
        byte[] payload = new UpdateDispatchTerminal(Enum.Parse<UpdateDispatchOutcome>(outcomeName), ReviewedAt.AddMinutes(2), 0, "forged").GetReservedPayload(forged);
        await fixture.ExecuteAsync($"PRAGMA ignore_check_constraints=ON; UPDATE update_dispatch_claims SET state='Final', terminal_payload=x'{Convert.ToHexString(payload)}', terminal_digest='{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(payload))}';");
        ReviewedUpdateHistoryRecord record = (await fixture.Store.GetReviewedUpdateHistoryAsync(10, CancellationToken.None)).Single();
        Assert.IsFalse(record.IntegrityVerified);
        Assert.IsTrue(ReviewedUpdateHistoryPresentation.Present(record).BlocksOtherUpdates);
    }

    [TestMethod]
    public void TerminalParserRoundTripsTheWriterAndRejectsMalformedSlots()
    {
        UpdateDispatchTerminal terminal = new(UpdateDispatchOutcome.Unverified, ReviewedAt, -5, "naïve diagnostic ✓");
        Assert.IsTrue(ReviewedUpdateTerminalRecord.TryParse(terminal.GetReservedPayload(), out ReviewedUpdateTerminalRecord? parsed));
        Assert.AreEqual(UpdateDispatchOutcome.Unverified, parsed!.Outcome);
        Assert.AreEqual(-5, parsed.ExitCode);
        Assert.AreEqual(UpdateDispatchSettlementState.InvocationUnknown, parsed.Settlement);
        Assert.AreEqual(terminal.Diagnostic, parsed.Diagnostic);

        byte[] good = terminal.GetReservedPayload();
        foreach (Func<byte[], byte[]> mutate in new Func<byte[], byte[]>[]
        {
            bytes => bytes[..100],
            bytes => { byte[] copy = (byte[])bytes.Clone(); copy[^1] = 1; return copy; },
            bytes => { byte[] copy = (byte[])bytes.Clone(); copy[27] = (byte)'3'; return copy; },
            bytes => System.Text.Encoding.ASCII.GetBytes("pc-steward-update-terminal:2\nSuccess2\n").Concat(new byte[16384]).Take(16384).ToArray(),
            _ => new byte[16384],
        })
            Assert.IsFalse(ReviewedUpdateTerminalRecord.TryParse(mutate(good), out _));
    }

    private static void AssertNeverClaimsHelperQualification(ReviewedUpdateHistoryView view)
    {
        foreach (string text in new[] { view.Title, view.Status, view.InstallerMode, view.CandidateEvidence, view.Disclosure ?? string.Empty, view.Diagnostic })
            Assert.IsFalse(text.Contains("helper qualified", StringComparison.OrdinalIgnoreCase) || text.Contains("helper-qualified", StringComparison.OrdinalIgnoreCase), text);
    }

    private static OperationOwnership Owner() => new(User, OperationGateContract.NamePrefix + User, false, reconciled: true);

    private static ReviewedUpdateOperation Chocolatey(UpdateRouteContract route)
    {
        ImmutableUpdateJob job = new UpdateJobReview(Guid.NewGuid(), Guid.NewGuid(), new UpdateJobSelection("choco:git.install",
            UpdateProvider.Chocolatey, UpdateSource.ChocolateyCommunity, "git.install", "1.0.0", "2.0.0", UpdateScope.Machine,
            UpdateArchitecture.Neutral, UpdateChannel.Stable, "mapping-v2")).Confirm(ReviewedAt);
        return new(job, User, "choco:git.install", "machine:choco", "evidence:1", ReviewedAt, route,
            ReviewedInstallerMode.NotControllable(UncontrolledInstallerModeReason.ProviderScriptControlled), ReviewedUpdateArchitecture.ProviderScriptSelected());
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string path = Path.Combine(Path.GetTempPath(), $"pcsteward-task50-history-{Guid.NewGuid():N}.db");
        private Fixture() => Store = new(path);
        internal SqliteDatabase Store { get; }

        internal static async Task<Fixture> CreateAsync()
        {
            Fixture result = new();
            await result.Store.MigrateAsync(CancellationToken.None);
            return result;
        }

        internal Task<ReviewedUpdateDraft> SaveWinGetAsync()
        {
            ImmutableUpdateJob job = new UpdateJobReview(Guid.NewGuid(), Guid.NewGuid(), new("winget:contoso.widget", UpdateProvider.WinGet, UpdateSource.WinGet,
                "Contoso.Widget", "1.0", "1.1", UpdateScope.User, UpdateArchitecture.X64, UpdateChannel.Stable, "mapping-v1")).Confirm(ReviewedAt);
            return Store.SaveReviewedOperationAsync(new(job, User, "native:1", "context:1", "review:1", ReviewedAt.AddSeconds(-1),
                UpdateRouteContract.WinGetUserExactV3, ReviewedInstallerMode.Selected(InstallerMode.Silent)), CancellationToken.None);
        }

        internal Task<UpdateDispatchClaim?> ClaimAsync(ReviewedUpdateDraft draft)
        {
            ReviewedUpdateOperation op = draft.Operation;
            FreshUpdateDispatchEvidence evidence = new(op.Selection, op.RequestingUserSid, op.NativeInstanceId, op.InstallationContextId, "fresh:1",
                ReviewedAt.AddMinutes(1), op.RouteContract, op.Mode, true, true);
            return Store.TryClaimAsync(draft, evidence, Owner(), ReviewedAt.AddMinutes(1), CancellationToken.None);
        }

        internal async Task ExecuteAsync(string sql)
        {
            await using SqliteConnection connection = new(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
            await connection.OpenAsync();
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync();
        }

        public ValueTask DisposeAsync()
        {
            foreach (string file in new[] { path, path + "-wal", path + "-shm" }) File.Delete(file);
            return ValueTask.CompletedTask;
        }
    }
}
