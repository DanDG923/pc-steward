using System.Reflection;
using System.Text;
using PCSteward.Domain;
using PCSteward.Execution;
using PCSteward.Provider.WinGet;

namespace PCSteward.Security.FixtureTests;

/// <summary>Protocol-1 compatibility, not a helper progress capability or endpoint-authentication host test.</summary>
[TestClass]
[TestCategory("FR-UPD-14")]
[TestCategory("FR-UPD-02")]
[TestCategory("FR-UPD-04")]
[TestCategory("TM-04")]
public sealed class Task49ProtocolCompatibilityTests
{
    private const string Nonce = "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF";
    private const string OtherNonce = "FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF";
    private const string Digest = "498AADB64349DD5CF67F00B629E23CB72D900884310699F1E9A5D62510A5FEAD";
    private static readonly Guid JobId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid CorrelationId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly DateTimeOffset ApprovedAt = new(2026, 10, 29, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset IssuedAt = ApprovedAt.AddSeconds(1);
    private static readonly string[] MachineArguments = ["upgrade", "--id", "Contoso.Widget", "--version", "1.1.0", "--source", "winget",
        "--exact", "--scope", "machine", "--architecture", "x64", "--disable-interactivity"];

    // Hand-pinned UTF-8 literals from the 607271d protocol contract. Default JSON encoding escapes '+'.
    // Expected bytes are never obtained from the serializer under test.
    private const string Request = """
        {"protocolVersion":"1","jobId":"11111111-2222-3333-4444-555555555555","correlationId":"aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee","operation":"UpdatePackage","provider":"WinGet","source":"WinGet","stableProductKey":"winget:Contoso.Widget","packageId":"Contoso.Widget","installedVersion":"1.0.0","targetVersion":"1.1.0","scope":"Machine","architecture":"X64","channel":"Stable","mappingEvidenceVersion":"mapping-v1","approvedAtUtc":"2026-10-29T10:00:00.0000000\u002B00:00","digest":"498AADB64349DD5CF67F00B629E23CB72D900884310699F1E9A5D62510A5FEAD","nonce":"0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF","issuedAtUtc":"2026-10-29T10:00:01.0000000\u002B00:00"}
        """;
    private const string CompletedResponse = """
        {"protocolVersion":"1","jobId":"11111111-2222-3333-4444-555555555555","nonce":"0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF","outcome":"Completed","exitCode":0,"sanitizedOutput":"completed"}
        """;
    private const string FailedResponse = """
        {"protocolVersion":"1","jobId":"11111111-2222-3333-4444-555555555555","nonce":"0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF","outcome":"Failed","exitCode":null,"sanitizedOutput":"helper-failed"}
        """;

    [TestMethod]
    public void RequestMatchesGoldenBytesAndRestoresEveryReviewedField()
    {
        ImmutableUpdateJob original = Job();
        Assert.AreEqual("1", typeof(ElevatedHelperProtocol).GetField("Version", BindingFlags.NonPublic | BindingFlags.Static)!.GetRawConstantValue());
        Assert.AreEqual(16_384, typeof(ElevatedHelperProtocol).GetField("MaximumMessageBytes", BindingFlags.NonPublic | BindingFlags.Static)!.GetRawConstantValue());
        Assert.AreEqual(TimeSpan.FromMinutes(2), ElevatedHelperProtocol.RequestLifetime);
        Assert.AreEqual(Digest, original.Digest);
        CollectionAssert.AreEqual(Bytes(Request), ElevatedHelperProtocol.CreateRequest(original, Nonce, IssuedAt));
        Assert.IsTrue(ElevatedHelperProtocol.TryReadRequest(Bytes(Request), Nonce, IssuedAt, out ImmutableUpdateJob? restored, out string rejection), rejection);
        Assert.IsNotNull(restored);
        Assert.AreEqual(JobId, restored.Id);
        Assert.AreEqual(CorrelationId, restored.CorrelationId);
        Assert.AreEqual(ApprovedAt, restored.ApprovedAt);
        Assert.AreEqual(Digest, restored.Digest);
        Assert.IsTrue(original.Selection.EqualsEvidence(restored.Selection));
    }

    [TestMethod]
    [TestCategory("FR-UPD-07")]
    [TestCategory("FR-UPD-09")]
    public void ResultsMatchGoldenBytesAndRetainOutcomeExitCodeAndDiagnosticMeanings()
    {
        CollectionAssert.AreEqual(Bytes(CompletedResponse), ElevatedHelperProtocol.CreateResponse(JobId, Nonce, ElevatedHelperOutcome.Completed, 0, "completed"));
        CollectionAssert.AreEqual(Bytes(FailedResponse), ElevatedHelperProtocol.CreateResponse(JobId, Nonce, ElevatedHelperOutcome.Failed, null, "helper-failed"));
        Assert.IsTrue(ElevatedHelperProtocol.TryReadResponse(Bytes(CompletedResponse), JobId, Nonce, out ElevatedHelperResult completed, out string rejection), rejection);
        Assert.AreEqual(ElevatedHelperOutcome.Completed, completed.Outcome);
        Assert.AreEqual(0, completed.ExitCode);
        Assert.AreEqual("completed", completed.SanitizedOutput);
        Assert.IsNull(completed.OwnedCompletion);
        Assert.IsTrue(ElevatedHelperProtocol.TryReadResponse(Bytes(FailedResponse), JobId, Nonce, out ElevatedHelperResult failed, out rejection), rejection);
        Assert.AreEqual(ElevatedHelperOutcome.Failed, failed.Outcome);
        Assert.IsNull(failed.ExitCode);
        Assert.AreEqual("helper-failed", failed.SanitizedOutput);
    }

    [TestMethod]
    public async Task LocalOwnedCompletionIsRelayedButNeverChangesResultOrRequestBytes()
    {
        TaskCompletionSource ownership = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ElevatedHelperResult local = new(ElevatedHelperOutcome.Completed, 0, "completed", ownership.Task);
        ImmutableUpdateJob job = Job();
        UpdateExecutionResult projected = await new ElevatedUpdateRunner(new FixtureTransport(local))
            .RunAsync(job, new(job.Selection, true, true), CancellationToken.None);
        Assert.AreSame(ownership.Task, projected.OwnedCompletion);
        Assert.AreEqual(UpdateExecutionStatus.AwaitingVerification, projected.Status);
        Assert.IsFalse(ownership.Task.IsCompleted);
        AssertWireUnchanged(local, job);
        ownership.SetResult();
        await ownership.Task;
        AssertWireUnchanged(local, job);
    }

    [TestMethod]
    [DataRow("\"progress\":{}")]
    [DataRow("\"phase\":\"ProcessStarted\"")]
    [DataRow("\"percent\":100")]
    [DataRow("\"percentage\":100")]
    [DataRow("\"ownedCompletion\":true")]
    [DataRow("\"OwnedCompletion\":null")]
    [DataRow("\"task\":{}")]
    [DataRow("\"executionLifetime\":{}")]
    [DataRow("\"sequence\":1")]
    [DataRow("\"completion\":\"Recorded\"")]
    [DataRow("\"finalStatus\":\"Updated\"")]
    [DataRow("\"degraded\":false")]
    public void BothClosedSchemasRejectLocalProgressOrOwnershipFields(string extra)
    {
        RejectRequest(Append(Request, extra), "message-property-unknown-or-duplicate");
        RejectResponse(Append(CompletedResponse, extra), "message-property-unknown-or-duplicate");
    }

    [TestMethod]
    public void CorrelationIntegrityNonceAndOneShotBindingsRemainRequired()
    {
        RejectRequest(Request.Replace(JobId.ToString("D"), "99999999-2222-3333-4444-555555555555", StringComparison.Ordinal), "request-job-integrity-invalid");
        RejectRequest(Request.Replace(CorrelationId.ToString("D"), "99999999-bbbb-cccc-dddd-eeeeeeeeeeee", StringComparison.Ordinal), "request-job-integrity-invalid");
        RejectRequest(Request.Replace(Nonce, OtherNonce, StringComparison.Ordinal), "request-authority-not-allowlisted");
        Assert.IsFalse(ElevatedHelperProtocol.TryReadResponse(Bytes(CompletedResponse), CorrelationId, Nonce, out _, out string rejection));
        Assert.AreEqual("response-field-validation-failed", rejection);
        Assert.IsFalse(ElevatedHelperProtocol.TryReadResponse(Bytes(CompletedResponse), JobId, OtherNonce, out _, out rejection));
        Assert.AreEqual("response-field-validation-failed", rejection);
        OneShotHelperRequestValidator validator = new(Nonce, () => IssuedAt);
        Assert.IsTrue(validator.TryAccept(Bytes(Request), out _, out rejection), rejection);
        Assert.IsFalse(validator.TryAccept(Bytes(Request), out _, out rejection));
        Assert.AreEqual("duplicate-request", rejection);
    }

    [TestMethod]
    [DataRow(-15, true)]
    [DataRow(-16, false)]
    [DataRow(120, true)]
    [DataRow(121, false)]
    public void RequestFreshnessRetainsExistingSkewAndTwoMinuteBound(int seconds, bool accepted)
    {
        Assert.AreEqual(accepted, ElevatedHelperProtocol.TryReadRequest(Bytes(Request), Nonce, IssuedAt.AddSeconds(seconds), out _, out string rejection));
        Assert.AreEqual(accepted ? string.Empty : "request-expired", rejection);
    }

    [TestMethod]
    public void BothSchemasRejectDuplicateMissingMalformedEmptyAndOversizeMessages()
    {
        RejectRequest(Append(Request, "\"jobId\":\"11111111-2222-3333-4444-555555555555\""), "message-property-unknown-or-duplicate");
        RejectResponse(Append(CompletedResponse, "\"exitCode\":0"), "message-property-unknown-or-duplicate");
        RejectRequest(Request.Replace("\"operation\":\"UpdatePackage\",", string.Empty, StringComparison.Ordinal), "message-property-missing");
        RejectResponse(CompletedResponse.Replace("\"exitCode\":0,", string.Empty, StringComparison.Ordinal), "message-property-missing");
        RejectRequest("{", "message-json-invalid");
        RejectResponse("{", "message-json-invalid");
        RejectRequest(string.Empty, "message-size-invalid");
        RejectResponse(string.Empty, "message-size-invalid");
        foreach (int length in new[] { 16_384, 16_385 })
        {
            string request = Request.PadRight(length);
            string response = CompletedResponse.PadRight(length);
            Assert.AreEqual(length == 16_384, ElevatedHelperProtocol.TryReadRequest(Bytes(request), Nonce, IssuedAt, out _, out string requestRejection));
            Assert.AreEqual(length == 16_384, ElevatedHelperProtocol.TryReadResponse(Bytes(response), JobId, Nonce, out _, out string responseRejection));
            Assert.AreEqual(length == 16_384 ? string.Empty : "message-size-invalid", requestRejection);
            Assert.AreEqual(length == 16_384 ? string.Empty : "message-size-invalid", responseRejection);
        }
    }

    [TestMethod]
    [DataRow("\"protocolVersion\":\"1\"", "\"protocolVersion\":\"2\"")]
    [DataRow("UpdatePackage", "InstallPackage")]
    [DataRow("UpdatePackage", "UninstallPackage")]
    [DataRow("\"scope\":\"Machine\"", "\"scope\":\"User\"")]
    [DataRow("\"provider\":\"WinGet\"", "\"provider\":\"Chocolatey\"")]
    public void ProgressDoesNotWidenRequestOperationOrProviderAuthority(string before, string after) =>
        RejectRequest(Request.Replace(before, after, StringComparison.Ordinal), "request-authority-not-allowlisted");

    [TestMethod]
    [DataRow("\"protocolVersion\":\"1\"", "\"protocolVersion\":\"2\"", "response-field-validation-failed")]
    [DataRow("\"outcome\":\"Completed\"", "\"outcome\":\"Progress\"", "response-field-validation-failed")]
    [DataRow("\"exitCode\":0", "\"exitCode\":\"0\"", "response-exit-code-invalid")]
    public void ResponseFieldMeaningsRemainClosed(string before, string after, string rejection) =>
        RejectResponse(CompletedResponse.Replace(before, after, StringComparison.Ordinal), rejection);

    [TestMethod]
    [TestCategory("FR-UPD-03")]
    public void RequestSignatureAndCompiledMachineArgumentsRemainUnchanged()
    {
        MethodInfo[] methods = typeof(ElevatedHelperProtocol).GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Where(method => method.Name == "CreateRequest").ToArray();
        Assert.HasCount(1, methods);
        Assert.AreEqual(typeof(byte[]), methods[0].ReturnType);
        CollectionAssert.AreEqual(new[] { typeof(ImmutableUpdateJob), typeof(string), typeof(DateTimeOffset) },
            methods[0].GetParameters().Select(parameter => parameter.ParameterType).ToArray());
        CollectionAssert.AreEqual(MachineArguments, WingetMachineUpdateExecutor.BuildUpdateArguments(Job()).ToArray());
    }

    private static void AssertWireUnchanged(ElevatedHelperResult local, ImmutableUpdateJob job)
    {
        CollectionAssert.AreEqual(Bytes(CompletedResponse), ElevatedHelperProtocol.CreateResponse(JobId, Nonce, local.Outcome, local.ExitCode, local.SanitizedOutput));
        CollectionAssert.AreEqual(Bytes(Request), ElevatedHelperProtocol.CreateRequest(job, Nonce, IssuedAt));
        Assert.IsTrue(ElevatedHelperProtocol.TryReadResponse(Bytes(CompletedResponse), JobId, Nonce, out ElevatedHelperResult decoded, out string rejection), rejection);
        Assert.IsNull(decoded.OwnedCompletion);
    }

    private static void RejectRequest(string json, string expected)
    {
        Assert.IsFalse(ElevatedHelperProtocol.TryReadRequest(Bytes(json), Nonce, IssuedAt, out ImmutableUpdateJob? job, out string rejection));
        Assert.IsNull(job);
        Assert.AreEqual(expected, rejection);
    }

    private static void RejectResponse(string json, string expected)
    {
        Assert.IsFalse(ElevatedHelperProtocol.TryReadResponse(Bytes(json), JobId, Nonce, out ElevatedHelperResult result, out string rejection));
        Assert.AreEqual(ElevatedHelperOutcome.Failed, result.Outcome);
        Assert.AreEqual(expected, rejection);
    }

    private static byte[] Bytes(string value) => Encoding.UTF8.GetBytes(value);
    private static string Append(string json, string extra) => json[..^1] + "," + extra + "}";
    private static ImmutableUpdateJob Job() => new UpdateJobReview(JobId, CorrelationId, new(
        "winget:Contoso.Widget", UpdateProvider.WinGet, UpdateSource.WinGet, "Contoso.Widget", "1.0.0", "1.1.0",
        UpdateScope.Machine, UpdateArchitecture.X64, UpdateChannel.Stable, "mapping-v1")).Confirm(ApprovedAt);

    private sealed class FixtureTransport(ElevatedHelperResult result) : IElevatedHelperTransport
    {
        public Task<ElevatedHelperResult> ExecuteAsync(ImmutableUpdateJob job, CancellationToken cancellationToken) => Task.FromResult(result);
    }
}
