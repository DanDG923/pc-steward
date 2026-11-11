using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PCSteward.Execution;

/// <summary>One file of the CI payload: its path under the install root, length and SHA-256 (uppercase hex).</summary>
internal sealed record ReleaseCheckFile(string Path, long Bytes, string Sha256);

/// <summary>One CI executable's PE checksum before signing, needed to compare a signed image with its unsigned original.</summary>
internal sealed record ReleaseCheckExecutable(string Path, uint OriginalChecksum);

/// <summary>
/// ADR-032 revision 2 §3: the parsed <c>release-check.json</c> the release-payload workflow publishes as the
/// <c>pc-steward-release-check</c> artefact. It is consistency data until the desktop has matched the artefact's digest to a
/// genuine run through GitHub's API; the helper re-reads its own copy and trusts it only as far as the request (which the
/// owner approved) names the same values.
/// </summary>
internal sealed record ReleaseCheckRecord(
    string Repository,
    long RunId,
    string SourceCommit,
    string ProductVersion,
    string MsiVersion,
    string PrimaryPin,
    string BackupPin,
    string CandidateManifestSha256,
    string MsiTablesSha256,
    IReadOnlyList<ReleaseCheckFile> Files,
    IReadOnlyList<ReleaseCheckExecutable> Executables);

/// <summary>
/// FR-UPD-04/15, TM-03/04/13; ADR-032 revision 2 §3 and §6: strict, pure parsing of the release check record and of the
/// artefact zip that carries it. No file, registry, environment, network or process access happens here.
/// <list type="bullet">
/// <item>The zip is at most <see cref="MaximumZipBytes"/> and holds exactly one entry, <see cref="EntryName"/>, stored or
/// deflated, of at most <see cref="MaximumRecordBytes"/> once inflated.</item>
/// <item>The record is strict UTF-8 without BOM, one JSON object with exactly the documented keys (no duplicates, compared
/// case-insensitively), values of the documented shapes, files that are unique relative install paths, and the two
/// executables, each listed among the files.</item>
/// </list>
/// Anything else is refused.
/// </summary>
internal static partial class ReleaseCheckPolicy
{
    internal const string Schema = "pc-steward-release-check:1";
    internal const string ArtifactName = "pc-steward-release-check";
    internal const string AssetName = "pc-steward-release-check.zip";
    internal const string EntryName = "release-check.json";
    internal const string Repository = "DanDG923/pc-steward";
    internal const int MaximumZipBytes = 1024 * 1024;
    internal const int MaximumRecordBytes = 1024 * 1024;
    internal static readonly IReadOnlyList<string> ExecutableNames = ["PCSteward.Desktop.exe", "PCSteward.ElevatedHelper.exe"];

    private static readonly HashSet<string> RecordKeys =
        ["schema", "repository", "runId", "sourceCommit", "productVersion", "msiVersion", "pins", "candidateManifestSha256", "msiTablesSha256", "files", "executables"];
    private static readonly HashSet<string> PinKeys = ["primary", "backup"];
    private static readonly HashSet<string> FileKeys = ["path", "bytes", "sha256"];
    private static readonly HashSet<string> ExecutableKeys = ["path", "originalChecksum"];

    /// <summary>The one record inside the artefact zip, as raw bytes, or false.</summary>
    internal static bool TryReadZip(ReadOnlySpan<byte> zip, out byte[]? record)
    {
        record = null;
        if (zip.Length is 0 or > MaximumZipBytes) return false;
        try
        {
            using MemoryStream stream = new(zip.ToArray(), writable: false);
            using ZipArchive archive = new(stream, ZipArchiveMode.Read, leaveOpen: false);
            if (archive.Entries.Count != 1) return false;
            ZipArchiveEntry entry = archive.Entries[0];
            if (!string.Equals(entry.FullName, EntryName, StringComparison.Ordinal) || entry.Length is <= 0 or > MaximumRecordBytes) return false;
            using Stream content = entry.Open();
            byte[] buffer = new byte[MaximumRecordBytes + 1];
            int total = 0;
            while (true)
            {
                int read = content.Read(buffer, total, buffer.Length - total);
                if (read == 0) break;
                total += read;
                if (total > MaximumRecordBytes) return false;
            }
            if (total != entry.Length) return false;
            record = buffer[..total];
            return true;
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or NotSupportedException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>Strict parse of <c>release-check.json</c> (see the class summary).</summary>
    internal static bool TryRead(ReadOnlySpan<byte> bytes, out ReleaseCheckRecord? record)
    {
        record = null;
        if (bytes.Length is 0 or > MaximumRecordBytes || bytes.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF])) return false;
        try
        {
            _ = DeploymentCapabilityPolicy.StrictUtf8.GetString(bytes);
            using JsonDocument document = JsonDocument.Parse(bytes.ToArray(), DeploymentCapabilityPolicy.JsonOptions);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !DeploymentCapabilityPolicy.TryCollect(root, out Dictionary<string, JsonElement> fields) ||
                !fields.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(RecordKeys) ||
                !DeploymentCapabilityPolicy.TryString(fields["schema"], 64, out string schema) || !string.Equals(schema, Schema, StringComparison.Ordinal) ||
                !DeploymentCapabilityPolicy.TryString(fields["repository"], 140, out string repository) || !string.Equals(repository, Repository, StringComparison.Ordinal) ||
                fields["runId"].ValueKind != JsonValueKind.Number || !fields["runId"].TryGetInt64(out long runId) || runId <= 0 ||
                !DeploymentCapabilityPolicy.TryString(fields["sourceCommit"], 40, out string commit) || !CommitPattern().IsMatch(commit) ||
                !DeploymentCapabilityPolicy.TryString(fields["productVersion"], 64, out string productVersion) || !ProductVersionPattern().IsMatch(productVersion) ||
                !DeploymentCapabilityPolicy.TryString(fields["msiVersion"], 32, out string msiVersion) || !MsiVersionPattern().IsMatch(msiVersion) ||
                !string.Equals(productVersion.Split('-', 2)[0], msiVersion, StringComparison.Ordinal) ||
                !TryUpperHex(fields["candidateManifestSha256"], out string candidateManifest) ||
                !TryUpperHex(fields["msiTablesSha256"], out string tables))
                return false;

            JsonElement pins = fields["pins"];
            if (pins.ValueKind != JsonValueKind.Object || !DeploymentCapabilityPolicy.TryCollect(pins, out Dictionary<string, JsonElement> pinFields) ||
                !pinFields.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(PinKeys) ||
                !TryUpperHex(pinFields["primary"], out string primary) || !TryUpperHex(pinFields["backup"], out string backup) ||
                string.Equals(primary, backup, StringComparison.Ordinal))
                return false;

            JsonElement fileList = fields["files"];
            if (fileList.ValueKind != JsonValueKind.Array || fileList.GetArrayLength() is 0 or > ReleaseManifestPolicy.MaximumFiles) return false;
            List<ReleaseCheckFile> files = [];
            HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
            foreach (JsonElement item in fileList.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object || !DeploymentCapabilityPolicy.TryCollect(item, out Dictionary<string, JsonElement> entry) ||
                    !entry.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(FileKeys) ||
                    !DeploymentCapabilityPolicy.TryString(entry["path"], ReleaseManifestPolicy.MaximumPathLength, out string path) ||
                    !ReleaseManifestPolicy.IsRelativeInstallPath(path) ||
                    string.Equals(path, ReleaseManifestPolicy.FileName, StringComparison.OrdinalIgnoreCase) || !paths.Add(path) ||
                    !TryUpperHex(entry["sha256"], out string hash) ||
                    entry["bytes"].ValueKind != JsonValueKind.Number || !entry["bytes"].TryGetInt64(out long length) || length < 0)
                    return false;
                files.Add(new(path, length, hash));
            }

            JsonElement executableList = fields["executables"];
            if (executableList.ValueKind != JsonValueKind.Array || executableList.GetArrayLength() != ExecutableNames.Count) return false;
            List<ReleaseCheckExecutable> executables = [];
            foreach (JsonElement item in executableList.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object || !DeploymentCapabilityPolicy.TryCollect(item, out Dictionary<string, JsonElement> entry) ||
                    !entry.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(ExecutableKeys) ||
                    !DeploymentCapabilityPolicy.TryString(entry["path"], 64, out string path) ||
                    entry["originalChecksum"].ValueKind != JsonValueKind.Number || !entry["originalChecksum"].TryGetUInt32(out uint checksum))
                    return false;
                executables.Add(new(path, checksum));
            }
            foreach (string name in ExecutableNames)
            {
                if (executables.Count(executable => string.Equals(executable.Path, name, StringComparison.Ordinal)) != 1 ||
                    files.Count(file => string.Equals(file.Path, name, StringComparison.Ordinal)) != 1)
                    return false;
            }

            record = new(repository, runId, commit, productVersion, msiVersion, primary, backup, candidateManifest, tables, files, executables);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or DecoderFallbackException or JsonException or FormatException or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>
    /// True when the record names exactly <paramref name="pins"/> (primary first). A record made for other keys belongs to
    /// another build of PC Steward and is never used.
    /// </summary>
    internal static bool NamesPins(ReleaseCheckRecord record, IReadOnlyList<string> pins)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(pins);
        return pins.Count == 2 && string.Equals(record.PrimaryPin, pins[0], StringComparison.Ordinal) &&
            string.Equals(record.BackupPin, pins[1], StringComparison.Ordinal);
    }

    /// <summary>An MSI ProductVersion as the release tag carries it: <c>major.minor.patch</c>.</summary>
    internal static bool IsMsiVersion(string? value) => value is not null && MsiVersionPattern().IsMatch(value);

    private static bool TryUpperHex(JsonElement element, out string value) =>
        DeploymentCapabilityPolicy.TryString(element, 64, out value) && value.Length == 64 &&
        value.All(static character => character is (>= '0' and <= '9') or (>= 'A' and <= 'F'));

    [GeneratedRegex(@"^[0-9a-f]{40}$", RegexOptions.CultureInvariant)]
    private static partial Regex CommitPattern();

    [GeneratedRegex(@"^(0|[1-9][0-9]{0,4})\.(0|[1-9][0-9]{0,4})\.(0|[1-9][0-9]{0,4})(-[a-z0-9]+(-[a-z0-9]+)*)?$", RegexOptions.CultureInvariant)]
    private static partial Regex ProductVersionPattern();

    [GeneratedRegex(@"^(0|[1-9][0-9]{0,4})\.(0|[1-9][0-9]{0,4})\.(0|[1-9][0-9]{0,4})$", RegexOptions.CultureInvariant)]
    private static partial Regex MsiVersionPattern();
}
