using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using PCSteward.Domain;
using PCSteward.Execution;
using PCSteward.Provider.WinGet;

namespace PCSteward.Security.FixtureTests;

[TestClass]
public sealed class ElevatedHelperSecurityTests
{
    private static readonly string[] ExpectedMachineArguments =
    [
        "upgrade", "--id", "Contoso.Widget", "--version", "1.1.0", "--source", "winget",
        "--exact", "--scope", "machine", "--architecture", "x64", "--disable-interactivity"
    ];

    [TestMethod]
    public async Task PerUserJobNeverInvokesElevatedTransportAndMachineJobDoes()
    {
        RecordingTransport transport = new(new ElevatedHelperResult(ElevatedHelperOutcome.Completed, 0, "completed"));
        ElevatedUpdateRunner runner = new(transport);
        ImmutableUpdateJob userJob = ApprovedJob(UpdateScope.User);
        ImmutableUpdateJob machineJob = ApprovedJob(UpdateScope.Machine);

        UpdateExecutionResult userResult = await runner.RunAsync(userJob, FreshEvidence(userJob), CancellationToken.None);
        UpdateExecutionResult machineResult = await runner.RunAsync(machineJob, FreshEvidence(machineJob), CancellationToken.None);

        Assert.AreEqual(UpdateExecutionStatus.Denied, userResult.Status);
        Assert.AreEqual(UpdateExecutionDenial.UnsupportedScope, userResult.Denial);
        Assert.AreEqual(UpdateExecutionStatus.AwaitingVerification, machineResult.Status);
        Assert.HasCount(1, transport.Jobs);
        Assert.AreSame(machineJob, transport.Jobs.Single());
    }

    [TestMethod]
    public async Task MachineProviderBuildsOnlyTheFixedMachineScopeOperation()
    {
        RecordingProcess process = new();
        ImmutableUpdateJob job = ApprovedJob(UpdateScope.Machine);

        WingetProcessResult result = await new WingetMachineUpdateExecutor(process).ExecuteAsync(job, CancellationToken.None);

        Assert.AreEqual(0, result.ExitCode);
        CollectionAssert.AreEqual(ExpectedMachineArguments, process.Arguments.Single().ToArray());
        Assert.ThrowsExactly<ArgumentException>(() => WingetMachineUpdateExecutor.BuildUpdateArguments(ApprovedJob(UpdateScope.User)));
    }

    [TestMethod]
    public async Task UserDeniedCancelledTimedOutFailedAndCompletedRemainDistinct()
    {
        Dictionary<ElevatedHelperOutcome, UpdateExecutionStatus> expected = new()
        {
            [ElevatedHelperOutcome.UserDenied] = UpdateExecutionStatus.ElevationDenied,
            [ElevatedHelperOutcome.Cancelled] = UpdateExecutionStatus.Cancelled,
            [ElevatedHelperOutcome.TimedOut] = UpdateExecutionStatus.TimedOut,
            [ElevatedHelperOutcome.Failed] = UpdateExecutionStatus.HelperFailed,
            [ElevatedHelperOutcome.Completed] = UpdateExecutionStatus.AwaitingVerification
        };

        foreach ((ElevatedHelperOutcome outcome, UpdateExecutionStatus status) in expected)
        {
            ImmutableUpdateJob job = ApprovedJob(UpdateScope.Machine);
            UpdateExecutionResult result = await new ElevatedUpdateRunner(
                new RecordingTransport(new ElevatedHelperResult(outcome, outcome == ElevatedHelperOutcome.Completed ? 0 : null, outcome.ToString())))
                .RunAsync(job, FreshEvidence(job), CancellationToken.None);

            Assert.AreEqual(status, result.Status, outcome.ToString());
            Assert.AreNotEqual("Success", result.Status.ToString());
        }
    }

    [TestMethod]
    public async Task StaleOrSubstitutedEvidenceNeverReachesTheHelper()
    {
        RecordingTransport transport = new(new ElevatedHelperResult(ElevatedHelperOutcome.Completed, 0, string.Empty));
        ElevatedUpdateRunner runner = new(transport);
        ImmutableUpdateJob job = ApprovedJob(UpdateScope.Machine);
        UpdateJobExecutionEvidence stale = new(job.Selection, true, false);
        UpdateJobExecutionEvidence substituted = new(Selection(UpdateScope.Machine, candidate: "9.9.9"), true, true);

        UpdateExecutionResult staleResult = await runner.RunAsync(job, stale, CancellationToken.None);
        UpdateExecutionResult substitutedResult = await runner.RunAsync(job, substituted, CancellationToken.None);

        Assert.AreEqual(UpdateExecutionStatus.Denied, staleResult.Status);
        Assert.AreEqual(UpdateExecutionStatus.Denied, substitutedResult.Status);
        Assert.HasCount(0, transport.Jobs);
    }

    [TestMethod]
    public void ProtocolRejectsMutationUnknownDuplicateMalformedAndOverlongMessages()
    {
        ImmutableUpdateJob job = ApprovedJob(UpdateScope.Machine);
        string nonce = ElevatedHelperProtocol.CreateNonce();
        DateTimeOffset issuedAt = DateTimeOffset.UtcNow;
        string valid = Encoding.UTF8.GetString(ElevatedHelperProtocol.CreateRequest(job, nonce, issuedAt));
        string substituted = valid.Replace("\"Contoso.Widget\"", "\"Fabrikam.Other\"", StringComparison.Ordinal);
        string withPath = valid.Replace("\"issuedAtUtc\"", "\"workingDirectory\":\"C:\\\\Windows\",\"issuedAtUtc\"", StringComparison.Ordinal);
        string duplicate = valid.Replace("\"packageId\"", "\"packageId\":\"Fabrikam.Other\",\"packageId\"", StringComparison.Ordinal);

        Assert.IsFalse(ElevatedHelperProtocol.TryReadRequest(Encoding.UTF8.GetBytes(substituted), nonce, issuedAt, out _, out string substitutionRejection));
        Assert.AreEqual("request-job-integrity-invalid", substitutionRejection);
        Assert.IsFalse(ElevatedHelperProtocol.TryReadRequest(Encoding.UTF8.GetBytes(withPath), nonce, issuedAt, out _, out string pathRejection));
        Assert.AreEqual("message-property-unknown-or-duplicate", pathRejection);
        Assert.IsFalse(ElevatedHelperProtocol.TryReadRequest(Encoding.UTF8.GetBytes(duplicate), nonce, issuedAt, out _, out string duplicateRejection));
        Assert.AreEqual("message-property-unknown-or-duplicate", duplicateRejection);
        Assert.IsFalse(ElevatedHelperProtocol.TryReadRequest("{"u8, nonce, issuedAt, out _, out string malformedRejection));
        Assert.AreEqual("message-json-invalid", malformedRejection);
        Assert.IsFalse(ElevatedHelperProtocol.TryReadRequest(new byte[ElevatedHelperProtocol.MaximumMessageBytes + 1], nonce, issuedAt, out _, out string sizeRejection));
        Assert.AreEqual("message-size-invalid", sizeRejection);
    }

    [TestMethod]
    public void ProtocolBindsNonceExpiryIntegrityAndOneShotReplay()
    {
        ImmutableUpdateJob job = ApprovedJob(UpdateScope.Machine);
        string nonce = ElevatedHelperProtocol.CreateNonce();
        DateTimeOffset issuedAt = DateTimeOffset.UtcNow;
        byte[] request = ElevatedHelperProtocol.CreateRequest(job, nonce, issuedAt);
        OneShotHelperRequestValidator validator = new(nonce, () => issuedAt);

        Assert.IsTrue(validator.TryAccept(request, out ImmutableUpdateJob? restored, out string acceptedRejection));
        Assert.AreEqual(string.Empty, acceptedRejection);
        Assert.AreEqual(job.Digest, restored!.Digest);
        Assert.IsFalse(validator.TryAccept(request, out _, out string replayRejection));
        Assert.AreEqual("duplicate-request", replayRejection);
        Assert.IsFalse(ElevatedHelperProtocol.TryReadRequest(request, ElevatedHelperProtocol.CreateNonce(), issuedAt, out _, out string nonceRejection));
        Assert.AreEqual("request-authority-not-allowlisted", nonceRejection);
        Assert.IsFalse(ElevatedHelperProtocol.TryReadRequest(request, nonce, issuedAt.Add(ElevatedHelperProtocol.RequestLifetime).AddSeconds(1), out _, out string expiryRejection));
        Assert.AreEqual("request-expired", expiryRejection);
    }

    [TestMethod]
    public void ResponseIsBoundToJobAndNonceAndSanitized()
    {
        Guid jobId = Guid.NewGuid();
        string nonce = ElevatedHelperProtocol.CreateNonce();
        byte[] response = ElevatedHelperProtocol.CreateResponse(
            jobId,
            nonce,
            ElevatedHelperOutcome.ProviderFailed,
            23,
            "token=abc C:\\Users\\Alice\\secret.txt");

        Assert.IsTrue(ElevatedHelperProtocol.TryReadResponse(response, jobId, nonce, out ElevatedHelperResult result, out _));
        Assert.AreEqual(ElevatedHelperOutcome.ProviderFailed, result.Outcome);
        Assert.AreEqual(23, result.ExitCode);
        StringAssert.Contains(result.SanitizedOutput, "token=[redacted]");
        StringAssert.Contains(result.SanitizedOutput, "[path]");
        Assert.IsFalse(ElevatedHelperProtocol.TryReadResponse(response, Guid.NewGuid(), nonce, out _, out _));
        Assert.IsFalse(ElevatedHelperProtocol.TryReadResponse(response, jobId, ElevatedHelperProtocol.CreateNonce(), out _, out _));
    }

    [TestMethod]
    public void BootstrapAcceptsOnlyProtocolPidPipeAndNonce()
    {
        string nonce = ElevatedHelperProtocol.CreateNonce();
        string pipe = $"pcsteward-elevated-{Guid.NewGuid():N}";
        string[] valid = ["--protocol", "1", "--desktop-pid", "42", "--pipe", pipe, "--nonce", nonce];
        string[] withProvider = [.. valid, "--provider", "WinGet"];
        string[] withPath = [.. valid, "--working-directory", "C:\\Windows"];
        string[] duplicate = ["--protocol", "1", "--desktop-pid", "42", "--pipe", pipe, "--pipe", pipe];

        Assert.IsTrue(HelperBootstrap.TryParse(valid, out HelperBootstrap? bootstrap));
        Assert.AreEqual(42, bootstrap!.DesktopProcessId);
        Assert.IsFalse(HelperBootstrap.TryParse(withProvider, out _));
        Assert.IsFalse(HelperBootstrap.TryParse(withPath, out _));
        Assert.IsFalse(HelperBootstrap.TryParse(duplicate, out _));
    }

    [TestMethod]
    public async Task RestrictedPipeUsesCurrentUserAndSystemAclAndBindsEndpointPids()
    {
        string sid = WindowsIdentity.GetCurrent().User!.Value;
        string sddl = RestrictedNamedPipe.BuildSecurityDescriptorSddl(sid);
        StringAssert.Contains(sddl, sid);
        StringAssert.Contains(sddl, ";;;SY");
        string name = $"pcsteward-elevated-{Guid.NewGuid():N}";

        using NamedPipeServerStream server = RestrictedNamedPipe.Create(name, sid);
        using NamedPipeClientStream client = new(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        Task waiting = server.WaitForConnectionAsync();
        await client.ConnectAsync(5000);
        await waiting;

        Assert.IsTrue(RestrictedNamedPipe.TryGetClientProcessId(server, out int clientPid));
        Assert.IsTrue(RestrictedNamedPipe.TryGetServerProcessId(client, out int serverPid));
        Assert.AreEqual(Environment.ProcessId, clientPid);
        Assert.AreEqual(Environment.ProcessId, serverPid);
    }

    [TestMethod]
    public void PublicElevatedRunnerSurfaceCannotAcceptExecutionAuthorityStrings()
    {
        Type[] parameters = typeof(ElevatedUpdateRunner).GetMethod(nameof(ElevatedUpdateRunner.RunAsync))!
            .GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        Assert.IsFalse(parameters.Contains(typeof(string)));
        Assert.IsFalse(parameters.Any(type => typeof(IEnumerable<string>).IsAssignableFrom(type)));
        Assert.IsFalse(parameters.Any(type => typeof(IDictionary<string, string>).IsAssignableFrom(type)));
        Assert.HasCount(1, typeof(ElevatedUpdateRunner).GetConstructors());
        CollectionAssert.AreEqual(Type.EmptyTypes, typeof(ElevatedUpdateRunner).GetConstructors().Single().GetParameters().Select(parameter => parameter.ParameterType).ToArray());
    }

    private static ImmutableUpdateJob ApprovedJob(UpdateScope scope) =>
        new UpdateJobReview(Guid.NewGuid(), Guid.NewGuid(), Selection(scope)).Confirm(DateTimeOffset.UtcNow);

    private static UpdateJobExecutionEvidence FreshEvidence(ImmutableUpdateJob job) => new(job.Selection, true, true);

    private static UpdateJobSelection Selection(UpdateScope scope, string candidate = "1.1.0") =>
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

    private sealed class RecordingTransport(ElevatedHelperResult result) : IElevatedHelperTransport
    {
        public List<ImmutableUpdateJob> Jobs { get; } = [];

        public Task<ElevatedHelperResult> ExecuteAsync(ImmutableUpdateJob job, CancellationToken cancellationToken)
        {
            Jobs.Add(job);
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(result);
        }
    }

    private sealed class RecordingProcess : IWingetProcess
    {
        public List<IReadOnlyList<string>> Arguments { get; } = [];

        public Task<WingetProcessResult> RunAsync(
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            Arguments.Add(arguments);
            return Task.FromResult(new WingetProcessResult(0, "no-op-fixture", string.Empty, false));
        }
    }
}
