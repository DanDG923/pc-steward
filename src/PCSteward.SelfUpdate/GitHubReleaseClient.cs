using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace PCSteward.SelfUpdate;

/// <summary>The result of one owner-requested "Check for PC Steward updates".</summary>
public enum SelfUpdateCheckStatus
{
    /// <summary>A genuine, newer release exists. Nothing has been downloaded beyond its two small description files.</summary>
    NewerRelease,

    /// <summary>The newest published release is genuine and not newer. Shown with the version seen, never as a bare "up to date".</summary>
    NoNewerRelease,

    /// <summary>The repository has no published release yet.</summary>
    NoReleasesPublished,

    /// <summary>A release was found but failed a check (see <see cref="SelfUpdateCheck.Refusal"/>).</summary>
    NotTrusted,

    /// <summary>GitHub could not be reached or answered unexpectedly. Never treated as "up to date".</summary>
    CouldNotCheck,
}

/// <param name="Status">What the check concluded.</param>
/// <param name="Refusal">Why a release was not trusted, when <paramref name="Status"/> is NotTrusted.</param>
/// <param name="Manifest">The verified manifest for NewerRelease and NoNewerRelease; otherwise null.</param>
/// <param name="ReleaseTag">The validated tag of the release looked at, for display only; null when none was read.</param>
public sealed record SelfUpdateCheck(SelfUpdateCheckStatus Status, ReleaseRefusal Refusal, ReleaseManifest? Manifest, string? ReleaseTag)
{
    /// <summary>The release page for <see cref="ReleaseTag"/>, built from compiled parts only.</summary>
    public Uri? ReleasePage => ReleaseTag is null ? null : new Uri(GitHubReleaseClient.RepositoryPage, "releases/tag/" + ReleaseTag);
}

/// <summary>
/// ADR-032 section 3 (Discover) for the notify step. One owner-requested check makes at most three unauthenticated HTTPS
/// GETs to compiled addresses: GitHub's "latest release" API for the fixed repository, then the release's signed manifest
/// and signature, whose download addresses are built from compiled parts and the validated tag, never from URLs in the
/// response. Redirects are followed by hand and only to compiled GitHub hosts over HTTPS. Every response is size-capped and
/// time-limited. No token, cookie, account or version number is sent (the User-Agent is just "PCSteward"). Anything
/// unexpected ends the check as CouldNotCheck or NotTrusted; nothing is executed, saved or installed here.
/// </summary>
public sealed class GitHubReleaseClient : IDisposable
{
    public const string ManifestAssetName = "pcsteward-release.json";
    public const string SignatureAssetName = "pcsteward-release.json.sig";
    internal const int MaximumReleaseJsonBytes = 256 * 1024;
    private const int MaximumRedirects = 5;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);
    private static readonly HashSet<string> AllowedHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "api.github.com", "github.com", "objects.githubusercontent.com", "release-assets.githubusercontent.com",
    };

    internal static readonly Uri RepositoryPage = new("https://github.com/" + ReleaseTrustAnchors.RepositoryOwner + "/" + ReleaseTrustAnchors.RepositoryName + "/");
    internal static readonly Uri LatestReleaseApi = new("https://api.github.com/repos/" + ReleaseTrustAnchors.RepositoryOwner + "/" + ReleaseTrustAnchors.RepositoryName + "/releases/latest");

    private readonly HttpMessageInvoker invoker;

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
        })
    {
    }

    internal GitHubReleaseClient(HttpMessageHandler handler) => invoker = new HttpMessageInvoker(handler, disposeHandler: true);

    /// <summary>Checks the newest published release against the compiled keys and this build's identity.</summary>
    public Task<SelfUpdateCheck> CheckAsync(RunningRelease running, long highestAcceptedSequence, CancellationToken cancellationToken) =>
        CheckAsync(running, highestAcceptedSequence, ReleaseTrustAnchors.ReleaseKeys, cancellationToken);

    internal async Task<SelfUpdateCheck> CheckAsync(RunningRelease running, long highestAcceptedSequence, IReadOnlyList<ReleaseSigningKey> keys, CancellationToken cancellationToken)
    {
        try
        {
            Fetch latest = await GetAsync(LatestReleaseApi, MaximumReleaseJsonBytes, cancellationToken).ConfigureAwait(false);
            if (latest.Status == HttpStatusCode.NotFound) return new(SelfUpdateCheckStatus.NoReleasesPublished, ReleaseRefusal.None, null, null);
            if (latest.Status != HttpStatusCode.OK || latest.Body is null) return CouldNotCheck();
            if (!TryReadLatestTag(latest.Body, out string? tag)) return new(SelfUpdateCheckStatus.NotTrusted, ReleaseRefusal.TagMismatch, null, null);

            Uri releaseFiles = new(RepositoryPage, "releases/download/" + tag + "/");
            Fetch manifest = await GetAsync(new Uri(releaseFiles, ManifestAssetName), ReleaseManifest.MaximumBytes, cancellationToken).ConfigureAwait(false);
            if (manifest.Status == HttpStatusCode.NotFound) return new(SelfUpdateCheckStatus.NotTrusted, ReleaseRefusal.MissingReleaseFiles, null, tag);
            if (manifest.Status != HttpStatusCode.OK || manifest.Body is null) return CouldNotCheck();
            Fetch signature = await GetAsync(new Uri(releaseFiles, SignatureAssetName), ReleaseSignature.MaximumBytes, cancellationToken).ConfigureAwait(false);
            if (signature.Status == HttpStatusCode.NotFound) return new(SelfUpdateCheckStatus.NotTrusted, ReleaseRefusal.MissingReleaseFiles, null, tag);
            if (signature.Status != HttpStatusCode.OK || signature.Body is null) return CouldNotCheck();

            ReleaseCheckResult result = ReleaseCandidateVerifier.Verify(manifest.Body, signature.Body, tag, running, highestAcceptedSequence, keys);
            return result.Outcome switch
            {
                ReleaseCheckOutcome.NewerRelease => new(SelfUpdateCheckStatus.NewerRelease, ReleaseRefusal.None, result.Manifest, tag),
                ReleaseCheckOutcome.NoNewerRelease => new(SelfUpdateCheckStatus.NoNewerRelease, ReleaseRefusal.None, result.Manifest, tag),
                _ => new(SelfUpdateCheckStatus.NotTrusted, result.Refusal, null, tag),
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // ADR-032 section 4b: a network or parsing failure ends the check; it is never a pass.
        catch (Exception)
#pragma warning restore CA1031
        {
            return CouldNotCheck();
        }
    }

    public void Dispose() => invoker.Dispose();

    /// <summary>
    /// Reads only <c>tag_name</c>, <c>draft</c> and <c>prerelease</c> from GitHub's release JSON. The tag must be <c>v</c>
    /// followed by a strict release version, so it is safe to use as one path segment. Drafts and pre-releases are refused.
    /// </summary>
    internal static bool TryReadLatestTag(byte[] body, out string? tag)
    {
        tag = null;
        try
        {
            using JsonDocument document = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 16 });
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("tag_name", out JsonElement tagElement) || tagElement.ValueKind != JsonValueKind.String ||
                !root.TryGetProperty("draft", out JsonElement draft) || draft.ValueKind != JsonValueKind.False ||
                !root.TryGetProperty("prerelease", out JsonElement prerelease) || prerelease.ValueKind != JsonValueKind.False)
                return false;
            string? text = tagElement.GetString();
            if (text is null || text.Length < 2 || text[0] != 'v' || !ReleaseVersion.TryParse(text[1..], out _)) return false;
            tag = text;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static SelfUpdateCheck CouldNotCheck() => new(SelfUpdateCheckStatus.CouldNotCheck, ReleaseRefusal.None, null, null);

    private async Task<Fetch> GetAsync(Uri address, int maximumBytes, CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);
        Uri current = address;
        for (int hop = 0; hop <= MaximumRedirects; hop++)
        {
            if (!IsAllowed(current)) return new(HttpStatusCode.Forbidden, null);
            using HttpRequestMessage request = new(HttpMethod.Get, current);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("PCSteward", null));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(current.Host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase) ? "application/vnd.github+json" : "application/octet-stream"));
            if (current.Host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase)) request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
            using HttpResponseMessage response = await invoker.SendAsync(request, timeout.Token).ConfigureAwait(false);
            int code = (int)response.StatusCode;
            if (code is 301 or 302 or 303 or 307 or 308)
            {
                Uri? location = response.Headers.Location;
                if (location is null) return new(response.StatusCode, null);
                current = location.IsAbsoluteUri ? location : new Uri(current, location);
                continue;
            }
            if (response.StatusCode != HttpStatusCode.OK) return new(response.StatusCode, null);
            if (response.Content.Headers.ContentLength is long length && (length < 0 || length > maximumBytes)) return new(HttpStatusCode.RequestEntityTooLarge, null);
            byte[]? body = await ReadCappedAsync(response.Content, maximumBytes, timeout.Token).ConfigureAwait(false);
            return body is null ? new(HttpStatusCode.RequestEntityTooLarge, null) : new(HttpStatusCode.OK, body);
        }
        return new(HttpStatusCode.LoopDetected, null);
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

/// <summary>Owner-facing sentences for each refusal (ADR-032 section 4: "Update found but not trusted" with the reason).</summary>
public static class ReleaseRefusalText
{
    public static string Describe(ReleaseRefusal refusal) => refusal switch
    {
        ReleaseRefusal.DevelopmentBuild => "This is a development build, so it does not update itself.",
        ReleaseRefusal.NoReleaseKey => "This version has no release signing key built in, so it cannot confirm any release.",
        ReleaseRefusal.MissingReleaseFiles => "The release has no PC Steward signature files.",
        ReleaseRefusal.SignatureUnreadable => "The release signature file is not in the expected format.",
        ReleaseRefusal.UnknownKey => "The release was signed with a key this version does not trust.",
        ReleaseRefusal.SignatureInvalid => "The release description does not match its signature.",
        ReleaseRefusal.ManifestUnreadable => "The release description is not in the expected format.",
        ReleaseRefusal.KeyMismatch => "The release description names a different key from the one that signed it.",
        ReleaseRefusal.WrongProduct => "The release is not for PC Steward.",
        ReleaseRefusal.WrongChannel => "The release is for a different release channel.",
        ReleaseRefusal.WrongSchema => "The release uses a description format this version does not understand.",
        ReleaseRefusal.TagMismatch => "The release's tag does not match the version it was signed for.",
        ReleaseRefusal.AssetNameMismatch => "The release names an unexpected download file.",
        ReleaseRefusal.OlderOrRepeatedRelease => "The release is older than, or a repeat of, one this PC has already seen.",
        ReleaseRefusal.UpdaterTooOld => "This version is too old to install that release directly. Update by hand from the release page.",
        ReleaseRefusal.UnexpectedError => "Something unexpected happened while checking the release, so it was not trusted.",
        _ => "The release could not be confirmed.",
    };

    public static string Describe(SelfUpdateCheck check)
    {
        ArgumentNullException.ThrowIfNull(check);
        return check.Status switch
        {
            SelfUpdateCheckStatus.NewerRelease => "PC Steward " + check.Manifest!.Version.Text + " is available and its signature checks out.",
            SelfUpdateCheckStatus.NoNewerRelease => "No newer verified release. The newest signed release is " + check.Manifest!.Version.Text + ".",
            SelfUpdateCheckStatus.NoReleasesPublished => "No PC Steward release has been published yet.",
            SelfUpdateCheckStatus.NotTrusted => "Update found but not trusted. " + Describe(check.Refusal),
            _ => "Could not check for PC Steward updates. Nothing was changed.",
        };
    }
}
