using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PCSteward.Execution;

/// <summary>
/// Why the elevated self-update session refused (ADR-032 revision 2 §6). Each value has one fixed code
/// (<see cref="SelfUpdateHelperProtocol.Code(SelfUpdateSessionRefusal)"/>), which is all the response carries: never a path, a hash or text from a file.
/// </summary>
internal enum SelfUpdateSessionRefusal
{
    InstallNotIntact,
    VersionNotNewer,
    PerUserRegistration,
    ProfileUnavailable,
    InputUnreadable,
    InputChanged,
    WorkFolderUnavailable,
    CheckFileRefused,
    RecordNotApproved,
    RunNotGenuine,
    ArtifactGone,
    ArtifactMismatch,
    CouldNotConfirm,
    MsiNotPinned,
    MsiUnreadable,
    MsiStructureRefused,
    MsiTablesDiffer,
    ExtractFailed,
    ImageLayoutRefused,
    ContentDiffers,
    ExecutablesNotPinned,
    HelperCopyFailed,
    JobFilesFailed,
    FinishNotStarted,
    TimedOut,
    InternalFault,
}

/// <summary>What the finish copy recorded in <c>result.json</c> (ADR-032 revision 2 §7).</summary>
internal enum SelfUpdateFinishOutcome
{
    /// <summary>msiexec exited 0 and the installed tree, DACLs, pins and product registration all matched.</summary>
    Installed,
    /// <summary>msiexec exited 3010 or 1641; the desktop decides after the restart (§8).</summary>
    RestartNeeded,
    /// <summary>msiexec exited with another code; Windows Installer rolled back.</summary>
    InstallFailed,
    /// <summary>The install finished but did not match; the helper copy uninstalled it.</summary>
    MismatchUninstalled,
    /// <summary>The install finished but did not match, and the uninstall also failed.</summary>
    MismatchUninstallFailed,
    /// <summary>PC Steward did not close within the wait; nothing was installed.</summary>
    PcStewardDidNotClose,
    /// <summary>The finish copy refused before msiexec (its own checks or the MSI re-hash); nothing was installed.</summary>
    RefusedBeforeInstall,
}

/// <summary>The session's one response: ready (with the work folder leaf) or refused (with one code).</summary>
internal sealed record SelfUpdateResponse(string Nonce, bool Ready, string? WorkFolder, string? ToVersion, string? ManifestSha256, SelfUpdateSessionRefusal? Reason);

/// <summary>The job file the session writes for the finish copy: the approved request without its nonce, and the desktop PID.</summary>
internal sealed record SelfUpdateJobFile(Guid JobId, string ToVersion, long RunId, string SourceCommit, string CheckZipSha256, string MsiSha256, int DesktopProcessId,
    string ProductCode);

/// <summary>The finish copy's result: what happened, msiexec's exit code if it ran, and the boot time when a restart is needed.</summary>
internal sealed record SelfUpdateResultFile(Guid JobId, SelfUpdateFinishOutcome Outcome, int? ExitCode, string ToVersion, string ManifestSha256,
    long? BootTimeUnixSeconds, string Detail);

/// <summary>
/// FR-UPD-15, TM-03/04/12/13/20/23/26; ADR-032 revision 2 §6–§8: the pure wire and file formats of PC Steward's own update.
/// <list type="bullet">
/// <item>Command lines: the session line <c>--kind pcsteward-self-update --protocol 2 --desktop-pid &lt;n&gt; --pipe
/// &lt;name&gt; --nonce &lt;64 uppercase hex&gt;</c> (ten arguments, this order only) and the finish line
/// <c>--finish-self-update &lt;work folder leaf&gt;</c> (two arguments).</item>
/// <item>The response frame, <c>job.json</c> and <c>result.json</c>: canonical ASCII JSON, read back only in the exact bytes
/// their writer makes.</item>
/// </list>
/// No file, registry, environment, network or process access happens here.
/// </summary>
internal static partial class SelfUpdateHelperProtocol
{
    internal const string Kind = "pcsteward-self-update";
    internal const string KindKey = "--kind";
    internal const string FinishKey = "--finish-self-update";
    internal const string JobFileName = "job.json";
    internal const string ResultFileName = "result.json";
    internal const string HelperCopyFolder = "bin";
    internal const string ImageFolder = "image";

    /// <summary>
    /// Mythos review S2: the administrators-only folder inside the work folder that holds the input copies, the administrative
    /// image, every msiexec log and the temporary folder. Only the job files, <c>bin</c> and the result stay readable by users.
    /// </summary>
    internal const string PrivateFolder = "private";
    internal const string ExtractLogName = "extract.log";
    internal const string InstallLogName = "install.log";
    internal const string UninstallLogName = "uninstall.log";
    internal const string JobSchema = "pc-steward-self-update-job:1";
    internal const string ResultSchema = "pc-steward-self-update-result:1";
    internal const int MaximumFileBytes = 64 * 1024;
    internal const int MaximumDetailLength = 200;
    private const int SessionArgumentCount = 10;

    private static readonly string[] Codes =
    [
        "install-not-intact", "version-not-newer", "per-user-registration", "profile-unavailable", "input-unreadable", "input-changed", "work-folder-unavailable", "check-file-refused",
        "record-not-approved", "run-not-genuine", "artifact-gone", "artifact-mismatch", "could-not-confirm", "msi-not-pinned", "msi-unreadable",
        "msi-structure-refused", "msi-tables-differ", "extract-failed", "image-layout-refused", "content-differs", "executables-not-pinned",
        "helper-copy-failed", "job-files-failed", "finish-not-started", "timed-out", "internal-fault",
    ];

    private static readonly string[] Outcomes =
        ["installed", "restart-needed", "install-failed", "mismatch-uninstalled", "mismatch-uninstall-failed", "pc-steward-did-not-close", "refused-before-install"];

    private static readonly HashSet<string> ReadyKeys = ["nonce", "outcome", "workFolder", "toVersion", "manifestSha256"];
    private static readonly HashSet<string> RefusedKeys = ["nonce", "outcome", "reason"];
    private static readonly HashSet<string> JobKeys = ["schema", "jobId", "toVersion", "runId", "sourceCommit", "checkZipSha256", "msiSha256", "desktopPid", "productCode"];
    private static readonly HashSet<string> ResultKeys = ["schema", "jobId", "outcome", "exitCode", "toVersion", "manifestSha256", "bootTime", "detail"];

    internal static string Code(SelfUpdateSessionRefusal reason) =>
        Enum.IsDefined(reason) ? Codes[(int)reason] : Codes[(int)SelfUpdateSessionRefusal.InternalFault];

    internal static string Code(SelfUpdateFinishOutcome outcome) =>
        Enum.IsDefined(outcome) ? Outcomes[(int)outcome] : throw new ArgumentOutOfRangeException(nameof(outcome));

    // ---- Command lines ----

    /// <summary>Dispatch: exactly ten arguments naming this kind at indices 0–1. The full parse decides afterwards.</summary>
    internal static bool SelectsSession(string[]? arguments) =>
        arguments is { Length: SessionArgumentCount } && string.Equals(arguments[0], KindKey, StringComparison.Ordinal) &&
        string.Equals(arguments[1], Kind, StringComparison.Ordinal);

    /// <summary>Dispatch: exactly two arguments, the first the finish key. The full parse decides afterwards.</summary>
    internal static bool SelectsFinish(string[]? arguments) =>
        arguments is { Length: 2 } && string.Equals(arguments[0], FinishKey, StringComparison.Ordinal);

    internal static bool TryParseSession(string[]? arguments, out int desktopProcessId, out string pipeName, out string nonce)
    {
        desktopProcessId = 0;
        pipeName = string.Empty;
        nonce = string.Empty;
        if (arguments is null || !SelectsSession(arguments) || arguments.Any(static argument => argument is null) ||
            !string.Equals(arguments[2], "--protocol", StringComparison.Ordinal) ||
            !string.Equals(arguments[3], ReviewedUpdateWireCodec.ProtocolVersion, StringComparison.Ordinal) ||
            !string.Equals(arguments[4], "--desktop-pid", StringComparison.Ordinal) ||
            !string.Equals(arguments[6], "--pipe", StringComparison.Ordinal) ||
            !string.Equals(arguments[8], "--nonce", StringComparison.Ordinal) ||
            !int.TryParse(arguments[5], NumberStyles.None, CultureInfo.InvariantCulture, out int pid) || pid <= 0 ||
            !string.Equals(arguments[5], pid.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal) ||
            !PipeNamePattern().IsMatch(arguments[7]) || !IsUpperHex64(arguments[9]))
            return false;
        desktopProcessId = pid;
        pipeName = arguments[7];
        nonce = arguments[9];
        return true;
    }

    internal static bool TryParseFinish(string[]? arguments, out string workFolderLeaf)
    {
        workFolderLeaf = string.Empty;
        if (!SelectsFinish(arguments) || arguments![1] is not { } leaf || !SelfUpdateInstallPolicy.IsWorkFolderName(leaf)) return false;
        workFolderLeaf = leaf;
        return true;
    }

    /// <summary>The finish copy's command line, quoted for <c>CreateProcessW</c>; the leaf has no character that needs escaping.</summary>
    internal static string FinishCommandLine(string helperCopyPath, string workFolderLeaf)
    {
        if (string.IsNullOrEmpty(helperCopyPath) || helperCopyPath.Contains('"', StringComparison.Ordinal) || !Path.IsPathFullyQualified(helperCopyPath) ||
            !SelfUpdateInstallPolicy.IsWorkFolderName(workFolderLeaf))
            throw new ArgumentException("The finish copy needs a fully qualified path and a work folder leaf.");
        return "\"" + helperCopyPath + "\" " + FinishKey + " \"" + workFolderLeaf + "\"";
    }

    // ---- Response ----

    internal static byte[] WriteResponse(SelfUpdateResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (!IsUpperHex64(response.Nonce)) throw new ArgumentException("The response needs the launch nonce.", nameof(response));
        string json;
        if (response.Ready)
        {
            if (!SelfUpdateInstallPolicy.IsWorkFolderName(response.WorkFolder) || !ReleaseCheckPolicy.IsMsiVersion(response.ToVersion) ||
                !IsUpperHex64(response.ManifestSha256) || response.Reason is not null)
                throw new ArgumentException("A ready response needs the work folder, version and manifest hash only.", nameof(response));
            json = "{\"nonce\":" + Quote(response.Nonce) + ",\"outcome\":\"ready\",\"workFolder\":" + Quote(response.WorkFolder!) +
                ",\"toVersion\":" + Quote(response.ToVersion!) + ",\"manifestSha256\":" + Quote(response.ManifestSha256!) + "}";
        }
        else
        {
            if (response.Reason is not { } reason || !Enum.IsDefined(reason) || response.WorkFolder is not null || response.ToVersion is not null ||
                response.ManifestSha256 is not null)
                throw new ArgumentException("A refused response carries one reason only.", nameof(response));
            json = "{\"nonce\":" + Quote(response.Nonce) + ",\"outcome\":\"refused\",\"reason\":" + Quote(Code(reason)) + "}";
        }
        return Encoding.ASCII.GetBytes(json);
    }

    internal static bool TryReadResponse(ReadOnlySpan<byte> bytes, out SelfUpdateResponse? response)
    {
        response = null;
        if (bytes.Length is 0 or > MaximumFileBytes) return false;
        try
        {
            if (!TryObject(bytes, out JsonDocument? document, out Dictionary<string, JsonElement> fields)) return false;
            using (document)
            {
                if (!DeploymentCapabilityPolicy.TryString(fields.GetValueOrDefault("nonce"), 64, out string nonce) ||
                    !DeploymentCapabilityPolicy.TryString(fields.GetValueOrDefault("outcome"), 16, out string outcome))
                    return false;
                SelfUpdateResponse candidate;
                if (outcome == "ready" && KeysAre(fields, ReadyKeys) &&
                    DeploymentCapabilityPolicy.TryString(fields["workFolder"], 64, out string folder) &&
                    DeploymentCapabilityPolicy.TryString(fields["toVersion"], 32, out string version) &&
                    DeploymentCapabilityPolicy.TryString(fields["manifestSha256"], 64, out string manifest))
                    candidate = new(nonce, true, folder, version, manifest, null);
                else if (outcome == "refused" && KeysAre(fields, RefusedKeys) &&
                    DeploymentCapabilityPolicy.TryString(fields["reason"], 64, out string code) && Array.IndexOf(Codes, code) is int index and >= 0)
                    candidate = new(nonce, false, null, null, null, (SelfUpdateSessionRefusal)index);
                else return false;
                if (!bytes.SequenceEqual(WriteResponse(candidate))) return false;
                response = candidate;
                return true;
            }
        }
        catch (Exception exception) when (IsParseFailure(exception))
        {
            return false;
        }
    }

    // ---- job.json ----

    internal static byte[] WriteJob(SelfUpdateJobFile job)
    {
        ArgumentNullException.ThrowIfNull(job);
        // The request rules apply to the job values; the nonce stays with the launch.
        if (!SelfUpdateInstallPolicy.TryReadRequest(SelfUpdateInstallPolicy.WriteRequest(new(new string('0', 64), job.JobId, job.ToVersion, job.RunId,
                job.SourceCommit, job.CheckZipSha256, job.MsiSha256)), out _) || job.DesktopProcessId <= 0 || !MsiProductRegistration.IsProductCode(job.ProductCode))
            throw new ArgumentException("The job file values are not well-formed.", nameof(job));
        string json = "{\"schema\":" + Quote(JobSchema) + ",\"jobId\":" + Quote(Id(job.JobId)) + ",\"toVersion\":" + Quote(job.ToVersion) +
            ",\"runId\":" + job.RunId.ToString(CultureInfo.InvariantCulture) + ",\"sourceCommit\":" + Quote(job.SourceCommit) +
            ",\"checkZipSha256\":" + Quote(job.CheckZipSha256) + ",\"msiSha256\":" + Quote(job.MsiSha256) +
            ",\"desktopPid\":" + job.DesktopProcessId.ToString(CultureInfo.InvariantCulture) + ",\"productCode\":" + Quote(job.ProductCode) + "}";
        return Encoding.ASCII.GetBytes(json);
    }

    internal static bool TryReadJob(ReadOnlySpan<byte> bytes, out SelfUpdateJobFile? job)
    {
        job = null;
        if (bytes.Length is 0 or > MaximumFileBytes) return false;
        try
        {
            if (!TryObject(bytes, out JsonDocument? document, out Dictionary<string, JsonElement> fields)) return false;
            using (document)
            {
                if (!KeysAre(fields, JobKeys) ||
                    !DeploymentCapabilityPolicy.TryString(fields["schema"], 64, out string schema) || schema != JobSchema ||
                    !DeploymentCapabilityPolicy.TryString(fields["jobId"], 36, out string jobText) || !Guid.TryParseExact(jobText, "D", out Guid jobId) ||
                    !DeploymentCapabilityPolicy.TryString(fields["toVersion"], 32, out string version) ||
                    fields["runId"].ValueKind != JsonValueKind.Number || !fields["runId"].TryGetInt64(out long runId) ||
                    !DeploymentCapabilityPolicy.TryString(fields["sourceCommit"], 40, out string commit) ||
                    !DeploymentCapabilityPolicy.TryString(fields["checkZipSha256"], 64, out string zip) ||
                    !DeploymentCapabilityPolicy.TryString(fields["msiSha256"], 64, out string msi) ||
                    fields["desktopPid"].ValueKind != JsonValueKind.Number || !fields["desktopPid"].TryGetInt32(out int pid) ||
                    !DeploymentCapabilityPolicy.TryString(fields["productCode"], 38, out string productCode))
                    return false;
                SelfUpdateJobFile candidate = new(jobId, version, runId, commit, zip, msi, pid, productCode);
                if (!bytes.SequenceEqual(WriteJob(candidate))) return false;
                job = candidate;
                return true;
            }
        }
        catch (Exception exception) when (IsParseFailure(exception))
        {
            return false;
        }
    }

    // ---- result.json ----

    internal static byte[] WriteResult(SelfUpdateResultFile result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.JobId == Guid.Empty || !Enum.IsDefined(result.Outcome) || !ReleaseCheckPolicy.IsMsiVersion(result.ToVersion) ||
            !IsUpperHex64(result.ManifestSha256) || result.BootTimeUnixSeconds is <= 0 ||
            (result.Outcome == SelfUpdateFinishOutcome.RestartNeeded) != result.BootTimeUnixSeconds.HasValue ||
            !IsDetail(result.Detail))
            throw new ArgumentException("The result values are not well-formed.", nameof(result));
        string json = "{\"schema\":" + Quote(ResultSchema) + ",\"jobId\":" + Quote(Id(result.JobId)) + ",\"outcome\":" + Quote(Code(result.Outcome)) +
            ",\"exitCode\":" + (result.ExitCode is { } code ? code.ToString(CultureInfo.InvariantCulture) : "null") +
            ",\"toVersion\":" + Quote(result.ToVersion) + ",\"manifestSha256\":" + Quote(result.ManifestSha256) +
            ",\"bootTime\":" + (result.BootTimeUnixSeconds is { } boot ? boot.ToString(CultureInfo.InvariantCulture) : "null") +
            ",\"detail\":" + Quote(result.Detail) + "}";
        return Encoding.ASCII.GetBytes(json);
    }

    internal static bool TryReadResult(ReadOnlySpan<byte> bytes, out SelfUpdateResultFile? result)
    {
        result = null;
        if (bytes.Length is 0 or > MaximumFileBytes) return false;
        try
        {
            if (!TryObject(bytes, out JsonDocument? document, out Dictionary<string, JsonElement> fields)) return false;
            using (document)
            {
                if (!KeysAre(fields, ResultKeys) ||
                    !DeploymentCapabilityPolicy.TryString(fields["schema"], 64, out string schema) || schema != ResultSchema ||
                    !DeploymentCapabilityPolicy.TryString(fields["jobId"], 36, out string jobText) || !Guid.TryParseExact(jobText, "D", out Guid jobId) ||
                    !DeploymentCapabilityPolicy.TryString(fields["outcome"], 32, out string outcomeText) || Array.IndexOf(Outcomes, outcomeText) is not (int outcome and >= 0) ||
                    !TryOptionalInt(fields["exitCode"], out long? exitCode) || exitCode is < int.MinValue or > int.MaxValue ||
                    !DeploymentCapabilityPolicy.TryString(fields["toVersion"], 32, out string version) ||
                    !DeploymentCapabilityPolicy.TryString(fields["manifestSha256"], 64, out string manifest) ||
                    !TryOptionalInt(fields["bootTime"], out long? boot) ||
                    fields["detail"].ValueKind != JsonValueKind.String || fields["detail"].GetString() is not { } detail)
                    return false;
                SelfUpdateResultFile candidate = new(jobId, (SelfUpdateFinishOutcome)outcome, exitCode is { } exit ? (int)exit : null, version, manifest, boot, detail);
                if (!bytes.SequenceEqual(WriteResult(candidate))) return false;
                result = candidate;
                return true;
            }
        }
        catch (Exception exception) when (IsParseFailure(exception))
        {
            return false;
        }
    }

    /// <summary>A result detail is a short fixed token or a code list: lowercase letters, digits, hyphens, spaces, semicolons and colons.</summary>
    internal static bool IsDetail(string? value) =>
        value is not null && value.Length <= MaximumDetailLength &&
        value.All(static character => character is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or ' ' or ';' or ':');

    internal static bool IsUpperHex64(string? value) =>
        value is { Length: 64 } && value.All(static character => character is (>= '0' and <= '9') or (>= 'A' and <= 'F'));

    private static bool TryObject(ReadOnlySpan<byte> bytes, out JsonDocument? document, out Dictionary<string, JsonElement> fields)
    {
        fields = [];
        document = null;
        _ = DeploymentCapabilityPolicy.StrictUtf8.GetString(bytes);
        document = JsonDocument.Parse(bytes.ToArray(), DeploymentCapabilityPolicy.JsonOptions);
        if (document.RootElement.ValueKind == JsonValueKind.Object && DeploymentCapabilityPolicy.TryCollect(document.RootElement, out fields)) return true;
        document.Dispose();
        document = null;
        return false;
    }

    private static bool KeysAre(Dictionary<string, JsonElement> fields, HashSet<string> keys) => fields.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(keys);

    private static bool TryOptionalInt(JsonElement element, out long? value)
    {
        value = null;
        if (element.ValueKind == JsonValueKind.Null) return true;
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt64(out long number)) return false;
        value = number;
        return true;
    }

    private static bool IsParseFailure(Exception exception) =>
        exception is ArgumentException or DecoderFallbackException or JsonException or FormatException or InvalidOperationException or KeyNotFoundException;

    private static string Id(Guid id) => id.ToString("D", CultureInfo.InvariantCulture);

    private static string Quote(string value) => MsiTableDigest.Quote(value);

    [GeneratedRegex("^pcsteward-reviewed-[a-f0-9]{32}$", RegexOptions.CultureInvariant)]
    private static partial Regex PipeNamePattern();
}
