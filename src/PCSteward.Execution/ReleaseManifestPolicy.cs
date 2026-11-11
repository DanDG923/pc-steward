using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PCSteward.Domain;

namespace PCSteward.Execution;

/// <summary>One installed file named by the release manifest: its path under the install root, length and SHA-256.</summary>
internal sealed record ReleaseManifestFile(string Path, long Bytes, string Sha256);

/// <summary>Passive parse of <c>PCSteward.ReleaseManifest.json</c>; consistency data only, never authority.</summary>
internal sealed record ReleaseManifest(string ProductVersion, IReadOnlyList<ReleaseManifestFile> Files);

/// <summary>
/// Bytes and facts already read by the Windows reader for a release install (ADR-034 §4). A null byte array means the file
/// is absent. <see cref="ExecutablesCarrySamePin"/> is the reader's ADR-034 §3 result for the two held executables.
/// </summary>
internal sealed record ReleaseCapabilityInputs(
    byte[]? ManifestBytes,
    byte[]? CapabilityBytes,
    IReadOnlyDictionary<string, DeploymentObservedFile> ObservedHashes,
    bool ExecutablesCarrySamePin);

/// <summary>
/// FR-UPD-15, TM-03/04/07/13; ADR-034 §4: pure evaluation of an ADR-034 release install, the counterpart of
/// <see cref="DeploymentCapabilityPolicy"/> for the ADR-022 ledger. It parses the release manifest strictly (closed key sets,
/// size limits, no duplicate keys or paths), requires every ADR-033 binding-set leaf to be listed exactly once, checks the
/// capability statement and the observed binding-set hashes against the manifest, and requires both executables to carry
/// the same compiled release pin. No file, registry, environment, network or database access happens here. Stages reuse
/// <see cref="DeploymentCapabilityStage"/>: <see cref="DeploymentCapabilityStage.Ledger"/> is the manifest itself.
/// </summary>
internal static partial class ReleaseManifestPolicy
{
    internal const string FileName = "PCSteward.ReleaseManifest.json";
    internal const string Format = "pc-steward-release-manifest:1";
    internal const int MaximumManifestBytes = 1024 * 1024;
    internal const int MaximumFiles = 4096;
    internal const int MaximumPathLength = 240;
    internal const int MaximumVersionLength = 64;

    private static readonly HashSet<string> ManifestKeys = ["format", "productVersion", "files"];
    private static readonly HashSet<string> FileKeys = ["path", "bytes", "sha256"];

    internal static HelperProtocolCapabilityResult Evaluate(ReleaseCapabilityInputs inputs, UpdateRouteContract route)
    {
        try { return HelperProtocolCapabilityResult.For(EvaluateCore(inputs, route, DeploymentCapabilityStage.BindingSet)); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return HelperProtocolCapabilityResult.For(HelperProtocolCapabilityStatus.Unreadable);
        }
    }

    /// <summary>
    /// The checks up to and including <paramref name="stage"/> (manifest, or manifest and statement), so the reader can stop
    /// before reading later inputs. <c>true</c> means only "no failure so far"; only <see cref="Evaluate"/> can return
    /// <see cref="HelperProtocolCapabilityStatus.Supported"/>.
    /// </summary>
    internal static bool PassesThrough(
        ReleaseCapabilityInputs inputs, UpdateRouteContract route, DeploymentCapabilityStage stage, out HelperProtocolCapabilityStatus failure)
    {
        try
        {
            if (stage is not (DeploymentCapabilityStage.Ledger or DeploymentCapabilityStage.Statement))
                throw new ArgumentOutOfRangeException(nameof(stage));
            HelperProtocolCapabilityStatus status = EvaluateCore(inputs, route, stage);
            failure = status;
            return status == HelperProtocolCapabilityStatus.Supported;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            failure = HelperProtocolCapabilityStatus.Unreadable;
            return false;
        }
    }

    private static HelperProtocolCapabilityStatus EvaluateCore(ReleaseCapabilityInputs inputs, UpdateRouteContract route, DeploymentCapabilityStage lastStage)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        if (!DeploymentCapabilityPolicy.CapabilityOperationAllowlist.Contains(route)) return HelperProtocolCapabilityStatus.Mismatch;

        // Manifest: present, strictly parsed, and listing every binding-set leaf exactly once at the install root.
        if (inputs.ManifestBytes is null) return HelperProtocolCapabilityStatus.Absent;
        if (!TryRead(inputs.ManifestBytes, out ReleaseManifest? manifest) || manifest is null) return HelperProtocolCapabilityStatus.Unreadable;
        Dictionary<string, ReleaseManifestFile> bound = new(StringComparer.Ordinal);
        foreach (string leaf in DeploymentCapabilityPolicy.BindingSet)
        {
            ReleaseManifestFile[] entries = [.. manifest.Files.Where(file => string.Equals(file.Path, leaf, StringComparison.OrdinalIgnoreCase))];
            if (entries.Length != 1 || !string.Equals(entries[0].Path, leaf, StringComparison.Ordinal)) return HelperProtocolCapabilityStatus.Mismatch;
            bound[leaf] = entries[0];
        }
        if (lastStage == DeploymentCapabilityStage.Ledger) return HelperProtocolCapabilityStatus.Supported;

        // Capability statement: the listed bytes, and the route allowed by the protocol this build speaks.
        ReleaseManifestFile statement = bound[DeploymentCapabilityPolicy.CapabilityFileName];
        if (inputs.CapabilityBytes is not { } capability || capability.Length != statement.Bytes ||
            !DeploymentCapabilityPolicy.HashEquals(DeploymentCapabilityPolicy.Sha256Hex(capability), statement.Sha256) ||
            !DeploymentCapabilityPolicy.TryParseCapability(capability, out string protocol, out IReadOnlyList<UpdateRouteContract> operations) ||
            !string.Equals(protocol, ReviewedUpdateWireCodec.ProtocolVersion, StringComparison.Ordinal) || !operations.Contains(route))
            return HelperProtocolCapabilityStatus.Mismatch;
        if (lastStage == DeploymentCapabilityStage.Statement) return HelperProtocolCapabilityStatus.Supported;

        // Binding set: bounds first, then lengths and hashes against the manifest, then the pinned signatures.
        IReadOnlyDictionary<string, DeploymentObservedFile> observed = inputs.ObservedHashes ?? throw new ArgumentException("Observations required.");
        if (observed.Count != DeploymentCapabilityPolicy.BindingSet.Count) return HelperProtocolCapabilityStatus.Unreadable;
        long total = 0;
        foreach (string leaf in DeploymentCapabilityPolicy.BindingSet)
        {
            if (!observed.TryGetValue(leaf, out DeploymentObservedFile file) || file.Length is < 0 or > DeploymentCapabilityPolicy.MaximumBindingFileBytes ||
                !DeploymentCapabilityPolicy.IsHex(file.Sha256, 64)) return HelperProtocolCapabilityStatus.Unreadable;
            total += file.Length;
            if (total > DeploymentCapabilityPolicy.MaximumBindingSetBytes) return HelperProtocolCapabilityStatus.Unreadable;
        }
        foreach (string leaf in DeploymentCapabilityPolicy.BindingSet)
        {
            if (observed[leaf].Length != bound[leaf].Bytes || !DeploymentCapabilityPolicy.HashEquals(observed[leaf].Sha256, bound[leaf].Sha256))
                return HelperProtocolCapabilityStatus.Mismatch;
        }
        return inputs.ExecutablesCarrySamePin ? HelperProtocolCapabilityStatus.Supported : HelperProtocolCapabilityStatus.Mismatch;
    }

    /// <summary>
    /// Strict manifest parse: at most 1 MiB, strict UTF-8 without BOM, one JSON object with exactly <c>format</c>,
    /// <c>productVersion</c> and <c>files</c> (no duplicate keys, compared case-insensitively); <c>format</c> is
    /// <see cref="Format"/>; the version is <c>major.minor.patch</c> with an optional pre-release label; <c>files</c> holds
    /// 1–<see cref="MaximumFiles"/> objects of exactly <c>path</c>, <c>bytes</c> and <c>sha256</c>, with unique
    /// (case-insensitive) relative paths that never name the manifest itself. Anything else is refused.
    /// </summary>
    internal static bool TryRead(ReadOnlySpan<byte> bytes, out ReleaseManifest? manifest)
    {
        manifest = null;
        if (bytes.Length is 0 or > MaximumManifestBytes || bytes.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF])) return false;
        try
        {
            _ = DeploymentCapabilityPolicy.StrictUtf8.GetString(bytes);
            using JsonDocument document = JsonDocument.Parse(bytes.ToArray(), DeploymentCapabilityPolicy.JsonOptions);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !DeploymentCapabilityPolicy.TryCollect(root, out Dictionary<string, JsonElement> fields) ||
                !fields.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(ManifestKeys) ||
                !DeploymentCapabilityPolicy.TryString(fields["format"], 64, out string format) || !string.Equals(format, Format, StringComparison.Ordinal) ||
                !DeploymentCapabilityPolicy.TryString(fields["productVersion"], MaximumVersionLength, out string version) || !VersionPattern().IsMatch(version))
                return false;
            JsonElement list = fields["files"];
            if (list.ValueKind != JsonValueKind.Array || list.GetArrayLength() is 0 or > MaximumFiles) return false;
            List<ReleaseManifestFile> files = [];
            HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
            foreach (JsonElement item in list.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object || !DeploymentCapabilityPolicy.TryCollect(item, out Dictionary<string, JsonElement> entry) ||
                    !entry.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(FileKeys) ||
                    !DeploymentCapabilityPolicy.TryString(entry["path"], MaximumPathLength, out string path) || !IsRelativeInstallPath(path) ||
                    string.Equals(path, FileName, StringComparison.OrdinalIgnoreCase) || !paths.Add(path) ||
                    !DeploymentCapabilityPolicy.TryString(entry["sha256"], 64, out string hash) || !DeploymentCapabilityPolicy.IsHex(hash, 64) ||
                    entry["bytes"].ValueKind != JsonValueKind.Number || !entry["bytes"].TryGetInt64(out long length) || length < 0)
                    return false;
                files.Add(new(path, length, hash));
            }
            manifest = new(version, files);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or DecoderFallbackException or JsonException or FormatException or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>
    /// A path below the install root: backslash-separated segments of printable ASCII, none empty, <c>.</c> or <c>..</c>, no
    /// drive, colon, forward slash, wildcard or other character Windows forbids in a name, and no trailing dot or space.
    /// </summary>
    internal static bool IsRelativeInstallPath(string path)
    {
        if (string.IsNullOrEmpty(path) || path.Length > MaximumPathLength) return false;
        foreach (string segment in path.Split('\\'))
        {
            if (segment.Length == 0 || segment is "." or ".." || segment[^1] is '.' or ' ') return false;
            foreach (char character in segment)
                if (character is < (char)0x20 or > (char)0x7E or '<' or '>' or ':' or '"' or '/' or '|' or '?' or '*') return false;
        }
        return true;
    }

    [GeneratedRegex(@"^(0|[1-9][0-9]{0,8})\.(0|[1-9][0-9]{0,8})\.(0|[1-9][0-9]{0,8})(-[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();
}
