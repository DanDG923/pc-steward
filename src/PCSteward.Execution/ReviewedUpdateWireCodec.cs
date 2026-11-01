using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using PCSteward.Application;
using PCSteward.Domain;
using PCSteward.Provider.Chocolatey;

namespace PCSteward.Execution;

internal enum UpdateWireInvocation { NotStarted, Started, Unknown }

/// <summary>Helper-observed data only. Aggregate settlement includes all preflight/native work, not helper exit.</summary>
internal sealed record UpdateWireFacts(UpdateWireInvocation Invocation, int? ExitCode,
    bool DirectProcessExitObserved, bool OwnedIoCompleted, bool PipeEofObserved,
    bool AggregateTerminationFailed, bool AggregateSettlementConfirmed,
    bool Cancelled, bool TimedOut, bool OutputLimitExceeded)
{
    internal bool IsConsistent => Enum.IsDefined(Invocation) &&
        (ExitCode.HasValue == DirectProcessExitObserved) &&
        (Invocation == UpdateWireInvocation.Started ||
            (ExitCode is null && !DirectProcessExitObserved && !OwnedIoCompleted && !PipeEofObserved)) &&
        (!AggregateSettlementConfirmed || (!AggregateTerminationFailed && Invocation != UpdateWireInvocation.Unknown &&
            (Invocation == UpdateWireInvocation.NotStarted || (DirectProcessExitObserved && OwnedIoCompleted && PipeEofObserved))));

    // This cannot authorize Final: the desktop must independently observe helper settlement and verify the target.
    internal bool RequiresUncertainty => !IsConsistent || !AggregateSettlementConfirmed;
}

/// <summary>Untrusted framed data, not an accepted operation or restored consent.</summary>
internal sealed class FramedUpdateRequest
{
    private readonly byte[] payload;
    internal FramedUpdateRequest(byte[] payload, string digest, string nonce, DateTimeOffset issuedAt)
    {
        this.payload = (byte[])payload.Clone();
        Digest = digest;
        Nonce = nonce;
        IssuedAt = issuedAt;
    }
    internal string Digest { get; }
    internal string Nonce { get; }
    internal DateTimeOffset IssuedAt { get; }
    internal byte[] GetPayload() => (byte[])payload.Clone();
}

/// <summary>Closed protocol-2 helper qualification claim (Task 50 fact extension §2 field 14). Never qualifies anything by itself.</summary>
internal enum ReviewedHelperQualification { Undetermined, Matched, Refused }

/// <summary>Closed protocol-2 refusal reason (§2 field 15). <c>None</c> unless the qualification is Refused.</summary>
internal enum ReviewedHelperRefusalReason
{
    None, RequestNotAdmitted, MachineInputsUnsupported, SourceNotCommunity, InstalledMismatch, ProfileMismatch,
    PreflightQueryFailed, CandidateMismatch, ApprovalExpired
}

/// <summary>
/// Untrusted helper-reported reviewed facts (§2 fields 14–18). The codec checks only operation-independent structure
/// (§3.2 S1–S13); the operation-bound predicates P1–P6 belong to the desktop adapter and are not checked here.
/// </summary>
internal sealed record ReviewedHelperFacts(ReviewedHelperQualification Qualification, ReviewedHelperRefusalReason RefusalReason,
    string? ObservedInstalledVersion, string? ObservedCandidateVersion, string? ObservedProfileFingerprint);

internal sealed record FramedUpdateResponse(UpdateWireFacts Facts, ReviewedHelperFacts Reviewed, string Diagnostic);

/// <summary>
/// Passive protocol-2 framing prerequisite. No active endpoint uses this codec. Protocol "2" is redefined in place
/// (Task 50 reviewed fact extension rev 3, O1): the response has exactly 20 properties; the 15-field shape is not accepted.
/// The future endpoint must independently admit schema, peer, freshness, capability and native evidence.
/// </summary>
internal static class ReviewedUpdateWireCodec
{
    internal const int MaximumFrameBytes = 16 * 1024;
    internal const int MaximumPayloadBytes = ReviewedUpdateOperation.MaximumCanonicalBytes;
    internal const int MaximumDiagnosticBytes = 2048;
    private const string UtcFormat = "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'";
    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Indented = false,
        SkipValidation = false
    };
    private static readonly string[] RequestFields =
        ["protocolVersion", "operation", "operationPayload", "operationDigest", "nonce", "issuedAtUtc"];
    private static readonly string[] ResponseFields =
    [
        "protocolVersion", "jobId", "operationDigest", "nonce", "invocation", "exitCode",
        "directProcessExitObserved", "ownedIoCompleted", "pipeEofObserved", "aggregateTerminationFailed",
        "aggregateSettlementConfirmed", "cancelled", "timedOut", "outputLimitExceeded", "qualification", "refusalReason",
        "observedInstalledVersion", "observedCandidateVersion", "observedProfileFingerprint", "diagnostic"
    ];
    internal const int MaximumVersionLength = 128;

    internal static byte[] CreateRequest(ReadOnlySpan<byte> payload, string digest, string nonce, DateTimeOffset issuedAt)
    {
        if (payload.Length is 0 or > MaximumPayloadBytes || !IsHex(digest) || !IsHex(nonce) || issuedAt == default ||
            Convert.ToHexString(SHA256.HashData(payload)) != digest)
            throw new ArgumentException("Bounded payload, exact digest, nonce and timestamp required.");
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream, WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("protocolVersion", "2");
            writer.WriteString("operation", "UpdatePackage");
            writer.WriteString("operationPayload", Convert.ToBase64String(payload));
            writer.WriteString("operationDigest", digest);
            writer.WriteString("nonce", nonce);
            writer.WriteString("issuedAtUtc", issuedAt.ToUniversalTime().ToString(UtcFormat, CultureInfo.InvariantCulture));
            writer.WriteEndObject();
        }
        return Bounded(stream);
    }

    internal static bool TryReadRequest(ReadOnlySpan<byte> frame, out FramedUpdateRequest? request)
    {
        request = null;
        if (!TryReadObject(frame, RequestFields, out Dictionary<string, JsonElement> fields)) return false;
        try
        {
            if (String(fields, "protocolVersion") != "2" || String(fields, "operation") != "UpdatePackage") return false;
            string payloadText = String(fields, "operationPayload");
            if (payloadText.Length > ((MaximumPayloadBytes + 2) / 3) * 4) return false;
            byte[] payload = Convert.FromBase64String(payloadText);
            if (Convert.ToBase64String(payload) != payloadText) return false;
            string digest = String(fields, "operationDigest"), nonce = String(fields, "nonce");
            if (!DateTimeOffset.TryParseExact(String(fields, "issuedAtUtc"), UtcFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset issued)) return false;
            if (!frame.SequenceEqual(CreateRequest(payload, digest, nonce, issued))) return false;
            request = new(payload, digest, nonce, issued);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or InvalidOperationException) { return false; }
    }

    internal static byte[] CreateResponse(Guid jobId, string digest, string nonce, UpdateWireFacts facts, ReviewedHelperFacts reviewed,
        string diagnostic)
    {
        if (jobId == Guid.Empty || !IsHex(digest) || !IsHex(nonce) || facts is null || reviewed is null || diagnostic is null ||
            StructuralViolations(facts, reviewed).Count != 0)
            throw new ArgumentException("Exact correlation, consistent native observations and structurally valid reviewed facts required.");
        string safeDiagnostic = AsciiDiagnostic(diagnostic);
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream, WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("protocolVersion", "2");
            writer.WriteString("jobId", jobId.ToString("D", CultureInfo.InvariantCulture));
            writer.WriteString("operationDigest", digest);
            writer.WriteString("nonce", nonce);
            writer.WriteString("invocation", facts.Invocation.ToString());
            if (facts.ExitCode is int exit) writer.WriteNumber("exitCode", exit);
            else writer.WriteNull("exitCode");
            writer.WriteBoolean("directProcessExitObserved", facts.DirectProcessExitObserved);
            writer.WriteBoolean("ownedIoCompleted", facts.OwnedIoCompleted);
            writer.WriteBoolean("pipeEofObserved", facts.PipeEofObserved);
            writer.WriteBoolean("aggregateTerminationFailed", facts.AggregateTerminationFailed);
            writer.WriteBoolean("aggregateSettlementConfirmed", facts.AggregateSettlementConfirmed);
            writer.WriteBoolean("cancelled", facts.Cancelled);
            writer.WriteBoolean("timedOut", facts.TimedOut);
            writer.WriteBoolean("outputLimitExceeded", facts.OutputLimitExceeded);
            writer.WriteString("qualification", reviewed.Qualification.ToString());
            writer.WriteString("refusalReason", reviewed.RefusalReason.ToString());
            WriteNullable(writer, "observedInstalledVersion", reviewed.ObservedInstalledVersion);
            WriteNullable(writer, "observedCandidateVersion", reviewed.ObservedCandidateVersion);
            WriteNullable(writer, "observedProfileFingerprint", reviewed.ObservedProfileFingerprint);
            writer.WriteString("diagnostic", safeDiagnostic);
            writer.WriteEndObject();
        }
        return Bounded(stream);
    }

    internal static bool TryReadResponse(ReadOnlySpan<byte> frame, Guid expectedJob, string expectedDigest, string expectedNonce,
        out FramedUpdateResponse? response)
    {
        response = null;
        if (expectedJob == Guid.Empty || !IsHex(expectedDigest) || !IsHex(expectedNonce) ||
            !TryReadObject(frame, ResponseFields, out Dictionary<string, JsonElement> fields)) return false;
        try
        {
            if (String(fields, "protocolVersion") != "2" ||
                String(fields, "jobId") != expectedJob.ToString("D", CultureInfo.InvariantCulture) ||
                !FixedEquals(String(fields, "operationDigest"), expectedDigest) || !FixedEquals(String(fields, "nonce"), expectedNonce)) return false;
            if (!TryExactEnum(String(fields, "invocation"), out UpdateWireInvocation invocation) ||
                !TryExactEnum(String(fields, "qualification"), out ReviewedHelperQualification qualification) ||
                !TryExactEnum(String(fields, "refusalReason"), out ReviewedHelperRefusalReason reason)) return false;
            JsonElement exitElement = fields["exitCode"];
            int? exit = exitElement.ValueKind == JsonValueKind.Null ? null : exitElement.GetInt32();
            UpdateWireFacts facts = new(invocation, exit, Bool(fields, "directProcessExitObserved"),
                Bool(fields, "ownedIoCompleted"), Bool(fields, "pipeEofObserved"), Bool(fields, "aggregateTerminationFailed"),
                Bool(fields, "aggregateSettlementConfirmed"), Bool(fields, "cancelled"), Bool(fields, "timedOut"), Bool(fields, "outputLimitExceeded"));
            ReviewedHelperFacts reviewed = new(qualification, reason, NullableString(fields, "observedInstalledVersion"),
                NullableString(fields, "observedCandidateVersion"), NullableString(fields, "observedProfileFingerprint"));
            string diagnostic = String(fields, "diagnostic");
            if (StructuralViolations(facts, reviewed).Count != 0 ||
                diagnostic.Length > MaximumDiagnosticBytes || diagnostic.Any(c => c is < ' ' or > '~') ||
                !frame.SequenceEqual(CreateResponse(expectedJob, expectedDigest, expectedNonce, facts, reviewed, diagnostic))) return false;
            response = new(facts, reviewed, diagnostic);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or InvalidOperationException or OverflowException) { return false; }
    }

    /// <summary>
    /// Returns the identifier of every failed operation-independent predicate: "Facts" for
    /// <see cref="UpdateWireFacts.IsConsistent"/>, then S1–S13 from the reviewed fact extension §3.2. Empty means valid.
    /// Over the three defined invocation values S3 and S4 are logically equivalent, so a violation reports both.
    /// </summary>
    internal static IReadOnlyList<string> StructuralViolations(UpdateWireFacts facts, ReviewedHelperFacts reviewed)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(reviewed);
        ReviewedHelperQualification q = reviewed.Qualification;
        ReviewedHelperRefusalReason r = reviewed.RefusalReason;
        bool matched = q == ReviewedHelperQualification.Matched, refused = q == ReviewedHelperQualification.Refused;
        bool undetermined = q == ReviewedHelperQualification.Undetermined;
        bool installed = reviewed.ObservedInstalledVersion is not null, candidate = reviewed.ObservedCandidateVersion is not null;
        bool fingerprint = reviewed.ObservedProfileFingerprint is not null;
        List<string> violations = [];
        void Require(string id, bool holds)
        {
            if (!holds) violations.Add(id);
        }
        Require("Facts", facts.IsConsistent);
        Require("S1", Enum.IsDefined(facts.Invocation) && Enum.IsDefined(q) && Enum.IsDefined(r));
        Require("S2", (r == ReviewedHelperRefusalReason.None) == !refused);
        Require("S3", facts.Invocation is not (UpdateWireInvocation.Started or UpdateWireInvocation.Unknown) || matched);
        Require("S4", !(refused || undetermined) || facts.Invocation == UpdateWireInvocation.NotStarted);
        Require("S5", !matched || (installed && candidate && fingerprint));
        Require("S6", r != ReviewedHelperRefusalReason.RequestNotAdmitted || (!installed && !candidate && !fingerprint));
        Require("S7", !candidate || (installed &&
            r is not (ReviewedHelperRefusalReason.RequestNotAdmitted or ReviewedHelperRefusalReason.PreflightQueryFailed)));
        Require("S8", r != ReviewedHelperRefusalReason.SourceNotCommunity || !fingerprint);
        Require("S9", r != ReviewedHelperRefusalReason.ProfileMismatch || (installed && fingerprint));
        Require("S10", !refused || (!facts.Cancelled && !facts.TimedOut &&
            (!facts.OutputLimitExceeded || r == ReviewedHelperRefusalReason.PreflightQueryFailed)));
        Require("S11", !undetermined || facts.Cancelled || facts.TimedOut || !facts.AggregateSettlementConfirmed);
        Require("S12", IsNullOrNormalizedVersion(reviewed.ObservedInstalledVersion) &&
            IsNullOrNormalizedVersion(reviewed.ObservedCandidateVersion) &&
            (reviewed.ObservedProfileFingerprint is null || IsHex(reviewed.ObservedProfileFingerprint)));
        Require("S13", !(matched && facts.Invocation == UpdateWireInvocation.NotStarted) || facts.AggregateSettlementConfirmed);
        return violations;
    }

    /// <summary>
    /// §2 fields 16/17 grammar: exactly a <c>ChocolateyNativeVersion.Normalized</c> string of at most 128 characters.
    /// Task 50 O9 choice (a): the codec reuses the pinned native parser through InternalsVisibleTo, not a second grammar.
    /// </summary>
    internal static bool IsNormalizedVersion(string? value) => value is { Length: > 0 and <= MaximumVersionLength } &&
        ChocolateyNativeVersion.TryParse(value, out ChocolateyNativeVersion? version) &&
        string.Equals(version!.Normalized, value, StringComparison.Ordinal);

    private static bool IsNullOrNormalizedVersion(string? value) => value is null || IsNormalizedVersion(value);

    private static bool TryExactEnum<TEnum>(string text, out TEnum value) where TEnum : struct, Enum =>
        Enum.TryParse(text, false, out value) && Enum.IsDefined(value) && string.Equals(value.ToString(), text, StringComparison.Ordinal);

    private static void WriteNullable(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is null) writer.WriteNull(name);
        else writer.WriteString(name, value);
    }

    private static bool TryReadObject(ReadOnlySpan<byte> frame, string[] expected, out Dictionary<string, JsonElement> fields)
    {
        fields = new(StringComparer.Ordinal);
        if (frame.Length is 0 or > MaximumFrameBytes) return false;
        try
        {
            using JsonDocument document = JsonDocument.Parse(frame.ToArray(), new JsonDocumentOptions
            {
                AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow, MaxDepth = 2
            });
            if (document.RootElement.ValueKind != JsonValueKind.Object) return false;
            foreach (JsonProperty property in document.RootElement.EnumerateObject())
                if (!expected.Contains(property.Name, StringComparer.Ordinal) || !fields.TryAdd(property.Name, property.Value.Clone())) return false;
            return fields.Count == expected.Length;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException) { return false; }
    }

    private static string String(Dictionary<string, JsonElement> fields, string key) =>
        fields[key].ValueKind == JsonValueKind.String ? fields[key].GetString()! : throw new ArgumentException("String required.");
    private static string? NullableString(Dictionary<string, JsonElement> fields, string key) =>
        fields[key].ValueKind == JsonValueKind.Null ? null : String(fields, key);
    private static bool Bool(Dictionary<string, JsonElement> fields, string key) => fields[key].GetBoolean();
    private static bool IsHex(string? value) => value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F');
    private static bool FixedEquals(string value, string expected) => IsHex(value) &&
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(value), Encoding.ASCII.GetBytes(expected));
    private static byte[] Bounded(MemoryStream stream) => stream.Length <= MaximumFrameBytes
        ? stream.ToArray() : throw new ArgumentException("Frame exceeds the fixed message ceiling.");
    private static string AsciiDiagnostic(string value)
    {
        // Preserve Unicode case-equivalent redactions first. Convert before the second redaction,
        // because replacing another Unicode letter may create a sensitive-value word boundary.
        string redacted = SanitizedDiagnosticBundle.Sanitize(value);
        StringBuilder ascii = new(redacted.Length);
        foreach (Rune rune in redacted.EnumerateRunes())
        {
            ascii.Append(rune.Value is >= 32 and <= 126 ? (char)rune.Value : Rune.IsWhiteSpace(rune) ? ' ' : '?');
        }
        string canonical = CanonicalDiagnostic(ascii.ToString());
        return CanonicalDiagnostic(canonical) == canonical ? canonical : "[diagnostic unavailable]";
    }

    private static string CanonicalDiagnostic(string ascii)
    {
        string safe = SanitizedDiagnosticBundle.Sanitize(ascii);
        return safe[..Math.Min(safe.Length, MaximumDiagnosticBytes)].TrimEnd();
    }
}
