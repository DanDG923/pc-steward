using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using PCSteward.Application;
using PCSteward.Domain;

namespace PCSteward.Execution;

internal enum UpdateWireInvocation { NotStarted, Started, Unknown }

/// <summary>Helper-observed data only. Aggregate settlement includes all preflight/native work, not helper exit.</summary>
internal sealed record UpdateWireFacts(UpdateWireInvocation Invocation, int? ExitCode,
    bool DirectProcessExitObserved, bool OwnedIoCompleted, bool PipeEofObserved,
    bool AggregateTerminationFailed, bool AggregateSettlementConfirmed,
    bool Cancelled, bool TimedOut, bool OutputLimitExceeded)
{
    internal bool IsConsistent => Enum.IsDefined(Invocation) &&
        (ExitCode is null || DirectProcessExitObserved) &&
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

internal sealed record FramedUpdateResponse(UpdateWireFacts Facts, string Diagnostic);

/// <summary>
/// Passive protocol-2 framing prerequisite. No active endpoint uses this codec.
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
        "aggregateSettlementConfirmed", "cancelled", "timedOut", "outputLimitExceeded", "diagnostic"
    ];

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

    internal static byte[] CreateResponse(Guid jobId, string digest, string nonce, UpdateWireFacts facts, string diagnostic)
    {
        if (jobId == Guid.Empty || !IsHex(digest) || !IsHex(nonce) || facts is null || !facts.IsConsistent || diagnostic is null)
            throw new ArgumentException("Exact correlation and consistent native observations required.");
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
            string invocationText = String(fields, "invocation");
            if (!Enum.TryParse(invocationText, false, out UpdateWireInvocation invocation) ||
                !Enum.IsDefined(invocation) || invocation.ToString() != invocationText) return false;
            JsonElement exitElement = fields["exitCode"];
            int? exit = exitElement.ValueKind == JsonValueKind.Null ? null : exitElement.GetInt32();
            UpdateWireFacts facts = new(invocation, exit, Bool(fields, "directProcessExitObserved"),
                Bool(fields, "ownedIoCompleted"), Bool(fields, "pipeEofObserved"), Bool(fields, "aggregateTerminationFailed"),
                Bool(fields, "aggregateSettlementConfirmed"), Bool(fields, "cancelled"), Bool(fields, "timedOut"), Bool(fields, "outputLimitExceeded"));
            string diagnostic = String(fields, "diagnostic");
            if (diagnostic.Length > MaximumDiagnosticBytes || diagnostic.Any(c => c is < ' ' or > '~') ||
                !frame.SequenceEqual(CreateResponse(expectedJob, expectedDigest, expectedNonce, facts, diagnostic))) return false;
            response = new(facts, diagnostic);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or InvalidOperationException or OverflowException) { return false; }
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
        catch (JsonException) { return false; }
    }

    private static string String(Dictionary<string, JsonElement> fields, string key) =>
        fields[key].ValueKind == JsonValueKind.String ? fields[key].GetString()! : throw new ArgumentException("String required.");
    private static bool Bool(Dictionary<string, JsonElement> fields, string key) => fields[key].GetBoolean();
    private static bool IsHex(string? value) => value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F');
    private static bool FixedEquals(string value, string expected) => IsHex(value) &&
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(value), Encoding.ASCII.GetBytes(expected));
    private static byte[] Bounded(MemoryStream stream) => stream.Length <= MaximumFrameBytes
        ? stream.ToArray() : throw new ArgumentException("Frame exceeds the fixed message ceiling.");
    private static string AsciiDiagnostic(string value)
    {
        StringBuilder result = new(MaximumDiagnosticBytes);
        foreach (Rune rune in SanitizedDiagnosticBundle.Sanitize(value).EnumerateRunes())
        {
            if (result.Length == MaximumDiagnosticBytes) break;
            result.Append(rune.Value is >= 32 and <= 126 ? (char)rune.Value : '?');
        }
        return result.ToString();
    }
}
