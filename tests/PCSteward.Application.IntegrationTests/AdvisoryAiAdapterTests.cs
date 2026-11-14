using System.Net;
using System.Text;
using PCSteward.AdvisoryAi.Windows;
using PCSteward.Domain;

namespace PCSteward.Application.IntegrationTests;

/// <summary>
/// FR-AI-03/04 (ADR-028 Q1/F11/Q8; Task 67 stage 3): the OpenAI SDK transport and the Credential Manager adapter, proven with a
/// counting fake network and a fake Credential Manager only. No key is real and nothing reaches the network.
/// </summary>
[TestClass]
public sealed class AdvisoryAiAdapterTests
{
    private const string Key = "sk-synthetic-not-a-real-key-0123456789";
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);
    private static readonly string[] ProfileHeaders = ["Authorization", "Accept", "User-Agent", "Content-Type", "Content-Length"];
    private static readonly string[] OnlyTarget = [IAdvisoryAiCredentialStore.Target];
    private static readonly AdvisoryAiModelProfile Model = new("synthetic-model-2026-01-01", 1, 128_000, 16_384, 64, true, 64, 65_536, "default");

    [TestMethod]
    [TestCategory("FR-AI-04")]
    public async Task TheExactApprovedBytesAreSentOnceWithOnlyTheClosedHeaderProfile()
    {
        AdvisoryAiApproval approval = Approval();
        FakeNetwork network = new(_ => Respond(HttpStatusCode.OK, "{\"object\":\"response\"}"));
        using AdvisoryAiApiKey key = NewKey();

        AdvisoryAiTransportResult result = await Transport(network).SendOnceAsync(approval, key, CancellationToken.None);

        Assert.AreEqual(AdvisoryAiTransportOutcome.ResponseReceived, result.Outcome);
        Assert.AreEqual(200, result.HttpStatus);
        Assert.AreEqual("{\"object\":\"response\"}", Encoding.UTF8.GetString(result.Body.AsSpan()));
        Assert.AreEqual(1, network.Calls);
        SeenRequest seen = network.Requests.Single();
        Assert.AreEqual("POST", seen.Method);
        Assert.AreEqual(AdvisoryAiRoute.Endpoint.AbsoluteUri, seen.Uri);
        CollectionAssert.AreEqual(approval.Body.ToArray(), seen.Body, "the SDK sent the approved bytes, not a body of its own");
        CollectionAssert.AreEquivalent(ProfileHeaders, seen.Headers.Keys.ToArray(),
            "nothing the SDK adds (its own user agent, request IDs, tracing) leaves the PC");
        Assert.AreEqual("Bearer " + Key, seen.Headers["Authorization"]);
        Assert.AreEqual(AdvisoryAiHeaderProfile.Accept, seen.Headers["Accept"]);
        Assert.AreEqual(AdvisoryAiHeaderProfile.UserAgent, seen.Headers["User-Agent"]);
        Assert.AreEqual(AdvisoryAiHeaderProfile.ContentType, seen.Headers["Content-Type"]);
        Assert.AreEqual(approval.Body.Length.ToString(System.Globalization.CultureInfo.InvariantCulture), seen.Headers["Content-Length"]);
    }

    [TestMethod]
    [TestCategory("FR-AI-04")]
    [DataRow(HttpStatusCode.TooManyRequests)]
    [DataRow(HttpStatusCode.InternalServerError)]
    [DataRow(HttpStatusCode.BadGateway)]
    [DataRow(HttpStatusCode.ServiceUnavailable)]
    [DataRow(HttpStatusCode.RequestTimeout)]
    public async Task AnErrorStatusIsReturnedAfterOneAttemptAndNeverRetried(HttpStatusCode status)
    {
        FakeNetwork network = new(_ => Respond(status, "{\"error\":{\"message\":\"x\"}}", retryAfter: true));
        using AdvisoryAiApiKey key = NewKey();

        AdvisoryAiTransportResult result = await Transport(network).SendOnceAsync(Approval(), key, CancellationToken.None);

        Assert.AreEqual(1, network.Calls, "zero automatic retries, including the SDK's own");
        Assert.AreEqual(AdvisoryAiTransportOutcome.ResponseReceived, result.Outcome);
        Assert.AreEqual((int)status, result.HttpStatus);
    }

    [TestMethod]
    [TestCategory("FR-AI-04")]
    [DataRow(HttpStatusCode.MovedPermanently, "https://elsewhere.example/v1/responses")]
    [DataRow(HttpStatusCode.Found, "https://elsewhere.example/v1/responses")]
    [DataRow(HttpStatusCode.TemporaryRedirect, "https://api.openai.com/v1/other")]
    [DataRow(HttpStatusCode.PermanentRedirect, "https://api.openai.com/v1/other")]
    [DataRow(HttpStatusCode.SeeOther, "/v1/responses")]
    public async Task ARedirectIsNotFollowedEvenToTheSameHost(HttpStatusCode status, string location)
    {
        FakeNetwork network = new(_ =>
        {
            HttpResponseMessage response = Respond(status, "");
            response.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
            return response;
        });
        using AdvisoryAiApiKey key = NewKey();

        AdvisoryAiTransportResult result = await Transport(network).SendOnceAsync(Approval(), key, CancellationToken.None);

        Assert.AreEqual(AdvisoryAiTransportOutcome.Redirected, result.Outcome);
        Assert.AreEqual((int)status, result.HttpStatus);
        Assert.AreEqual(1, network.Calls);
        Assert.IsTrue(result.Body.IsEmpty);
    }

    [TestMethod]
    [TestCategory("FR-AI-04")]
    public void TheNetworkHandlerFollowsNoRedirectAndHasTheConnectDeadline()
    {
        using SocketsHttpHandler handler = OpenAiResponsesTransport.CreateNetworkHandler();
        Assert.IsFalse(handler.AllowAutoRedirect);
        Assert.AreEqual(AdvisoryAiResourcePolicy.ConnectDeadline, handler.ConnectTimeout);
        Assert.AreEqual(DecompressionMethods.None, handler.AutomaticDecompression);
        Assert.IsFalse(handler.UseCookies);
        Assert.IsNull(handler.Credentials);
        Assert.IsNull(handler.SslOptions.RemoteCertificateValidationCallback, "certificate validation is never replaced");
    }

    [TestMethod]
    [TestCategory("FR-AI-04")]
    [DataRow(true)]
    [DataRow(false)]
    public async Task ABodyLargerThanTheAdmittedSizeIsNotRead(bool declaresLength)
    {
        AdvisoryAiApproval approval = Approval();
        long admitted = AdvisoryAiResponseReader.AdmittedBytes(approval);
        FakeNetwork network = new(_ => Respond(HttpStatusCode.OK, new string('a', (int)admitted + 1), declaresLength));
        using AdvisoryAiApiKey key = NewKey();

        AdvisoryAiTransportResult result = await Transport(network).SendOnceAsync(approval, key, CancellationToken.None);

        Assert.AreEqual(AdvisoryAiTransportOutcome.ResponseTooLarge, result.Outcome);
        Assert.IsTrue(result.Body.IsEmpty);

        FakeNetwork exact = new(_ => Respond(HttpStatusCode.OK, new string('a', (int)admitted), declaresLength));
        AdvisoryAiTransportResult atBound = await Transport(exact).SendOnceAsync(approval, key, CancellationToken.None);
        Assert.AreEqual(AdvisoryAiTransportOutcome.ResponseReceived, atBound.Outcome, "exactly the admitted size is read");
        Assert.AreEqual(admitted, atBound.Body.Length);
    }

    [TestMethod]
    [TestCategory("FR-AI-04")]
    public async Task AnErrorBodyLargerThanTheAdmittedSizeIsNotBufferedEither()
    {
        AdvisoryAiApproval approval = Approval();
        long admitted = AdvisoryAiResponseReader.AdmittedBytes(approval);
        FakeNetwork network = new(_ => Respond(HttpStatusCode.InternalServerError, new string('e', (int)admitted * 2), declaresLength: false));
        using AdvisoryAiApiKey key = NewKey();

        AdvisoryAiTransportResult result = await Transport(network).SendOnceAsync(approval, key, CancellationToken.None);

        Assert.AreEqual(AdvisoryAiTransportOutcome.ResponseTooLarge, result.Outcome);
        Assert.AreEqual(1, network.Calls);
    }

    [TestMethod]
    [TestCategory("FR-AI-04")]
    public async Task ConnectionAndOtherFailuresAreClassifiedWithoutText()
    {
        using AdvisoryAiApiKey key = NewKey();
        FakeNetwork refused = new(_ => throw new HttpRequestException(HttpRequestError.ConnectionError, "connect failed " + Key));
        AdvisoryAiTransportResult connect = await Transport(refused).SendOnceAsync(Approval(), key, CancellationToken.None);
        Assert.AreEqual(AdvisoryAiTransportOutcome.ConnectFailed, connect.Outcome);
        Assert.AreEqual(1, refused.Calls);

        FakeNetwork tls = new(_ => throw new HttpRequestException(HttpRequestError.SecureConnectionError, "bad certificate"));
        Assert.AreEqual(AdvisoryAiTransportOutcome.ConnectFailed, (await Transport(tls).SendOnceAsync(Approval(), key, CancellationToken.None)).Outcome);

        FakeNetwork broken = new(_ => throw new IOException("reset " + Key));
        AdvisoryAiTransportResult failed = await Transport(broken).SendOnceAsync(Approval(), key, CancellationToken.None);
        Assert.AreEqual(AdvisoryAiTransportOutcome.Failed, failed.Outcome);
        Assert.AreEqual(1, broken.Calls, "a failure after sending is not retried");
        Assert.IsFalse(failed.ToString().Contains(Key, StringComparison.Ordinal), "no exception text or key is carried");
    }

    [TestMethod]
    [TestCategory("FR-AI-04")]
    public async Task CancellationBeforeSendingSendsNothingAndDuringSendingIsCancelled()
    {
        using AdvisoryAiApiKey key = NewKey();
        FakeNetwork network = new(_ => Respond(HttpStatusCode.OK, "{}"));
        using CancellationTokenSource before = new();
        await before.CancelAsync();
        Assert.AreEqual(AdvisoryAiTransportOutcome.NotSent, (await Transport(network).SendOnceAsync(Approval(), key, before.Token)).Outcome);
        Assert.AreEqual(0, network.Calls);

        using CancellationTokenSource during = new();
        FakeNetwork slow = new(_ => Respond(HttpStatusCode.OK, "{}"), onSend: () => during.Cancel(), hang: true);
        Assert.AreEqual(AdvisoryAiTransportOutcome.Cancelled, (await Transport(slow).SendOnceAsync(Approval(), key, during.Token)).Outcome);
        Assert.AreEqual(1, slow.Calls);
    }

    [TestMethod]
    [TestCategory("FR-AI-04")]
    public async Task TheOverallDeadlineEndsAHungRequestAsTimedOut()
    {
        using AdvisoryAiApiKey key = NewKey();
        FakeNetwork hung = new(_ => Respond(HttpStatusCode.OK, "{}"), hang: true);
        AdvisoryAiTransportResult result = await new OpenAiResponsesTransport(() => hung, TimeSpan.FromMilliseconds(300)).SendOnceAsync(Approval(), key, CancellationToken.None);
        Assert.AreEqual(AdvisoryAiTransportOutcome.TimedOut, result.Outcome);
        Assert.AreEqual(1, hung.Calls);
    }

    [TestMethod]
    [TestCategory("FR-AI-03")]
    public async Task TheKeyIsSavedOnlyUnderTheExactTargetAndCheckedByReadingItBack()
    {
        FakeCredentialManager manager = new();
        WindowsCredentialManagerStore store = new(manager);
        using AdvisoryAiApiKey key = NewKey();

        Assert.AreEqual(AdvisoryAiCredentialPresence.Absent, await store.CheckPresenceAsync(CancellationToken.None));
        Assert.AreEqual(AdvisoryAiCredentialWrite.Saved, await store.SaveAsync(key, CancellationToken.None));
        CollectionAssert.AreEqual(Encoding.ASCII.GetBytes(Key), manager.Stored);
        Assert.AreEqual(AdvisoryAiCredentialPresence.Present, await store.CheckPresenceAsync(CancellationToken.None));
        using (AdvisoryAiApiKey? read = await store.ReadForDispatchAsync(CancellationToken.None))
            Assert.AreEqual(Key, new string(read!.Reveal()));
        CollectionAssert.AreEqual(OnlyTarget, manager.Targets.Distinct().ToArray(), "no other credential is ever named");
    }

    [TestMethod]
    [TestCategory("FR-AI-03")]
    public async Task AFailedOrUnverifiedSaveIsReportedAsFailed()
    {
        using AdvisoryAiApiKey key = NewKey();
        FakeCredentialManager denied = new() { WriteError = 5 };
        Assert.AreEqual(AdvisoryAiCredentialWrite.Failed, await new WindowsCredentialManagerStore(denied).SaveAsync(key, CancellationToken.None));

        FakeCredentialManager lost = new() { DropWrites = true };
        Assert.AreEqual(AdvisoryAiCredentialWrite.Failed, await new WindowsCredentialManagerStore(lost).SaveAsync(key, CancellationToken.None), "not saved unless it reads back");
    }

    [TestMethod]
    [TestCategory("FR-AI-03")]
    [DataRow(1312, DisplayName = "no logon session")]
    [DataRow(5, DisplayName = "access denied")]
    [DataRow(1004, DisplayName = "invalid flags")]
    public async Task AnUnreadableCredentialIsUnknownAndNeverAKey(int error)
    {
        FakeCredentialManager manager = new() { Stored = Encoding.ASCII.GetBytes(Key), ReadError = error };
        WindowsCredentialManagerStore store = new(manager);
        Assert.AreEqual(AdvisoryAiCredentialPresence.Unknown, await store.CheckPresenceAsync(CancellationToken.None));
        Assert.IsNull(await store.ReadForDispatchAsync(CancellationToken.None));
    }

    [TestMethod]
    [TestCategory("FR-AI-03")]
    [DataRow("", DisplayName = "empty")]
    [DataRow("sk with space", DisplayName = "whitespace")]
    [DataRow("sk-é", DisplayName = "not ASCII")]
    public async Task AStoredValueThatIsNotAValidKeyIsNeverUsed(string stored)
    {
        FakeCredentialManager manager = new() { Stored = Encoding.Latin1.GetBytes(stored) };
        WindowsCredentialManagerStore store = new(manager);
        Assert.AreEqual(AdvisoryAiCredentialPresence.Unknown, await store.CheckPresenceAsync(CancellationToken.None));
        Assert.IsNull(await store.ReadForDispatchAsync(CancellationToken.None));
    }

    [TestMethod]
    [TestCategory("FR-AI-03")]
    public async Task RemovalIsReportedOnlyWhenAFreshReadFindsNothing()
    {
        FakeCredentialManager present = new() { Stored = Encoding.ASCII.GetBytes(Key) };
        Assert.AreEqual(AdvisoryAiCredentialRemoval.Removed, await new WindowsCredentialManagerStore(present).RemoveAndVerifyAsync(CancellationToken.None));
        Assert.IsNull(present.Stored);

        Assert.AreEqual(AdvisoryAiCredentialRemoval.AlreadyAbsent, await new WindowsCredentialManagerStore(new FakeCredentialManager()).RemoveAndVerifyAsync(CancellationToken.None));

        FakeCredentialManager denied = new() { Stored = Encoding.ASCII.GetBytes(Key), DeleteError = 5 };
        Assert.AreEqual(AdvisoryAiCredentialRemoval.Incomplete, await new WindowsCredentialManagerStore(denied).RemoveAndVerifyAsync(CancellationToken.None));

        FakeCredentialManager noSession = new() { Stored = Encoding.ASCII.GetBytes(Key), DeleteError = 1312 };
        Assert.AreEqual(AdvisoryAiCredentialRemoval.Incomplete, await new WindowsCredentialManagerStore(noSession).RemoveAndVerifyAsync(CancellationToken.None));

        FakeCredentialManager sticky = new() { Stored = Encoding.ASCII.GetBytes(Key), IgnoreDeletes = true };
        Assert.AreEqual(AdvisoryAiCredentialRemoval.Incomplete, await new WindowsCredentialManagerStore(sticky).RemoveAndVerifyAsync(CancellationToken.None), "still there afterwards");

        FakeCredentialManager unverifiable = new() { Stored = Encoding.ASCII.GetBytes(Key), ReadError = 1312 };
        Assert.AreEqual(AdvisoryAiCredentialRemoval.Incomplete, await new WindowsCredentialManagerStore(unverifiable).RemoveAndVerifyAsync(CancellationToken.None), "absence could not be verified");
    }

    private static OpenAiResponsesTransport Transport(FakeNetwork network) => new(() => network, AdvisoryAiResourcePolicy.OverallDeadline);

    private static AdvisoryAiApiKey NewKey()
    {
        Assert.AreEqual(AdvisoryAiKeyProblem.None, AdvisoryAiApiKey.TryCreate(Key, out AdvisoryAiApiKey? key));
        return key!;
    }

    private static AdvisoryAiApproval Approval()
    {
        AdvisoryAiFailedUpdateFacts facts = new("Contoso Editor", "WinGet", "1.2.0", "1.3.0", "InstallerFailed", 1603,
            [new(Now.AddMinutes(-5), "InstallerFailed", 1603, "Installer failed with 1603.")]);
        AdvisoryAiInputAllowance allowance = AdvisoryAiInputAllowance.For(AdvisoryAiLimits.Defaults, Model, AdvisoryAiInstructions.Text);
        Assert.AreEqual(AdvisoryAiDraftProblem.None, AdvisoryAiFailedUpdateDraftBuilder.TryBuild(Guid.NewGuid(), facts, allowance, null, out AdvisoryAiEvidenceDraft? draft));
        Assert.AreEqual(AdvisoryAiPreviewProblem.None, AdvisoryAiRequestBuilder.TryBuild(draft!, Model, AdvisoryAiLimits.Defaults, out AdvisoryAiRequestPreview? preview));
        return AdvisoryAiApproval.Approve(preview!, new AdvisoryAiApprovalBinding(1, 1, 1, 1), Now);
    }

    private static HttpResponseMessage Respond(HttpStatusCode status, string body, bool declaresLength = true, bool retryAfter = false)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(body);
        HttpResponseMessage response = new(status)
        {
            Content = declaresLength ? new ByteArrayContent(bytes) : new StreamContent(new UnknownLengthStream(bytes))
        };
        response.Content.Headers.ContentType = new("application/json");
        if (retryAfter) response.Headers.TryAddWithoutValidation("Retry-After", "0");
        return response;
    }

    private sealed record SeenRequest(string Method, string? Uri, Dictionary<string, string> Headers, byte[] Body);

    /// <summary>Stands in for the network: counts calls and records exactly what would have been sent.</summary>
    private sealed class FakeNetwork(Func<HttpRequestMessage, HttpResponseMessage> respond, Action? onSend = null, bool hang = false) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        public List<SeenRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Dictionary<string, string> headers = new(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, IEnumerable<string>> header in request.Headers) headers[header.Key] = string.Join(", ", header.Value);
            byte[] body = [];
            if (request.Content is not null)
            {
                foreach (KeyValuePair<string, IEnumerable<string>> header in request.Content.Headers) headers[header.Key] = string.Join(", ", header.Value);
                body = await request.Content.ReadAsByteArrayAsync(cancellationToken);
            }
            Requests.Add(new(request.Method.Method, request.RequestUri?.AbsoluteUri, headers, body));
            onSend?.Invoke();
            if (hang) await Task.Delay(Timeout.Infinite, cancellationToken);
            return respond(request);
        }
    }

    private sealed class UnknownLengthStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }

    /// <summary>A fake Credential Manager holding at most the one entry, returning the documented Win32 errors.</summary>
    private sealed class FakeCredentialManager : ICredentialManagerApi
    {
        public byte[]? Stored { get; set; }
        public int WriteError { get; init; }
        public int ReadError { get; init; }
        public int DeleteError { get; init; }
        public bool DropWrites { get; init; }
        public bool IgnoreDeletes { get; init; }
        public List<string> Targets { get; } = [];

        public int Write(string target, byte[] blob)
        {
            Targets.Add(target);
            if (WriteError != 0) return WriteError;
            if (!DropWrites) Stored = [.. blob];
            return 0;
        }

        public int Read(string target, out byte[]? blob)
        {
            Targets.Add(target);
            blob = null;
            if (ReadError != 0) return ReadError;
            if (Stored is null) return 1168;
            blob = [.. Stored];
            return 0;
        }

        public int Delete(string target)
        {
            Targets.Add(target);
            if (DeleteError != 0) return DeleteError;
            if (Stored is null) return 1168;
            if (!IgnoreDeletes) Stored = null;
            return 0;
        }
    }
}
