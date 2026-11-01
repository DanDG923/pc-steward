using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PCSteward.Domain;

namespace PCSteward.Execution;

/// <summary>Length and SHA-256 (hex) of one binding-set leaf, read by the caller through its own protected handle.</summary>
internal readonly record struct DeploymentObservedFile(long Length, string Sha256);

/// <summary>
/// Bytes and hashes already read by the caller (adapter gates §A.2). A null byte array means the file is absent. The
/// policy trusts none of it beyond the checks it performs.
/// </summary>
internal sealed record DeploymentCapabilityInputs(
    byte[]? LedgerBytes,
    byte[]? CandidateManifestBytes,
    byte[]? CapabilityBytes,
    string CurrentUserSid,
    string ExpectedInstallRoot,
    IReadOnlyDictionary<string, DeploymentObservedFile> ObservedHashes);

/// <summary>The ADR-022 ledger writer that produced a ledger (adapter gates §R A2-L1).</summary>
internal enum DeploymentLedgerVariant
{
    /// <summary><c>Write-PendingLedger</c>: <c>completed=false</c>, <c>rollbackRequired=false</c>, <c>pendingCertificate</c>.</summary>
    Pending,
    /// <summary>Installer failure path: <c>completed=false</c>, <c>rollbackRequired=true</c>, <c>rollbackFailures</c>, <c>originalFailure</c>.</summary>
    Rollback,
    /// <summary>Pre-Task-48 completed ledger without <c>payloadOrigin</c> (reported as <c>legacy-v6</c>).</summary>
    CompletedLegacy,
    /// <summary>Task 48 completed ledger with <c>payloadOrigin</c>, <c>candidateManifestSha256</c> and <c>executableImages</c>.</summary>
    CompletedPinned,
}

internal sealed record DeploymentLedgerFile(string Path, string Sha256);

/// <summary>Passive parse of one ledger; consistency data only, never authority.</summary>
internal sealed record DeploymentLedger(
    DeploymentLedgerVariant Variant,
    string Schema,
    string InstallRoot,
    string InstallerSid,
    string? PayloadOrigin,
    string? CandidateManifestSha256,
    IReadOnlyList<DeploymentLedgerFile> Files);

/// <summary>
/// FR-UPD-15, TM-03/04/07/13; adapter gates §A.3–§A.4 steps 3–6 and §R A2-L1/A2-L4: pure evaluation of the ADR-022
/// protected deployment ledger, the pinned candidate manifest, the pinned capability statement and the binding-set hashes.
/// No file, registry, environment, network or database access happens here; the Windows reader supplies the bytes. The
/// helper reuses this policy over its own reads (G1). Any exception yields <see cref="HelperProtocolCapabilityStatus.Unreadable"/>.
/// </summary>
internal static class DeploymentCapabilityPolicy
{
    internal const string LedgerSchema = "pc-steward-owner-only-protected-test-v6";
    internal const string PayloadOriginV1 = "pinned-unsigned-v1";
    internal const string PayloadOriginV2 = "pinned-unsigned-v2";
    internal const string CapabilityFileName = "PCSteward.DeploymentCapability.txt";
    internal const string CapabilityHeader = "pc-steward-deployment-capability:1";
    internal const string ProtocolLinePrefix = "helper-protocol:";
    internal const string OperationLinePrefix = "operation:";
    internal const int MaximumLedgerBytes = 1024 * 1024;
    internal const int MaximumCandidateManifestBytes = 1024 * 1024;
    internal const int MaximumCapabilityBytes = 1024;
    internal const int MaximumLedgerFiles = 512;
    internal const int MaximumCapabilityOperations = 8;
    internal const long MaximumBindingFileBytes = 32L * 1024 * 1024;
    internal const long MaximumBindingSetBytes = 64L * 1024 * 1024;

    /// <summary>Compiled closed allowlist of capability operations. Today it has one entry.</summary>
    internal static readonly IReadOnlyList<UpdateRouteContract> CapabilityOperationAllowlist = [UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3];

    /// <summary>
    /// Compiled closed binding set (A.3, A2-L4): the desktop apphost and its main assembly, the helper apphost, assembly,
    /// deps and runtimeconfig, the helper's project-assembly closure, and the capability statement.
    /// </summary>
    internal static readonly IReadOnlyList<string> BindingSet =
    [
        "PCSteward.Desktop.exe",
        "PCSteward.Desktop.dll",
        "PCSteward.ElevatedHelper.exe",
        "PCSteward.ElevatedHelper.dll",
        "PCSteward.ElevatedHelper.deps.json",
        "PCSteward.ElevatedHelper.runtimeconfig.json",
        "PCSteward.Execution.dll",
        "PCSteward.Application.dll",
        "PCSteward.Domain.dll",
        "PCSteward.Provider.Chocolatey.dll",
        "PCSteward.Provider.WinGet.dll",
        CapabilityFileName,
    ];

    private static readonly string[] CertificateStores = ["My", "Root", "TrustedPublisher"];
    private static readonly string[] SignedExecutables = ["PCSteward.Desktop.exe", "PCSteward.ElevatedHelper.exe"];
    private static readonly string[] CommonKeys =
        ["schema", "installRoot", "installerSid", "certificateThumbprint", "certificateSubject", "ownedCertificateStores", "completed"];
    private static readonly HashSet<string> PendingKeys = Keys(CommonKeys, "rollbackRequired", "pendingCertificate");
    private static readonly HashSet<string> RollbackKeys = Keys(CommonKeys, "rollbackRequired", "rollbackFailures", "originalFailure");
    private static readonly HashSet<string> LegacyKeys = Keys(CommonKeys, "createdAtUtc", "files");
    private static readonly HashSet<string> PinnedKeys = Keys(CommonKeys, "createdAtUtc", "files", "payloadOrigin", "candidateManifestSha256", "executableImages");
    private static readonly HashSet<string> FileKeys = Keys([], "path", "sha256");
    private static readonly HashSet<string> ManifestEntryKeys = Keys([], "bytes", "path", "sha256");
    private static readonly HashSet<string> ImageKeys = Keys([], "path", "bytes", "originalChecksum");
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly JsonDocumentOptions JsonOptions = new() { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow, MaxDepth = 8 };

    /// <summary>Bytes generated from <see cref="ReviewedUpdateWireCodec.ProtocolVersion"/> and the compiled allowlist (A.3).</summary>
    internal static byte[] CanonicalCapabilityBytes()
    {
        StringBuilder text = new();
        text.Append(CapabilityHeader).Append('\n').Append(ProtocolLinePrefix + ReviewedUpdateWireCodec.ProtocolVersion).Append('\n');
        foreach (string token in CapabilityOperationAllowlist.Select(OperationToken).Order(StringComparer.Ordinal))
            text.Append(OperationLinePrefix).Append(token).Append('\n');
        return Encoding.ASCII.GetBytes(text.ToString());
    }

    internal static string OperationToken(UpdateRouteContract route) => "Update/" + route;

    internal static HelperProtocolCapabilityResult Evaluate(DeploymentCapabilityInputs inputs, UpdateRouteContract route)
    {
        try { return HelperProtocolCapabilityResult.For(EvaluateCore(inputs, route)); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return HelperProtocolCapabilityResult.For(HelperProtocolCapabilityStatus.Unreadable);
        }
    }

    private static HelperProtocolCapabilityStatus EvaluateCore(DeploymentCapabilityInputs inputs, UpdateRouteContract route)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        // Step 3: ledger. Order follows the specification table.
        if (inputs.LedgerBytes is null) return HelperProtocolCapabilityStatus.Absent;
        if (!TryReadLedger(inputs.LedgerBytes, out DeploymentLedger? ledger) || ledger is null || ledger.Schema != LedgerSchema)
            return HelperProtocolCapabilityStatus.Unreadable;
        if (ledger.Variant is DeploymentLedgerVariant.Pending or DeploymentLedgerVariant.Rollback) return HelperProtocolCapabilityStatus.Incomplete;
        if (!string.Equals(ledger.InstallRoot, inputs.ExpectedInstallRoot, StringComparison.Ordinal) ||
            !string.Equals(ledger.InstallerSid, inputs.CurrentUserSid, StringComparison.Ordinal))
            return HelperProtocolCapabilityStatus.Mismatch;
        if (ledger.Variant == DeploymentLedgerVariant.CompletedLegacy || ledger.PayloadOrigin == PayloadOriginV1) return HelperProtocolCapabilityStatus.Absent;
        if (ledger.PayloadOrigin != PayloadOriginV2 || ledger.CandidateManifestSha256 is null) return HelperProtocolCapabilityStatus.Unreadable;

        // Step 4: pinned candidate manifest and its single capability entry.
        if (inputs.CandidateManifestBytes is null || inputs.CandidateManifestBytes.Length > MaximumCandidateManifestBytes ||
            !HashEquals(Sha256Hex(inputs.CandidateManifestBytes), ledger.CandidateManifestSha256) ||
            !TryReadCandidateManifest(inputs.CandidateManifestBytes, out IReadOnlyList<(string Path, long Bytes, string Sha256)> manifest))
            return HelperProtocolCapabilityStatus.Mismatch;
        (string Path, long Bytes, string Sha256)[] capabilityEntries = manifest.Where(entry => IsLeaf(entry.Path, CapabilityFileName)).ToArray();
        DeploymentLedgerFile[] ledgerCapability = ledger.Files.Where(file => IsLeaf(file.Path, CapabilityFileName)).ToArray();
        if (capabilityEntries.Length != 1 || ledgerCapability.Length != 1 ||
            !string.Equals(capabilityEntries[0].Path, CapabilityFileName, StringComparison.Ordinal) ||
            !string.Equals(ledgerCapability[0].Path, CapabilityFileName, StringComparison.Ordinal) ||
            !HashEquals(capabilityEntries[0].Sha256, ledgerCapability[0].Sha256))
            return HelperProtocolCapabilityStatus.Mismatch;

        // Step 5: the capability statement itself.
        if (inputs.CapabilityBytes is not { } capability || capability.Length != capabilityEntries[0].Bytes ||
            !HashEquals(Sha256Hex(capability), ledgerCapability[0].Sha256) ||
            !TryParseCapability(capability, out string protocol, out IReadOnlyList<UpdateRouteContract> operations) ||
            !string.Equals(protocol, ReviewedUpdateWireCodec.ProtocolVersion, StringComparison.Ordinal) || !operations.Contains(route))
            return HelperProtocolCapabilityStatus.Mismatch;

        // Step 6: binding-set observations, bounds first, then hashes against the ledger.
        IReadOnlyDictionary<string, DeploymentObservedFile> observed = inputs.ObservedHashes ?? throw new ArgumentException("Observations required.");
        if (observed.Count != BindingSet.Count) return HelperProtocolCapabilityStatus.Unreadable;
        long total = 0;
        foreach (string leaf in BindingSet)
        {
            if (!observed.TryGetValue(leaf, out DeploymentObservedFile file) || file.Length is < 0 or > MaximumBindingFileBytes ||
                !IsHex(file.Sha256, 64)) return HelperProtocolCapabilityStatus.Unreadable;
            total += file.Length;
            if (total > MaximumBindingSetBytes) return HelperProtocolCapabilityStatus.Unreadable;
        }
        foreach (string leaf in BindingSet)
        {
            DeploymentLedgerFile[] entries = ledger.Files.Where(file => string.Equals(file.Path, leaf, StringComparison.Ordinal)).ToArray();
            if (entries.Length != 1 || !HashEquals(observed[leaf].Sha256, entries[0].Sha256)) return HelperProtocolCapabilityStatus.Mismatch;
        }
        return HelperProtocolCapabilityStatus.Supported;
    }

    /// <summary>
    /// Strict ledger parse (A.4 step 3, §R A2-L1): at most 1 MiB, strict UTF-8 without BOM, one JSON object without duplicate
    /// (case-insensitive) or unknown keys, exact types, and the closed key set of exactly one writer variant. Values that
    /// the installer can legitimately write (null thumbprint in pending/rollback ledgers, the PowerShell-unrolled
    /// <c>ownedCertificateStores</c> of a rollback ledger, lowercase manifest hashes) are accepted; anything else is refused.
    /// </summary>
    internal static bool TryReadLedger(ReadOnlySpan<byte> bytes, out DeploymentLedger? ledger)
    {
        ledger = null;
        if (bytes.Length is 0 or > MaximumLedgerBytes || bytes.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF])) return false;
        try
        {
            _ = StrictUtf8.GetString(bytes);
            using JsonDocument document = JsonDocument.Parse(bytes.ToArray(), JsonOptions);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !TryCollect(root, out Dictionary<string, JsonElement> fields) ||
                !fields.TryGetValue("completed", out JsonElement completedElement) || !TryBool(completedElement, out bool completed))
                return false;
            DeploymentLedgerVariant variant;
            if (!completed)
            {
                if (!fields.TryGetValue("rollbackRequired", out JsonElement rollbackElement) || !TryBool(rollbackElement, out bool rollback)) return false;
                variant = rollback ? DeploymentLedgerVariant.Rollback : DeploymentLedgerVariant.Pending;
            }
            else variant = fields.ContainsKey("payloadOrigin") ? DeploymentLedgerVariant.CompletedPinned : DeploymentLedgerVariant.CompletedLegacy;
            HashSet<string> expected = variant switch
            {
                DeploymentLedgerVariant.Pending => PendingKeys,
                DeploymentLedgerVariant.Rollback => RollbackKeys,
                DeploymentLedgerVariant.CompletedLegacy => LegacyKeys,
                _ => PinnedKeys,
            };
            if (!fields.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(expected)) return false;

            bool isCompleted = variant is DeploymentLedgerVariant.CompletedLegacy or DeploymentLedgerVariant.CompletedPinned;
            if (!TryString(fields["schema"], 128, out string schema) || !TryString(fields["installRoot"], 260, out string installRoot) ||
                !TryString(fields["installerSid"], 184, out string installerSid)) return false;
            JsonElement thumbprint = fields["certificateThumbprint"];
            if (!(thumbprint.ValueKind == JsonValueKind.Null && !isCompleted) &&
                !(TryString(thumbprint, 40, out string thumb) && IsHex(thumb, 40))) return false;
            JsonElement subject = fields["certificateSubject"];
            if (!(subject.ValueKind == JsonValueKind.Null && variant == DeploymentLedgerVariant.Rollback) && !TryString(subject, 1024, out _)) return false;
            if (!ValidStores(fields["ownedCertificateStores"], variant)) return false;

            IReadOnlyList<DeploymentLedgerFile> files = [];
            string? origin = null, manifestHash = null;
            switch (variant)
            {
                case DeploymentLedgerVariant.Pending:
                    if (!TryBool(fields["pendingCertificate"], out _)) return false;
                    break;
                case DeploymentLedgerVariant.Rollback:
                    if (!TryString(fields["originalFailure"], 65536, out _) ||
                        !TryStringArray(fields["rollbackFailures"], 1, 64, 65536, out _)) return false;
                    break;
                default:
                    if (!TryString(fields["createdAtUtc"], 64, out string created) ||
                        !DateTimeOffset.TryParseExact(created, "O", System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.None, out _) ||
                        !TryFiles(fields["files"], out files)) return false;
                    if (variant == DeploymentLedgerVariant.CompletedPinned)
                    {
                        if (!TryString(fields["payloadOrigin"], 64, out string payloadOrigin) ||
                            !TryString(fields["candidateManifestSha256"], 64, out string hash) || !IsHex(hash, 64) ||
                            !ValidImages(fields["executableImages"])) return false;
                        origin = payloadOrigin;
                        manifestHash = hash;
                    }
                    break;
            }
            ledger = new(variant, schema, installRoot, installerSid, origin, manifestHash, files);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or DecoderFallbackException or JsonException or FormatException or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>
    /// Exact capability statement (A.3): printable ASCII, LF only with a final LF, no BOM or other whitespace, at most
    /// 1,024 B; header, one protocol line, then 1–8 unique ordinal-sorted allowlisted operation lines. Anything else fails.
    /// </summary>
    internal static bool TryParseCapability(ReadOnlySpan<byte> bytes, out string protocol, out IReadOnlyList<UpdateRouteContract> operations)
    {
        protocol = string.Empty;
        operations = [];
        if (bytes.Length is 0 or > MaximumCapabilityBytes || bytes[^1] != (byte)'\n') return false;
        foreach (byte value in bytes)
            if (value != (byte)'\n' && value is < 0x20 or > 0x7E) return false;
        string[] lines = Encoding.ASCII.GetString(bytes[..^1]).Split('\n');
        if (lines.Length is < 3 || lines.Length > 2 + MaximumCapabilityOperations || lines[0] != CapabilityHeader ||
            !lines[1].StartsWith(ProtocolLinePrefix, StringComparison.Ordinal)) return false;
        string version = lines[1][ProtocolLinePrefix.Length..];
        if (version.Length == 0) return false;
        Dictionary<string, UpdateRouteContract> allowed = CapabilityOperationAllowlist.ToDictionary(OperationToken, route => route, StringComparer.Ordinal);
        List<UpdateRouteContract> parsed = [];
        string? previous = null;
        foreach (string line in lines.AsSpan(2))
        {
            if (!line.StartsWith(OperationLinePrefix, StringComparison.Ordinal)) return false;
            string token = line[OperationLinePrefix.Length..];
            if (!allowed.TryGetValue(token, out UpdateRouteContract route) ||
                (previous is not null && string.CompareOrdinal(previous, token) >= 0)) return false;
            parsed.Add(route);
            previous = token;
        }
        protocol = version;
        operations = parsed;
        return true;
    }

    /// <summary>
    /// The pinned candidate manifest: a JSON array of exactly <c>{bytes,path,sha256}</c> objects (the shape
    /// <c>Read-OwnerOnlyCandidateManifest</c> accepts, including its tolerated leading BOM; the bytes are hash-pinned first).
    /// </summary>
    internal static bool TryReadCandidateManifest(ReadOnlySpan<byte> bytes, out IReadOnlyList<(string Path, long Bytes, string Sha256)> entries)
    {
        entries = [];
        if (bytes.Length is < 2 or > MaximumCandidateManifestBytes) return false;
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF])) bytes = bytes[3..];
        try
        {
            _ = StrictUtf8.GetString(bytes);
            using JsonDocument document = JsonDocument.Parse(bytes.ToArray(), JsonOptions);
            if (document.RootElement.ValueKind != JsonValueKind.Array) return false;
            int count = document.RootElement.GetArrayLength();
            if (count is 0 or > MaximumLedgerFiles) return false;
            List<(string, long, string)> result = [];
            HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
            foreach (JsonElement item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object || !TryCollect(item, out Dictionary<string, JsonElement> fields) ||
                    !fields.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(ManifestEntryKeys) ||
                    !TryString(fields["path"], 240, out string path) || !paths.Add(path) ||
                    !TryString(fields["sha256"], 64, out string hash) || !IsHex(hash, 64) ||
                    fields["bytes"].ValueKind != JsonValueKind.Number || !fields["bytes"].TryGetInt64(out long length) || length < 0)
                    return false;
                result.Add((path, length, hash));
            }
            entries = result;
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or DecoderFallbackException or JsonException or FormatException or InvalidOperationException)
        {
            return false;
        }
    }

    private static bool ValidStores(JsonElement element, DeploymentLedgerVariant variant)
    {
        // Pending/completed writers use @(...) directly, so the value is always an array. The rollback writer wraps @(...) in an
        // `if` expression, whose pipeline output PowerShell unrolls: null (no certificate state), one string, or an array.
        if (variant == DeploymentLedgerVariant.Rollback)
        {
            if (element.ValueKind == JsonValueKind.Null) return true;
            if (element.ValueKind == JsonValueKind.String) return CertificateStores.Contains(element.GetString(), StringComparer.Ordinal);
        }
        if (!TryStringArray(element, variant == DeploymentLedgerVariant.Rollback ? 0 : 1, CertificateStores.Length, 32, out string[] stores)) return false;
        return stores.All(store => CertificateStores.Contains(store, StringComparer.Ordinal)) && stores.Distinct(StringComparer.Ordinal).Count() == stores.Length;
    }

    private static bool TryFiles(JsonElement element, out IReadOnlyList<DeploymentLedgerFile> files)
    {
        files = [];
        if (element.ValueKind != JsonValueKind.Array) return false;
        int count = element.GetArrayLength();
        if (count is 0 or > MaximumLedgerFiles) return false;
        List<DeploymentLedgerFile> result = [];
        HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
        foreach (JsonElement item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || !TryCollect(item, out Dictionary<string, JsonElement> fields) ||
                !fields.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(FileKeys) ||
                !TryString(fields["path"], 240, out string path) || !paths.Add(path) ||
                !TryString(fields["sha256"], 64, out string hash) || !IsHex(hash, 64)) return false;
            result.Add(new(path, hash));
        }
        files = result;
        return true;
    }

    private static bool ValidImages(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() != SignedExecutables.Length) return false;
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (JsonElement item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || !TryCollect(item, out Dictionary<string, JsonElement> fields) ||
                !fields.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(ImageKeys) ||
                !TryString(fields["path"], 64, out string path) || !SignedExecutables.Contains(path, StringComparer.Ordinal) || !seen.Add(path) ||
                fields["bytes"].ValueKind != JsonValueKind.Number || !fields["bytes"].TryGetInt64(out long length) || length < 0 ||
                fields["originalChecksum"].ValueKind != JsonValueKind.Number || !fields["originalChecksum"].TryGetInt64(out long checksum) ||
                checksum is < 0 or > uint.MaxValue) return false;
        }
        return true;
    }

    private static bool TryCollect(JsonElement element, out Dictionary<string, JsonElement> fields)
    {
        fields = new(StringComparer.Ordinal);
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (!seen.Add(property.Name)) return false;
            fields[property.Name] = property.Value;
        }
        return true;
    }

    private static bool TryBool(JsonElement element, out bool value)
    {
        value = element.ValueKind == JsonValueKind.True;
        return element.ValueKind is JsonValueKind.True or JsonValueKind.False;
    }

    private static bool TryString(JsonElement element, int maximumLength, out string value)
    {
        value = string.Empty;
        if (element.ValueKind != JsonValueKind.String || element.GetString() is not { } text || text.Length == 0 || text.Length > maximumLength) return false;
        value = text;
        return true;
    }

    private static bool TryStringArray(JsonElement element, int minimum, int maximum, int maximumLength, out string[] values)
    {
        values = [];
        if (element.ValueKind != JsonValueKind.Array) return false;
        int count = element.GetArrayLength();
        if (count < minimum || count > maximum) return false;
        List<string> result = [];
        foreach (JsonElement item in element.EnumerateArray())
        {
            if (!TryString(item, maximumLength, out string value)) return false;
            result.Add(value);
        }
        values = [.. result];
        return true;
    }

    private static HashSet<string> Keys(string[] common, params string[] extra) => [.. common, .. extra];

    private static bool IsLeaf(string path, string leaf) => string.Equals(path, leaf, StringComparison.OrdinalIgnoreCase);

    private static bool IsHex(string value, int length) =>
        value.Length == length && value.All(c => c is (>= '0' and <= '9') or (>= 'A' and <= 'F') or (>= 'a' and <= 'f'));

    private static string Sha256Hex(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    // Manifest-sourced ledger hashes may be lowercase (the script accepts either case), so compare decoded bytes.
    private static bool HashEquals(string left, string right) =>
        IsHex(left, 64) && IsHex(right, 64) && CryptographicOperations.FixedTimeEquals(Convert.FromHexString(left), Convert.FromHexString(right));
}
