using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PCSteward.Execution;

/// <summary>One file of a tree under an install root: its relative path, length and SHA-256 (uppercase hex).</summary>
internal sealed record SelfUpdateTreeFile(string Path, long Bytes, string Sha256);

/// <summary>
/// ADR-032 revision 2 §6 request: the owner-approved job's values and nothing else. There is no path: the helper derives the
/// two input files from <see cref="CheckZipSha256"/> and <see cref="MsiSha256"/> under the requesting user's fixed download
/// folder. <see cref="Nonce"/> is the launch nonce from the helper's command line, binding the request to this launch.
/// </summary>
internal sealed record SelfUpdateRequest(string Nonce, Guid JobId, string ToVersion, long RunId, string SourceCommit, string CheckZipSha256, string MsiSha256);

/// <summary>
/// ADR-032 revision 2 §6 step 6 and §8: what the install must leave in <c>%ProgramFiles%\PC Steward</c>, written by the
/// elevated helper into its protected work folder before <c>msiexec</c> runs, and read by the helper copy after the install
/// and by the reopened desktop after a restart.
/// </summary>
internal sealed record SelfUpdateExpectedInstall(Guid JobId, string ToVersion, string ManifestSha256, IReadOnlyList<SelfUpdateTreeFile> Files);

/// <summary>Why a tree did not match.</summary>
internal enum SelfUpdateTreeVerdict
{
    Matches,
    ManifestMissing,
    ManifestUnreadable,
    VersionMismatch,
    FileSetDiffers,
    FileDiffers,
    ExecutableDiffers,
}

/// <summary>
/// FR-UPD-15, TM-12, TM-20, TM-23, TM-24; ADR-032 revision 2 §6–§8: the pure rules of the elevated self-update operation.
/// <list type="bullet">
/// <item><see cref="TryReadRequest"/>: the request is one strict JSON object of at most <see cref="MaximumRequestBytes"/> with
/// exactly the job values, in the canonical bytes <see cref="WriteRequest"/> makes; anything else, including any key that
/// could carry a path, is refused.</item>
/// <item><see cref="CheckExtracted"/>: the MSI's unpacked tree is exactly the CI payload of the (GitHub-confirmed) check record
/// plus a release manifest that lists exactly those files; non-executable files are byte-identical to CI, and each executable
/// is the CI one with exactly one signature added (<see cref="ReleaseImageComparison"/>). Whether both carry the same compiled
/// pin is the native caller's <see cref="AuthenticodeIdentity"/> check. A match yields the expected installed tree.</item>
/// <item><see cref="CheckInstalled"/>: an installed tree is exactly the expected one, no file more or less.</item>
/// <item><see cref="WriteExpected"/> and <see cref="TryReadExpected"/>: the expected tree as canonical ASCII JSON, read back
/// strictly.</item>
/// </list>
/// No file, registry, environment, network or process access happens here; DACLs, held handles and Authenticode are the
/// native caller's.
/// </summary>
internal static partial class SelfUpdateInstallPolicy
{
    internal const string ExpectedInstallSchema = "pc-steward-expected-install:1";
    internal const string ExpectedInstallFileName = "expected-install.json";
    internal const string WorkFolderPrefix = "PC Steward Update ";
    internal const int MaximumRequestBytes = 16 * 1024;
    internal const int MaximumExpectedBytes = 1024 * 1024;

    private static readonly HashSet<string> RequestKeys = ["nonce", "jobId", "toVersion", "runId", "sourceCommit", "checkZipSha256", "msiSha256"];
    private static readonly HashSet<string> ExpectedKeys = ["schema", "jobId", "toVersion", "manifestSha256", "files"];
    private static readonly HashSet<string> FileKeys = ["path", "bytes", "sha256"];

    /// <summary>The work folder's leaf name for a job: the fixed prefix and the job id as 32 lowercase hex characters.</summary>
    internal static string WorkFolderName(Guid jobId) => WorkFolderPrefix + jobId.ToString("N", CultureInfo.InvariantCulture);

    /// <summary>True only for a leaf <see cref="WorkFolderName"/> could have produced.</summary>
    internal static bool IsWorkFolderName(string? name) => name is not null && WorkFolderPattern().IsMatch(name);

    /// <summary>The download file names the desktop writes and the helper reads, built only from the hashes.</summary>
    internal static string MsiFileName(string msiSha256) =>
        IsUpperHex64(msiSha256) ? msiSha256 + ".msi" : throw new ArgumentException("Expected 64 uppercase hex characters.", nameof(msiSha256));

    internal static string CheckZipFileName(string checkZipSha256) =>
        IsUpperHex64(checkZipSha256) ? checkZipSha256 + ".zip" : throw new ArgumentException("Expected 64 uppercase hex characters.", nameof(checkZipSha256));

    internal static byte[] WriteRequest(SelfUpdateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsValid(request)) throw new ArgumentException("The self-update request is not well-formed.", nameof(request));
        string json = "{\"nonce\":" + Json(request.Nonce) +
            ",\"jobId\":" + Json(request.JobId.ToString("D", CultureInfo.InvariantCulture)) +
            ",\"toVersion\":" + Json(request.ToVersion) +
            ",\"runId\":" + request.RunId.ToString(CultureInfo.InvariantCulture) +
            ",\"sourceCommit\":" + Json(request.SourceCommit) +
            ",\"checkZipSha256\":" + Json(request.CheckZipSha256) +
            ",\"msiSha256\":" + Json(request.MsiSha256) + "}";
        return Encoding.ASCII.GetBytes(json);
    }

    internal static bool TryReadRequest(ReadOnlySpan<byte> bytes, out SelfUpdateRequest? request)
    {
        request = null;
        if (bytes.Length is 0 or > MaximumRequestBytes) return false;
        try
        {
            _ = DeploymentCapabilityPolicy.StrictUtf8.GetString(bytes);
            using JsonDocument document = JsonDocument.Parse(bytes.ToArray(), DeploymentCapabilityPolicy.JsonOptions);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !DeploymentCapabilityPolicy.TryCollect(document.RootElement, out Dictionary<string, JsonElement> fields) ||
                !fields.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(RequestKeys) ||
                !DeploymentCapabilityPolicy.TryString(fields["nonce"], 64, out string nonce) ||
                !DeploymentCapabilityPolicy.TryString(fields["jobId"], 36, out string jobText) ||
                !Guid.TryParseExact(jobText, "D", out Guid jobId) || !string.Equals(jobText, jobId.ToString("D", CultureInfo.InvariantCulture), StringComparison.Ordinal) ||
                !DeploymentCapabilityPolicy.TryString(fields["toVersion"], 32, out string version) ||
                fields["runId"].ValueKind != JsonValueKind.Number || !fields["runId"].TryGetInt64(out long runId) ||
                !DeploymentCapabilityPolicy.TryString(fields["sourceCommit"], 40, out string commit) ||
                !DeploymentCapabilityPolicy.TryString(fields["checkZipSha256"], 64, out string zip) ||
                !DeploymentCapabilityPolicy.TryString(fields["msiSha256"], 64, out string msi))
                return false;
            SelfUpdateRequest candidate = new(nonce, jobId, version, runId, commit, zip, msi);
            // Canonical bytes only: the request is exactly what WriteRequest makes of its values.
            if (!IsValid(candidate) || !bytes.SequenceEqual(WriteRequest(candidate))) return false;
            request = candidate;
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or DecoderFallbackException or JsonException or FormatException or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>
    /// Security review L1: is <paramref name="toVersion"/> (major.minor.patch) later than the major.minor.patch of the installed
    /// release manifest's product version (which may carry a suffix such as <c>-private-beta</c>)?
    /// </summary>
    internal static bool IsNewerVersion(string toVersion, string installedProductVersion)
    {
        if (toVersion is null || installedProductVersion is null) return false;
        string installedCore = installedProductVersion.Split('-', 2)[0];
        return ReleaseCheckPolicy.IsMsiVersion(toVersion) && ReleaseCheckPolicy.IsMsiVersion(installedCore) &&
            Version.TryParse(toVersion, out Version? to) && Version.TryParse(installedCore, out Version? from) && to > from;
    }

    /// <summary>
    /// The record the helper has confirmed with GitHub must be the one the owner approved: the same run, commit and version,
    /// and both compiled pins.
    /// </summary>
    internal static bool RecordMatchesRequest(ReleaseCheckRecord record, SelfUpdateRequest request, IReadOnlyList<string> compiledPins)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(request);
        return record.RunId == request.RunId && string.Equals(record.SourceCommit, request.SourceCommit, StringComparison.Ordinal) &&
            string.Equals(record.MsiVersion, request.ToVersion, StringComparison.Ordinal) &&
            string.Equals(record.Repository, ReleaseCheckPolicy.Repository, StringComparison.Ordinal) &&
            ReleaseCheckPolicy.NamesPins(record, compiledPins);
    }

    /// <summary>
    /// ADR-032 revision 2 §6 step 5: is the extracted tree exactly the CI payload plus its release manifest? On a match,
    /// <paramref name="expected"/> is the tree the install must leave. <paramref name="readExecutable"/> returns the bytes of
    /// an extracted executable the caller has already hashed through a held handle.
    /// </summary>
    internal static SelfUpdateTreeVerdict CheckExtracted(ReleaseCheckRecord record, Guid jobId, IReadOnlyList<SelfUpdateTreeFile> tree,
        byte[]? manifestBytes, Func<string, byte[]> readExecutable, out SelfUpdateExpectedInstall? expected)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(readExecutable);
        expected = null;
        if (manifestBytes is null) return SelfUpdateTreeVerdict.ManifestMissing;
        if (!ReleaseManifestPolicy.TryRead(manifestBytes, out ReleaseManifest? manifest) || manifest is null) return SelfUpdateTreeVerdict.ManifestUnreadable;
        if (!string.Equals(manifest.ProductVersion, record.ProductVersion, StringComparison.Ordinal)) return SelfUpdateTreeVerdict.VersionMismatch;

        Dictionary<string, SelfUpdateTreeFile>? actual = Index(tree);
        Dictionary<string, ReleaseManifestFile> listed = manifest.Files.ToDictionary(file => file.Path, StringComparer.OrdinalIgnoreCase);
        Dictionary<string, ReleaseCheckFile> ci = record.Files.ToDictionary(file => file.Path, StringComparer.OrdinalIgnoreCase);
        if (actual is null || !actual.TryGetValue(ReleaseManifestPolicy.FileName, out SelfUpdateTreeFile? manifestFile) ||
            actual.Count != listed.Count + 1 || listed.Count != ci.Count ||
            !listed.Keys.All(path => actual.ContainsKey(path) && ci.ContainsKey(path)))
            return SelfUpdateTreeVerdict.FileSetDiffers;
        if (manifestFile.Bytes != manifestBytes.Length ||
            !string.Equals(manifestFile.Sha256, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(manifestBytes)), StringComparison.Ordinal))
            return SelfUpdateTreeVerdict.ManifestUnreadable;

        foreach (ReleaseManifestFile file in manifest.Files)
        {
            SelfUpdateTreeFile found = actual[file.Path];
            if (found.Bytes != file.Bytes || !string.Equals(found.Sha256, file.Sha256, StringComparison.OrdinalIgnoreCase)) return SelfUpdateTreeVerdict.FileDiffers;
            ReleaseCheckFile original = ci[file.Path];
            ReleaseCheckExecutable? executable = record.Executables.FirstOrDefault(entry => string.Equals(entry.Path, file.Path, StringComparison.OrdinalIgnoreCase));
            if (executable is null)
            {
                if (original.Bytes != file.Bytes || !string.Equals(original.Sha256, file.Sha256, StringComparison.OrdinalIgnoreCase)) return SelfUpdateTreeVerdict.FileDiffers;
                continue;
            }
            byte[] signed = readExecutable(file.Path);
            if (signed.LongLength != file.Bytes ||
                !string.Equals(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(signed)), file.Sha256, StringComparison.OrdinalIgnoreCase) ||
                !ReleaseImageComparison.MatchesUnsignedOriginal(signed, original.Bytes, original.Sha256, executable.OriginalChecksum))
                return SelfUpdateTreeVerdict.ExecutableDiffers;
        }

        List<SelfUpdateTreeFile> files = [.. actual.Values.Select(file => file with { Sha256 = file.Sha256.ToUpperInvariant() })];
        files.Sort((left, right) => string.CompareOrdinal(left.Path, right.Path));
        expected = new(jobId, record.MsiVersion, manifestFile.Sha256.ToUpperInvariant(), files);
        return SelfUpdateTreeVerdict.Matches;
    }

    /// <summary>ADR-032 revision 2 §7 step 4 and §8: the installed tree is exactly the expected one.</summary>
    internal static SelfUpdateTreeVerdict CheckInstalled(SelfUpdateExpectedInstall expected, IReadOnlyList<SelfUpdateTreeFile> installed)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(installed);
        Dictionary<string, SelfUpdateTreeFile>? actual = Index(installed);
        if (actual is null || actual.Count != expected.Files.Count) return SelfUpdateTreeVerdict.FileSetDiffers;
        foreach (SelfUpdateTreeFile file in expected.Files)
        {
            if (!actual.TryGetValue(file.Path, out SelfUpdateTreeFile? found)) return SelfUpdateTreeVerdict.FileSetDiffers;
            if (found.Bytes != file.Bytes || !string.Equals(found.Sha256, file.Sha256, StringComparison.OrdinalIgnoreCase)) return SelfUpdateTreeVerdict.FileDiffers;
        }
        SelfUpdateTreeFile? manifest = expected.Files.FirstOrDefault(file => string.Equals(file.Path, ReleaseManifestPolicy.FileName, StringComparison.Ordinal));
        return manifest is not null && string.Equals(manifest.Sha256, expected.ManifestSha256, StringComparison.OrdinalIgnoreCase)
            ? SelfUpdateTreeVerdict.Matches : SelfUpdateTreeVerdict.ManifestUnreadable;
    }

    /// <summary>Canonical ASCII JSON: fixed key order, files sorted ordinally by path, uppercase hashes.</summary>
    internal static byte[] WriteExpected(SelfUpdateExpectedInstall expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        StringBuilder json = new();
        json.Append("{\"schema\":").Append(Json(ExpectedInstallSchema))
            .Append(",\"jobId\":").Append(Json(expected.JobId.ToString("D", CultureInfo.InvariantCulture)))
            .Append(",\"toVersion\":").Append(Json(expected.ToVersion))
            .Append(",\"manifestSha256\":").Append(Json(expected.ManifestSha256.ToUpperInvariant()))
            .Append(",\"files\":[");
        bool first = true;
        foreach (SelfUpdateTreeFile file in expected.Files.OrderBy(file => file.Path, StringComparer.Ordinal))
        {
            if (!ReleaseManifestPolicy.IsRelativeInstallPath(file.Path) || file.Bytes < 0 || !DeploymentCapabilityPolicy.IsHex(file.Sha256, 64))
                throw new ArgumentException("The expected install holds a malformed file entry.", nameof(expected));
            if (!first) json.Append(',');
            first = false;
            json.Append("{\"path\":").Append(Json(file.Path))
                .Append(",\"bytes\":").Append(file.Bytes.ToString(CultureInfo.InvariantCulture))
                .Append(",\"sha256\":").Append(Json(file.Sha256.ToUpperInvariant())).Append('}');
        }
        json.Append("]}");
        byte[] bytes = Encoding.ASCII.GetBytes(json.ToString());
        if (bytes.Length > MaximumExpectedBytes) throw new ArgumentException("The expected install exceeds its size bound.", nameof(expected));
        return bytes;
    }

    internal static bool TryReadExpected(ReadOnlySpan<byte> bytes, out SelfUpdateExpectedInstall? expected)
    {
        expected = null;
        if (bytes.Length is 0 or > MaximumExpectedBytes) return false;
        try
        {
            _ = DeploymentCapabilityPolicy.StrictUtf8.GetString(bytes);
            using JsonDocument document = JsonDocument.Parse(bytes.ToArray(), DeploymentCapabilityPolicy.JsonOptions);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !DeploymentCapabilityPolicy.TryCollect(document.RootElement, out Dictionary<string, JsonElement> fields) ||
                !fields.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(ExpectedKeys) ||
                !DeploymentCapabilityPolicy.TryString(fields["schema"], 64, out string schema) || !string.Equals(schema, ExpectedInstallSchema, StringComparison.Ordinal) ||
                !DeploymentCapabilityPolicy.TryString(fields["jobId"], 36, out string jobText) || !Guid.TryParseExact(jobText, "D", out Guid jobId) ||
                !DeploymentCapabilityPolicy.TryString(fields["toVersion"], 32, out string version) || !ReleaseCheckPolicy.IsMsiVersion(version) ||
                !DeploymentCapabilityPolicy.TryString(fields["manifestSha256"], 64, out string manifestSha) || !IsUpperHex64(manifestSha))
                return false;
            JsonElement list = fields["files"];
            if (list.ValueKind != JsonValueKind.Array || list.GetArrayLength() is 0 or > ReleaseManifestPolicy.MaximumFiles + 1) return false;
            List<SelfUpdateTreeFile> files = [];
            HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
            foreach (JsonElement item in list.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object || !DeploymentCapabilityPolicy.TryCollect(item, out Dictionary<string, JsonElement> entry) ||
                    !entry.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(FileKeys) ||
                    !DeploymentCapabilityPolicy.TryString(entry["path"], ReleaseManifestPolicy.MaximumPathLength, out string path) ||
                    !ReleaseManifestPolicy.IsRelativeInstallPath(path) || !paths.Add(path) ||
                    !DeploymentCapabilityPolicy.TryString(entry["sha256"], 64, out string hash) || !IsUpperHex64(hash) ||
                    entry["bytes"].ValueKind != JsonValueKind.Number || !entry["bytes"].TryGetInt64(out long length) || length < 0)
                    return false;
                files.Add(new(path, length, hash));
            }
            if (!paths.Contains(ReleaseManifestPolicy.FileName)) return false;
            expected = new(jobId, version, manifestSha, files);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or DecoderFallbackException or JsonException or FormatException or InvalidOperationException)
        {
            return false;
        }
    }

    private static bool IsValid(SelfUpdateRequest request) =>
        IsUpperHex64(request.Nonce) && request.JobId != Guid.Empty && ReleaseCheckPolicy.IsMsiVersion(request.ToVersion) && request.RunId > 0 &&
        CommitPattern().IsMatch(request.SourceCommit) &&
        IsUpperHex64(request.CheckZipSha256) && IsUpperHex64(request.MsiSha256);

    private static bool IsUpperHex64(string? value) =>
        value is { Length: 64 } && value.All(static character => character is (>= '0' and <= '9') or (>= 'A' and <= 'F'));

    /// <summary>Unique relative install paths (case-insensitive, as Windows compares names), or null.</summary>
    private static Dictionary<string, SelfUpdateTreeFile>? Index(IReadOnlyList<SelfUpdateTreeFile> tree)
    {
        Dictionary<string, SelfUpdateTreeFile> index = new(StringComparer.OrdinalIgnoreCase);
        foreach (SelfUpdateTreeFile file in tree)
            if (file is null || !ReleaseManifestPolicy.IsRelativeInstallPath(file.Path) || file.Bytes < 0 ||
                !DeploymentCapabilityPolicy.IsHex(file.Sha256, 64) || !index.TryAdd(file.Path, file))
                return null;
        return index;
    }

    private static string Json(string value) => MsiTableDigest.Quote(value);

    [GeneratedRegex("^PC Steward Update [0-9a-f]{32}$", RegexOptions.CultureInvariant)]
    private static partial Regex WorkFolderPattern();

    [GeneratedRegex("^[0-9a-f]{40}$", RegexOptions.CultureInvariant)]
    private static partial Regex CommitPattern();
}
