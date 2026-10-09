using PCSteward.Application;
using PCSteward.Domain;
using PCSteward.Execution;
using PCSteward.Provider.WinGet;

namespace PCSteward.Security.FixtureTests;

/// <summary>Task 49: FR-BAT-05/08 — executors refuse the wrong phase, helper revalidation, bounded app closing.</summary>
[TestClass]
public sealed class BatchExecutorSecurityTests
{
    private static readonly string[] UserInstallArguments = ["install", "--id", "Vendor.App", "--exact", "--version", "2.0.0", "--source", "winget", "--scope", "user", "--architecture", "x64", "--silent", "--disable-interactivity"];

    [TestMethod]
    public async Task DesktopExecutorRunsOnlyUnelevatedWinGetOperations()
    {
        RecordingProcess process = new();
        DesktopOperationExecutor executor = new(new WingetUpdateExecutor(process), new WingetInstallExecutor(process));
        ImmutableUpdateJob install = Approve(Install(UpdateScope.User, elevates: false));
        ImmutableUpdateJob selfElevating = Approve(Install(UpdateScope.User, elevates: true));
        ImmutableUpdateJob machine = Approve(Install(UpdateScope.Machine, elevates: false));

        ProviderExitAssessment ran = await executor.ExecuteAsync(install, Evidence(install), CancellationToken.None);
        ProviderExitAssessment refusedElevating = await executor.ExecuteAsync(selfElevating, Evidence(selfElevating), CancellationToken.None);
        ProviderExitAssessment refusedMachine = await executor.ExecuteAsync(machine, Evidence(machine), CancellationToken.None);
        ProviderExitAssessment refusedStale = await executor.ExecuteAsync(install, new UpdateJobExecutionEvidence(install.Selection, true, false), CancellationToken.None);

        Assert.AreEqual(UpdateProviderAttemptStatus.AwaitingVerification, ran.Status);
        CollectionAssert.AreEqual(UserInstallArguments, process.Calls.Single().ToArray());
        Assert.AreEqual("operation-requires-elevated-session", refusedElevating.Diagnostic);
        Assert.AreEqual("operation-requires-elevated-session", refusedMachine.Diagnostic);
        Assert.AreEqual("current-evidence-mismatch", refusedStale.Diagnostic);
        Assert.HasCount(1, process.Calls);
    }

    [TestMethod]
    public async Task HelperExecutorRefusesUnelevatedOrMismatchedWork()
    {
        HelperOperationExecutor executor = new();
        ImmutableUpdateJob user = Approve(Install(UpdateScope.User, elevates: false));
        ImmutableUpdateJob machine = Approve(Install(UpdateScope.Machine, elevates: false));
        Assert.AreEqual("unelevated-operation-in-helper", (await executor.ExecuteAsync(user, Evidence(user), CancellationToken.None)).Diagnostic);
        Assert.AreEqual("current-evidence-mismatch", (await executor.ExecuteAsync(machine, Evidence(user), CancellationToken.None)).Diagnostic);
    }

    [TestMethod]
    public async Task HelperRevalidationRebuildsUpgradeEvidenceFromFreshProviderVersions()
    {
        ImmutableUpdateJob upgrade = Approve(new UpdateJobSelection("winget:vendor.app", UpdateProvider.WinGet, UpdateSource.WinGet, "Vendor.App", "1.0.0", "1.1.0", UpdateScope.Machine, UpdateArchitecture.X64, UpdateChannel.Stable, "winget-com-v1"));
        FakeCandidates unchanged = new(new("winget", "vendor.app", "1.0.0", "1.1.0"));
        FakeCandidates newer = new(new("winget", "Vendor.App", "1.0.0", "1.2.0"));
        FakeCandidates other = new(new("winget", "Vendor.Other", "1.0.0", "1.1.0"));
        FakeCandidates none = new(null);

        UpdateJobExecutionEvidence? same = await new HelperOperationRevalidator(new NoInstalls(), unchanged).RevalidateAsync(upgrade, CancellationToken.None);
        Assert.IsTrue(upgrade.Matches(same!), "Provider ID casing differs but the approved identity is kept.");
        Assert.AreEqual("winget", unchanged.SourceIds.Single());
        Assert.IsFalse(upgrade.Matches((await new HelperOperationRevalidator(new NoInstalls(), newer).RevalidateAsync(upgrade, CancellationToken.None))!), "A newer candidate is a changed invariant.");
        Assert.IsNull(await new HelperOperationRevalidator(new NoInstalls(), other).RevalidateAsync(upgrade, CancellationToken.None));
        Assert.IsNull(await new HelperOperationRevalidator(new NoInstalls(), none).RevalidateAsync(upgrade, CancellationToken.None));
    }

    [TestMethod]
    public async Task HelperRevalidationRepeatsTheInstallPreflightForTheApprovedVersion()
    {
        ImmutableUpdateJob install = Approve(Install(UpdateScope.Machine, elevates: false));
        NoInstalls preflight = new() { Result = InstallPreflightResult.Actionable(install.Selection, [], InstallerElevation.ElevationRequired) };

        UpdateJobExecutionEvidence? evidence = await new HelperOperationRevalidator(preflight, new FakeCandidates(null)).RevalidateAsync(install, CancellationToken.None);

        Assert.IsTrue(install.Matches(evidence!));
        Assert.AreEqual("2.0.0", preflight.Requests.Single().ExpectedVersion);
        Assert.IsTrue(preflight.Requests.Single().IsRevalidation);
    }

    [TestMethod]
    public async Task CloserIsGracefulBoundedAndForcesOnlyWithConsent()
    {
        FakeProcess cooperative = new(exitsOnClose: true);
        FakeProcess stubborn = new(exitsOnClose: false);
        WindowsRunningApplicationCloser closer = new(name => name == "cooperative" ? [cooperative] : [stubborn]);

        Assert.IsTrue(await closer.CloseAsync(["cooperative"], allowForce: false, TimeSpan.FromSeconds(1), CancellationToken.None));
        Assert.IsTrue(cooperative.CloseRequested);
        Assert.IsFalse(cooperative.Killed);

        Assert.IsFalse(await closer.CloseAsync(["stubborn"], allowForce: false, TimeSpan.FromMilliseconds(100), CancellationToken.None));
        Assert.IsFalse(stubborn.Killed, "No forced termination without the second per-run consent.");
        Assert.IsTrue(await closer.CloseAsync(["stubborn"], allowForce: true, TimeSpan.FromMilliseconds(100), CancellationToken.None));
        Assert.IsTrue(stubborn.Killed);
        Assert.IsTrue(cooperative.Disposed && stubborn.Disposed);
    }

    [TestMethod]
    public async Task CloserRejectsUnboundedOrHostileRequestsWithoutLookingUpProcesses()
    {
        int lookups = 0;
        WindowsRunningApplicationCloser closer = new(_ => { lookups++; return []; });
        Assert.IsFalse(await closer.CloseAsync(["..\\evil"], true, TimeSpan.FromSeconds(1), CancellationToken.None));
        Assert.IsFalse(await closer.CloseAsync(["a b"], true, TimeSpan.FromSeconds(1), CancellationToken.None));
        Assert.IsFalse(await closer.CloseAsync([], true, TimeSpan.FromSeconds(1), CancellationToken.None));
        Assert.IsFalse(await closer.CloseAsync([.. Enumerable.Range(0, 17).Select(index => "app" + index)], true, TimeSpan.FromSeconds(1), CancellationToken.None));
        Assert.AreEqual(0, lookups);
        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => closer.CloseAsync(["app"], false, TimeSpan.FromMinutes(5), CancellationToken.None));
    }

    private static UpdateJobSelection Install(UpdateScope scope, bool elevates) =>
        UpdateJobSelection.ForInstall("pcs:app", UpdateProvider.WinGet, UpdateSource.WinGet, "Vendor.App", "2.0.0", scope, UpdateArchitecture.X64, UpdateChannel.Stable, "winget-com-v1", null, elevates);

    private static ImmutableUpdateJob Approve(UpdateJobSelection selection) => new UpdateJobReview(Guid.NewGuid(), Guid.NewGuid(), selection).Confirm(DateTimeOffset.UtcNow);

    private static UpdateJobExecutionEvidence Evidence(ImmutableUpdateJob job) => new(job.Selection, true, true);

    private sealed class RecordingProcess : IWingetProcess
    {
        public List<IReadOnlyList<string>> Calls { get; } = [];

        public Task<WingetProcessResult> RunAsync(IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken)
        {
            Calls.Add(arguments);
            return Task.FromResult(new WingetProcessResult(0, string.Empty, string.Empty, false));
        }
    }

    private sealed class FakeCandidates(StructuredUpdateCandidateEvidence? evidence) : IStructuredUpdateCandidateAuthority
    {
        public List<string> SourceIds { get; } = [];

        public Task<StructuredUpdateCandidateEvidence?> ResolveAsync(string sourceId, string packageId, CancellationToken cancellationToken)
        {
            SourceIds.Add(sourceId);
            return Task.FromResult(evidence);
        }
    }

    private sealed class NoInstalls : IInstallPreflightAuthority
    {
        public List<InstallPreflightRequest> Requests { get; } = [];
        public InstallPreflightResult Result { get; set; } = InstallPreflightResult.NotActionable("not-configured");

        public Task<InstallPreflightResult> PreflightAsync(InstallPreflightRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(Result);
        }
    }

    private sealed class FakeProcess(bool exitsOnClose) : IClosableProcess
    {
        private readonly TaskCompletionSource exited = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool CloseRequested { get; private set; }
        public bool Killed { get; private set; }
        public bool Disposed { get; private set; }
        public bool HasExited => exited.Task.IsCompleted;

        public bool CloseMainWindow()
        {
            CloseRequested = true;
            if (exitsOnClose) exited.TrySetResult();
            return true;
        }

        public Task WaitForExitAsync(CancellationToken cancellationToken) => exited.Task.WaitAsync(cancellationToken);

        public void Kill()
        {
            Killed = true;
            exited.TrySetResult();
        }

        public void Dispose() => Disposed = true;
    }
}
