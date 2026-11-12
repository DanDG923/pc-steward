using System.Security.Cryptography;
using System.Text;
using PCSteward.Domain;
using PCSteward.Provider.WinGet;

namespace PCSteward.Application.IntegrationTests;

/// <summary>
/// FR-UPD-15, FR-UPD-01/03, AC-UPD-03, TM-03/04/13. Stage C4 (Task 54 R1; owner decision 2026-11-12 "Silent for all per-user apps"):
/// a per-user WinGet review binds the Silent mode, the mode is re-checked against fresh evidence before approval, and the approved job
/// is dispatched only with the reviewed operation frozen from it, whose arguments come from the reviewed mapping.
/// </summary>
[TestClass]
[TestCategory("FR-UPD-15")]
public sealed class WingetUserReviewedRouteTests
{
    private const string Sid = "S-1-5-21-11-22-33-1001";
    private static readonly ReviewedInstallerMode Silent = ReviewedInstallerMode.Selected(InstallerMode.Silent);

    [TestMethod]
    [DataRow(UpdateSource.WinGet)]
    [DataRow(UpdateSource.MicrosoftStore)]
    public async Task PerUserReviewBindsSilentAndDispatchesOnlyItsFrozenReviewedOperation(UpdateSource source)
    {
        UpdateJobSelection selection = Selection(source: source);
        RecordingStore store = new();
        ReviewedRecordingExecutor executor = new();
        ReviewedUpdateExecutionWorkflow workflow = Workflow(selection, store, executor);

        UpdateReviewPreparation preparation = await workflow.BeginReviewAsync(selection.StableProductKey, CancellationToken.None);
        Assert.AreEqual(Silent, preparation.Review!.InstallerMode, "the mode is bound when the review is made");
        Assert.HasCount(0, store.Jobs);

        ReviewedUpdateExecutionResult result = await workflow.ConfirmAndRunAsync(preparation.Review, false, CancellationToken.None);

        Assert.IsTrue(result.Started, result.Message);
        ImmutableUpdateJob job = store.Jobs.Single();
        ReviewedUpdateOperation reviewed = executor.Reviewed.Single()!;
        Assert.AreEqual(UpdateRouteContract.WinGetUserSilentV1, reviewed.RouteContract);
        Assert.AreEqual(Silent, reviewed.Mode);
        Assert.AreEqual(Sid, reviewed.RequestingUserSid);
        Assert.IsTrue(reviewed.BindsJob(job));
        Assert.IsTrue(WingetUserReviewedRoute.Admits(reviewed));
        CollectionAssert.AreEqual(new[]
        {
            "upgrade", "--id", "Contoso.Widget", "--version", "1.1.0", "--source", source == UpdateSource.WinGet ? "winget" : "msstore", "--exact",
            "--scope", "user", "--architecture", "x64", "--disable-interactivity", "--silent"
        }, WingetReviewedUpdateArguments.Build(reviewed).ToArray());
    }

    [TestMethod]
    public async Task MachineScopeWinGetBindsNoModeAndRunsWithoutAReviewedOperation()
    {
        UpdateJobSelection selection = Selection(UpdateScope.Machine);
        RecordingStore store = new();
        ReviewedRecordingExecutor executor = new();
        ReviewedUpdateExecutionWorkflow workflow = Workflow(selection, store, executor);

        UpdateReviewPreparation preparation = await workflow.BeginReviewAsync(selection.StableProductKey, CancellationToken.None);
        Assert.IsNull(preparation.Review!.InstallerMode, "machine scope has no reviewed mode until Phase D stage D4b");
        ReviewedUpdateExecutionResult result = await workflow.ConfirmAndRunAsync(preparation.Review, false, CancellationToken.None);

        Assert.IsTrue(result.Started, result.Message);
        Assert.IsNull(executor.Reviewed.Single());
    }

    [TestMethod]
    public async Task ChocolateyIsNeitherReviewedNorRunOnThisRoute()
    {
        UpdateJobSelection chocolatey = new("chocolatey:chocolatey-community:git", UpdateProvider.Chocolatey, UpdateSource.ChocolateyCommunity, "git",
            "2.45.1", "2.46.0", UpdateScope.Machine, UpdateArchitecture.Neutral, UpdateChannel.Stable, "chocolatey-fixed-community-v1");
        RecordingStore store = new();
        ReviewedRecordingExecutor executor = new();
        ReviewedUpdateExecutionWorkflow workflow = Workflow(chocolatey, store, executor);

        UpdateReviewPreparation preparation = await workflow.BeginReviewAsync(chocolatey.StableProductKey, CancellationToken.None);
        Assert.IsNull(preparation.Review);
        Assert.AreEqual(ReviewedUpdateExecutionWorkflow.ChocolateyNotOnThisRouteMessage, preparation.Message);

        // A review made elsewhere is still refused before approval.
        ReviewedUpdateExecutionResult result = await workflow.ConfirmAndRunAsync(new UpdateJobReview(Guid.NewGuid(), Guid.NewGuid(), chocolatey), false, CancellationToken.None);
        Assert.IsFalse(result.Started);
        Assert.AreEqual(ReviewedUpdateExecutionWorkflow.ChocolateyNotOnThisRouteMessage, result.Message);
        Assert.HasCount(0, store.Jobs);
        Assert.HasCount(0, executor.Reviewed);
    }

    [TestMethod]
    public async Task AReviewWithoutTheBoundModeOrWithAnotherModeIsRefusedBeforeApproval()
    {
        UpdateJobSelection selection = Selection();
        RecordingStore store = new();
        ReviewedRecordingExecutor executor = new();
        ReviewedUpdateExecutionWorkflow workflow = Workflow(selection, store, executor);

        foreach (UpdateJobReview review in new[]
                 {
                     new UpdateJobReview(Guid.NewGuid(), Guid.NewGuid(), selection),
                     new UpdateJobReview(Guid.NewGuid(), Guid.NewGuid(), selection, ReviewedInstallerMode.Selected(InstallerMode.Interactive)),
                     new UpdateJobReview(Guid.NewGuid(), Guid.NewGuid(), selection, ReviewedInstallerMode.NotControllable(UncontrolledInstallerModeReason.ProviderScriptControlled)),
                 })
        {
            ReviewedUpdateExecutionResult result = await workflow.ConfirmAndRunAsync(review, false, CancellationToken.None);
            Assert.IsFalse(result.Started);
            Assert.AreEqual(WingetUserReviewedRoute.ModeChangedMessage, result.Message);
        }
        Assert.HasCount(0, store.Jobs, "nothing was approved");
        Assert.HasCount(0, executor.Reviewed, "nothing was started");
    }

    [TestMethod]
    public async Task WithoutTheRequestingUserNoPerUserUpdateIsApprovedOrStarted()
    {
        UpdateJobSelection selection = Selection();
        foreach (Func<string?>? sid in new Func<string?>?[] { null, () => null, () => " ", () => throw new InvalidOperationException("no identity") })
        {
            RecordingStore store = new();
            ReviewedRecordingExecutor executor = new();
            ReviewedUpdateExecutionWorkflow workflow = Workflow(selection, store, executor, sid, useSid: true);
            UpdateReviewPreparation preparation = await workflow.BeginReviewAsync(selection.StableProductKey, CancellationToken.None);
            ReviewedUpdateExecutionResult result = await workflow.ConfirmAndRunAsync(preparation.Review!, false, CancellationToken.None);
            Assert.IsFalse(result.Started);
            Assert.AreEqual(WingetUserReviewedRoute.RequesterUnavailableMessage, result.Message);
            Assert.HasCount(0, store.Jobs);
            Assert.HasCount(0, executor.Reviewed);
        }
    }

    [TestMethod]
    public void ModeBindingCoversEveryPerUserWinGetUpdateAndNothingElse()
    {
        foreach (UpdateArchitecture architecture in Enum.GetValues<UpdateArchitecture>())
            foreach (UpdateChannel channel in Enum.GetValues<UpdateChannel>())
                foreach (UpdateSource source in new[] { UpdateSource.WinGet, UpdateSource.MicrosoftStore })
                    Assert.AreEqual(Silent, WingetUserReviewedRoute.BindMode(Selection(source: source, architecture: architecture, channel: channel)));
        Assert.IsNull(WingetUserReviewedRoute.BindMode(Selection(UpdateScope.Machine)));
        Assert.AreEqual(Silent, UpdateInstallerModeCapabilities.Resolve(UpdateProvider.WinGet, UpdateSource.MicrosoftStore, UpdateScope.User));
        Assert.IsNull(UpdateInstallerModeCapabilities.Resolve(UpdateProvider.WinGet, UpdateSource.WinGet, UpdateScope.Machine));
        Assert.IsNull(UpdateInstallerModeCapabilities.Resolve(UpdateProvider.WinGet, UpdateSource.ChocolateyCommunity, UpdateScope.User));
    }

    [TestMethod]
    [DataRow(UpdateArchitecture.X86, "x86")]
    [DataRow(UpdateArchitecture.X64, "x64")]
    [DataRow(UpdateArchitecture.Arm64, "arm64")]
    [DataRow(UpdateArchitecture.Neutral, "neutral")]
    public void PerUserMappingPinsSilentWithoutAnInstallerTypeOrChannelArgument(UpdateArchitecture architecture, string argument)
    {
        foreach (UpdateChannel channel in Enum.GetValues<UpdateChannel>())
        {
            IReadOnlyList<string> arguments = WingetReviewedUpdateArguments.Build(Frozen(Selection(architecture: architecture, channel: channel)));
            CollectionAssert.AreEqual(new[]
            {
                "upgrade", "--id", "Contoso.Widget", "--version", "1.1.0", "--source", "winget", "--exact",
                "--scope", "user", "--architecture", argument, "--disable-interactivity", "--silent"
            }, arguments.ToArray());
            Assert.ThrowsExactly<NotSupportedException>(() => ((IList<string>)arguments)[0] = "install");
        }
    }

    [TestMethod]
    [DataRow("interactive")]
    [DataRow("uncontrolled")]
    [DataRow("provider")]
    [DataRow("machine")]
    [DataRow("script")]
    [DataRow("chocolatey-source")]
    public void RecomputedIntegrityCannotRestoreAnUnsupportedPerUserBinding(string change)
    {
        string[] fields = Encoding.UTF8.GetString(Frozen(Selection()).GetCanonicalBytes()).Split('\n');
        switch (change)
        {
            case "interactive": fields[23] = "Interactive"; break;
            case "uncontrolled": fields[22] = "NotControllable"; fields[23] = "None"; fields[24] = "ProviderScriptControlled"; break;
            case "provider": fields[7] = "Chocolatey"; fields[8] = "ChocolateyCommunity"; fields[12] = "Machine"; break;
            case "machine": fields[12] = "Machine"; break;
            case "script": fields[13] = "ProviderScriptSelected"; fields[14] = "None"; break;
            case "chocolatey-source": fields[8] = "ChocolateyCommunity"; break;
            default: Assert.Fail("Unknown fixture mutation."); break;
        }
        byte[] bytes = Encoding.UTF8.GetBytes(string.Join('\n', fields));
        Assert.IsFalse(ReviewedUpdateOperation.TryRestore(bytes, Convert.ToHexString(SHA256.HashData(bytes)), out ReviewedUpdateOperation? restored),
            "the route's shape is enforced on construction and restore alike");
        Assert.IsNull(restored);
    }

    [TestMethod]
    public void FreezeRefusesAMissingModeAnotherJobOrEvidenceAfterApproval()
    {
        DateTimeOffset at = new(2026, 11, 12, 20, 0, 0, TimeSpan.Zero);
        ImmutableUpdateJob job = new UpdateJobReview(Guid.NewGuid(), Guid.NewGuid(), Selection()).Confirm(at);
        Assert.IsNull(WingetUserReviewedRoute.Freeze(job, null, Sid, "inventory:fixture", at));
        Assert.IsNull(WingetUserReviewedRoute.Freeze(job, ReviewedInstallerMode.Selected(InstallerMode.Interactive), Sid, "inventory:fixture", at));
        Assert.IsNull(WingetUserReviewedRoute.Freeze(job, Silent, "not-a-sid", "inventory:fixture", at));
        Assert.IsNull(WingetUserReviewedRoute.Freeze(job, Silent, Sid, "inventory:fixture", at.AddTicks(1)), "evidence observed after approval");
        ImmutableUpdateJob machine = new UpdateJobReview(Guid.NewGuid(), Guid.NewGuid(), Selection(UpdateScope.Machine)).Confirm(at);
        Assert.IsNull(WingetUserReviewedRoute.Freeze(machine, Silent, Sid, "inventory:fixture", at));

        ReviewedUpdateOperation frozen = WingetUserReviewedRoute.Freeze(job, Silent, Sid, "inventory:fixture", at)!;
        ImmutableUpdateJob other = new UpdateJobReview(Guid.NewGuid(), Guid.NewGuid(), Selection()).Confirm(at);
        Assert.IsTrue(frozen.BindsJob(job));
        Assert.IsFalse(frozen.BindsJob(other));
        Assert.IsFalse(frozen.BindsJob(null));
    }

    private static ReviewedUpdateOperation Frozen(UpdateJobSelection selection)
    {
        DateTimeOffset at = new(2026, 11, 12, 20, 0, 0, TimeSpan.Zero);
        ImmutableUpdateJob job = new UpdateJobReview(Guid.NewGuid(), Guid.NewGuid(), selection).Confirm(at);
        return WingetUserReviewedRoute.Freeze(job, Silent, Sid, "inventory:fixture", at)
            ?? throw new InvalidOperationException("The fixture must freeze on the per-user route.");
    }

    private static ReviewedUpdateExecutionWorkflow Workflow(UpdateJobSelection selection, RecordingStore store, ReviewedRecordingExecutor executor,
        Func<string?>? sid = null, bool useSid = false)
    {
        RunningDetector running = new();
        SequentialUpdateBatchCoordinator coordinator = new(executor, new VerifiedInventory(), new InMemoryHistory(), running);
        return new ReviewedUpdateExecutionWorkflow((_, _) => Task.FromResult(new UpdateSelectionPreparation(selection, "fresh")),
            new UpdateJobApprovalService(store), coordinator, running, requestingUserSid: useSid ? sid : () => Sid);
    }

    private static UpdateJobSelection Selection(UpdateScope scope = UpdateScope.User, UpdateSource source = UpdateSource.WinGet,
        UpdateArchitecture architecture = UpdateArchitecture.X64, UpdateChannel channel = UpdateChannel.Unknown) =>
        new("winget:contoso.widget", UpdateProvider.WinGet, source, "Contoso.Widget", "1.0.0", "1.1.0", scope, architecture, channel, "winget-com-v1");

    private sealed class ReviewedRecordingExecutor : IApprovedUpdateExecutor
    {
        public List<ReviewedUpdateOperation?> Reviewed { get; } = [];

        public Task<UpdateProviderAttempt> ExecuteAsync(ImmutableUpdateJob job, UpdateJobExecutionEvidence currentEvidence, CancellationToken cancellationToken) =>
            throw new AssertFailedException("The coordinator must pass the reviewed operation.");

        public Task<UpdateProviderAttempt> ExecuteAsync(ImmutableUpdateJob job, ReviewedUpdateOperation? reviewed, UpdateJobExecutionEvidence currentEvidence,
            UpdateItemProgress progress, CancellationToken cancellationToken)
        {
            Reviewed.Add(reviewed);
            return Task.FromResult(new UpdateProviderAttempt(UpdateProviderAttemptStatus.AwaitingVerification, 0, "provider-completed"));
        }
    }

    private sealed class RecordingStore : IUpdateJobStore
    {
        public List<ImmutableUpdateJob> Jobs { get; } = [];
        public Task SaveApprovedJobAsync(ImmutableUpdateJob job, CancellationToken cancellationToken) { Jobs.Add(job); return Task.CompletedTask; }
        public Task<ImmutableUpdateJob?> GetApprovedJobAsync(Guid jobId, CancellationToken cancellationToken) => Task.FromResult<ImmutableUpdateJob?>(Jobs.SingleOrDefault(job => job.Id == jobId));
    }

    private sealed class VerifiedInventory : IPostUpdateInventory
    {
        public Task<PostUpdateInventoryResult> ReInventoryAsync(ImmutableUpdateJob job, CancellationToken cancellationToken) => Task.FromResult(new PostUpdateInventoryResult(true, false));
    }

    private sealed class InMemoryHistory : IUpdateHistoryStore
    {
        public List<UpdateHistoryEntry> Entries { get; } = [];
        public Task RecordAsync(UpdateHistoryEntry entry, CancellationToken cancellationToken) { Entries.Add(entry); return Task.CompletedTask; }
        public Task<IReadOnlyCollection<UpdateHistoryEntry>> GetHistoryForProductAsync(string stableProductKey, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<UpdateHistoryEntry>>(Entries.Where(entry => entry.StableProductKey == stableProductKey).ToArray());
        public Task RecoverInterruptedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PruneAsync(UpdateHistoryRetention retention, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class RunningDetector : IRunningApplicationDetector
    {
        public Task<RunningApplicationDetection> DetectAsync(UpdateJobSelection selection, CancellationToken cancellationToken) =>
            Task.FromResult(RunningApplicationDetection.NotDetected);
    }
}
