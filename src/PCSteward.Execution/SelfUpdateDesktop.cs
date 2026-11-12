using System.Globalization;
using System.Text;
using System.Text.Json;

namespace PCSteward.Execution;

/// <summary>
/// The desktop's record of one owner-approved update (ADR-032 revision 2 §5 and §8), kept in the per-user
/// <c>downloads\SelfUpdate</c> folder from the helper's "ready" answer until the job is decided. It is the desktop's own
/// note, not evidence: the decision reads only the protected work folder it names and PC Steward's own install.
/// </summary>
internal sealed record SelfUpdateDesktopJob(Guid JobId, string FromVersion, string ToVersion, long RunId, string SourceCommit, string CheckZipSha256,
    string MsiSha256, DateTimeOffset ApprovedAt, string WorkFolder, string ManifestSha256);

/// <summary>Where an update job stands when PC Steward opens (ADR-032 revision 2 §8).</summary>
internal enum SelfUpdateJobState
{
    /// <summary>The helper copy has not written its result yet and the install window has not passed.</summary>
    InProgress,
    /// <summary>The reopened PC Steward is the new version, its install is intact, and the install was verified.</summary>
    Updated,
    /// <summary>Windows asked for a restart and the PC has not restarted since.</summary>
    RestartToFinish,
    /// <summary>The update was not installed, was rolled back or removed, or the installed tree did not pass the check.</summary>
    Failed,
    /// <summary>The work folder or its files are missing or unreadable.</summary>
    Inconclusive,
}

/// <summary>
/// One decision: the state and a fixed lowercase code naming why (shown to the owner through compiled text only).
/// <paramref name="BlocksUpdates"/>: ADR-032 §8, the install may differ from a signed release, so no further self-update is
/// offered and the job is kept (and decided again at every launch) until a signed release is installed again.
/// </summary>
internal sealed record SelfUpdateJobVerdict(SelfUpdateJobState State, string Code, bool BlocksUpdates = false)
{
    /// <summary>The job is over; the desktop forgets it and removes its downloads once the owner has seen the verdict.</summary>
    internal bool IsFinal => !BlocksUpdates && State is SelfUpdateJobState.Updated or SelfUpdateJobState.Failed or SelfUpdateJobState.Inconclusive;
}

/// <summary>What the reopened desktop can see, read once per launch (§8). Every file read is bounded and read-only.</summary>
/// <param name="RunningVersion">This process's own MSI version (assembly version, three parts).</param>
/// <param name="InstallIntact">The Desktop-role release check of this install (ADR-034 §4, no route) is Supported.</param>
/// <param name="InstalledManifestSha256">SHA-256 of the installed release manifest, or null when unreadable.</param>
/// <param name="WorkFolderProtected">The job's work folder carries the protected descriptor, owner and no reparse point.</param>
/// <param name="ResultBytes">The work folder's <c>result.json</c>, or null when absent or unreadable.</param>
/// <param name="ExpectedBytes">The work folder's <c>expected-install.json</c>, or null when absent or unreadable.</param>
/// <param name="BootTimeUnixSeconds">The current boot time.</param>
/// <param name="InstalledTreeMatches">PC Steward's own read-only tree and DACL check against the expected file; run only after a restart.</param>
/// <param name="Now">The current time.</param>
internal sealed record SelfUpdateOutcomeInputs(string RunningVersion, bool InstallIntact, string? InstalledManifestSha256, bool WorkFolderProtected,
    byte[]? ResultBytes, byte[]? ExpectedBytes, long BootTimeUnixSeconds, Func<SelfUpdateExpectedInstall, bool> InstalledTreeMatches, DateTimeOffset Now);

/// <summary>
/// FR-UPD-04/15, TM-12/26; ADR-032 revision 2 §5 and §8: the desktop's side of PC Steward's own update, with no I/O.
/// <list type="bullet">
/// <item>The session line the desktop asks Windows to elevate (the only <c>src</c> builder; the helper parses it back with
/// <see cref="SelfUpdateHelperProtocol.TryParseSession"/>).</item>
/// <item>The helper's one answer, accepted only with this launch's nonce, the job's work folder and to-version.</item>
/// <item>The desktop job file, canonical and read back strictly.</item>
/// <item><see cref="Decide"/>: Updated only when the reopened PC Steward is the to-version, its install is intact, its
/// installed manifest is the expected one, and either the helper copy verified the install or, after a restart, PC Steward's
/// own read-only check passes. Inconclusive only when the work folder or its files are missing or unreadable.</item>
/// </list>
/// </summary>
internal static partial class SelfUpdateDesktop
{
    internal const string FolderName = "SelfUpdate";
    internal const string JobFileName = "job.json";
    internal const string JobSchema = "pc-steward-self-update-desktop-job:1";
    internal const int MaximumJobBytes = 4 * 1024;

    /// <summary>How long a job without a result counts as still installing: the close wait plus the install limit, with margin.</summary>
    internal static readonly TimeSpan InstallWindow = SelfUpdateFinish.CloseWait + SelfUpdateFinish.InstallLimit + TimeSpan.FromMinutes(10);

    /// <summary>A boot time must be this much later than the recorded one to count as a restart (the two are derived from clocks).</summary>
    internal const long RestartMarginSeconds = 120;

    private static readonly HashSet<string> JobKeys =
    [
        "schema", "jobId", "fromVersion", "toVersion", "runId", "sourceCommit", "checkZipSha256", "msiSha256", "approvedAt", "workFolder", "manifestSha256",
    ];

    /// <summary>The ten-argument session line: kind first, so the UAC details show it right after the program path.</summary>
    internal static IReadOnlyList<string> SessionArguments(int desktopProcessId, string pipeName, string nonce)
    {
        string[] arguments =
        [
            SelfUpdateHelperProtocol.KindKey, SelfUpdateHelperProtocol.Kind,
            "--protocol", ReviewedUpdateWireCodec.ProtocolVersion,
            "--desktop-pid", desktopProcessId.ToString(CultureInfo.InvariantCulture),
            "--pipe", pipeName ?? string.Empty,
            "--nonce", nonce ?? string.Empty,
        ];
        if (!SelfUpdateHelperProtocol.TryParseSession(arguments, out _, out _, out _))
            throw new ArgumentException("The session line values are not well-formed.");
        return arguments;
    }

    /// <summary>The helper's answer for this launch: its nonce, and for "ready" the job's own work folder and to-version.</summary>
    internal static bool TryAcceptResponse(ReadOnlySpan<byte> frame, SelfUpdateRequest request, out SelfUpdateResponse? response)
    {
        ArgumentNullException.ThrowIfNull(request);
        response = null;
        if (!SelfUpdateHelperProtocol.TryReadResponse(frame, out SelfUpdateResponse? candidate) || candidate is null ||
            !string.Equals(candidate.Nonce, request.Nonce, StringComparison.Ordinal))
            return false;
        if (candidate.Ready &&
            (!string.Equals(candidate.WorkFolder, SelfUpdateInstallPolicy.WorkFolderName(request.JobId), StringComparison.Ordinal) ||
             !string.Equals(candidate.ToVersion, request.ToVersion, StringComparison.Ordinal) ||
             !SelfUpdateHelperProtocol.IsUpperHex64(candidate.ManifestSha256)))
            return false;
        response = candidate;
        return true;
    }

    // ---- The desktop job file ----

    internal static byte[] WriteJob(SelfUpdateDesktopJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        if (!IsWellFormed(job)) throw new ArgumentException("The desktop job values are not well-formed.", nameof(job));
        string json = "{\"schema\":" + Quote(JobSchema) + ",\"jobId\":" + Quote(job.JobId.ToString("D", CultureInfo.InvariantCulture)) +
            ",\"fromVersion\":" + Quote(job.FromVersion) + ",\"toVersion\":" + Quote(job.ToVersion) +
            ",\"runId\":" + job.RunId.ToString(CultureInfo.InvariantCulture) + ",\"sourceCommit\":" + Quote(job.SourceCommit) +
            ",\"checkZipSha256\":" + Quote(job.CheckZipSha256) + ",\"msiSha256\":" + Quote(job.MsiSha256) +
            ",\"approvedAt\":" + job.ApprovedAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture) +
            ",\"workFolder\":" + Quote(job.WorkFolder) + ",\"manifestSha256\":" + Quote(job.ManifestSha256) + "}";
        return Encoding.ASCII.GetBytes(json);
    }

    internal static bool TryReadJob(ReadOnlySpan<byte> bytes, out SelfUpdateDesktopJob? job)
    {
        job = null;
        if (bytes.Length is 0 or > MaximumJobBytes) return false;
        try
        {
            _ = DeploymentCapabilityPolicy.StrictUtf8.GetString(bytes);
            using JsonDocument document = JsonDocument.Parse(bytes.ToArray(), DeploymentCapabilityPolicy.JsonOptions);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !DeploymentCapabilityPolicy.TryCollect(document.RootElement, out Dictionary<string, JsonElement> fields) ||
                !fields.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(JobKeys) ||
                !DeploymentCapabilityPolicy.TryString(fields["schema"], 64, out string schema) || schema != JobSchema ||
                !DeploymentCapabilityPolicy.TryString(fields["jobId"], 36, out string jobText) || !Guid.TryParseExact(jobText, "D", out Guid jobId) ||
                !DeploymentCapabilityPolicy.TryString(fields["fromVersion"], 32, out string from) ||
                !DeploymentCapabilityPolicy.TryString(fields["toVersion"], 32, out string to) ||
                fields["runId"].ValueKind != JsonValueKind.Number || !fields["runId"].TryGetInt64(out long runId) ||
                !DeploymentCapabilityPolicy.TryString(fields["sourceCommit"], 40, out string commit) ||
                !DeploymentCapabilityPolicy.TryString(fields["checkZipSha256"], 64, out string zip) ||
                !DeploymentCapabilityPolicy.TryString(fields["msiSha256"], 64, out string msi) ||
                fields["approvedAt"].ValueKind != JsonValueKind.Number || !fields["approvedAt"].TryGetInt64(out long approved) ||
                approved is < 0 or > 253402300799 ||
                !DeploymentCapabilityPolicy.TryString(fields["workFolder"], 64, out string folder) ||
                !DeploymentCapabilityPolicy.TryString(fields["manifestSha256"], 64, out string manifest))
                return false;
            SelfUpdateDesktopJob candidate = new(jobId, from, to, runId, commit, zip, msi, DateTimeOffset.FromUnixTimeSeconds(approved), folder, manifest);
            if (!IsWellFormed(candidate) || !bytes.SequenceEqual(WriteJob(candidate))) return false;
            job = candidate;
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or DecoderFallbackException or JsonException or FormatException or
            InvalidOperationException or KeyNotFoundException)
        {
            return false;
        }
    }

    /// <summary>The job's values satisfy the request rules (no path, upper-case hashes) and name the job's own work folder.</summary>
    private static bool IsWellFormed(SelfUpdateDesktopJob job) =>
        ReleaseCheckPolicy.IsMsiVersion(job.FromVersion) &&
        SelfUpdateInstallPolicy.TryReadRequest(SelfUpdateInstallPolicy.WriteRequest(new(new string('0', 64), job.JobId, job.ToVersion, job.RunId,
            job.SourceCommit, job.CheckZipSha256, job.MsiSha256)), out _) &&
        string.Equals(job.WorkFolder, SelfUpdateInstallPolicy.WorkFolderName(job.JobId), StringComparison.Ordinal) &&
        SelfUpdateHelperProtocol.IsUpperHex64(job.ManifestSha256);

    // ---- §8: the decision ----

    internal static SelfUpdateJobVerdict Decide(SelfUpdateDesktopJob job, SelfUpdateOutcomeInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(inputs);
        if (!inputs.WorkFolderProtected) return new(SelfUpdateJobState.Inconclusive, "work-folder-unavailable");
        if (inputs.ExpectedBytes is null || !SelfUpdateInstallPolicy.TryReadExpected(inputs.ExpectedBytes, out SelfUpdateExpectedInstall? expected) ||
            expected is null || expected.JobId != job.JobId || !string.Equals(expected.ToVersion, job.ToVersion, StringComparison.Ordinal) ||
            !string.Equals(expected.ManifestSha256, job.ManifestSha256, StringComparison.Ordinal))
            return new(SelfUpdateJobState.Inconclusive, "expected-install-unreadable");
        if (inputs.ResultBytes is null)
            return inputs.Now - job.ApprovedAt < InstallWindow && inputs.Now >= job.ApprovedAt
                ? new(SelfUpdateJobState.InProgress, "installing")
                : new(SelfUpdateJobState.Inconclusive, "no-result");
        if (!SelfUpdateHelperProtocol.TryReadResult(inputs.ResultBytes, out SelfUpdateResultFile? result) || result is null ||
            result.JobId != job.JobId || !string.Equals(result.ToVersion, job.ToVersion, StringComparison.Ordinal) ||
            !string.Equals(result.ManifestSha256, expected.ManifestSha256, StringComparison.Ordinal))
            return new(SelfUpdateJobState.Inconclusive, "result-unreadable");

        switch (result.Outcome)
        {
            // A difference in the running install blocks further updates until a signed release is installed again (checked at
            // every launch): see Reinstalled.
            case SelfUpdateFinishOutcome.Installed:
                return RunningAsExpected(job, expected, inputs) is { } installedDifference
                    ? new(SelfUpdateJobState.Failed, installedDifference, BlocksUpdates: !Reinstalled(expected, inputs))
                    : new(SelfUpdateJobState.Updated, "installed");
            case SelfUpdateFinishOutcome.RestartNeeded:
                if (result.BootTimeUnixSeconds is not { } recorded || inputs.BootTimeUnixSeconds <= recorded + RestartMarginSeconds)
                    return new(SelfUpdateJobState.RestartToFinish, "restart-needed");
                if (RunningAsExpected(job, expected, inputs) is { } restartedDifference)
                    return new(SelfUpdateJobState.Failed, restartedDifference, BlocksUpdates: !Reinstalled(expected, inputs));
                return TreeMatches(expected, inputs)
                    ? new(SelfUpdateJobState.Updated, "installed-after-restart")
                    : new(SelfUpdateJobState.Failed, "installed-tree-differs", BlocksUpdates: !Reinstalled(expected, inputs));
            case SelfUpdateFinishOutcome.InstallFailed:
                return new(SelfUpdateJobState.Failed, "install-failed");
            // The finish copy found an install unlike the CI build. That blocks further updates until a signed release is
            // installed again: an intact install of another release, or one whose whole tree and permissions match the expected one.
            case SelfUpdateFinishOutcome.MismatchUninstalled:
                return new(SelfUpdateJobState.Failed, "mismatch-uninstalled", BlocksUpdates: !Reinstalled(expected, inputs));
            case SelfUpdateFinishOutcome.MismatchUninstallFailed:
                return new(SelfUpdateJobState.Failed, "mismatch-uninstall-failed", BlocksUpdates: !Reinstalled(expected, inputs));
            case SelfUpdateFinishOutcome.PcStewardDidNotClose:
                return new(SelfUpdateJobState.Failed, "pc-steward-did-not-close");
            case SelfUpdateFinishOutcome.RefusedBeforeInstall:
                return new(SelfUpdateJobState.Failed, "refused-before-install");
            default:
                return new(SelfUpdateJobState.Inconclusive, "result-unreadable");
        }
    }

    private static bool TreeMatches(SelfUpdateExpectedInstall expected, SelfUpdateOutcomeInputs inputs)
    {
        try { return inputs.InstalledTreeMatches(expected); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { return false; }
    }

    /// <summary>
    /// A signed release is installed again: the running install is intact (pinned executables, protected binding set) and is
    /// either another release or exactly the expected tree with its permissions.
    /// </summary>
    private static bool Reinstalled(SelfUpdateExpectedInstall expected, SelfUpdateOutcomeInputs inputs) =>
        inputs.InstallIntact && inputs.InstalledManifestSha256 is { } manifest &&
        (!string.Equals(manifest, expected.ManifestSha256, StringComparison.Ordinal) || TreeMatches(expected, inputs));

    /// <summary>Null when this process is the to-version, its install is intact and its manifest is the expected one; otherwise a code.</summary>
    private static string? RunningAsExpected(SelfUpdateDesktopJob job, SelfUpdateExpectedInstall expected, SelfUpdateOutcomeInputs inputs)
    {
        if (!string.Equals(inputs.RunningVersion, job.ToVersion, StringComparison.Ordinal)) return "running-version-differs";
        if (!inputs.InstallIntact) return "install-not-intact";
        if (!string.Equals(inputs.InstalledManifestSha256, expected.ManifestSha256, StringComparison.Ordinal)) return "installed-manifest-differs";
        return null;
    }

    private static string Quote(string value) => MsiTableDigest.Quote(value);
}
