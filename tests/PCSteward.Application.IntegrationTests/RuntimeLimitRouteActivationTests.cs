using System.Text;
using PCSteward.Domain;
using PCSteward.Persistence.Sqlite;
using PCSteward.Provider.Chocolatey;
using Kind = PCSteward.Application.RecordedHelperCheckKind;
using Reason = PCSteward.Application.RecordedHelperRefusalReason;

namespace PCSteward.Application.IntegrationTests;

/// <summary>
/// FR-UPD-15, FR-UPD-01, FR-HIS-01, TM-03/04/07/13; ADR-031 owner decision 7 (Task 50 step C-A, review fixes M2, L4 and L9;
/// <c>docs/review/TASK_50_ACTIVATION_CHANGE.md</c>).
/// <list type="bullet">
/// <item>Composition record review L1 (route-admission obligation): the real <see cref="ReviewedUpdateDispatchWorkflow"/> on a
/// <see cref="UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3"/> draft with a consistent Confirmed and a consistent
/// Refused helper check, through the real SQLite store and history reader: <c>result.HelperCheck</c> equals the recorded
/// <c>Terminal.HelperCheck</c>, the row is a version-3 terminal and no marker is recorded.</item>
/// <item>The activation term admits only the exact runtime-limit shape: every client identity (the shared Domain predicate,
/// equal to the provider's <c>IsClientTarget</c>), the Preview channel, an observed architecture binding and a controllable
/// mode are refused.</item>
/// </list>
/// </summary>
[TestClass]
public sealed class RuntimeLimitRouteActivationTests
{
    private const string User = "S-1-5-21-11-22-33-1001";
    private const string Installed = "1.2.3";
    private const string Candidate = "1.2.4";
    private static readonly DateTimeOffset ReviewedAt = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);
    private static readonly string Evidence = ReviewedUpdateOperation.ProfileEvidencePrefix + new string('A', 64);

    // ---- L1: the workflow records the consistent helper check unchanged ----

    [TestMethod]
    public async Task AConsistentConfirmedCheckIsRecordedUnchangedOnAVersion3RowWithNoMarker()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        RecordedHelperCandidateCheck confirmed = RecordedHelperCandidateCheck.FromDesktopDerived(Kind.Confirmed, Reason.None, Installed, Candidate);
        ReviewedUpdateDispatchObservation started = new(ReviewedUpdateInvocation.Started, ReviewedUpdateProviderStatus.Completed, 0, "launch-started",
            Task.CompletedTask, true, true, true, false, false, HelperCheck: confirmed);
        (ReviewedUpdateDispatchResult result, ReviewedUpdateHistoryRecord record, byte[] payload) = await fixture.RunAsync(started);

        Assert.AreEqual(ReviewedUpdateDispatchDisposition.Finalized, result.Disposition, result.Message);
        Assert.AreEqual(UpdateDispatchOutcome.Success, result.Outcome, result.Message);
        Assert.AreEqual(UpdateDispatchSettlementState.OwnedWorkStopped, record.Terminal!.Settlement);
        Assert.AreEqual(1, fixture.Verifier.Calls);
        AssertRecordedUnchanged(result, record, payload, confirmed);
    }

    [TestMethod]
    public async Task AConsistentRefusedCheckIsRecordedUnchangedOnAVersion3RowWithNoMarker()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        RecordedHelperCandidateCheck refused = RecordedHelperCandidateCheck.FromDesktopDerived(Kind.Refused, Reason.MachineInputsUnsupported, null, null);
        ReviewedUpdateDispatchObservation notStarted = ReviewedUpdateDispatchObservation.NotStarted(ReviewedUpdateProviderStatus.Refused, "helper-refused")
            with { HelperCheck = refused };
        (ReviewedUpdateDispatchResult result, ReviewedUpdateHistoryRecord record, byte[] payload) = await fixture.RunAsync(notStarted);

        Assert.AreEqual(UpdateDispatchOutcome.Refused, result.Outcome, result.Message);
        Assert.AreEqual(UpdateDispatchSettlementState.NotInvoked, record.Terminal!.Settlement);
        Assert.AreEqual(0, fixture.Verifier.Calls, "a refusal is never verified");
        AssertRecordedUnchanged(result, record, payload, refused);
    }

    private static void AssertRecordedUnchanged(ReviewedUpdateDispatchResult result, ReviewedUpdateHistoryRecord record, byte[] payload,
        RecordedHelperCandidateCheck expected)
    {
        Assert.AreEqual(UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3, result.Route);
        Assert.IsTrue(record.IntegrityVerified, "the SQLite history reader restores the row strictly");
        Assert.IsNotNull(result.HelperCheck);
        AssertSameCheck(result.HelperCheck, record.Terminal!.HelperCheck, "result.HelperCheck == record.Terminal.HelperCheck");
        AssertSameCheck(expected, record.Terminal.HelperCheck, "the dispatcher's consistent check, unchanged");
        StringAssert.StartsWith(Encoding.ASCII.GetString(payload, 0, 29), "pc-steward-update-terminal:3\n", "a version-3 row");
        Assert.AreEqual(result.Message, record.Terminal.Diagnostic, "live text equals the recorded text");
        foreach (string marker in new[] { ReviewedUpdateDispatchWorkflow.HelperCheckNotRecordedDiagnostic, ReviewedUpdateLingeringProcesses.Token })
            Assert.IsFalse(record.Terminal.Diagnostic.Contains(marker, StringComparison.Ordinal), marker);
        ReviewedUpdateHistoryView view = ReviewedUpdateHistoryPresentation.Present(record);
        Assert.IsNotNull(view.HelperReport, "history shows the recorded-at-the-time report");
        Assert.IsNull(view.LingeringProcessesNote);
        Assert.IsFalse(view.BlocksOtherUpdates);
    }

    private static void AssertSameCheck(RecordedHelperCandidateCheck expected, RecordedHelperCandidateCheck actual, string message)
    {
        Assert.AreEqual(expected.Kind, actual.Kind, message);
        Assert.AreEqual(expected.RefusalReason, actual.RefusalReason, message);
        Assert.AreEqual(expected.ReportedInstalledVersion, actual.ReportedInstalledVersion, message);
        Assert.AreEqual(expected.ReportedCandidateVersion, actual.ReportedCandidateVersion, message);
    }

    // ---- L4 / L9: the exact runtime-limit shape only ----

    [TestMethod]
    [DataRow("control", true)]
    [DataRow("client-chocolatey", false)]
    [DataRow("client-chocolatey-upper", false)]
    [DataRow("client-extension", false)]
    [DataRow("client-extension-mixed-case", false)]
    [DataRow("preview-channel", false)]
    [DataRow("observed-architecture", false)]
    [DataRow("controllable-mode", false)]
    [DataRow("lowercase-evidence", false)]
    public void TheActivationTermAdmitsOnlyTheExactRuntimeLimitShape(string variant, bool admitted)
    {
        string package = variant switch
        {
            "client-chocolatey" => "chocolatey",
            "client-chocolatey-upper" => "CHOCOLATEY",
            "client-extension" => "chocolatey.extension",
            "client-extension-mixed-case" => "Chocolatey.Extension",
            _ => "git.install",
        };
        UpdateChannel channel = variant == "preview-channel" ? UpdateChannel.Preview : UpdateChannel.Stable;
        ReviewedUpdateArchitecture architecture = variant == "observed-architecture"
            ? ReviewedUpdateArchitecture.Observed(UpdateArchitecture.X64) : ReviewedUpdateArchitecture.ProviderScriptSelected();
        ReviewedInstallerMode mode = variant == "controllable-mode"
            ? ReviewedInstallerMode.Selected(InstallerMode.Silent) : ReviewedInstallerMode.NotControllable(UncontrolledInstallerModeReason.ProviderScriptControlled);
        string evidence = variant == "lowercase-evidence" ? ReviewedUpdateOperation.ProfileEvidencePrefix + new string('a', 64) : Evidence;
        ImmutableUpdateJob job = new UpdateJobReview(Guid.NewGuid(), Guid.NewGuid(), new UpdateJobSelection("choco:" + package, UpdateProvider.Chocolatey,
            UpdateSource.ChocolateyCommunity, package, "1.0.0", "2.0.0", UpdateScope.Machine, architecture.Value ?? UpdateArchitecture.Neutral, channel,
            "mapping-v2")).Confirm(ReviewedAt);
        ReviewedUpdateOperation operation;
        try
        {
            operation = new(job, User, "choco:" + package, "machine:choco", evidence, ReviewedAt.AddSeconds(-1),
                UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3, mode, architecture);
        }
        catch (ArgumentException) when (!admitted)
        {
            return; // The Domain constructor already refuses this near miss.
        }
        Assert.AreEqual(admitted, ReviewedChocolateyRouteAdmission.Admits(operation), variant);
        Assert.AreEqual(admitted, ReviewedUpdateReview.HasSupportedMode(operation), variant);
    }

    [TestMethod]
    public void TheClientIdentityRuleIsSharedWithTheProvider()
    {
        foreach (string id in new[] { "chocolatey", "Chocolatey", "CHOCOLATEY", "chocolatey.extension", "Chocolatey.Extension", "CHOCOLATEY.EXTENSION" })
        {
            Assert.IsTrue(ReviewedUpdateOperation.IsChocolateyClientIdentity(id), id);
            Assert.IsTrue(ChocolateyClientReleasePolicy.IsClientTarget(id), id);
        }
        foreach (string id in new[] { "git", "chocolatey-core.extension", "chocolateygui", "chocolatey.extension.x", " chocolatey", "" })
        {
            Assert.IsFalse(ReviewedUpdateOperation.IsChocolateyClientIdentity(id), id);
            Assert.IsFalse(ChocolateyClientReleasePolicy.IsClientTarget(id), id);
        }
        Assert.IsFalse(ReviewedUpdateOperation.IsChocolateyClientIdentity(null));
    }

    // ---- Fixture ----

    private static OperationOwnership Owner() => new(User, OperationGateContract.NamePrefix + User, false, reconciled: true);

    private static ReviewedUpdateOperation RuntimeLimits()
    {
        ImmutableUpdateJob job = new UpdateJobReview(Guid.NewGuid(), Guid.NewGuid(), new UpdateJobSelection("choco:git", UpdateProvider.Chocolatey,
            UpdateSource.ChocolateyCommunity, "git", Installed, Candidate, UpdateScope.Machine, UpdateArchitecture.Neutral, UpdateChannel.Stable,
            "mapping-v2")).Confirm(ReviewedAt);
        return new(job, User, "choco:git", "machine:choco", Evidence, ReviewedAt.AddSeconds(-1), UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3,
            ReviewedInstallerMode.NotControllable(UncontrolledInstallerModeReason.ProviderScriptControlled), ReviewedUpdateArchitecture.ProviderScriptSelected());
    }

    /// <summary>F′ with the frozen profile identity, observed after review and within the window.</summary>
    private static FreshUpdateDispatchEvidence Fresh(ReviewedUpdateOperation op) => new(op.Selection, op.RequestingUserSid, op.NativeInstanceId,
        op.InstallationContextId, op.EvidenceId, ReviewedAt.AddMinutes(1), op.RouteContract, op.Mode, true, true);

    private sealed class Dispatcher(ReviewedUpdateDispatchObservation observation) : IReviewedUpdateRouteDispatcher
    {
        internal int Calls;
        public bool Supports(ReviewedUpdateOperation operation) => operation.RouteContract == UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3;
        public Task<ReviewedUpdateDispatchObservation> DispatchAsync(ReviewedUpdateDispatchTicket ticket, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(observation);
        }
    }

    private sealed class FreshEvidence : IReviewedUpdateDispatchEvidenceSource
    {
        public Task<FreshUpdateDispatchEvidence?> ObserveAsync(ReviewedUpdateOperation operation, OperationOwnership ownership, CancellationToken cancellationToken) =>
            Task.FromResult<FreshUpdateDispatchEvidence?>(Fresh(operation));
    }

    private sealed class Verifier : IReviewedUpdateVerifier
    {
        internal int Calls;
        public Task<PostUpdateInventoryResult> VerifyAsync(ReviewedUpdateOperation operation, OperationOwnership ownership, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new PostUpdateInventoryResult(true, false));
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string path = Path.Combine(Path.GetTempPath(), $"pcsteward-task50-runtime-activation-{Guid.NewGuid():N}.db");
        private Fixture() => Store = new(path, new ChocolateyHelperVersionGrammar());

        /// <summary>The production shape: dispatch store and history reader with the helper version grammar.</summary>
        internal SqliteDatabase Store { get; }
        internal Verifier Verifier { get; } = new();
        internal FixedClock Clock { get; } = new(ReviewedAt.AddMinutes(1));

        internal static async Task<Fixture> CreateAsync()
        {
            Fixture result = new();
            await result.Store.MigrateAsync(CancellationToken.None);
            return result;
        }

        /// <summary>Save, review, confirm and run the admitted route through the SQLite store, then read the row back.</summary>
        internal async Task<(ReviewedUpdateDispatchResult, ReviewedUpdateHistoryRecord, byte[])> RunAsync(ReviewedUpdateDispatchObservation observation)
        {
            ReviewedUpdateDraft draft = await Store.SaveReviewedOperationAsync(RuntimeLimits(), CancellationToken.None);
            Assert.IsTrue(ReviewedUpdateReview.HasSupportedMode(draft.Operation), "the activation admits the exact runtime-limit shape");
            Dispatcher dispatcher = new(observation);
            ReviewedUpdateDispatchWorkflow workflow = new((work, _, ct) => work(Owner(), ct), new FreshEvidence(), dispatcher, Verifier, Store, Clock);
            ReviewedUpdateDispatchResult result = await workflow.RunAsync(new ReviewedUpdateReview(draft, Clock).Confirm(), CancellationToken.None);
            Assert.AreEqual(1, dispatcher.Calls, result.Message);
            ReviewedUpdateHistoryRecord record = (await Store.GetReviewedUpdateHistoryAsync(10, CancellationToken.None)).Single();
            Assert.AreEqual(draft.Operation.Digest, record.Operation!.Digest);
            Assert.IsNotNull(record.Terminal, "the terminal parsed with the production grammar");
            return (result, record, await ReadTerminalAsync(draft.Operation.JobId));
        }

        private async Task<byte[]> ReadTerminalAsync(Guid jobId)
        {
            await using Microsoft.Data.Sqlite.SqliteConnection connection = new(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
            await connection.OpenAsync();
            await using Microsoft.Data.Sqlite.SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT terminal_payload FROM update_dispatch_claims WHERE job_id=$job;";
            command.Parameters.AddWithValue("$job", jobId.ToString("D"));
            return (byte[])(await command.ExecuteScalarAsync())!;
        }

        public ValueTask DisposeAsync()
        {
            foreach (string file in new[] { path, path + "-wal", path + "-shm" }) File.Delete(file);
            return ValueTask.CompletedTask;
        }
    }
}
