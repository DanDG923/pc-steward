using System.Collections.Immutable;
using System.Text;
using PCSteward.Domain;

namespace PCSteward.Application;

/// <summary>Why a pasted key was refused. The key itself is never logged or echoed.</summary>
public enum AdvisoryAiKeyProblem
{
    None,
    Empty,
    ContainsWhitespace,
    NotPrintableAscii,
    TooLong
}

/// <summary>
/// FR-AI-03 (ADR-028 Q8): one API key held only as long as needed. It is never shown, logged, saved by PC Steward or included in
/// any exception or text: <see cref="ToString"/> returns a placeholder. Dispose clears the characters (no guarantee is made for
/// copies the runtime or the operating system may have made).
/// </summary>
public sealed class AdvisoryAiApiKey : IDisposable
{
    /// <summary>ADR-028 Q8: the most UTF-8 bytes a key may have. Larger keys are refused, never cut.</summary>
    public const int MaximumUtf8Bytes = 2048;

    private readonly char[] value;
    private bool disposed;

    private AdvisoryAiApiKey(char[] value) => this.value = value;

    /// <summary>
    /// ADR-028 Q8: accepts a key that is not empty, has no whitespace, is printable ASCII (it travels in an HTTP header) and is at
    /// most 2,048 bytes. The caller's buffer is copied; the caller clears its own.
    /// </summary>
    public static AdvisoryAiKeyProblem TryCreate(ReadOnlySpan<char> text, out AdvisoryAiApiKey? key)
    {
        key = null;
        if (text.IsEmpty) return AdvisoryAiKeyProblem.Empty;
        foreach (char character in text)
        {
            if (char.IsWhiteSpace(character)) return AdvisoryAiKeyProblem.ContainsWhitespace;
            if (character is < '!' or > '~') return AdvisoryAiKeyProblem.NotPrintableAscii;
        }
        if (Encoding.UTF8.GetByteCount(text) > MaximumUtf8Bytes) return AdvisoryAiKeyProblem.TooLong;
        key = new(text.ToArray());
        return AdvisoryAiKeyProblem.None;
    }

    public int Length => disposed ? throw new ObjectDisposedException(nameof(AdvisoryAiApiKey)) : value.Length;

    /// <summary>The key's characters, for the credential adapter's write and the transport's authorization header only.</summary>
    public ReadOnlySpan<char> Reveal() => disposed ? throw new ObjectDisposedException(nameof(AdvisoryAiApiKey)) : value;

    public override string ToString() => "[API key hidden]";

    public void Dispose()
    {
        if (disposed) return;
        Array.Clear(value);
        disposed = true;
    }
}

/// <summary>Whether the exact PC Steward credential exists. Checked only in a deliberate connect flow, never at ordinary startup.</summary>
public enum AdvisoryAiCredentialPresence
{
    Absent,
    Present,
    Unknown
}

/// <summary>FR-AI-03 (ADR-028 Q8): the result of removing the exact credential. Only a verified absence is reported as removed.</summary>
public enum AdvisoryAiCredentialRemoval
{
    /// <summary>Deleted, and a fresh read confirmed it is gone.</summary>
    Removed,

    /// <summary>It was not there, which is the same end state.</summary>
    AlreadyAbsent,

    /// <summary>Access, logon or unknown failure, or still present afterwards: shown as "credential removal incomplete".</summary>
    Incomplete
}

public enum AdvisoryAiCredentialWrite
{
    Saved,
    Failed
}

/// <summary>
/// FR-AI-03 (ADR-028 Q8): the one exact current-user credential. The adapter knows only <see cref="Target"/>: it never lists,
/// searches or deletes any other credential, has no prefix or wildcard form, and is composed only by the desktop app. Saving makes no
/// network call and no billable "test" request. Removing does not revoke the key at the provider.
/// </summary>
public interface IAdvisoryAiCredentialStore
{
    /// <summary>The exact generic credential target. A future rename needs an explicit, tested migration list.</summary>
    public const string Target = "PCSteward/OpenAI/ApiKey.v1";

    Task<AdvisoryAiCredentialWrite> SaveAsync(AdvisoryAiApiKey key, CancellationToken cancellationToken);

    Task<AdvisoryAiCredentialPresence> CheckPresenceAsync(CancellationToken cancellationToken);

    /// <summary>Reads the key only after every local send prerequisite has passed. The caller disposes it as soon as the request is sent.</summary>
    Task<AdvisoryAiApiKey?> ReadForDispatchAsync(CancellationToken cancellationToken);

    Task<AdvisoryAiCredentialRemoval> RemoveAndVerifyAsync(CancellationToken cancellationToken);
}

/// <summary>The closed result of one transport attempt.</summary>
public enum AdvisoryAiTransportOutcome
{
    /// <summary>A complete body within the admitted size arrived with this HTTP status.</summary>
    ResponseReceived,

    /// <summary>The transport proved nothing left the PC (for example the approval no longer matched the body).</summary>
    NotSent,
    ConnectFailed,
    TimedOut,
    Cancelled,

    /// <summary>A 3xx: redirects are never followed, so the request and key went nowhere else.</summary>
    Redirected,
    ResponseTooLarge,
    Failed
}

/// <summary>
/// FR-AI-04 (ADR-028 Q1): one attempt's result. <see cref="Body"/> holds the complete response body only for
/// <see cref="AdvisoryAiTransportOutcome.ResponseReceived"/>. No header, exception text or key is ever carried.
/// </summary>
public sealed record AdvisoryAiTransportResult(AdvisoryAiTransportOutcome Outcome, int? HttpStatus, ImmutableArray<byte> Body)
{
    public static AdvisoryAiTransportResult Of(AdvisoryAiTransportOutcome outcome) => new(outcome, null, []);
}

/// <summary>
/// FR-AI-04 (ADR-028 Q1/F11): sends one approved body once to <see cref="AdvisoryAiRoute.Endpoint"/>: exact approved bytes, the
/// closed header profile, no automatic retry, no redirect, certificate validation on, 10-second connect and 45-second overall
/// deadlines, response read with the admitted size bound. Composed only by the desktop app; the scheduled host, the elevated helper
/// and update execution have no AI transport or credential capability.
/// </summary>
public interface IAdvisoryAiTransport
{
    Task<AdvisoryAiTransportResult> SendOnceAsync(AdvisoryAiApproval approval, AdvisoryAiApiKey key, CancellationToken cancellationToken);
}

/// <summary>
/// FR-AI-01/02: which recorded update jobs can be explained, and the facts taken from PC Steward's own history for one of them.
/// Only the selected job's recorded events are used; its diagnostics are the already sanitized summaries PC Steward recorded.
/// </summary>
public static class AdvisoryAiFailedUpdateEvidence
{
    /// <summary>An update that ended in a failure, a timeout or an inconclusive result. A cancelled update or a declined UAC prompt has nothing to explain.</summary>
    public static bool IsExplainable(UpdateHistoryStatus status) => status is
        UpdateHistoryStatus.SourceFailed or UpdateHistoryStatus.DownloadFailed or UpdateHistoryStatus.InstallerFailed or
        UpdateHistoryStatus.TimedOut or UpdateHistoryStatus.VerificationFailed or UpdateHistoryStatus.ProductFailed or
        UpdateHistoryStatus.Inconclusive;

    public static AdvisoryAiFailedUpdateFacts Facts(UpdateJobRecord job, IEnumerable<UpdateHistoryEntry> history, string? displayName)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(history);
        AdvisoryAiFailedUpdateEvent[] events = [.. history
            .Where(entry => entry.JobId == job.JobId)
            .OrderBy(entry => entry.OccurredAt.UtcTicks)
            .Select(entry => new AdvisoryAiFailedUpdateEvent(entry.OccurredAt.ToUniversalTime(), entry.Status.ToString(), entry.ExitCode,
                string.IsNullOrWhiteSpace(entry.Diagnostic) ? null : entry.Diagnostic))];
        string app = string.IsNullOrWhiteSpace(displayName) ? job.PackageId : displayName;
        return new(app, job.Provider.ToString(), job.InstalledVersion, job.TargetVersion, job.Final.Status.ToString(), job.Final.ExitCode, events);
    }
}
