using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using PCSteward.Execution;

namespace PCSteward.SelfUpdate;

/// <summary>The result of one owner-requested "Check for updates".</summary>
public enum SelfUpdateCheckStatus
{
    /// <summary>A newer release passed every check of ADR-032 revision 2 §3–§4. Only its check file has been downloaded.</summary>
    NewerRelease,

    /// <summary>The newest published release passed every check and is not newer than this build.</summary>
    CurrentRelease,

    /// <summary>The repository has no published release yet.</summary>
    NoReleasesPublished,

    /// <summary>A release was found but failed a check (see <see cref="SelfUpdateCheck.Refusal"/>). Never "up to date".</summary>
    NotTrusted,

    /// <summary>GitHub could not be reached or answered unexpectedly. Never "up to date".</summary>
    CouldNotCheck,
}

/// <summary>
/// A release that passed the check: the version, the CI run and commit it was built from, and the check zip whose digest
/// GitHub reported for that run. <see cref="Notes"/> is GitHub's release text, unchecked and for display only.
/// </summary>
public sealed record VerifiedRelease(
    ReleaseVersion Version,
    string Tag,
    long RunId,
    string SourceCommit,
    DateTimeOffset? PublishedAt,
    long MsiBytes,
    byte[] CheckZip,
    string CheckZipSha256,
    string CandidateManifestSha256,
    string MsiTablesSha256,
    string Notes)
{
    /// <summary>The MSI asset name for this version.</summary>
    public string MsiAssetName => GitHubReleaseEvidence.MsiAssetName(Version.Text);

    /// <summary>The release page, built from compiled parts and the checked tag.</summary>
    public Uri ReleasePage => new(GitHubReleaseClient.RepositoryPage, "releases/tag/" + Tag);

    /// <summary>The CI run page, built from compiled parts and the checked run id.</summary>
    public Uri RunPage => new(GitHubReleaseClient.RepositoryPage, "actions/runs/" + RunId.ToString(CultureInfo.InvariantCulture));

    /// <summary>The commit page, built from compiled parts and the checked commit.</summary>
    public Uri CommitPage => new(GitHubReleaseClient.RepositoryPage, "commit/" + SourceCommit);
}

/// <param name="Status">What the check concluded.</param>
/// <param name="Refusal">Why a release was not trusted, when <paramref name="Status"/> is NotTrusted.</param>
/// <param name="Release">The checked release for NewerRelease and CurrentRelease; otherwise null.</param>
/// <param name="CheckedAt">When the check finished (UTC).</param>
public sealed record SelfUpdateCheck(SelfUpdateCheckStatus Status, SelfUpdateRefusal Refusal, VerifiedRelease? Release, DateTimeOffset CheckedAt);

/// <summary>The downloaded MSI: its SHA-256 and length, as written to the caller's stream.</summary>
public sealed record SelfUpdateDownload(string Sha256, long Bytes);

/// <summary>
/// ADR-032 revision 2 §3–§5. One owner-requested check makes at most four unauthenticated HTTPS GETs to compiled addresses:
/// the repository's latest release, the release's check zip, the CI run it names, and that run's check artefact record.
/// The MSI is fetched only by <see cref="DownloadMsiAsync"/>, after the owner asks. Download addresses are built from
/// compiled parts and the checked tag, never from URLs in a response; redirects are followed by hand and only to compiled
/// GitHub hosts over HTTPS on the default port. Every response is size-capped and time-limited. No token, cookie, account
/// or version number is sent (the User-Agent is just "PCSteward"). Anything unexpected ends the check as CouldNotCheck or
/// NotTrusted; nothing is executed, saved or installed here.
/// </summary>
public sealed class GitHubReleaseClient : IDisposable
{
    internal const int MaximumApiJsonBytes = ReleaseRunEvidence.MaximumAnswerBytes;
    private const int MaximumRedirects = 5;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(20);
    private static readonly HashSet<string> AllowedHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "api.github.com", "github.com", "objects.githubusercontent.com", "release-assets.githubusercontent.com",
    };

    internal static readonly Uri RepositoryPage = new("https://github.com/" + ReleaseCheckPolicy.Repository + "/");
    internal static readonly Uri RepositoryApi = new("https://api.github.com/repos/" + ReleaseCheckPolicy.Repository + "/");

    private readonly HttpMessageInvoker invoker;
    private readonly TimeProvider clock;

    public GitHubReleaseClient()
        : this(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            Credentials = null,
            PreAuthenticate = false,
            AutomaticDecompression = DecompressionMethods.None,
            ConnectTimeout = TimeSpan.FromSeconds(15),
            MaxResponseHeadersLength = 64,
        }, TimeProvider.System)
    {
    }

    internal GitHubReleaseClient(HttpMessageHandler handler, TimeProvider clock)
    {
        invoker = new HttpMessageInvoker(handler, disposeHandler: true);
        this.clock = clock;
    }

    /// <summary>Checks the newest published release against this build's version and compiled release pins.</summary>
    public Task<SelfUpdateCheck> CheckAsync(ReleaseVersion running, CancellationToken cancellationToken) =>
        CheckAsync(running, ReleaseSigningPins.Compiled, cancellationToken);

    internal async Task<SelfUpdateCheck> CheckAsync(ReleaseVersion running, IReadOnlyList<string> pins, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(running);
        ArgumentNullException.ThrowIfNull(pins);
        try
        {
            Fetch latest = await GetAsync(new Uri(RepositoryApi, "releases/latest"), MaximumApiJsonBytes, cancellationToken).ConfigureAwait(false);
            if (latest.Status == HttpStatusCode.NotFound) return Result(SelfUpdateCheckStatus.NoReleasesPublished);
            if (latest.Status != HttpStatusCode.OK || latest.Body is null) return Result(SelfUpdateCheckStatus.CouldNotCheck);
            SelfUpdateRefusal refusal = GitHubReleaseEvidence.ReadRelease(latest.Body, out PublishedRelease? release);
            if (refusal != SelfUpdateRefusal.None || release is null) return NotTrusted(refusal);

            // The check zip, by its compiled name under the checked tag; its SHA-256 is what GitHub must report for the run.
            Uri files = new(RepositoryPage, "releases/download/" + release.Tag + "/");
            Fetch zip = await GetAsync(new Uri(files, ReleaseCheckPolicy.AssetName), ReleaseCheckPolicy.MaximumZipBytes, cancellationToken).ConfigureAwait(false);
            if (zip.Status != HttpStatusCode.OK || zip.Body is null) return Result(SelfUpdateCheckStatus.CouldNotCheck);
            if (zip.Body.LongLength != release.CheckZipBytes ||
                !ReleaseCheckPolicy.TryReadZip(zip.Body, out byte[]? recordBytes) || recordBytes is null ||
                !ReleaseCheckPolicy.TryRead(recordBytes, out ReleaseCheckRecord? record) || record is null)
                return NotTrusted(SelfUpdateRefusal.CheckFileUnreadable);
            if (!ReleaseCheckPolicy.NamesPins(record, pins)) return NotTrusted(SelfUpdateRefusal.CheckFileForOtherKeys);
            if (!string.Equals(record.MsiVersion, release.Version.Text, StringComparison.Ordinal)) return NotTrusted(SelfUpdateRefusal.TagMismatch);
            string zipSha256 = Convert.ToHexString(SHA256.HashData(zip.Body));

            // The run the record names, and that run's check artefact record, through the public API.
            string run = "actions/runs/" + record.RunId.ToString(CultureInfo.InvariantCulture);
            Fetch runBody = await GetAsync(new Uri(RepositoryApi, run), MaximumApiJsonBytes, cancellationToken).ConfigureAwait(false);
            if (runBody.Status == HttpStatusCode.NotFound) return NotTrusted(SelfUpdateRefusal.RunNotFound);
            if (runBody.Status != HttpStatusCode.OK || runBody.Body is null) return Result(SelfUpdateCheckStatus.CouldNotCheck);
            refusal = GitHubReleaseEvidence.CheckRun(runBody.Body, record.RunId, record.SourceCommit);
            if (refusal != SelfUpdateRefusal.None) return NotTrusted(refusal);
            Fetch artifacts = await GetAsync(new Uri(RepositoryApi, run + "/artifacts?name=" + ReleaseCheckPolicy.ArtifactName), MaximumApiJsonBytes, cancellationToken).ConfigureAwait(false);
            if (artifacts.Status != HttpStatusCode.OK || artifacts.Body is null) return Result(SelfUpdateCheckStatus.CouldNotCheck);
            refusal = GitHubReleaseEvidence.CheckArtifact(artifacts.Body, zipSha256);
            if (refusal != SelfUpdateRefusal.None) return NotTrusted(refusal);

            VerifiedRelease verified = new(release.Version, release.Tag, record.RunId, record.SourceCommit, release.PublishedAt, release.MsiBytes,
                zip.Body, zipSha256, record.CandidateManifestSha256, record.MsiTablesSha256, release.Notes);
            SelfUpdateCheckStatus status = release.Version.CompareCore(running) > 0 ? SelfUpdateCheckStatus.NewerRelease : SelfUpdateCheckStatus.CurrentRelease;
            return new(status, SelfUpdateRefusal.None, verified, clock.GetUtcNow());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // ADR-032 revision 2 §4: a network or parsing failure ends the check; it is never a pass.
        catch (Exception)
#pragma warning restore CA1031
        {
            return Result(SelfUpdateCheckStatus.CouldNotCheck);
        }
    }

    /// <summary>
    /// ADR-032 revision 2 §5: downloads the release's MSI into <paramref name="destination"/>, refusing anything other than
    /// exactly <see cref="VerifiedRelease.MsiBytes"/> bytes. Returns null when the download failed; the caller then discards
    /// what was written. The caller still checks the pin and the tables before showing Install.
    /// </summary>
    public async Task<SelfUpdateDownload?> DownloadMsiAsync(VerifiedRelease release, Stream destination, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(release);
        ArgumentNullException.ThrowIfNull(destination);
        if (release.MsiBytes is <= 0 or > GitHubReleaseEvidence.MaximumMsiBytes) return null;
        try
        {
            Uri address = new(RepositoryPage, "releases/download/" + release.Tag + "/" + release.MsiAssetName);
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(DownloadTimeout);
            using HttpResponseMessage? response = await SendFollowingRedirectsAsync(address, timeout.Token).ConfigureAwait(false);
            if (response is null || response.StatusCode != HttpStatusCode.OK) return null;
            if (response.Content.Headers.ContentLength is long length && length != release.MsiBytes) return null;
            Stream source = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            await using (source.ConfigureAwait(false))
            {
                using IncrementalHash sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                byte[] chunk = new byte[64 * 1024];
                long total = 0;
                while (true)
                {
                    int read = await source.ReadAsync(chunk, timeout.Token).ConfigureAwait(false);
                    if (read == 0) break;
                    total += read;
                    if (total > release.MsiBytes) return null;
                    sha.AppendData(chunk, 0, read);
                    await destination.WriteAsync(chunk.AsMemory(0, read), timeout.Token).ConfigureAwait(false);
                }
                if (total != release.MsiBytes) return null;
                await destination.FlushAsync(timeout.Token).ConfigureAwait(false);
                return new(Convert.ToHexString(sha.GetHashAndReset()), total);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // ADR-032 revision 2 §5: any download failure is reported as a failed download, never a pass.
        catch (Exception)
#pragma warning restore CA1031
        {
            return null;
        }
    }

    public void Dispose() => invoker.Dispose();

    private SelfUpdateCheck Result(SelfUpdateCheckStatus status) => new(status, SelfUpdateRefusal.None, null, clock.GetUtcNow());

    private SelfUpdateCheck NotTrusted(SelfUpdateRefusal refusal) => new(SelfUpdateCheckStatus.NotTrusted, refusal, null, clock.GetUtcNow());

    private async Task<Fetch> GetAsync(Uri address, int maximumBytes, CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);
        using HttpResponseMessage? response = await SendFollowingRedirectsAsync(address, timeout.Token).ConfigureAwait(false);
        if (response is null) return new(HttpStatusCode.Forbidden, null);
        if (response.StatusCode != HttpStatusCode.OK) return new(response.StatusCode, null);
        if (response.Content.Headers.ContentLength is long length && (length < 0 || length > maximumBytes)) return new(HttpStatusCode.RequestEntityTooLarge, null);
        byte[]? body = await ReadCappedAsync(response.Content, maximumBytes, timeout.Token).ConfigureAwait(false);
        return body is null ? new(HttpStatusCode.RequestEntityTooLarge, null) : new(HttpStatusCode.OK, body);
    }

    /// <summary>Sends GETs, following at most five redirects by hand to compiled HTTPS hosts only. Null means refused.</summary>
    private async Task<HttpResponseMessage?> SendFollowingRedirectsAsync(Uri address, CancellationToken cancellationToken)
    {
        Uri current = address;
        for (int hop = 0; hop <= MaximumRedirects; hop++)
        {
            if (!IsAllowed(current)) return null;
            using HttpRequestMessage request = new(HttpMethod.Get, current);
            bool api = current.Host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("PCSteward", null));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(api ? "application/vnd.github+json" : "application/octet-stream"));
            if (api) request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
            HttpResponseMessage response = await invoker.SendAsync(request, cancellationToken).ConfigureAwait(false);
            int code = (int)response.StatusCode;
            if (code is 301 or 302 or 303 or 307 or 308)
            {
                Uri? location = response.Headers.Location;
                response.Dispose();
                if (location is null) return null;
                current = location.IsAbsoluteUri ? location : new Uri(current, location);
                continue;
            }
            return response;
        }
        return null;
    }

    private static bool IsAllowed(Uri address) =>
        address.IsAbsoluteUri && address.Scheme == Uri.UriSchemeHttps && address.IsDefaultPort && string.IsNullOrEmpty(address.UserInfo) &&
        AllowedHosts.Contains(address.Host);

    private static async Task<byte[]?> ReadCappedAsync(HttpContent content, int maximumBytes, CancellationToken cancellationToken)
    {
        Stream stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            using MemoryStream buffer = new();
            byte[] chunk = new byte[16 * 1024];
            while (true)
            {
                int read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);
                if (read == 0) return buffer.ToArray();
                if (buffer.Length + read > maximumBytes) return null;
                buffer.Write(chunk, 0, read);
            }
        }
    }

    private readonly record struct Fetch(HttpStatusCode Status, byte[]? Body);
}
