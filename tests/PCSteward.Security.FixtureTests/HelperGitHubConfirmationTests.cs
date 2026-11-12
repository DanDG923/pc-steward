using System.Net;
using System.Net.Http.Headers;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using PCSteward.Execution;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// FR-UPD-15, TM-20, TM-23, TM-24; ADR-032 revision 2 §6 step 3 (owner decision 2): the elevated helper's own confirmation
/// of the CI anchor makes exactly two GETs to compiled addresses with no credentials, follows no redirect, refuses anything
/// but a bounded 200 answer, applies the shared run and artefact rules, and its handler uses no proxy, cookies or
/// credentials and anchors TLS only in the machine's root stores.
/// </summary>
[TestClass]
public sealed class HelperGitHubConfirmationTests
{
    private const string ZipSha256 = "AB12CD34EF56AB12CD34EF56AB12CD34EF56AB12CD34EF56AB12CD34EF56AB12";
    private const string RunAddress = "https://api.github.com/repos/DanDG923/pc-steward/actions/runs/4242";
    private const string ArtifactsAddress = RunAddress + "/artifacts?name=pc-steward-release-check";

    private static readonly string Run = "{\"id\":4242,\"path\":\".github/workflows/release-payload.yml\",\"event\":\"workflow_dispatch\"," +
        "\"head_branch\":\"phase/4-beta\",\"head_sha\":\"" + ReleaseCheckFixture.Commit + "\",\"status\":\"completed\",\"conclusion\":\"success\"," +
        "\"repository\":{\"id\":1331300752,\"full_name\":\"DanDG923/pc-steward\"},\"head_repository\":{\"id\":1331300752,\"full_name\":\"DanDG923/pc-steward\"}}";

    private static readonly string Artifacts = "{\"total_count\":1,\"artifacts\":[{\"name\":\"pc-steward-release-check\",\"digest\":\"sha256:" +
        ZipSha256.ToLowerInvariant() + "\",\"expired\":false}]}";

    [TestMethod]
    public async Task AGenuineRunIsConfirmedWithTwoFixedAnonymousRequests()
    {
        Handler handler = new();
        Assert.AreEqual(HelperGitHubVerdict.Confirmed, await Confirm(handler));
        CollectionAssert.AreEqual(new[] { RunAddress, ArtifactsAddress }, handler.Requests.Select(request => request.Address).ToArray());
        foreach (Handler.Seen request in handler.Requests)
        {
            Assert.AreEqual("GET", request.Method);
            Assert.IsFalse(request.HasAuthorization, "no credentials are sent");
            Assert.IsFalse(request.HasCookie, "no cookies are sent");
            Assert.AreEqual("PCSteward", request.UserAgent);
        }
    }

    [TestMethod]
    public async Task AnswersThatAreNotGenuineAreRefused()
    {
        Assert.AreEqual(HelperGitHubVerdict.RunNotGenuine, await Confirm(new Handler { RunBody = Run.Replace("success", "failure", StringComparison.Ordinal) }));
        Assert.AreEqual(HelperGitHubVerdict.RunNotGenuine, await Confirm(new Handler(), commit: new string('9', 40)));
        Assert.AreEqual(HelperGitHubVerdict.ArtifactMismatch, await Confirm(new Handler(), zip: new string('0', 64)));
        Assert.AreEqual(HelperGitHubVerdict.ArtifactGone, await Confirm(new Handler { ArtifactsBody = "{\"total_count\":0,\"artifacts\":[]}" }));
    }

    [TestMethod]
    public async Task RedirectsErrorsAndOversizedAnswersAreRefused()
    {
        // A redirect is never followed: the helper sees the 3xx itself and stops.
        Handler redirect = new() { RunStatus = HttpStatusCode.Found, RunLocation = "https://evil.example/run" };
        Assert.AreEqual(HelperGitHubVerdict.CouldNotReach, await Confirm(redirect));
        Assert.HasCount(1, redirect.Requests);
        Assert.AreEqual(HelperGitHubVerdict.CouldNotReach, await Confirm(new Handler { RunStatus = HttpStatusCode.Forbidden }));
        Assert.AreEqual(HelperGitHubVerdict.CouldNotReach, await Confirm(new Handler { ArtifactsStatus = HttpStatusCode.InternalServerError }));
        Assert.AreEqual(HelperGitHubVerdict.CouldNotReach, await Confirm(new Handler { RunBody = Run + new string(' ', ReleaseRunEvidence.MaximumAnswerBytes) }));
        Assert.AreEqual(HelperGitHubVerdict.CouldNotReach, await Confirm(new Handler { RunBody = "" }));
        Assert.AreEqual(HelperGitHubVerdict.CouldNotReach, await Confirm(new Handler { Throw = new HttpRequestException("TLS", new AuthenticationException("untrusted root")) }));
    }

    [TestMethod]
    public void TheAddressesAreBuiltFromCompiledPartsOnly()
    {
        Assert.AreEqual(RunAddress, HelperGitHubConfirmation.RunAddress(4242).AbsoluteUri);
        Assert.AreEqual(ArtifactsAddress, HelperGitHubConfirmation.ArtifactsAddress(4242).AbsoluteUri);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => HelperGitHubConfirmation.RunAddress(0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => HelperGitHubConfirmation.RunAddress(-1));
    }

    [TestMethod]
    public void TheChainPolicyTrustsOnlyTheGivenRootsWithOnlineRevocation()
    {
        X509ChainPolicy policy = HelperGitHubConfirmation.MachineRootPolicy([]);
        Assert.AreEqual(X509ChainTrustMode.CustomRootTrust, policy.TrustMode);
        Assert.AreEqual(X509RevocationMode.Online, policy.RevocationMode);
        Assert.AreEqual(X509RevocationFlag.EntireChain, policy.RevocationFlag);
        Assert.AreEqual(X509VerificationFlags.NoFlag, policy.VerificationFlags);
        Assert.IsEmpty(policy.CustomTrustStore);
        if (!OperatingSystem.IsWindows()) return;

        using SocketsHttpHandler handler = HelperGitHubConfirmation.CreateHandler();
        Assert.IsFalse(handler.UseProxy);
        Assert.IsNull(handler.Proxy);
        Assert.IsFalse(handler.UseCookies);
        Assert.IsNull(handler.Credentials);
        Assert.IsFalse(handler.AllowAutoRedirect);
        Assert.AreEqual(DecompressionMethods.None, handler.AutomaticDecompression);
        Assert.AreEqual("api.github.com", handler.SslOptions.TargetHost);
        Assert.AreEqual(SslProtocols.Tls12 | SslProtocols.Tls13, handler.SslOptions.EnabledSslProtocols);
        Assert.AreEqual(X509ChainTrustMode.CustomRootTrust, handler.SslOptions.CertificateChainPolicy!.TrustMode);
        Assert.IsNotEmpty(handler.SslOptions.CertificateChainPolicy.CustomTrustStore, "the machine's root stores are not empty");
    }

    private static async Task<HelperGitHubVerdict> Confirm(Handler handler, string? commit = null, string? zip = null)
    {
        using HelperGitHubConfirmation confirmation = new(handler);
        return await confirmation.ConfirmAsync(4242, commit ?? ReleaseCheckFixture.Commit, zip ?? ZipSha256, CancellationToken.None);
    }

    private sealed class Handler : HttpMessageHandler
    {
        internal sealed record Seen(string Method, string Address, bool HasAuthorization, bool HasCookie, string UserAgent);

        public List<Seen> Requests { get; } = [];
        public string RunBody { get; init; } = Run;
        public string ArtifactsBody { get; init; } = Artifacts;
        public HttpStatusCode RunStatus { get; init; } = HttpStatusCode.OK;
        public HttpStatusCode ArtifactsStatus { get; init; } = HttpStatusCode.OK;
        public string? RunLocation { get; init; }
        public Exception? Throw { get; init; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string address = request.RequestUri!.AbsoluteUri;
            Requests.Add(new(request.Method.Method, address, request.Headers.Authorization is not null, request.Headers.Contains("Cookie"),
                request.Headers.UserAgent.ToString()));
            if (Throw is not null) throw Throw;
            bool run = string.Equals(address, RunAddress, StringComparison.Ordinal);
            if (!run && !string.Equals(address, ArtifactsAddress, StringComparison.Ordinal)) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            HttpResponseMessage response = new(run ? RunStatus : ArtifactsStatus)
            {
                Content = new ByteArrayContent(Encoding.UTF8.GetBytes(run ? RunBody : ArtifactsBody)),
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            if (run && RunLocation is not null) response.Headers.Location = new Uri(RunLocation);
            return Task.FromResult(response);
        }
    }
}
