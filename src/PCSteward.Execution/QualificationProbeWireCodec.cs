using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using PCSteward.Provider.Chocolatey;

namespace PCSteward.Execution;

/// <summary>
/// Task 50 qualification probe spec §4.1 field 3: the requester-token verdict, spelled exactly as the 18 native verdict names
/// (the enum name is the wire value; a test pins both lists, §4.4 L6). Declared here so the S2 codec names no native type; S3
/// maps the native verdict to this value by exact name.
/// </summary>
internal enum QualificationProbeTokenVerdict
{
    Accepted, FactsMalformed, PeerNotLimited, PeerSidMismatch, EvaluationSidMismatch, AuthenticationIdMismatch, EvaluationNotLimited,
    EvaluationNotMediumIntegrity, EvaluationRestricted, EvaluationAppContainer, EvaluationAdministratorsEnabled,
    EvaluationPrivilegeNotAllowed, PeerProcessIdMismatch, PeerExited, PeerTokenUnreadable, NoLinkedToken, EvaluationTokenUnreadable,
    EvaluationNotIdentificationLevel
}

/// <summary>§4.1 field 4 values: the closed G1 capability statuses plus <see cref="NotEvaluated"/>.</summary>
internal enum QualificationProbeCapabilityStatus { Supported, Absent, Incomplete, Unreadable, Mismatch, NotEvaluated }

/// <summary>§4.1 field 5. There is no <c>Cancelled</c> value: a cancelled observation is never written (§3 B′, review L4).</summary>
internal enum QualificationProbeObservation { NotRun, Observed, Refused, TimedOut }

/// <summary>§4.1 field 7 (§3 S3 observer additions, review L5): which check at the failed step refused.</summary>
internal enum QualificationProbeFailedCheck { None, StepRule, InstalledVersion, SetupAccessGuard, ReleaseLeftover }

/// <summary>§4.1 field 8 (review L5; §10 answer 1): the access verdict behind the refusal.</summary>
internal enum QualificationProbeFailedAccess { None, Granted, Unreadable, Other }

/// <summary>§4.1 field 9. No <c>ProfileMismatch</c>: the probe has no expected fingerprint.</summary>
internal enum QualificationProbeRefusalReason { None, MachineInputsUnsupported, SourceNotCommunity, InstalledMismatch }

/// <summary>§4.1 field 10: the two disclosed refusals that run after step 7 passed.</summary>
internal enum QualificationProbeRefusalDetail { None, UnlistedReleaseLeftover, SetupAccessOverride }

/// <summary>One <c>g1Status</c> property: a compiled allow-list operation token and its closed status.</summary>
internal readonly record struct QualificationProbeCapabilityEntry(string Token, QualificationProbeCapabilityStatus Status);

/// <summary>
/// Untrusted helper-reported probe facts: §4.1 fields 3–16 (the nonce, field 2, is passed separately as in the update codec).
/// No field carries a path, user name, SID, fingerprint value, pipe name, PID or free text. Values only; nothing here
/// qualifies a route or authorizes anything.
/// </summary>
internal sealed record QualificationProbeFacts(
    QualificationProbeTokenVerdict Verdict,
    IReadOnlyList<QualificationProbeCapabilityEntry> G1Status,
    QualificationProbeObservation Observation,
    int? FailedStep,
    QualificationProbeFailedCheck FailedCheck,
    QualificationProbeFailedAccess FailedAccess,
    QualificationProbeRefusalReason RefusalReason,
    QualificationProbeRefusalDetail RefusalDetail,
    int NodesEvaluated,
    int AncestorsEvaluated,
    string? ObservedClientVersion,
    bool FingerprintPresent,
    int FingerprintLength,
    string Diagnostic)
{
    /// <summary>Value equality, including the ordered <see cref="G1Status"/> entries.</summary>
    public bool Equals(QualificationProbeFacts? other) => other is not null &&
        Verdict == other.Verdict && Observation == other.Observation && FailedStep == other.FailedStep &&
        FailedCheck == other.FailedCheck && FailedAccess == other.FailedAccess && RefusalReason == other.RefusalReason &&
        RefusalDetail == other.RefusalDetail && NodesEvaluated == other.NodesEvaluated && AncestorsEvaluated == other.AncestorsEvaluated &&
        string.Equals(ObservedClientVersion, other.ObservedClientVersion, StringComparison.Ordinal) &&
        FingerprintPresent == other.FingerprintPresent && FingerprintLength == other.FingerprintLength &&
        string.Equals(Diagnostic, other.Diagnostic, StringComparison.Ordinal) &&
        (ReferenceEquals(G1Status, other.G1Status) || (G1Status is not null && other.G1Status is not null && G1Status.SequenceEqual(other.G1Status)));

    public override int GetHashCode() => HashCode.Combine(Verdict, Observation, FailedStep, RefusalReason, NodesEvaluated,
        AncestorsEvaluated, Diagnostic, G1Status?.Count ?? -1);
}

/// <summary>
/// The compiled inputs of rules Q4 and Q10: the <c>g1Status</c> keys (the compiled capability allow-list tokens, ordinal order)
/// and the compiled listed client releases (ascending; the last is <c>frozenInstalled</c>). Production uses only
/// <see cref="Compiled"/>; <see cref="CreateForTests"/> has no <c>src</c> caller (architecture test).
/// </summary>
internal sealed class QualificationProbeContract
{
    /// <summary>§4.3 (review L8): the bound assumes at most 8 allow-list tokens of at most 64 UTF-8 bytes each.</summary>
    internal const int MaximumG1Keys = 8;
    internal const int MaximumG1KeyBytes = 64;

    private QualificationProbeContract(IEnumerable<string> g1Keys, IEnumerable<string> listedClientVersions)
    {
        string[] keys = [.. g1Keys];
        string[] versions = [.. listedClientVersions];
        if (keys.Length is 0 or > MaximumG1Keys || keys.Any(key => !IsOperationToken(key)) ||
            keys.Zip(keys.Skip(1)).Any(pair => string.CompareOrdinal(pair.First, pair.Second) >= 0))
            throw new ArgumentException("One to eight distinct allow-list tokens of at most 64 bytes, in ordinal order, are required.", nameof(g1Keys));
        if (versions.Length == 0 || versions.Any(version => !ReviewedUpdateWireCodec.IsNormalizedVersion(version)) ||
            versions.Distinct(StringComparer.Ordinal).Count() != versions.Length)
            throw new ArgumentException("At least one distinct normalized listed release is required.", nameof(listedClientVersions));
        G1Keys = Array.AsReadOnly(keys);
        ListedClientVersions = Array.AsReadOnly(versions);
    }

    /// <summary>The compiled contract: <c>CapabilityOperationAllowlist.Select(OperationToken)</c> in ordinal order (Q4) and the listed releases (Q10).</summary>
    internal static QualificationProbeContract Compiled { get; } = new(
        DeploymentCapabilityPolicy.CapabilityOperationAllowlist.Select(DeploymentCapabilityPolicy.OperationToken).Order(StringComparer.Ordinal),
        ChocolateyClientReleasePolicy.ListedVersions());

    /// <summary>Test-only (an architecture test bans every <c>src</c> caller): a fixture contract under the same validation.</summary>
    internal static QualificationProbeContract CreateForTests(IEnumerable<string> g1Keys, IEnumerable<string> listedClientVersions) =>
        new(g1Keys, listedClientVersions);

    internal IReadOnlyList<string> G1Keys { get; }

    /// <summary>Ascending; the last entry is the newest listed release (<c>frozenInstalled</c>, §3 B′).</summary>
    internal IReadOnlyList<string> ListedClientVersions { get; }

    // "Update/<route>": ASCII letters and digits after the fixed prefix, so the key is written without any escape.
    private static bool IsOperationToken(string? key) => key is { Length: > 7 and <= MaximumG1KeyBytes } &&
        key.StartsWith("Update/", StringComparison.Ordinal) && key[7..].All(char.IsAsciiLetterOrDigit);
}

/// <summary>
/// Task 50 qualification probe spec rev 4 §4 (slice S2): the closed probe <b>response</b> codec (helper → desktop) under protocol
/// "2". There is no probe request codec: a probe-mode helper never reads a frame. Encoding follows the protocol-2 update codec's
/// conventions (compact <see cref="Utf8JsonWriter"/>, relaxed escaping, enum names, canonical integers) and the reader enforces
/// whole-frame re-encode equality (Q1). Passive: no endpoint, transport or entry point uses this codec yet (S3/S4 wire it).
/// </summary>
internal static class QualificationProbeWireCodec
{
    /// <summary>§1: the fixed kind literal; the protocol value stays <see cref="ReviewedUpdateWireCodec.ProtocolVersion"/>.</summary>
    internal const string Kind = "qualify-chocolatey-helper";

    /// <summary>Q14: the codec bound, inside the unchanged 16 KiB <see cref="PipeMessageFraming"/> limit (largest possible frame 1,341 B).</summary>
    internal const int MaximumFrameBytes = 2048;

    /// <summary>§4.1 fields 11 and 12: the saturating maxima.</summary>
    internal const int MaximumNodesEvaluated = 1_048_576;
    internal const int MaximumAncestorsEvaluated = 65_536;

    /// <summary>§4.1 field 15: the only non-zero fingerprint length.</summary>
    internal const int FingerprintLength = 64;

    /// <summary>Q13: the fixed diagnostic tokens, one per row.</summary>
    internal const string TokenRefusedDiagnostic = "probe-token-refused";
    internal const string CapabilityRefusedDiagnostic = "probe-capability-refused";
    internal const string ObservedDiagnostic = "probe-observed";
    internal const string ObservationRefusedDiagnostic = "probe-observation-refused";
    internal const string ObservationTimedOutDiagnostic = "probe-observation-timed-out";

    /// <summary>§4.1: the 17 properties, in order.</summary>
    internal static readonly IReadOnlyList<string> Fields = Array.AsReadOnly(new[]
    {
        "protocolVersion", "kind", "nonce", "tokenVerdict", "g1Status", "observation", "failedStep", "failedCheck", "failedAccess",
        "refusalReason", "refusalDetail", "nodesEvaluated", "ancestorsEvaluated", "observedClientVersion", "fingerprintPresent",
        "fingerprintLength", "diagnostic",
    });

    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Indented = false,
        SkipValidation = false
    };

    private static readonly JsonDocumentOptions ReaderOptions = new()
    {
        AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow, MaxDepth = 2
    };

    /// <summary>Encodes a probe response over the compiled contract. Throws (nothing may be written) unless every Q rule holds.</summary>
    internal static byte[] CreateProbeResponse(string nonce, QualificationProbeFacts facts) =>
        CreateProbeResponse(nonce, facts, QualificationProbeContract.Compiled);

    internal static byte[] CreateProbeResponse(string nonce, QualificationProbeFacts facts, QualificationProbeContract contract)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(contract);
        if (RuleViolations(nonce, facts, contract).Count != 0)
            throw new ArgumentException("The probe response violates the closed response rules.", nameof(facts));
        byte[] frame = Encode(nonce, facts);
        return frame.Length <= MaximumFrameBytes ? frame : throw new ArgumentException("The probe response exceeds the codec bound (Q14).", nameof(facts));
    }

    /// <summary>Reads a probe response over the compiled contract. Any deviation from §4 returns false and no facts.</summary>
    internal static bool TryReadProbeResponse(ReadOnlySpan<byte> frame, string expectedNonce, out QualificationProbeFacts? facts) =>
        TryReadProbeResponse(frame, expectedNonce, QualificationProbeContract.Compiled, out facts);

    internal static bool TryReadProbeResponse(ReadOnlySpan<byte> frame, string expectedNonce, QualificationProbeContract contract,
        out QualificationProbeFacts? facts)
    {
        facts = null;
        ArgumentNullException.ThrowIfNull(contract);
        if (frame.Length is 0 or > MaximumFrameBytes || !IsHex(expectedNonce)) return false;
        try
        {
            using JsonDocument document = JsonDocument.Parse(frame.ToArray(), ReaderOptions);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return false;
            Dictionary<string, JsonElement> fields = new(StringComparer.Ordinal);
            foreach (JsonProperty property in document.RootElement.EnumerateObject())
                if (!Fields.Contains(property.Name, StringComparer.Ordinal) || !fields.TryAdd(property.Name, property.Value.Clone())) return false;
            if (fields.Count != Fields.Count) return false;

            if (String(fields, "protocolVersion") != ReviewedUpdateWireCodec.ProtocolVersion || String(fields, "kind") != Kind ||
                !FixedEquals(String(fields, "nonce"), expectedNonce)) return false;

            JsonElement g1Element = fields["g1Status"];
            if (g1Element.ValueKind != JsonValueKind.Object) return false;
            List<QualificationProbeCapabilityEntry> g1 = [];
            HashSet<string> seen = new(StringComparer.Ordinal);
            foreach (JsonProperty entry in g1Element.EnumerateObject())
            {
                if (!seen.Add(entry.Name) || entry.Value.ValueKind != JsonValueKind.String ||
                    !TryExactEnum(entry.Value.GetString()!, out QualificationProbeCapabilityStatus status)) return false;
                g1.Add(new(entry.Name, status));
            }

            JsonElement stepElement = fields["failedStep"];
            int? step = stepElement.ValueKind == JsonValueKind.Null ? null : Int(stepElement);
            JsonElement versionElement = fields["observedClientVersion"];
            string? version = versionElement.ValueKind == JsonValueKind.Null ? null : String(fields, "observedClientVersion");
            if (!TryExactEnum(String(fields, "tokenVerdict"), out QualificationProbeTokenVerdict verdict) ||
                !TryExactEnum(String(fields, "observation"), out QualificationProbeObservation observation) ||
                !TryExactEnum(String(fields, "failedCheck"), out QualificationProbeFailedCheck check) ||
                !TryExactEnum(String(fields, "failedAccess"), out QualificationProbeFailedAccess access) ||
                !TryExactEnum(String(fields, "refusalReason"), out QualificationProbeRefusalReason reason) ||
                !TryExactEnum(String(fields, "refusalDetail"), out QualificationProbeRefusalDetail detail)) return false;
            QualificationProbeFacts read = new(verdict, g1.AsReadOnly(), observation, step, check, access, reason, detail,
                Int(fields["nodesEvaluated"]), Int(fields["ancestorsEvaluated"]), version, Bool(fields["fingerprintPresent"]),
                Int(fields["fingerprintLength"]), String(fields, "diagnostic"));

            if (RuleViolations(expectedNonce, read, contract).Count != 0 || !frame.SequenceEqual(Encode(expectedNonce, read))) return false;
            facts = read;
            return true;
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or FormatException or InvalidOperationException or OverflowException)
        {
            return false;
        }
    }

    /// <summary>
    /// The identifier of every failed value rule (Q2–Q13, Q15, Q16) over the facts and nonce; empty means valid. Q1 (exact
    /// property set and canonical form) and Q14 (size) are frame rules, checked by the reader and on encoding.
    /// </summary>
    internal static IReadOnlyList<string> RuleViolations(string? nonce, QualificationProbeFacts facts, QualificationProbeContract contract)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(contract);
        IReadOnlyList<QualificationProbeCapabilityEntry> g1 = facts.G1Status ?? [];
        bool accepted = facts.Verdict == QualificationProbeTokenVerdict.Accepted;
        QualificationProbeObservation o = facts.Observation;
        bool notRun = o == QualificationProbeObservation.NotRun, observed = o == QualificationProbeObservation.Observed;
        bool refused = o == QualificationProbeObservation.Refused, timedOut = o == QualificationProbeObservation.TimedOut;
        int? step = facts.FailedStep;
        QualificationProbeFailedCheck check = facts.FailedCheck;
        QualificationProbeFailedAccess access = facts.FailedAccess;
        QualificationProbeRefusalReason reason = facts.RefusalReason;
        QualificationProbeRefusalDetail detail = facts.RefusalDetail;
        string? version = facts.ObservedClientVersion;
        IReadOnlyList<string> listed = contract.ListedClientVersions;
        List<string> violations = [];
        void Require(string id, bool holds)
        {
            if (!holds) violations.Add(id);
        }

        Require("Q2", IsHex(nonce));
        Require("Q3", Enum.IsDefined(facts.Verdict) && Enum.IsDefined(o) && Enum.IsDefined(check) && Enum.IsDefined(access) &&
            Enum.IsDefined(reason) && Enum.IsDefined(detail) && g1.All(entry => Enum.IsDefined(entry.Status)));
        Require("Q4", facts.G1Status is not null && g1.Select(entry => entry.Token).SequenceEqual(contract.G1Keys, StringComparer.Ordinal));
        Require("Q5", accepted
            ? g1.All(entry => entry.Status != QualificationProbeCapabilityStatus.NotEvaluated)
            : notRun && g1.All(entry => entry.Status == QualificationProbeCapabilityStatus.NotEvaluated));
        Require("Q6", !notRun == (accepted && g1.All(entry => entry.Status == QualificationProbeCapabilityStatus.Supported)));
        Require("Q7", refused == (reason != QualificationProbeRefusalReason.None) && refused == step.HasValue &&
            refused == (check != QualificationProbeFailedCheck.None) && refused == (access != QualificationProbeFailedAccess.None));
        Require("Q8",
            (reason != QualificationProbeRefusalReason.SourceNotCommunity || (step == 8 && check == QualificationProbeFailedCheck.StepRule)) &&
            (reason == QualificationProbeRefusalReason.InstalledMismatch) == (check == QualificationProbeFailedCheck.InstalledVersion) &&
            (reason != QualificationProbeRefusalReason.InstalledMismatch || step == 8) &&
            (reason != QualificationProbeRefusalReason.MachineInputsUnsupported || step is >= 1 and <= 7 or 9));
        Require("Q9",
            (detail == QualificationProbeRefusalDetail.SetupAccessOverride) == (check == QualificationProbeFailedCheck.SetupAccessGuard) &&
            (detail == QualificationProbeRefusalDetail.UnlistedReleaseLeftover) == (check == QualificationProbeFailedCheck.ReleaseLeftover) &&
            (detail == QualificationProbeRefusalDetail.None || (reason == QualificationProbeRefusalReason.MachineInputsUnsupported && step == 7)));
        Require("Q10", observed ? string.Equals(version, listed[^1], StringComparison.Ordinal)
            : reason == QualificationProbeRefusalReason.InstalledMismatch
                ? version is not null && listed.Take(listed.Count - 1).Contains(version, StringComparer.Ordinal)
                : version is null);
        Require("Q11", facts.FingerprintPresent == observed && facts.FingerprintLength == (facts.FingerprintPresent ? FingerprintLength : 0));
        Require("Q12", facts.NodesEvaluated is >= 0 and <= MaximumNodesEvaluated && facts.AncestorsEvaluated is >= 0 and <= MaximumAncestorsEvaluated &&
            (!notRun || (facts.NodesEvaluated == 0 && facts.AncestorsEvaluated == 0)) &&
            (!observed || (facts.NodesEvaluated >= 1 && facts.AncestorsEvaluated >= 1)));
        Require("Q13", string.Equals(facts.Diagnostic, ExpectedDiagnostic(facts.Verdict, o), StringComparison.Ordinal));
        Require("Q15", access is not (QualificationProbeFailedAccess.Granted or QualificationProbeFailedAccess.Unreadable) ||
            (reason == QualificationProbeRefusalReason.MachineInputsUnsupported && check == QualificationProbeFailedCheck.StepRule &&
             step is 1 or 4 or 5 or 6 or 7));
        Require("Q16", !timedOut || (step is null && check == QualificationProbeFailedCheck.None && access == QualificationProbeFailedAccess.None &&
            reason == QualificationProbeRefusalReason.None && detail == QualificationProbeRefusalDetail.None && version is null));
        return violations;
    }

    /// <summary>Q13: the row's fixed token; null for an undefined observation.</summary>
    internal static string? ExpectedDiagnostic(QualificationProbeTokenVerdict verdict, QualificationProbeObservation observation) => observation switch
    {
        QualificationProbeObservation.NotRun => verdict == QualificationProbeTokenVerdict.Accepted ? CapabilityRefusedDiagnostic : TokenRefusedDiagnostic,
        QualificationProbeObservation.Observed => ObservedDiagnostic,
        QualificationProbeObservation.Refused => ObservationRefusedDiagnostic,
        QualificationProbeObservation.TimedOut => ObservationTimedOutDiagnostic,
        _ => null,
    };

    private static byte[] Encode(string nonce, QualificationProbeFacts facts)
    {
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream, WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("protocolVersion", ReviewedUpdateWireCodec.ProtocolVersion);
            writer.WriteString("kind", Kind);
            writer.WriteString("nonce", nonce);
            writer.WriteString("tokenVerdict", facts.Verdict.ToString());
            writer.WriteStartObject("g1Status");
            foreach (QualificationProbeCapabilityEntry entry in facts.G1Status)
                writer.WriteString(entry.Token, entry.Status.ToString());
            writer.WriteEndObject();
            writer.WriteString("observation", facts.Observation.ToString());
            if (facts.FailedStep is int step) writer.WriteNumber("failedStep", step);
            else writer.WriteNull("failedStep");
            writer.WriteString("failedCheck", facts.FailedCheck.ToString());
            writer.WriteString("failedAccess", facts.FailedAccess.ToString());
            writer.WriteString("refusalReason", facts.RefusalReason.ToString());
            writer.WriteString("refusalDetail", facts.RefusalDetail.ToString());
            writer.WriteNumber("nodesEvaluated", facts.NodesEvaluated);
            writer.WriteNumber("ancestorsEvaluated", facts.AncestorsEvaluated);
            if (facts.ObservedClientVersion is null) writer.WriteNull("observedClientVersion");
            else writer.WriteString("observedClientVersion", facts.ObservedClientVersion);
            writer.WriteBoolean("fingerprintPresent", facts.FingerprintPresent);
            writer.WriteNumber("fingerprintLength", facts.FingerprintLength);
            writer.WriteString("diagnostic", facts.Diagnostic);
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }

    private static bool TryExactEnum<TEnum>(string text, out TEnum value) where TEnum : struct, Enum =>
        Enum.TryParse(text, false, out value) && Enum.IsDefined(value) && string.Equals(value.ToString(), text, StringComparison.Ordinal);

    private static string String(Dictionary<string, JsonElement> fields, string key) =>
        fields[key].ValueKind == JsonValueKind.String ? fields[key].GetString()! : throw new ArgumentException("String required.");
    private static int Int(JsonElement element) =>
        element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out int value) ? value : throw new ArgumentException("Integer required.");
    private static bool Bool(JsonElement element) =>
        element.ValueKind is JsonValueKind.True or JsonValueKind.False ? element.GetBoolean() : throw new ArgumentException("Boolean required.");
    private static bool IsHex(string? value) => value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F');
    private static bool FixedEquals(string value, string expected) => IsHex(value) &&
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(value), Encoding.ASCII.GetBytes(expected));
}
