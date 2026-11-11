using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCSteward.SelfUpdate;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// ADR-032 section 3 and TM-26/TM-27/TM-28: the notify-only check talks only to compiled GitHub addresses, builds download
/// addresses itself, follows redirects only to compiled HTTPS hosts, caps every response and never turns a network or format
/// problem into "up to date" or a candidate. Uses an in-memory handler; no request leaves the test.
/// </summary>
[TestClass]
public sealed class SelfUpdateDiscoveryTests
{
    private const string Files = "https://github.com/DanDG923/pc-steward/releases/download/v0.2.0-beta.1/";
    private const string AssetHost = "https://release-assets.githubusercontent.com/github-production-release-asset/1/";
    private static readonly RunningRelease Running = new(Parse("0.1.0-private-beta"), 1);

    [TestMethod]
    public async Task GenuineNewerReleaseIsReportedWithoutDownloadingThePackage()
    {
        using Fixture fixture = new();
        SelfUpdateCheck check = await fixture.CheckAsync();

        Assert.AreEqual(SelfUpdateCheckStatus.NewerRelease, check.Status);
        Assert.AreEqual("0.2.0-beta.1", check.Manifest!.Version.Text);
        Assert.AreEqual(new Uri("https://github.com/DanDG923/pc-steward/releases/tag/v0.2.0-beta.1"), check.ReleasePage);
        CollectionAssert.AreEqual(
            new[]
            {
                "https://api.github.com/repos/DanDG923/pc-steward/releases/latest",
                Files + "pcsteward-release.json",
                AssetHost + "manifest",
                Files + "pcsteward-release.json.sig",
                AssetHost + "signature",
            },
            fixture.Handler.Requests.Select(request => request.RequestUri!.AbsoluteUri).ToArray());
    }

    [TestMethod]
    public async Task RequestsCarryNoIdentityBeyondTheProductName()
    {
        using Fixture fixture = new();
        _ = await fixture.CheckAsync();

        foreach (HttpRequestMessage request in fixture.Handler.Requests)
        {
            Assert.AreEqual(HttpMethod.Get, request.Method);
            Assert.AreEqual("PCSteward", request.Headers.UserAgent.ToString());
            Assert.IsNull(request.Headers.Authorization);
            Assert.IsFalse(request.Headers.Contains("Cookie"));
            Assert.IsNull(request.Content);
        }
    }

    [TestMethod]
    public async Task DownloadAddressesComeFromCompiledPartsNotFromTheResponse()
    {
        using Fixture fixture = new();
        // A hostile release body names other download URLs; they are ignored.
        fixture.Latest = "{\"tag_name\":\"v0.2.0-beta.1\",\"draft\":false,\"prerelease\":false,\"assets\":[{\"name\":\"pcsteward-release.json\",\"browser_download_url\":\"https://evil.example/x\"}]}";
        SelfUpdateCheck check = await fixture.CheckAsync();

        Assert.AreEqual(SelfUpdateCheckStatus.NewerRelease, check.Status);
        Assert.IsFalse(fixture.Handler.Requests.Any(request => request.RequestUri!.Host.Contains("evil", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task UnsafeOrUnusualTagsAreRefusedBeforeAnyDownload()
    {
        foreach (string latest in new[]
        {
            "{\"tag_name\":\"v0.2.0-beta.1/../../x\",\"draft\":false,\"prerelease\":false}",
            "{\"tag_name\":\"0.2.0-beta.1\",\"draft\":false,\"prerelease\":false}",
            "{\"tag_name\":\"v0.2.0-beta.1\",\"draft\":true,\"prerelease\":false}",
            "{\"tag_name\":\"v0.2.0-beta.1\",\"draft\":false,\"prerelease\":true}",
            "{\"tag_name\":\"v0.2.0-beta.1\",\"prerelease\":false}",
            "{\"tag_name\":7,\"draft\":false,\"prerelease\":false}",
            "[]",
            "not json",
        })
        {
            using Fixture fixture = new() { Latest = latest };
            SelfUpdateCheck check = await fixture.CheckAsync();

            Assert.AreEqual(SelfUpdateCheckStatus.NotTrusted, check.Status, latest);
            Assert.AreEqual(1, fixture.Handler.Requests.Count, latest);
        }
    }

    [TestMethod]
    public async Task NoPublishedReleaseIsReportedAsSuch()
    {
        using Fixture fixture = new();
        fixture.Handler.Override["https://api.github.com/repos/DanDG923/pc-steward/releases/latest"] = () => new HttpResponseMessage(HttpStatusCode.NotFound);

        Assert.AreEqual(SelfUpdateCheckStatus.NoReleasesPublished, (await fixture.CheckAsync()).Status);
    }

    [TestMethod]
    public async Task ReleaseWithoutSignatureFilesIsNotTrusted()
    {
        using Fixture fixture = new();
        fixture.Handler.Override[Files + "pcsteward-release.json.sig"] = () => new HttpResponseMessage(HttpStatusCode.NotFound);
        SelfUpdateCheck check = await fixture.CheckAsync();

        Assert.AreEqual(SelfUpdateCheckStatus.NotTrusted, check.Status);
        Assert.AreEqual(ReleaseRefusal.MissingReleaseFiles, check.Refusal);
        Assert.IsNull(check.Manifest);
    }

    [TestMethod]
    public async Task RedirectsLeavingTheCompiledHttpsHostsAreNeverFollowed()
    {
        foreach (string location in new[] { "https://evil.example/manifest", "http://release-assets.githubusercontent.com/manifest", "https://release-assets.githubusercontent.com:8443/manifest", "https://user@github.com/x", "file:///C:/Windows/win.ini" })
        {
            using Fixture fixture = new();
            fixture.Handler.Override[Files + "pcsteward-release.json"] = () => Redirect(location);
            SelfUpdateCheck check = await fixture.CheckAsync();

            Assert.AreEqual(SelfUpdateCheckStatus.CouldNotCheck, check.Status, location);
            Assert.IsFalse(fixture.Handler.Requests.Any(request => request.RequestUri!.AbsoluteUri.StartsWith(location, StringComparison.Ordinal)), location);
        }
    }

    [TestMethod]
    public async Task EndlessRedirectsStop()
    {
        using Fixture fixture = new();
        fixture.Handler.Override[Files + "pcsteward-release.json"] = () => Redirect(Files + "pcsteward-release.json");

        Assert.AreEqual(SelfUpdateCheckStatus.CouldNotCheck, (await fixture.CheckAsync()).Status);
        Assert.IsTrue(fixture.Handler.Requests.Count <= 8);
    }

    [TestMethod]
    public async Task OversizedResponsesAreRefusedWithOrWithoutALength()
    {
        using Fixture declared = new();
        declared.Handler.Override[AssetHost + "manifest"] = () => Ok(new byte[ReleaseManifest.MaximumBytes + 1]);
        Assert.AreEqual(SelfUpdateCheckStatus.CouldNotCheck, (await declared.CheckAsync()).Status);

        using Fixture streamed = new();
        streamed.Handler.Override[AssetHost + "signature"] = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new UnknownLengthStream(ReleaseSignature.MaximumBytes + 1)) };
        Assert.AreEqual(SelfUpdateCheckStatus.CouldNotCheck, (await streamed.CheckAsync()).Status);
    }

    [TestMethod]
    public async Task NetworkFailuresAreCouldNotCheckNeverUpToDate()
    {
        foreach (Func<HttpResponseMessage> failure in new Func<HttpResponseMessage>[]
        {
            () => throw new HttpRequestException("offline"),
            () => throw new IOException("reset"),
            () => throw new TaskCanceledException("timeout"),
            () => throw new InvalidOperationException("unexpected"),
            () => new HttpResponseMessage(HttpStatusCode.InternalServerError),
            () => new HttpResponseMessage((HttpStatusCode)403),
        })
        {
            using Fixture fixture = new();
            fixture.Handler.Override[AssetHost + "manifest"] = failure;

            Assert.AreEqual(SelfUpdateCheckStatus.CouldNotCheck, (await fixture.CheckAsync()).Status);
        }
    }

    [TestMethod]
    public async Task OwnerCancellationIsNotSwallowed()
    {
        using Fixture fixture = new();
        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() => fixture.Client.CheckAsync(Running, 0, [fixture.Key], cancelled.Token));
    }

    [TestMethod]
    public async Task VerifierRefusalsReachTheOwnerWithTheirReason()
    {
        using Fixture fixture = new();
        fixture.ManifestBytes[^3] ^= 0x01;
        SelfUpdateCheck check = await fixture.CheckAsync();

        Assert.AreEqual(SelfUpdateCheckStatus.NotTrusted, check.Status);
        Assert.AreEqual(ReleaseRefusal.SignatureInvalid, check.Refusal);
        StringAssert.StartsWith(ReleaseRefusalText.Describe(check), "Update found but not trusted.");
    }

    [TestMethod]
    public void EveryRefusalHasItsOwnSentence()
    {
        string[] sentences = [.. Enum.GetValues<ReleaseRefusal>().Where(value => value != ReleaseRefusal.None).Select(ReleaseRefusalText.Describe)];
        Assert.AreEqual(sentences.Length, sentences.Distinct(StringComparer.Ordinal).Count());
    }

    private static ReleaseVersion Parse(string text)
    {
        Assert.IsTrue(ReleaseVersion.TryParse(text, out ReleaseVersion? version));
        return version!;
    }

    private static HttpResponseMessage Ok(byte[] body) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };

    private static HttpResponseMessage Redirect(string location)
    {
        HttpResponseMessage response = new(HttpStatusCode.Found);
        response.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
        return response;
    }

    private sealed class Fixture : IDisposable
    {
        private readonly ECDsa ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        // The signature covers the manifest as first built, so a later edit to ManifestBytes is tampering.
        private readonly string signature;

        public Fixture()
        {
            ECParameters parameters = ecdsa.ExportParameters(false);
            Key = ReleaseSigningKey.FromHex("test", Convert.ToHexString(parameters.Q.X!), Convert.ToHexString(parameters.Q.Y!));
            ManifestBytes = Encoding.UTF8.GetBytes("{\"schema\":1,\"product\":\"PCSteward\",\"channel\":\"beta\",\"version\":\"0.2.0-beta.1\",\"sequence\":2," +
                "\"publishedUtc\":\"2026-11-11T12:00:00Z\",\"minimumUpdaterVersion\":\"0.1.0-private-beta\",\"assetName\":\"PCSteward-0.2.0-beta.1-win-x64.zip\"," +
                "\"assetSize\":1048576,\"assetSha256\":\"" + new string('a', 64) + "\",\"payloadManifestSha256\":\"" + new string('b', 64) + "\",\"sbomSha256\":\"" +
                new string('c', 64) + "\",\"schemaVersionAfter\":1,\"keyId\":\"" + Key.KeyId + "\"}");
            signature = new ReleaseSignature(Key.KeyId, ecdsa.SignData(ManifestBytes, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)).ToFileText();
            Handler = new FakeHandler(this);
            Client = new GitHubReleaseClient(Handler);
        }

        public ReleaseSigningKey Key { get; }

        public byte[] ManifestBytes { get; }

        public string Latest { get; set; } = "{\"tag_name\":\"v0.2.0-beta.1\",\"name\":\"<script>\",\"body\":\"untrusted\",\"draft\":false,\"prerelease\":false}";

        public FakeHandler Handler { get; }

        public GitHubReleaseClient Client { get; }

        public Task<SelfUpdateCheck> CheckAsync() => Client.CheckAsync(Running, 0, [Key], CancellationToken.None);

        public HttpResponseMessage Respond(string address) => address switch
        {
            "https://api.github.com/repos/DanDG923/pc-steward/releases/latest" => Ok(Encoding.UTF8.GetBytes(Latest)),
            Files + "pcsteward-release.json" => Redirect(AssetHost + "manifest"),
            Files + "pcsteward-release.json.sig" => Redirect(AssetHost + "signature"),
            AssetHost + "manifest" => Ok([.. ManifestBytes]),
            AssetHost + "signature" => Ok(Encoding.ASCII.GetBytes(signature)),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        };

        public void Dispose()
        {
            Client.Dispose();
            ecdsa.Dispose();
        }
    }

    private sealed class FakeHandler(Fixture fixture) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        public Dictionary<string, Func<HttpResponseMessage>> Override { get; } = new(StringComparer.Ordinal);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            string address = request.RequestUri!.AbsoluteUri;
            return Task.FromResult(Override.TryGetValue(address, out Func<HttpResponseMessage>? response) ? response() : fixture.Respond(address));
        }
    }

    private sealed class UnknownLengthStream(int length) : Stream
    {
        private int remaining = length;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int read = Math.Min(count, remaining);
            Array.Fill(buffer, (byte)'A', offset, read);
            remaining -= read;
            return read;
        }

        public override void Flush() { }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
