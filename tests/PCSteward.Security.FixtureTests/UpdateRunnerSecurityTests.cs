using PCSteward.Application;
using PCSteward.Domain;
using PCSteward.Execution;
using PCSteward.Provider.WinGet;

namespace PCSteward.Security.FixtureTests;

[TestClass]
public sealed class UpdateRunnerSecurityTests
{
    [TestMethod]
    [TestCategory("FR-UPD-14")]
    [DataRow(UpdateScope.User)]
    [DataRow(UpdateScope.Machine)]
    public async Task CompiledUpgradeExecutorsSelectOwnedUpdateHandlingInsteadOfInventoryHandling(UpdateScope scope)
    {
        UpgradeOnlyProcess process = new();
        ImmutableUpdateJob job = ApprovedJob(scope);
        WingetProcessResult result = scope == UpdateScope.User
            ? await new WingetReviewedUserUpdateExecutor(process).ExecuteAsync(Reviewed(job), null, CancellationToken.None)
            : await new WingetMachineUpdateExecutor(process).ExecuteAsync(job, CancellationToken.None);
        Assert.AreEqual(1, process.UpgradeCalls);
        Assert.AreEqual(0, result.ExitCode);
        Assert.AreEqual("upgrade", process.Arguments[0]);
    }

    [TestMethod]
    [TestCategory("FR-UPD-14")]
    [DataRow(false, true, false, UpdateExecutionStatus.VerificationRequired)]
    [DataRow(true, false, false, UpdateExecutionStatus.Cancelled)]
    [DataRow(false, false, true, UpdateExecutionStatus.VerificationRequired)]
    public async Task IncompleteOrCancelledOutputNeverBecomesZeroExitSuccessAndRetainsOwnership(
        bool cancelled, bool incomplete, bool uncertain, UpdateExecutionStatus expected)
    {
        TaskCompletionSource owned = new(TaskCreationOptions.RunContinuationsAsynchronously);
        WingetProcessResult raw = new(0, "partial token=secret", string.Empty, false,
            Cancelled: cancelled, OutputIncomplete: incomplete, OwnedCompletion: owned.Task, ExecutionUncertain: uncertain);
        ImmutableUpdateJob job = ApprovedJob();
        NonElevatedUpdateRunner runner = new(new WingetReviewedUserUpdateExecutor(new RecordingProcess(raw)));
        UpdateExecutionResult result = await runner.RunAsync(job, Reviewed(job), FreshEvidence(job), CancellationToken.None);
        Assert.AreEqual(expected, result.Status);
        Assert.AreSame(owned.Task, result.OwnedCompletion);
        Assert.IsFalse(result.SanitizedOutput.Contains("secret", StringComparison.Ordinal));
        IApprovedUpdateExecutor executor = new ApprovedUpdateExecutor(runner);
        Assert.IsNotNull(executor.ExecutionLifetime);
        UpdateProviderAttempt attempt = await executor.ExecuteAsync(job, Reviewed(job), FreshEvidence(job), new UpdateItemProgress(job, null), CancellationToken.None);
        Assert.AreSame(owned.Task, attempt.OwnedCompletion);
        Assert.AreEqual(cancelled ? UpdateProviderAttemptStatus.Cancelled : UpdateProviderAttemptStatus.VerificationRequired, attempt.Status);
        owned.SetResult();
    }

    [TestMethod]
    [TestCategory("FR-UPD-14")]
    public async Task AlreadyCancelledUserUpdateNeverInvokesTheProvider()
    {
        RecordingProcess process = new(new(0, string.Empty, string.Empty, false));
        NonElevatedUpdateRunner runner = new(new WingetReviewedUserUpdateExecutor(process));
        ImmutableUpdateJob job = ApprovedJob();
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        UpdateExecutionResult result = await runner.RunAsync(job, Reviewed(job), FreshEvidence(job), cancellation.Token);
        Assert.AreEqual(UpdateExecutionStatus.Cancelled, result.Status);
        Assert.HasCount(0, process.Arguments);
    }

    private static readonly string[] ExpectedArguments =
    [
        "upgrade", "--id", "Contoso.Widget", "--version", "1.1.0", "--source", "winget",
        "--exact", "--scope", "user", "--architecture", "x64", "--disable-interactivity", "--silent"
    ];

    [TestMethod]
    [TestCategory("FR-UPD-15")]
    [Description("Stage C4 (Task 54 R1): the confirmed user job runs only the reviewed Silent mapping built from its reviewed operation.")]
    public async Task ConfirmedUserJobBuildsOnlyTheAllowlistedProviderOperation()
    {
        RecordingProcess process = new(new WingetProcessResult(0, "updated", string.Empty, false));
        ImmutableUpdateJob job = ApprovedJob();
        NonElevatedUpdateRunner runner = new(new WingetReviewedUserUpdateExecutor(process));

        UpdateExecutionResult result = await runner.RunAsync(job, Reviewed(job), FreshEvidence(job), CancellationToken.None);

        Assert.AreEqual(UpdateExecutionStatus.AwaitingVerification, result.Status);
        Assert.AreEqual(0, result.ExitCode);
        CollectionAssert.AreEqual(ExpectedArguments, process.Arguments.Single().ToArray());
        Assert.IsFalse(Enum.GetNames<UpdateExecutionStatus>().Contains("Success", StringComparer.Ordinal));
    }

    [TestMethod]
    [TestCategory("FR-UPD-15")]
    [Description("Stage C4 (Task 54 R1): a user job without a reviewed operation bound to it, or with one frozen from another job, never starts.")]
    public async Task UserJobWithoutItsReviewedOperationNeverReachesTheProvider()
    {
        RecordingProcess process = new(new WingetProcessResult(0, string.Empty, string.Empty, false));
        NonElevatedUpdateRunner runner = new(new WingetReviewedUserUpdateExecutor(process));
        ImmutableUpdateJob job = ApprovedJob();
        ImmutableUpdateJob other = ApprovedJob();

        UpdateExecutionResult missing = await runner.RunAsync(job, null, FreshEvidence(job), CancellationToken.None);
        UpdateExecutionResult foreign = await runner.RunAsync(job, Reviewed(other), FreshEvidence(job), CancellationToken.None);
        UpdateProviderAttempt viaExecutor = await new ApprovedUpdateExecutor(runner).ExecuteAsync(job, FreshEvidence(job), CancellationToken.None);

        foreach (UpdateExecutionResult denied in new[] { missing, foreign })
        {
            Assert.AreEqual(UpdateExecutionStatus.Denied, denied.Status);
            Assert.AreEqual(UpdateExecutionDenial.ReviewedModeUnavailable, denied.Denial);
            Assert.AreEqual("reviewed-installer-mode-unavailable", denied.SanitizedOutput);
        }
        Assert.AreEqual(UpdateProviderAttemptStatus.ProductFailed, viaExecutor.Status, "the mode-less executor overload cannot run a user job");
        Assert.HasCount(0, process.Arguments);
    }

    [TestMethod]
    [TestCategory("FR-UPD-15")]
    public async Task ReviewedUserExecutorRefusesAnOperationOutsideThePerUserRoute()
    {
        RecordingProcess process = new(new WingetProcessResult(0, string.Empty, string.Empty, false));
        DateTimeOffset at = DateTimeOffset.UtcNow;
        ImmutableUpdateJob job = new UpdateJobReview(Guid.NewGuid(), Guid.NewGuid(), Selection()).Confirm(at);
        ReviewedUpdateOperation msixRoute = new(job, Sid, "winget:contoso.widget", "user:winget", "inventory:fixture", at,
            UpdateRouteContract.WinGetUserExactV3, ReviewedInstallerMode.Selected(InstallerMode.Silent));

        await Assert.ThrowsExactlyAsync<ArgumentException>(() => new WingetReviewedUserUpdateExecutor(process).ExecuteAsync(msixRoute, null, CancellationToken.None));
        UpdateExecutionResult result = await new NonElevatedUpdateRunner(new WingetReviewedUserUpdateExecutor(process)).RunAsync(job, msixRoute, FreshEvidence(job), CancellationToken.None);
        Assert.AreEqual(UpdateExecutionDenial.ReviewedModeUnavailable, result.Denial);
        Assert.HasCount(0, process.Arguments);
    }

    [TestMethod]
    public async Task CurrentEvidenceMismatchAndMachineScopeNeverReachTheNonElevatedProcess()
    {
        RecordingProcess process = new(new WingetProcessResult(0, string.Empty, string.Empty, false));
        NonElevatedUpdateRunner runner = new(new WingetReviewedUserUpdateExecutor(process));
        ImmutableUpdateJob userJob = ApprovedJob();
        UpdateJobExecutionEvidence changedVersion = new(Selection(candidate: "1.2.0"), true, true);

        UpdateExecutionResult mismatch = await runner.RunAsync(userJob, Reviewed(userJob), changedVersion, CancellationToken.None);
        ImmutableUpdateJob machineJob = ApprovedJob(UpdateScope.Machine);
        UpdateExecutionResult elevation = await runner.RunAsync(machineJob, null, FreshEvidence(machineJob), CancellationToken.None);

        Assert.AreEqual(UpdateExecutionStatus.Denied, mismatch.Status);
        Assert.AreEqual(UpdateExecutionDenial.CurrentEvidenceMismatch, mismatch.Denial);
        Assert.AreEqual(UpdateExecutionStatus.ElevationRequired, elevation.Status);
        Assert.HasCount(0, process.Arguments);
    }

    [TestMethod]
    public async Task StandaloneExecutorRejectsMachineScopeWithoutElevatedTransport()
    {
        RecordingProcess process = new(new WingetProcessResult(0, string.Empty, string.Empty, false));
        ApprovedUpdateExecutor executor = new(new NonElevatedUpdateRunner(new WingetReviewedUserUpdateExecutor(process)));
        ImmutableUpdateJob machineJob = ApprovedJob(UpdateScope.Machine);

        UpdateProviderAttempt result = await executor.ExecuteAsync(machineJob, FreshEvidence(machineJob), CancellationToken.None);

        Assert.AreEqual(UpdateProviderAttemptStatus.ProductFailed, result.Status);
        Assert.AreEqual("standalone-package-machine-scope-disabled", result.SanitizedDiagnostic);
        Assert.HasCount(0, process.Arguments);
    }

    [TestMethod]
    public async Task NonzeroTimeoutOverflowAndCancellationRemainDistinctFromSuccess()
    {
        UpdateExecutionResult failed = await RunWithAsync(new WingetProcessResult(23, "provider failed", string.Empty, false));
        UpdateExecutionResult timeout = await RunWithAsync(new WingetProcessResult(-1, string.Empty, "late", true));
        UpdateExecutionResult overflow = await RunWithAsync(new WingetProcessResult(-1, new string('x', 5000), string.Empty, false, true));
        ImmutableUpdateJob productJob = ApprovedJob();
        UpdateExecutionResult productFailure = await new NonElevatedUpdateRunner(new FailingExecutor()).RunAsync(productJob, Reviewed(productJob), FreshEvidence(productJob), CancellationToken.None);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        ImmutableUpdateJob cancelledJob = ApprovedJob();
        UpdateExecutionResult cancelled = await new NonElevatedUpdateRunner(new CancellingExecutor()).RunAsync(cancelledJob, Reviewed(cancelledJob), FreshEvidence(cancelledJob), cancellation.Token);

        Assert.AreEqual(UpdateExecutionStatus.ProviderFailed, failed.Status);
        Assert.AreEqual(UpdateExecutionStatus.TimedOut, timeout.Status);
        Assert.AreEqual(UpdateExecutionStatus.OutputLimitExceeded, overflow.Status);
        Assert.AreEqual(4096, overflow.SanitizedOutput.Length);
        Assert.AreEqual(UpdateExecutionStatus.ProductFailed, productFailure.Status);
        StringAssert.Contains(productFailure.SanitizedOutput, "[path]");
        Assert.AreEqual(UpdateExecutionStatus.Cancelled, cancelled.Status);
    }

    [TestMethod]
    [TestCategory("FR-UPD-09")]
    [TestCategory("AC-UPD-07")]
    [Description("Task 51: only WinGet's documented restart exit codes are restart evidence. Restart text with exit 0 is not; a raw installer 3010 is not a WinGet exit code; restart-before-install applied nothing.")]
    [DataRow(0, "Restart your PC to finish installation.", UpdateExecutionStatus.AwaitingVerification, "")]
    [DataRow(unchecked((int)0x8A150109), "", UpdateExecutionStatus.RestartReported, "provider-reported-restart-required-to-finish")]
    [DataRow(unchecked((int)0x8A15010B), "", UpdateExecutionStatus.RestartReported, "provider-reported-restart-initiated")]
    [DataRow(unchecked((int)0x8A15010A), "", UpdateExecutionStatus.ProviderFailed, "restart-required-before-install; update-not-applied")]
    [DataRow(3010, "Restart required", UpdateExecutionStatus.ProviderFailed, "")]
    [DataRow(1641, "", UpdateExecutionStatus.ProviderFailed, "")]
    [DataRow(1, "reboot required", UpdateExecutionStatus.ProviderFailed, "")]
    public async Task OnlyTypedWingetRestartExitCodesAreRestartEvidence(int exitCode, string output, UpdateExecutionStatus expected, string prefix)
    {
        UpdateExecutionResult result = await RunWithAsync(new WingetProcessResult(exitCode, output, string.Empty, false));

        Assert.AreEqual(expected, result.Status);
        Assert.AreEqual(exitCode, result.ExitCode, "the exit code is recorded as observed (AC-UPD-11)");
        if (prefix.Length == 0) Assert.IsFalse(result.SanitizedOutput.StartsWith("provider-reported", StringComparison.Ordinal) || result.SanitizedOutput.StartsWith("restart-required", StringComparison.Ordinal));
        else StringAssert.StartsWith(result.SanitizedOutput, prefix);
        Assert.AreEqual(prefix, WingetRestartExitCodes.DiagnosticPrefix(exitCode));
    }

    [TestMethod]
    [TestCategory("FR-UPD-09")]
    public void WingetRestartExitCodesMatchTheDocumentedHresults()
    {
        Assert.AreEqual("8A150109", WingetRestartExitCodes.RebootRequiredToFinish.ToString("X8", System.Globalization.CultureInfo.InvariantCulture));
        Assert.AreEqual("8A15010A", WingetRestartExitCodes.RebootRequiredForInstall.ToString("X8", System.Globalization.CultureInfo.InvariantCulture));
        Assert.AreEqual("8A15010B", WingetRestartExitCodes.RebootInitiated.ToString("X8", System.Globalization.CultureInfo.InvariantCulture));
        Assert.AreEqual(WingetRestartSignal.RestartRequiredToFinish, WingetRestartExitCodes.Classify(WingetRestartExitCodes.RebootRequiredToFinish));
        Assert.AreEqual(WingetRestartSignal.RestartInitiated, WingetRestartExitCodes.Classify(WingetRestartExitCodes.RebootInitiated));
        Assert.AreEqual(WingetRestartSignal.RestartRequiredBeforeInstall, WingetRestartExitCodes.Classify(WingetRestartExitCodes.RebootRequiredForInstall));
        Assert.AreEqual(WingetRestartSignal.None, WingetRestartExitCodes.Classify(0));
        Assert.AreEqual(WingetRestartSignal.None, WingetRestartExitCodes.Classify(3010));
        Assert.IsFalse(WingetRestartExitCodes.ReportsFinishedPendingRestart(WingetRestartExitCodes.RebootRequiredForInstall), "nothing was applied");
        Assert.AreEqual(UpdateOutcomePresenter.RestartBeforeInstallDiagnostic, WingetRestartExitCodes.DiagnosticPrefix(WingetRestartExitCodes.RebootRequiredForInstall));
    }

    [TestMethod]
    [TestCategory("FR-UPD-09")]
    public async Task ApprovedExecutorCarriesATypedRestartAsRestartReported()
    {
        ApprovedUpdateExecutor executor = new(new NonElevatedUpdateRunner(new WingetReviewedUserUpdateExecutor(
            new RecordingProcess(new WingetProcessResult(WingetRestartExitCodes.RebootRequiredToFinish, string.Empty, string.Empty, false)))));
        ImmutableUpdateJob job = ApprovedJob();

        UpdateProviderAttempt attempt = await executor.ExecuteAsync(job, Reviewed(job), FreshEvidence(job), new UpdateItemProgress(job, null), CancellationToken.None);

        Assert.AreEqual(UpdateProviderAttemptStatus.RestartReported, attempt.Status);
        Assert.AreEqual(WingetRestartExitCodes.RebootRequiredToFinish, attempt.ExitCode);
    }

    [TestMethod]
    public void RunnerSurfaceCannotAcceptCommandsPathsArgumentsWorkingDirectoriesOrEnvironment()
    {
        Type[] parameterTypes = typeof(NonElevatedUpdateRunner).GetMethod(nameof(NonElevatedUpdateRunner.RunAsync))!
            .GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        Assert.IsFalse(parameterTypes.Contains(typeof(string)));
        Assert.IsFalse(parameterTypes.Any(type => typeof(IEnumerable<string>).IsAssignableFrom(type)));
        Assert.IsFalse(parameterTypes.Any(type => typeof(IDictionary<string, string>).IsAssignableFrom(type)));
        Assert.IsFalse(typeof(IWingetProcess).IsPublic);
        Assert.IsFalse(typeof(IWingetProcess).GetMethod(nameof(IWingetProcess.RunAsync))!.GetParameters().Any(parameter => parameter.ParameterType == typeof(string)));
        Assert.IsTrue(typeof(WingetReviewedUserUpdateExecutor).GetConstructors().All(constructor => constructor.GetParameters().Length == 0));
        Assert.IsTrue(typeof(WingetMachineUpdateExecutor).GetConstructors().All(constructor => constructor.GetParameters().Length == 0));
    }

    [TestMethod]
    public async Task ProviderOutputIsBoundedControlSafeAndCredentialAndPathRedacted()
    {
        UpdateExecutionResult result = await RunWithAsync(new WingetProcessResult(
            7,
            "token=abc123\r\nC:\\Users\\Alice\\secret.txt",
            "password: hunter2\u0000",
            false));

        Assert.AreEqual(UpdateExecutionStatus.ProviderFailed, result.Status);
        StringAssert.Contains(result.SanitizedOutput, "token=[redacted]");
        StringAssert.Contains(result.SanitizedOutput, "password=[redacted]");
        StringAssert.Contains(result.SanitizedOutput, "[path]");
        Assert.IsFalse(result.SanitizedOutput.Any(char.IsControl));
    }

    private static async Task<UpdateExecutionResult> RunWithAsync(WingetProcessResult processResult)
    {
        ImmutableUpdateJob job = ApprovedJob();
        return await new NonElevatedUpdateRunner(new WingetReviewedUserUpdateExecutor(new RecordingProcess(processResult)))
            .RunAsync(job, Reviewed(job), FreshEvidence(job), CancellationToken.None);
    }

    private const string Sid = "S-1-5-21-11-22-33-1001";

    private static ImmutableUpdateJob ApprovedJob(UpdateScope scope = UpdateScope.User) =>
        new UpdateJobReview(Guid.NewGuid(), Guid.NewGuid(), Selection(scope)).Confirm(DateTimeOffset.UtcNow);

    /// <summary>Stage C4: the per-user reviewed operation frozen from <paramref name="job"/> with the Silent mode bound at review.</summary>
    private static ReviewedUpdateOperation Reviewed(ImmutableUpdateJob job) =>
        WingetUserReviewedRoute.Freeze(job, ReviewedInstallerMode.Selected(InstallerMode.Silent), Sid, "inventory:fixture", job.ApprovedAt)
        ?? throw new InvalidOperationException("The fixture job must freeze on the per-user route.");

    private static UpdateJobExecutionEvidence FreshEvidence(ImmutableUpdateJob job) => new(job.Selection, true, true);

    private static UpdateJobSelection Selection(UpdateScope scope = UpdateScope.User, string candidate = "1.1.0") =>
        new(
            "winget:contoso.widget",
            UpdateProvider.WinGet,
            UpdateSource.WinGet,
            "Contoso.Widget",
            "1.0.0",
            candidate,
            scope,
            UpdateArchitecture.X64,
            UpdateChannel.Stable,
            "mapping-v1");

    private sealed class RecordingProcess(WingetProcessResult result) : IWingetProcess
    {
        public List<IReadOnlyList<string>> Arguments { get; } = [];
        public Task<WingetProcessResult> RunAsync(IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken)
        {
            Arguments.Add(arguments);
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(result);
        }
    }
    private sealed class UpgradeOnlyProcess : IWingetProcess
    {
        internal int UpgradeCalls { get; private set; }
        internal IReadOnlyList<string> Arguments { get; private set; } = [];
        public Task<WingetProcessResult> RunAsync(IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("An approved upgrade must not use inventory process handling.");
        public Task<WingetProcessResult> RunUpgradeAsync(IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken)
        {
            UpgradeCalls++;
            Arguments = arguments;
            return Task.FromResult(new WingetProcessResult(0, string.Empty, string.Empty, false));
        }
    }

    private sealed class CancellingExecutor : IWingetReviewedUserUpdateExecutor
    {
        public Task<WingetProcessResult> ExecuteAsync(ReviewedUpdateOperation operation, UpdateItemProgress? progress, CancellationToken cancellationToken) =>
            Task.FromCanceled<WingetProcessResult>(cancellationToken);
    }

    private sealed class FailingExecutor : IWingetReviewedUserUpdateExecutor
    {
        public Task<WingetProcessResult> ExecuteAsync(ReviewedUpdateOperation operation, UpdateItemProgress? progress, CancellationToken cancellationToken) =>
            throw new System.ComponentModel.Win32Exception("Could not start C:\\Users\\Alice\\winget.exe");
    }
}
