using System.Net;
using System.Security.Cryptography;
using System.Text;
using PCSteward.SelfUpdate;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// FR-UPD-04/15, TM-03/13 and ADR-032 revision 2 §3–§5 (threats TM-19, TM-24, TM-27, TM-28): the update check talks only to
/// compiled GitHub addresses, builds download addresses itself, follows redirects only to compiled HTTPS hosts, caps every
/// response, accepts a check file only when GitHub's public API reports its digest for a green, hand-started
/// release-payload.yml run of this repository on a release branch at the commit it names, and never turns a network or
/// format problem into "up to date". Uses an in-memory handler; no request leaves the test.
/// </summary>
[TestClass]
public sealed class SelfUpdateCheckTests
{
    private const string Api = "https://api.github.com/repos/DanDG923/pc-steward/";
    private const string Files = "https://github.com/DanDG923/pc-steward/releases/download/v0.1.1/";
    private const string AssetHost = "https://release-assets.githubusercontent.com/github-production-release-asset/1/";
    private const string Run = Api + "actions/runs/4242";
    private const string Artifacts = Run + "/artifacts?name=pc-steward-release-check";

    [TestMethod]
    public async Task AGenuineNewerReleaseIsReportedAfterOnlyTheCheckFileIsDownloaded()
    {
        using Fixture fixture = new();
        SelfUpdateCheck check = await fixture.CheckAsync();

        Assert.AreEqual(SelfUpdateCheckStatus.NewerRelease, check.Status, check.Refusal.ToString());
        VerifiedRelease release = check.Release!;
        Assert.AreEqual("0.1.1", release.Version.Text);
        Assert.AreEqual(4242, release.RunId);
        Assert.AreEqual(ReleaseCheckFixture.Commit, release.SourceCommit);
        Assert.AreEqual("PCSteward-0.1.1.msi", release.MsiAssetName);
        Assert.AreEqual(Convert.ToHexString(SHA256.HashData(fixture.Zip)), release.CheckZipSha256);
        Assert.AreEqual(new Uri("https://github.com/DanDG923/pc-steward/actions/runs/4242"), release.RunPage);
        Assert.AreEqual(new Uri("https://github.com/DanDG923/pc-steward/commit/" + ReleaseCheckFixture.Commit), release.CommitPage);
        Assert.AreEqual("Notes", release.Notes);
        CollectionAssert.AreEqual(
            new[] { Api + "releases/latest", Files + "pc-steward-release-check.zip", AssetHost + "check", Run, Artifacts },
            fixture.Handler.Requests.Select(request => request.RequestUri!.AbsoluteUri).ToArray());
    }

    [TestMethod]
    public async Task TheSameOrAnOlderVersionIsCurrentOnlyAfterEveryCheckPasses()
    {
        foreach (string running in new[] { "0.1.1-private-beta", "0.1.1", "0.2.0-private-beta" })
        {
            using Fixture fixture = new() { Running = running };
            SelfUpdateCheck check = await fixture.CheckAsync();
            Assert.AreEqual(SelfUpdateCheckStatus.CurrentRelease, check.Status, running);
            Assert.IsNotNull(check.Release);
        }

        using Fixture failing = new() { Running = "0.1.1-private-beta" };
        failing.RunJson = failing.RunJson.Replace("\"success\"", "\"failure\"", StringComparison.Ordinal);
        Assert.AreEqual(SelfUpdateCheckStatus.NotTrusted, (await failing.CheckAsync()).Status);
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
        fixture.Latest = fixture.Latest.Replace("\"name\":\"pc-steward-release-check.zip\"", "\"name\":\"pc-steward-release-check.zip\",\"browser_download_url\":\"https://evil.example/x\"", StringComparison.Ordinal);
        SelfUpdateCheck check = await fixture.CheckAsync();

        Assert.AreEqual(SelfUpdateCheckStatus.NewerRelease, check.Status);
        Assert.IsFalse(fixture.Handler.Requests.Any(request => request.RequestUri!.Host.Contains("evil", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task UnusualReleasesAreRefusedBeforeAnyDownload()
    {
        foreach ((string from, string to, SelfUpdateRefusal refusal) in new[]
        {
            ("\"tag_name\":\"v0.1.1\"", "\"tag_name\":\"v0.1.1/../../x\"", SelfUpdateRefusal.ReleaseUnreadable),
            ("\"tag_name\":\"v0.1.1\"", "\"tag_name\":\"0.1.1\"", SelfUpdateRefusal.ReleaseUnreadable),
            ("\"tag_name\":\"v0.1.1\"", "\"tag_name\":\"v0.1.1-beta\"", SelfUpdateRefusal.ReleaseUnreadable),
            ("\"draft\":false", "\"draft\":true", SelfUpdateRefusal.ReleaseUnreadable),
            ("\"prerelease\":false", "\"prerelease\":true", SelfUpdateRefusal.ReleaseUnreadable),
            ("\"name\":\"PCSteward-0.1.1.msi\"", "\"name\":\"PCSteward-0.1.2.msi\"", SelfUpdateRefusal.UnexpectedAssets),
            ("\"name\":\"PCSteward-0.1.1.msi\"", "\"name\":\"pc-steward-release-check.zip\"", SelfUpdateRefusal.UnexpectedAssets),
            ("\"state\":\"uploaded\"", "\"state\":\"starter\"", SelfUpdateRefusal.UnexpectedAssets),
            ("\"assets\":[", "\"assets\":[{\"name\":\"setup.exe\",\"state\":\"uploaded\",\"size\":5},", SelfUpdateRefusal.UnexpectedAssets),
            ("\"size\":2000000", "\"size\":999999999999", SelfUpdateRefusal.MsiTooLarge),
        })
        {
            using Fixture fixture = new();
            Assert.Contains(from, fixture.Latest, from);
            fixture.Latest = fixture.Latest.Replace(from, to, StringComparison.Ordinal);
            SelfUpdateCheck check = await fixture.CheckAsync();

            Assert.AreEqual(SelfUpdateCheckStatus.NotTrusted, check.Status, to);
            Assert.AreEqual(refusal, check.Refusal, to);
            Assert.HasCount(1, fixture.Handler.Requests, to);
        }
    }

    [TestMethod]
    public async Task CheckFilesThatAreNotThisReleasesAreRefused()
    {
        (Action<Fixture> change, SelfUpdateRefusal refusal)[] cases =
        [
            (fixture => fixture.Record = ReleaseCheckFixture.Record(msiVersion: "0.1.2"), SelfUpdateRefusal.TagMismatch),
            (fixture => fixture.Record = ReleaseCheckFixture.Record(pins: [new string('E', 64), new string('B', 64)]), SelfUpdateRefusal.CheckFileForOtherKeys),
            (fixture => fixture.Record = [.. "{"u8], SelfUpdateRefusal.CheckFileUnreadable),
            (fixture => fixture.ZipOverride = "not a zip"u8.ToArray(), SelfUpdateRefusal.CheckFileUnreadable),
            (fixture => fixture.CheckSizeOverride = 1, SelfUpdateRefusal.CheckFileUnreadable),
        ];
        foreach ((Action<Fixture> change, SelfUpdateRefusal refusal) in cases)
        {
            using Fixture fixture = new();
            change(fixture);
            SelfUpdateCheck check = await fixture.CheckAsync();
            Assert.AreEqual(SelfUpdateCheckStatus.NotTrusted, check.Status, refusal.ToString());
            Assert.AreEqual(refusal, check.Refusal);
            Assert.IsFalse(fixture.Handler.Requests.Any(request => request.RequestUri!.AbsoluteUri.StartsWith(Run, StringComparison.Ordinal)), "the run is not asked about");
        }
    }

    [TestMethod]
    public async Task OnlyAGreenHandStartedReleasePayloadRunOfThisRepositoryAtTheCommitCounts()
    {
        foreach ((string from, string to) in new[]
        {
            ("\"id\":4242", "\"id\":4243"),
            ("\"path\":\".github/workflows/release-payload.yml\"", "\"path\":\".github/workflows/ci.yml\""),
            ("\"event\":\"workflow_dispatch\"", "\"event\":\"push\""),
            ("\"head_branch\":\"phase/4-beta\"", "\"head_branch\":\"feature\""),
            ("\"status\":\"completed\"", "\"status\":\"in_progress\""),
            ("\"conclusion\":\"success\"", "\"conclusion\":\"failure\""),
            ("\"head_sha\":\"" + ReleaseCheckFixture.Commit + "\"", "\"head_sha\":\"" + new string('f', 40) + "\""),
            ("\"repository\":{\"id\":1331300752", "\"repository\":{\"id\":1"),
            ("\"head_repository\":{\"id\":1331300752,\"full_name\":\"DanDG923/pc-steward\"", "\"head_repository\":{\"id\":1331300752,\"full_name\":\"fork/pc-steward\""),
        })
        {
            using Fixture fixture = new();
            Assert.Contains(from, fixture.RunJson, from);
            fixture.RunJson = fixture.RunJson.Replace(from, to, StringComparison.Ordinal);
            SelfUpdateCheck check = await fixture.CheckAsync();
            Assert.AreEqual(SelfUpdateCheckStatus.NotTrusted, check.Status, to);
            Assert.AreEqual(SelfUpdateRefusal.RunNotGenuine, check.Refusal, to);
        }

        using Fixture main = new();
        main.RunJson = main.RunJson.Replace("\"head_branch\":\"phase/4-beta\"", "\"head_branch\":\"main\"", StringComparison.Ordinal);
        Assert.AreEqual(SelfUpdateCheckStatus.NewerRelease, (await main.CheckAsync()).Status);

        using Fixture missing = new();
        missing.Handler.Override[Run] = () => new HttpResponseMessage(HttpStatusCode.NotFound);
        Assert.AreEqual(SelfUpdateRefusal.RunNotFound, (await missing.CheckAsync()).Refusal);
    }

    [TestMethod]
    public async Task TheCheckZipMustBeTheArtefactGitHubRecordedForThatRun()
    {
        using Fixture other = new();
        other.ArtifactsJson = other.ArtifactsJson.Replace(other.Digest, "sha256:" + new string('0', 64), StringComparison.Ordinal);
        Assert.AreEqual(SelfUpdateRefusal.CheckArtifactMismatch, (await other.CheckAsync()).Refusal);

        using Fixture upper = new();
        upper.ArtifactsJson = upper.ArtifactsJson.Replace(upper.Digest, upper.Digest.ToUpperInvariant().Replace("SHA256", "sha256", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.AreEqual(SelfUpdateRefusal.CheckArtifactMismatch, (await upper.CheckAsync()).Refusal);

        using Fixture twice = new();
        twice.ArtifactsJson = twice.ArtifactsJson.Replace("[{", "[{\"name\":\"pc-steward-release-check\",\"digest\":\"" + twice.Digest + "\"},{", StringComparison.Ordinal);
        Assert.AreEqual(SelfUpdateRefusal.CheckArtifactMismatch, (await twice.CheckAsync()).Refusal);

        using Fixture gone = new();
        gone.ArtifactsJson = "{\"total_count\":0,\"artifacts\":[]}";
        Assert.AreEqual(SelfUpdateRefusal.CheckArtifactGone, (await gone.CheckAsync()).Refusal);

        using Fixture noDigest = new();
        noDigest.ArtifactsJson = noDigest.ArtifactsJson.Replace(",\"digest\":\"" + noDigest.Digest + "\"", "", StringComparison.Ordinal);
        Assert.AreEqual(SelfUpdateRefusal.CheckArtifactGone, (await noDigest.CheckAsync()).Refusal);
    }

    [TestMethod]
    public async Task NoPublishedReleaseIsReportedAsSuch()
    {
        using Fixture fixture = new();
        fixture.Handler.Override[Api + "releases/latest"] = () => new HttpResponseMessage(HttpStatusCode.NotFound);
        Assert.AreEqual(SelfUpdateCheckStatus.NoReleasesPublished, (await fixture.CheckAsync()).Status);
    }

    [TestMethod]
    public async Task RedirectsLeavingTheCompiledHttpsHostsAreNeverFollowed()
    {
        foreach (string location in new[] { "https://evil.example/check", "http://release-assets.githubusercontent.com/check", "https://release-assets.githubusercontent.com:8443/check", "https://user@github.com/x", "file:///C:/Windows/win.ini" })
        {
            using Fixture fixture = new();
            fixture.Handler.Override[Files + "pc-steward-release-check.zip"] = () => Redirect(location);
            SelfUpdateCheck check = await fixture.CheckAsync();

            Assert.AreEqual(SelfUpdateCheckStatus.CouldNotCheck, check.Status, location);
            Assert.IsFalse(fixture.Handler.Requests.Any(request => request.RequestUri!.AbsoluteUri.StartsWith(location, StringComparison.Ordinal)), location);
        }
    }

    [TestMethod]
    public async Task EndlessRedirectsStop()
    {
        using Fixture fixture = new();
        fixture.Handler.Override[Files + "pc-steward-release-check.zip"] = () => Redirect(Files + "pc-steward-release-check.zip");
        Assert.AreEqual(SelfUpdateCheckStatus.CouldNotCheck, (await fixture.CheckAsync()).Status);
        Assert.IsLessThanOrEqualTo(8, fixture.Handler.Requests.Count);
    }

    [TestMethod]
    public async Task OversizedResponsesAreRefusedWithOrWithoutALength()
    {
        using Fixture declared = new();
        declared.Handler.Override[AssetHost + "check"] = () => Ok(new byte[PCSteward.Execution.ReleaseCheckPolicy.MaximumZipBytes + 1]);
        Assert.AreEqual(SelfUpdateCheckStatus.CouldNotCheck, (await declared.CheckAsync()).Status);

        using Fixture streamed = new();
        streamed.Handler.Override[Run] = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new UnknownLengthStream(GitHubReleaseClient.MaximumApiJsonBytes + 1)) };
        Assert.AreEqual(SelfUpdateCheckStatus.CouldNotCheck, (await streamed.CheckAsync()).Status);
    }

    [TestMethod]
    public async Task NetworkFailuresAreCouldNotCheckNeverUpToDate()
    {
        foreach (string address in new[] { Api + "releases/latest", AssetHost + "check", Run, Artifacts })
        {
            foreach (Func<HttpResponseMessage> failure in new Func<HttpResponseMessage>[]
            {
                () => throw new HttpRequestException("offline"),
                () => throw new IOException("reset"),
                () => throw new TaskCanceledException("timeout"),
                () => new HttpResponseMessage(HttpStatusCode.InternalServerError),
                () => new HttpResponseMessage((HttpStatusCode)403),
            })
            {
                using Fixture fixture = new();
                fixture.Handler.Override[address] = failure;
                Assert.AreEqual(SelfUpdateCheckStatus.CouldNotCheck, (await fixture.CheckAsync()).Status, address);
            }
        }
    }

    [TestMethod]
    public async Task OwnerCancellationIsNotSwallowed()
    {
        using Fixture fixture = new();
        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(() => fixture.Client.CheckAsync(Parse("0.1.0-private-beta"), ReleaseCheckFixture.Pins, cancelled.Token));
    }

    [TestMethod]
    public async Task TheMsiDownloadIsExactlyTheReleasedSize()
    {
        using Fixture fixture = new();
        VerifiedRelease release = (await fixture.CheckAsync()).Release!;
        byte[] msi = new byte[2_000_000];
        RandomNumberGenerator.Fill(msi);

        fixture.Handler.Override[Files + "PCSteward-0.1.1.msi"] = () => Redirect(AssetHost + "msi");
        fixture.Handler.Override[AssetHost + "msi"] = () => Ok(msi);
        using (MemoryStream written = new())
        {
            SelfUpdateDownload? download = await fixture.Client.DownloadMsiAsync(release, written, CancellationToken.None);
            Assert.IsNotNull(download);
            Assert.AreEqual(Convert.ToHexString(SHA256.HashData(msi)), download.Sha256);
            CollectionAssert.AreEqual(msi, written.ToArray());
        }

        foreach (Func<HttpResponseMessage> wrong in new Func<HttpResponseMessage>[]
        {
            () => Ok(msi[..^1]),
            () => Ok([.. msi, 0]),
            () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new UnknownLengthStream(msi.Length + 1)) },
            () => Redirect("https://evil.example/msi"),
            () => throw new HttpRequestException("offline"),
        })
        {
            fixture.Handler.Override[AssetHost + "msi"] = wrong;
            using MemoryStream written = new();
            Assert.IsNull(await fixture.Client.DownloadMsiAsync(release, written, CancellationToken.None));
        }
    }

    [TestMethod]
    public void EveryRefusalHasItsOwnSentence()
    {
        string[] sentences = [.. Enum.GetValues<SelfUpdateRefusal>().Where(value => value != SelfUpdateRefusal.None).Select(SelfUpdateText.Describe)];
        Assert.AreEqual(sentences.Length, sentences.Distinct(StringComparer.Ordinal).Count());
    }

    [TestMethod]
    public void ReleaseNotesAreBoundedDisplayText()
    {
        using Fixture fixture = new();
        string latest = fixture.Latest.Replace("\"body\":\"Notes\"", "\"body\":\"a\\u202Eb\\u0007c\\n" + new string('x', 5000) + "\"", StringComparison.Ordinal);
        Assert.AreEqual(SelfUpdateRefusal.None, GitHubReleaseEvidence.ReadRelease(Encoding.UTF8.GetBytes(latest), out PublishedRelease? release));
        StringAssert.StartsWith(release!.Notes, "abc\nxxx");
        Assert.IsLessThanOrEqualTo(GitHubReleaseEvidence.MaximumNotesCharacters, release.Notes.Length);
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
        public Fixture()
        {
            Handler = new FakeHandler(this);
            Client = new GitHubReleaseClient(Handler, TimeProvider.System);
        }

        public string Running { get; set; } = "0.1.0-private-beta";

        public byte[] Record { get; set; } = ReleaseCheckFixture.Record();

        public byte[]? ZipOverride { get; set; }

        public long? CheckSizeOverride { get; set; }

        public byte[] Zip => ZipOverride ?? ReleaseCheckFixture.Zip(Record);

        public string Digest => "sha256:" + Convert.ToHexString(SHA256.HashData(Zip)).ToLowerInvariant();

        private string? latest;

        public string Latest
        {
            get => latest ?? "{\"tag_name\":\"v0.1.1\",\"name\":\"<script>\",\"body\":\"Notes\",\"draft\":false,\"prerelease\":false,\"published_at\":\"2026-11-12T10:00:00Z\",\"assets\":[" +
                "{\"name\":\"PCSteward-0.1.1.msi\",\"state\":\"uploaded\",\"size\":2000000},{\"name\":\"pc-steward-release-check.zip\",\"state\":\"uploaded\",\"size\":" + (CheckSizeOverride ?? Zip.Length) + "}]}";
            set => latest = value;
        }

        public string RunJson { get; set; } = "{\"id\":4242,\"name\":\"Release payload\",\"path\":\".github/workflows/release-payload.yml\",\"event\":\"workflow_dispatch\"," +
            "\"head_branch\":\"phase/4-beta\",\"head_sha\":\"" + ReleaseCheckFixture.Commit + "\",\"status\":\"completed\",\"conclusion\":\"success\"," +
            "\"repository\":{\"id\":1331300752,\"full_name\":\"DanDG923/pc-steward\"},\"head_repository\":{\"id\":1331300752,\"full_name\":\"DanDG923/pc-steward\"}}";

        private string? artifactsJson;

        public string ArtifactsJson
        {
            get => artifactsJson ?? "{\"total_count\":1,\"artifacts\":[{\"name\":\"pc-steward-release-check\",\"digest\":\"" + Digest + "\",\"expired\":false}]}";
            set => artifactsJson = value;
        }

        public FakeHandler Handler { get; }

        public GitHubReleaseClient Client { get; }

        public Task<SelfUpdateCheck> CheckAsync() => Client.CheckAsync(Parse(Running), ReleaseCheckFixture.Pins, CancellationToken.None);

        public HttpResponseMessage Respond(string address) => address switch
        {
            Api + "releases/latest" => Ok(Encoding.UTF8.GetBytes(Latest)),
            Files + "pc-steward-release-check.zip" => Redirect(AssetHost + "check"),
            AssetHost + "check" => Ok(Zip),
            Run => Ok(Encoding.UTF8.GetBytes(RunJson)),
            Artifacts => Ok(Encoding.UTF8.GetBytes(ArtifactsJson)),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        };

        public void Dispose() => Client.Dispose();
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
