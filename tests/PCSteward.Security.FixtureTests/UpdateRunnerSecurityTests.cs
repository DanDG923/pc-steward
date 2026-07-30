using PCSteward.Application;
using PCSteward.Domain;
using PCSteward.Execution;
using PCSteward.Provider.WinGet;

namespace PCSteward.Security.FixtureTests;

[TestClass]
public sealed class UpdateRunnerSecurityTests
{
    private static readonly string[] ExpectedArguments =
    [
        "upgrade", "--id", "Contoso.Widget", "--version", "1.1.0", "--source", "winget",
        "--exact", "--scope", "user", "--architecture", "x64", "--disable-interactivity"
    ];

    [TestMethod]
    public async Task ConfirmedUserJobBuildsOnlyTheAllowlistedProviderOperation()
    {
        RecordingProcess process = new(new WingetProcessResult(0, "updated", string.Empty, false));
        ImmutableUpdateJob job = ApprovedJob();
        NonElevatedUpdateRunner runner = new(new WingetUpdateExecutor(process));

        UpdateExecutionResult result = await runner.RunAsync(job, FreshEvidence(job), CancellationToken.None);

        Assert.AreEqual(UpdateExecutionStatus.AwaitingVerification, result.Status);
        Assert.AreEqual(0, result.ExitCode);
        CollectionAssert.AreEqual(ExpectedArguments, process.Arguments.Single().ToArray());
        Assert.IsFalse(Enum.GetNames<UpdateExecutionStatus>().Contains("Success", StringComparer.Ordinal));
    }

    [TestMethod]
    public async Task CurrentEvidenceMismatchAndMachineScopeNeverReachTheNonElevatedProcess()
    {
        RecordingProcess process = new(new WingetProcessResult(0, string.Empty, string.Empty, false));
        NonElevatedUpdateRunner runner = new(new WingetUpdateExecutor(process));
        ImmutableUpdateJob userJob = ApprovedJob();
        UpdateJobExecutionEvidence changedVersion = new(Selection(candidate: "1.2.0"), true, true);

        UpdateExecutionResult mismatch = await runner.RunAsync(userJob, changedVersion, CancellationToken.None);
        ImmutableUpdateJob machineJob = ApprovedJob(UpdateScope.Machine);
        UpdateExecutionResult elevation = await runner.RunAsync(machineJob, FreshEvidence(machineJob), CancellationToken.None);

        Assert.AreEqual(UpdateExecutionStatus.Denied, mismatch.Status);
        Assert.AreEqual(UpdateExecutionDenial.CurrentEvidenceMismatch, mismatch.Denial);
        Assert.AreEqual(UpdateExecutionStatus.ElevationRequired, elevation.Status);
        Assert.HasCount(0, process.Arguments);
    }

    [TestMethod]
    public async Task StandaloneExecutorRejectsMachineScopeWithoutElevatedTransport()
    {
        RecordingProcess process = new(new WingetProcessResult(0, string.Empty, string.Empty, false));
        ApprovedUpdateExecutor executor = new(new NonElevatedUpdateRunner(new WingetUpdateExecutor(process)));
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
        UpdateExecutionResult productFailure = await new NonElevatedUpdateRunner(new FailingExecutor()).RunAsync(productJob, FreshEvidence(productJob), CancellationToken.None);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        ImmutableUpdateJob cancelledJob = ApprovedJob();
        UpdateExecutionResult cancelled = await new NonElevatedUpdateRunner(new CancellingExecutor()).RunAsync(cancelledJob, FreshEvidence(cancelledJob), cancellation.Token);

        Assert.AreEqual(UpdateExecutionStatus.ProviderFailed, failed.Status);
        Assert.AreEqual(UpdateExecutionStatus.TimedOut, timeout.Status);
        Assert.AreEqual(UpdateExecutionStatus.OutputLimitExceeded, overflow.Status);
        Assert.AreEqual(4096, overflow.SanitizedOutput.Length);
        Assert.AreEqual(UpdateExecutionStatus.ProductFailed, productFailure.Status);
        StringAssert.Contains(productFailure.SanitizedOutput, "[path]");
        Assert.AreEqual(UpdateExecutionStatus.Cancelled, cancelled.Status);
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
        Assert.IsTrue(typeof(WingetUpdateExecutor).GetConstructors().All(constructor => constructor.GetParameters().Length == 0));
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
        return await new NonElevatedUpdateRunner(new WingetUpdateExecutor(new RecordingProcess(processResult)))
            .RunAsync(job, FreshEvidence(job), CancellationToken.None);
    }

    private static ImmutableUpdateJob ApprovedJob(UpdateScope scope = UpdateScope.User) =>
        new UpdateJobReview(Guid.NewGuid(), Guid.NewGuid(), Selection(scope)).Confirm(DateTimeOffset.UtcNow);

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

    private sealed class CancellingExecutor : IWingetUpdateExecutor
    {
        public Task<WingetProcessResult> ExecuteAsync(ImmutableUpdateJob job, CancellationToken cancellationToken) =>
            Task.FromCanceled<WingetProcessResult>(cancellationToken);
    }

    private sealed class FailingExecutor : IWingetUpdateExecutor
    {
        public Task<WingetProcessResult> ExecuteAsync(ImmutableUpdateJob job, CancellationToken cancellationToken) =>
            throw new System.ComponentModel.Win32Exception("Could not start C:\\Users\\Alice\\winget.exe");
    }
}
