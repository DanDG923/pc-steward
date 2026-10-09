using PCSteward.Application;
using PCSteward.Domain;
using PCSteward.Execution;
using PCSteward.Provider.WinGet;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// Task 48: v0.3 operations (Install, agreement-bearing or re-routed Upgrade) never reach the v0.2 single-job
/// runners, the upgrade argv builders or helper protocol v1. They wait for the Task 49 batch path.
/// </summary>
[TestClass]
public sealed class OperationKindSecurityTests
{
    private const string Hash = "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF";

    [TestMethod]
    public async Task NonElevatedRunnerDeniesNonPlainOperationsBeforeAnyProcess()
    {
        RecordingProcess process = new();
        NonElevatedUpdateRunner runner = new(new WingetUpdateExecutor(process));

        foreach (ImmutableUpdateJob job in new[] { Approve(Install(UpdateScope.User)), Approve(UpgradeWithAgreement()) })
        {
            UpdateExecutionResult result = await runner.RunAsync(job, new UpdateJobExecutionEvidence(job.Selection, true, true), CancellationToken.None);
            Assert.AreEqual(UpdateExecutionStatus.Denied, result.Status);
            Assert.AreEqual(UpdateExecutionDenial.UnsupportedOperation, result.Denial);
        }

        Assert.AreEqual(0, process.Calls);
    }

    [TestMethod]
    public async Task ElevatedRunnerDeniesInstallBeforeRequestingElevation()
    {
        RecordingTransport transport = new();
        ElevatedUpdateRunner runner = new(transport);
        ImmutableUpdateJob job = Approve(Install(UpdateScope.Machine));

        UpdateExecutionResult result = await runner.RunAsync(job, new UpdateJobExecutionEvidence(job.Selection, true, true), CancellationToken.None);

        Assert.AreEqual(UpdateExecutionDenial.UnsupportedOperation, result.Denial);
        Assert.IsNull(result.InstalledVersion);
        Assert.AreEqual(0, transport.Calls, "No UAC prompt for an operation the v1 helper cannot carry.");
    }

    [TestMethod]
    public void HelperProtocolVersionOneCarriesOnlyPlainUpgrades()
    {
        string nonce = ElevatedHelperProtocol.CreateNonce();
        Assert.ThrowsExactly<ArgumentException>(() => ElevatedHelperProtocol.CreateRequest(Approve(Install(UpdateScope.Machine)), nonce, DateTimeOffset.UtcNow));
        Assert.ThrowsExactly<ArgumentException>(() => ElevatedHelperProtocol.CreateRequest(Approve(UpgradeWithAgreement(UpdateScope.Machine)), nonce, DateTimeOffset.UtcNow));
        Assert.ThrowsExactly<ArgumentException>(() => WingetMachineUpdateExecutor.BuildUpdateArguments(Approve(Install(UpdateScope.Machine))));
    }

    private static ImmutableUpdateJob Approve(UpdateJobSelection selection) => new UpdateJobReview(Guid.NewGuid(), Guid.NewGuid(), selection).Confirm(DateTimeOffset.UtcNow);

    private static UpdateJobSelection Install(UpdateScope scope) =>
        UpdateJobSelection.ForInstall("pcs:widget", UpdateProvider.WinGet, UpdateSource.WinGet, "Contoso.Widget", "2.0.0", scope, UpdateArchitecture.X64, UpdateChannel.Stable, "winget-com-v1", null, false);

    private static UpdateJobSelection UpgradeWithAgreement(UpdateScope scope = UpdateScope.User) =>
        new("winget:contoso.widget", UpdateProvider.WinGet, UpdateSource.WinGet, "Contoso.Widget", "1.0.0", "1.1.0", scope, UpdateArchitecture.X64, UpdateChannel.Stable, "mapping-v1", Hash);

    private sealed class RecordingProcess : IWingetProcess
    {
        public int Calls { get; private set; }

        public Task<WingetProcessResult> RunAsync(IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new WingetProcessResult(0, string.Empty, string.Empty, false));
        }
    }

    private sealed class RecordingTransport : IElevatedHelperTransport
    {
        public int Calls { get; private set; }

        public Task<ElevatedHelperResult> ExecuteAsync(ImmutableUpdateJob job, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new ElevatedHelperResult(ElevatedHelperOutcome.Completed, 0, string.Empty));
        }
    }
}
