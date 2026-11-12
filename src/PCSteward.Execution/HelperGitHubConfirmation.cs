using System.Net;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace PCSteward.Execution;

/// <summary>The helper's answer to "is this check file the artefact of a genuine CI run?".</summary>
internal enum HelperGitHubVerdict
{
    Confirmed,
    RunNotGenuine,
    ArtifactGone,
    ArtifactMismatch,
    CouldNotReach,
}

/// <summary>
/// FR-UPD-15, TM-20, TM-23, TM-24; ADR-032 revision 2 §6 step 3 (owner decision 2, "Yes, helper asks GitHub"): the elevated
/// helper's own confirmation of the CI anchor. Exactly two unauthenticated GETs to compiled addresses,
/// <c>https://api.github.com/repos/DanDG923/pc-steward/actions/runs/{runId}</c> and its <c>/artifacts?name=…</c>, judged by
/// the same <see cref="ReleaseRunEvidence"/> rules as the desktop. Nothing a same-user program controls can steer it:
/// <list type="bullet">
/// <item>no proxy (the user's settings and environment are ignored), no cookies, no credentials, no redirects (any 3xx is a
/// refusal), no compression;</item>
/// <item>TLS 1.2 or 1.3 to <c>api.github.com</c> only, with the chain built to a root in the machine's <c>Root</c> or
/// <c>AuthRoot</c> store (administrator-only), never the user's own store, and revocation checked online: a chain or
/// revocation error is a refusal;</item>
/// <item>each answer at most <see cref="ReleaseRunEvidence.MaximumAnswerBytes"/>, both within <see cref="Budget"/>.</item>
/// </list>
/// The run id is the number from the check record; nothing GitHub returns is used as an address, path, command or argument.
/// </summary>
internal sealed class HelperGitHubConfirmation : IDisposable
{
    internal const string Host = "api.github.com";
    internal static readonly Uri RepositoryApi = new("https://" + Host + "/repos/" + ReleaseCheckPolicy.Repository + "/");
    internal static readonly TimeSpan Budget = TimeSpan.FromSeconds(30);

    private readonly HttpClient client;

    internal HelperGitHubConfirmation() : this(CreateHandler()) { }

    internal HelperGitHubConfirmation(HttpMessageHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        client = new HttpClient(handler, disposeHandler: true) { Timeout = Budget, MaxResponseContentBufferSize = ReleaseRunEvidence.MaximumAnswerBytes };
    }

    /// <summary>The address of the run answer; <paramref name="runId"/> is formatted as a positive decimal number.</summary>
    internal static Uri RunAddress(long runId) =>
        runId > 0 ? new Uri(RepositoryApi, "actions/runs/" + runId.ToString(System.Globalization.CultureInfo.InvariantCulture))
            : throw new ArgumentOutOfRangeException(nameof(runId));

    internal static Uri ArtifactsAddress(long runId) => new(RunAddress(runId) + "/artifacts?name=" + ReleaseCheckPolicy.ArtifactName);

    internal async Task<HelperGitHubVerdict> ConfirmAsync(long runId, string sourceCommit, string checkZipSha256, CancellationToken cancellationToken)
    {
        using CancellationTokenSource budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(Budget);
        byte[]? run = await GetAsync(RunAddress(runId), budget.Token).ConfigureAwait(false);
        if (run is null) return HelperGitHubVerdict.CouldNotReach;
        if (ReleaseRunEvidence.CheckRun(run, runId, sourceCommit) != ReleaseRunVerdict.Genuine) return HelperGitHubVerdict.RunNotGenuine;
        byte[]? artifacts = await GetAsync(ArtifactsAddress(runId), budget.Token).ConfigureAwait(false);
        if (artifacts is null) return HelperGitHubVerdict.CouldNotReach;
        return ReleaseRunEvidence.CheckArtifact(artifacts, checkZipSha256) switch
        {
            ReleaseRunVerdict.Genuine => HelperGitHubVerdict.Confirmed,
            ReleaseRunVerdict.ArtifactMismatch => HelperGitHubVerdict.ArtifactMismatch,
            _ => HelperGitHubVerdict.ArtifactGone,
        };
    }

    public void Dispose() => client.Dispose();

    /// <summary>One GET; null for anything but a complete 200 answer within the size bound.</summary>
    private async Task<byte[]?> GetAsync(Uri address, CancellationToken cancellationToken)
    {
        try
        {
            using HttpRequestMessage request = new(HttpMethod.Get, address) { Version = HttpVersion.Version11, VersionPolicy = HttpVersionPolicy.RequestVersionOrLower };
            request.Headers.UserAgent.ParseAdd("PCSteward");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK) return null;
            if (response.Content.Headers.ContentLength is > ReleaseRunEvidence.MaximumAnswerBytes) return null;
            await using Stream body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            byte[] buffer = new byte[ReleaseRunEvidence.MaximumAnswerBytes + 1];
            int total = 0;
            while (total < buffer.Length)
            {
                int read = await body.ReadAsync(buffer.AsMemory(total), cancellationToken).ConfigureAwait(false);
                if (read == 0) break;
                total += read;
            }
            return total is 0 or > ReleaseRunEvidence.MaximumAnswerBytes ? null : buffer[..total];
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or IOException or AuthenticationException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>The constrained handler: see the class summary. Only the machine's root stores anchor the chain.</summary>
    internal static SocketsHttpHandler CreateHandler()
    {
        X509Certificate2Collection roots = MachineRoots();
        return new SocketsHttpHandler
        {
            UseProxy = false,
            Proxy = null,
            UseCookies = false,
            Credentials = null,
            PreAuthenticate = false,
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            ConnectTimeout = TimeSpan.FromSeconds(15),
            MaxConnectionsPerServer = 1,
            MaxResponseHeadersLength = 64,
            PooledConnectionLifetime = Budget,
            SslOptions = new SslClientAuthenticationOptions
            {
                TargetHost = Host,
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                CertificateRevocationCheckMode = X509RevocationMode.Online,
                CertificateChainPolicy = MachineRootPolicy(roots),
                RemoteCertificateValidationCallback = (_, _, _, errors) => errors == SslPolicyErrors.None,
            },
        };
    }

    /// <summary>A chain policy that trusts only <paramref name="roots"/>, with online revocation for the whole chain.</summary>
    internal static X509ChainPolicy MachineRootPolicy(X509Certificate2Collection roots)
    {
        ArgumentNullException.ThrowIfNull(roots);
        X509ChainPolicy policy = new()
        {
            TrustMode = X509ChainTrustMode.CustomRootTrust,
            RevocationMode = X509RevocationMode.Online,
            RevocationFlag = X509RevocationFlag.EntireChain,
            VerificationFlags = X509VerificationFlags.NoFlag,
            UrlRetrievalTimeout = TimeSpan.FromSeconds(10),
        };
        policy.CustomTrustStore.AddRange(roots);
        return policy;
    }

    /// <summary>
    /// The machine's trusted roots: <c>LocalMachine\Root</c> and <c>LocalMachine\AuthRoot</c> (where Windows places roots it
    /// fetches through automatic root update). Only administrators can change either store.
    /// </summary>
    private static X509Certificate2Collection MachineRoots()
    {
        X509Certificate2Collection roots = [];
        foreach (StoreName name in new[] { StoreName.Root, StoreName.AuthRoot })
        {
            using X509Store store = new(name, StoreLocation.LocalMachine, OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
            roots.AddRange(store.Certificates);
        }
        return roots;
    }
}
