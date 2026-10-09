using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PCSteward.Application;
using PCSteward.Domain;

namespace PCSteward.Execution;

/// <summary>One elevated item as the helper accepted it. <see cref="SkipReason"/> can only withhold execution.</summary>
internal sealed record HelperBatchItem(int Sequence, ImmutableUpdateJob Job, string? SkipReason);

/// <summary>The elevated tail of one approved batch, accepted after every frame and digest check.</summary>
internal sealed record HelperBatchRequest(Guid BatchId, Guid CorrelationId, DateTimeOffset ApprovedAt, IReadOnlyList<HelperBatchItem> Items);

internal abstract record HelperBatchEvent;
internal sealed record HelperItemStartedEvent(int Sequence, Guid JobId) : HelperBatchEvent;
internal sealed record HelperItemResultEvent(int Sequence, Guid JobId, UpdateProviderAttemptStatus Status, int? ExitCode, bool RestartPending, string Diagnostic) : HelperBatchEvent;
internal sealed record HelperBatchCompleteEvent(int ItemCount) : HelperBatchEvent;

/// <summary>
/// ADR-018 protocol v2 (FR-BAT-05/06, AC-V3-05): one approved batch per helper process. The desktop sends a
/// header, one frame per elevated item and an end frame; each frame is a strict JSON object within the
/// 16 KiB bound. The helper streams ItemStarted/ItemResult frames and a final BatchComplete. The only frame
/// the desktop may send after the request is CancelRemaining, which takes effect between items.
/// </summary>
internal static partial class ElevatedHelperProtocolV2
{
    internal const string Version = "2";
    internal const int MaximumFrameBytes = ElevatedHelperProtocol.MaximumMessageBytes;
    internal const int MaximumDiagnosticLength = 4096;
    internal static readonly TimeSpan RequestLifetime = TimeSpan.FromMinutes(2);
    private static readonly string[] AllowedSkipReasons = [BatchSkipReasons.RunningApplication, BatchSkipReasons.DesktopRevalidationFailed];

    internal static readonly string[] HeaderFields = ["frame", "protocolVersion", "batchId", "correlationId", "approvedAtUtc", "batchDigest", "elevatedDigest", "firstSequence", "itemCount", "nonce", "issuedAtUtc"];
    internal static readonly string[] ItemFields = ["frame", "protocolVersion", "batchId", "sequence", "jobId", "operation", "provider", "source", "stableProductKey", "packageId", "installedVersion", "targetVersion", "scope", "architecture", "channel", "mappingEvidenceVersion", "agreementHash", "runsElevated", "jobDigest", "skipReason"];
    internal static readonly string[] EndFields = ["frame", "protocolVersion", "batchId", "itemCount"];
    internal static readonly string[] CancelFields = ["frame", "protocolVersion", "batchId", "nonce"];
    internal static readonly string[] StartedFields = ["frame", "protocolVersion", "batchId", "nonce", "sequence", "jobId"];
    internal static readonly string[] ResultFields = ["frame", "protocolVersion", "batchId", "nonce", "sequence", "jobId", "status", "exitCode", "restartPending", "sanitizedOutput"];
    internal static readonly string[] CompleteFields = ["frame", "protocolVersion", "batchId", "nonce", "itemCount"];

    internal static bool IsAllowedSkipReason(string? reason) => reason is null || AllowedSkipReasons.Contains(reason, StringComparer.Ordinal);

    /// <summary>Frames for the elevated tail of <paramref name="batch"/>. Skip reasons only withhold items.</summary>
    internal static IReadOnlyList<byte[]> CreateBatchRequest(ApprovedOperationBatch batch, IReadOnlyDictionary<Guid, string>? skipReasons, string nonce, DateTimeOffset issuedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ValidateNonce(nonce);
        if (!batch.HasValidDigest) throw new ArgumentException("The batch failed its integrity check.", nameof(batch));
        IReadOnlyList<ImmutableUpdateJob> elevated = batch.ElevatedItems;
        if (elevated.Count == 0) throw new ArgumentException("The batch has no elevated items.", nameof(batch));
        if (skipReasons is not null && skipReasons.Any(pair => !elevated.Any(job => job.Id == pair.Key) || pair.Value is null || !IsAllowedSkipReason(pair.Value)))
        {
            throw new ArgumentException("Skip reasons must name elevated items with an allowlisted reason.", nameof(skipReasons));
        }

        int firstSequence = batch.UnelevatedItems.Count;
        string batchId = Id(batch.Id);
        List<byte[]> frames =
        [
            Serialize(HeaderFields, new()
            {
                ["frame"] = "BatchHeader",
                ["protocolVersion"] = Version,
                ["batchId"] = batchId,
                ["correlationId"] = Id(batch.CorrelationId),
                ["approvedAtUtc"] = Time(batch.ApprovedAt),
                ["batchDigest"] = batch.Digest,
                ["elevatedDigest"] = batch.ElevatedDigest,
                ["firstSequence"] = firstSequence,
                ["itemCount"] = elevated.Count,
                ["nonce"] = nonce,
                ["issuedAtUtc"] = Time(issuedAtUtc)
            })
        ];
        for (int index = 0; index < elevated.Count; index++)
        {
            ImmutableUpdateJob job = elevated[index];
            UpdateJobSelection selection = job.Selection;
            frames.Add(Serialize(ItemFields, new()
            {
                ["frame"] = "BatchItem",
                ["protocolVersion"] = Version,
                ["batchId"] = batchId,
                ["sequence"] = firstSequence + index,
                ["jobId"] = Id(job.Id),
                ["operation"] = selection.Operation.ToString(),
                ["provider"] = selection.Provider.ToString(),
                ["source"] = selection.Source.ToString(),
                ["stableProductKey"] = selection.StableProductKey,
                ["packageId"] = selection.PackageId,
                ["installedVersion"] = selection.InstalledVersion,
                ["targetVersion"] = selection.CandidateVersion,
                ["scope"] = selection.Scope.ToString(),
                ["architecture"] = selection.Architecture.ToString(),
                ["channel"] = selection.Channel.ToString(),
                ["mappingEvidenceVersion"] = selection.MappingEvidenceVersion,
                ["agreementHash"] = selection.AgreementHash,
                ["runsElevated"] = selection.RunsElevated,
                ["jobDigest"] = job.Digest,
                ["skipReason"] = skipReasons?.GetValueOrDefault(job.Id)
            }));
        }

        frames.Add(Serialize(EndFields, new() { ["frame"] = "BatchEnd", ["protocolVersion"] = Version, ["batchId"] = batchId, ["itemCount"] = elevated.Count }));
        return frames;
    }

    internal static byte[] CreateCancelRemaining(Guid batchId, string nonce)
    {
        ValidateNonce(nonce);
        return Serialize(CancelFields, new() { ["frame"] = "CancelRemaining", ["protocolVersion"] = Version, ["batchId"] = Id(batchId), ["nonce"] = nonce });
    }

    internal static byte[] CreateItemStarted(Guid batchId, string nonce, int sequence, Guid jobId)
    {
        ValidateNonce(nonce);
        return Serialize(StartedFields, new() { ["frame"] = "ItemStarted", ["protocolVersion"] = Version, ["batchId"] = Id(batchId), ["nonce"] = nonce, ["sequence"] = sequence, ["jobId"] = Id(jobId) });
    }

    internal static byte[] CreateItemResult(Guid batchId, string nonce, int sequence, Guid jobId, UpdateProviderAttemptStatus status, int? exitCode, bool restartPending, string diagnostic)
    {
        ValidateNonce(nonce);
        if (!Enum.IsDefined(status)) throw new ArgumentOutOfRangeException(nameof(status));
        return Serialize(ResultFields, new()
        {
            ["frame"] = "ItemResult",
            ["protocolVersion"] = Version,
            ["batchId"] = Id(batchId),
            ["nonce"] = nonce,
            ["sequence"] = sequence,
            ["jobId"] = Id(jobId),
            ["status"] = status.ToString(),
            ["exitCode"] = exitCode,
            ["restartPending"] = restartPending,
            ["sanitizedOutput"] = NonElevatedUpdateRunner.Sanitize(diagnostic)
        });
    }

    internal static byte[] CreateBatchComplete(Guid batchId, string nonce, int itemCount)
    {
        ValidateNonce(nonce);
        return Serialize(CompleteFields, new() { ["frame"] = "BatchComplete", ["protocolVersion"] = Version, ["batchId"] = Id(batchId), ["nonce"] = nonce, ["itemCount"] = itemCount });
    }

    /// <summary>Strict frame parse: bounded size, one JSON object, no duplicate or unknown fields, protocol 2.</summary>
    internal static bool TryReadFrame(ReadOnlySpan<byte> utf8, IReadOnlyDictionary<string, string[]> allowedFrames, out string frameKind, out Dictionary<string, JsonElement> fields, out string rejection)
    {
        frameKind = string.Empty;
        fields = [];
        if (utf8.IsEmpty || utf8.Length > MaximumFrameBytes) { rejection = "frame-size-invalid"; return false; }
        try
        {
            using JsonDocument document = JsonDocument.Parse(utf8.ToArray(), new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow, MaxDepth = 2 });
            if (document.RootElement.ValueKind != JsonValueKind.Object) { rejection = "frame-not-object"; return false; }
            Dictionary<string, JsonElement> parsed = new(StringComparer.Ordinal);
            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                if (!parsed.TryAdd(property.Name, property.Value.Clone())) { rejection = "frame-duplicate-field"; return false; }
            }

            if (!TryText(parsed, "frame", out string kind) || !allowedFrames.TryGetValue(kind, out string[]? expected)) { rejection = "frame-kind-unexpected"; return false; }
            if (parsed.Count != expected.Length || !expected.All(parsed.ContainsKey)) { rejection = "frame-fields-invalid"; return false; }
            if (!TryText(parsed, "protocolVersion", out string version) || version != Version) { rejection = "frame-protocol-invalid"; return false; }
            frameKind = kind;
            fields = parsed;
            rejection = string.Empty;
            return true;
        }
        catch (JsonException)
        {
            rejection = "frame-json-invalid";
            return false;
        }
    }

    internal static bool TryText(Dictionary<string, JsonElement> fields, string name, out string value, int maximumLength = 256)
    {
        value = string.Empty;
        if (!fields.TryGetValue(name, out JsonElement element) || element.ValueKind != JsonValueKind.String) return false;
        value = element.GetString() ?? string.Empty;
        return value.Length > 0 && value.Length <= maximumLength && !value.Any(char.IsControl);
    }

    internal static bool TryOptionalText(Dictionary<string, JsonElement> fields, string name, out string? value)
    {
        value = null;
        if (!fields.TryGetValue(name, out JsonElement element)) return false;
        if (element.ValueKind == JsonValueKind.Null) return true;
        bool valid = TryText(fields, name, out string text);
        value = valid ? text : null;
        return valid;
    }

    internal static bool TryInt(Dictionary<string, JsonElement> fields, string name, out int value)
    {
        value = 0;
        return fields.TryGetValue(name, out JsonElement element) && element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out value);
    }

    internal static bool TryBool(Dictionary<string, JsonElement> fields, string name, out bool value)
    {
        value = false;
        if (!fields.TryGetValue(name, out JsonElement element) || element.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
        value = element.GetBoolean();
        return true;
    }

    internal static bool TryGuid(Dictionary<string, JsonElement> fields, string name, out Guid value)
    {
        value = Guid.Empty;
        return TryText(fields, name, out string text) && Guid.TryParseExact(text, "D", out value) && value != Guid.Empty;
    }

    internal static bool TryTime(Dictionary<string, JsonElement> fields, string name, out DateTimeOffset value)
    {
        value = default;
        return TryText(fields, name, out string text) && DateTimeOffset.TryParseExact(text, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out value);
    }

    internal static bool TryEnum<T>(Dictionary<string, JsonElement> fields, string name, out T value) where T : struct, Enum
    {
        value = default;
        return TryText(fields, name, out string text) && !int.TryParse(text, out _) && Enum.TryParse(text, ignoreCase: false, out value) && Enum.IsDefined(value);
    }

    internal static bool IsDigest(string value) => DigestPattern().IsMatch(value);

    internal static bool FixedEquals(string first, string second)
    {
        byte[] left = Encoding.UTF8.GetBytes(first);
        byte[] right = Encoding.UTF8.GetBytes(second);
        return left.Length == right.Length && CryptographicOperations.FixedTimeEquals(left, right);
    }

    private static string Id(Guid value) => value.ToString("D", CultureInfo.InvariantCulture);

    private static string Time(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static void ValidateNonce(string nonce)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nonce);
        if (!NoncePattern().IsMatch(nonce)) throw new ArgumentException("The nonce is invalid.", nameof(nonce));
    }

    private static byte[] Serialize(string[] order, Dictionary<string, object?> fields)
    {
        using MemoryStream buffer = new();
        using (Utf8JsonWriter writer = new(buffer, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            foreach (string property in order)
            {
                writer.WritePropertyName(property);
                switch (fields[property])
                {
                    case null: writer.WriteNullValue(); break;
                    case int integer: writer.WriteNumberValue(integer); break;
                    case bool flag: writer.WriteBooleanValue(flag); break;
                    case string text: writer.WriteStringValue(text); break;
                    default: throw new InvalidOperationException("Unsupported frame value.");
                }
            }
            writer.WriteEndObject();
        }

        if (buffer.Length > MaximumFrameBytes) throw new InvalidOperationException("The helper frame exceeded its fixed bound.");
        return buffer.ToArray();
    }

    [GeneratedRegex("^[A-F0-9]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex NoncePattern();

    [GeneratedRegex("^[A-F0-9]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex DigestPattern();
}

internal enum HelperReceiverState { AwaitingHeader, ReceivingItems, Complete, Rejected }

/// <summary>
/// Helper side, one-shot: accepts exactly BatchHeader, the announced number of BatchItem frames in contiguous
/// sequence, and BatchEnd. Every item must be an elevated operation whose job digest verifies; the elevated
/// digest over the received items must match the header. Any deviation rejects the whole batch.
/// </summary>
internal sealed class HelperBatchRequestReceiver(string expectedNonce, Func<DateTimeOffset>? utcNow = null)
{
    private static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(15);
    private static readonly Dictionary<string, string[]> HeaderFrame = new(StringComparer.Ordinal) { ["BatchHeader"] = ElevatedHelperProtocolV2.HeaderFields };
    private static readonly Dictionary<string, string[]> ItemOrEndFrame = new(StringComparer.Ordinal) { ["BatchItem"] = ElevatedHelperProtocolV2.ItemFields, ["BatchEnd"] = ElevatedHelperProtocolV2.EndFields };
    private readonly string expectedNonce = expectedNonce;
    private readonly Func<DateTimeOffset> utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    private readonly List<HelperBatchItem> items = [];
    private Guid batchId;
    private Guid correlationId;
    private DateTimeOffset approvedAt;
    private string batchDigest = string.Empty;
    private string elevatedDigest = string.Empty;
    private int firstSequence;
    private int expectedCount;

    public HelperReceiverState State { get; private set; } = HelperReceiverState.AwaitingHeader;
    public string Rejection { get; private set; } = string.Empty;
    public HelperBatchRequest? Request { get; private set; }

    public bool Accept(ReadOnlySpan<byte> frame)
    {
        if (State is HelperReceiverState.Complete or HelperReceiverState.Rejected) return Reject("frame-after-terminal-state");
        IReadOnlyDictionary<string, string[]> allowed = State == HelperReceiverState.AwaitingHeader ? HeaderFrame : ItemOrEndFrame;
        if (!ElevatedHelperProtocolV2.TryReadFrame(frame, allowed, out string kind, out Dictionary<string, JsonElement> fields, out string rejection)) return Reject(rejection);
        return kind switch
        {
            "BatchHeader" => AcceptHeader(fields),
            "BatchItem" => AcceptItem(fields),
            _ => AcceptEnd(fields)
        };
    }

    private bool AcceptHeader(Dictionary<string, JsonElement> fields)
    {
        if (!ElevatedHelperProtocolV2.TryText(fields, "nonce", out string nonce) || !ElevatedHelperProtocolV2.FixedEquals(nonce, expectedNonce)) return Reject("nonce-mismatch");
        if (!ElevatedHelperProtocolV2.TryGuid(fields, "batchId", out batchId) || !ElevatedHelperProtocolV2.TryGuid(fields, "correlationId", out correlationId)) return Reject("batch-identity-invalid");
        if (!ElevatedHelperProtocolV2.TryTime(fields, "issuedAtUtc", out DateTimeOffset issuedAt) || !ElevatedHelperProtocolV2.TryTime(fields, "approvedAtUtc", out approvedAt)) return Reject("batch-time-invalid");
        DateTimeOffset now = utcNow();
        TimeSpan age = now - issuedAt;
        if (age < -ClockSkew || age > ElevatedHelperProtocolV2.RequestLifetime) return Reject("request-expired");
        if (!ApprovedOperationBatch.IsUsableAt(approvedAt, now)) return Reject("batch-expired");
        if (!ElevatedHelperProtocolV2.TryText(fields, "batchDigest", out batchDigest) || !ElevatedHelperProtocolV2.IsDigest(batchDigest) ||
            !ElevatedHelperProtocolV2.TryText(fields, "elevatedDigest", out elevatedDigest) || !ElevatedHelperProtocolV2.IsDigest(elevatedDigest)) return Reject("batch-digest-invalid");
        if (!ElevatedHelperProtocolV2.TryInt(fields, "firstSequence", out firstSequence) || !ElevatedHelperProtocolV2.TryInt(fields, "itemCount", out expectedCount) ||
            firstSequence < 0 || expectedCount is < 1 or > ApprovedOperationBatch.MaximumItems || firstSequence + expectedCount > ApprovedOperationBatch.MaximumItems) return Reject("batch-count-invalid");
        State = HelperReceiverState.ReceivingItems;
        return true;
    }

    private bool AcceptItem(Dictionary<string, JsonElement> fields)
    {
        if (!ElevatedHelperProtocolV2.TryGuid(fields, "batchId", out Guid itemBatch) || itemBatch != batchId) return Reject("item-batch-mismatch");
        if (items.Count >= expectedCount) return Reject("item-appended");
        if (!ElevatedHelperProtocolV2.TryInt(fields, "sequence", out int sequence) || sequence != firstSequence + items.Count) return Reject("item-out-of-order");
        if (!ElevatedHelperProtocolV2.TryBool(fields, "runsElevated", out bool runsElevated) || !runsElevated) return Reject("unelevated-item-sent-to-helper");
        if (!ElevatedHelperProtocolV2.TryOptionalText(fields, "skipReason", out string? skipReason) || !ElevatedHelperProtocolV2.IsAllowedSkipReason(skipReason)) return Reject("item-skip-reason-invalid");
        if (!ElevatedHelperProtocolV2.TryGuid(fields, "jobId", out Guid jobId) ||
            !ElevatedHelperProtocolV2.TryEnum(fields, "operation", out PackageOperationKind operation) ||
            !ElevatedHelperProtocolV2.TryEnum(fields, "provider", out UpdateProvider provider) ||
            !ElevatedHelperProtocolV2.TryEnum(fields, "source", out UpdateSource source) ||
            !ElevatedHelperProtocolV2.TryEnum(fields, "scope", out UpdateScope scope) ||
            !ElevatedHelperProtocolV2.TryEnum(fields, "architecture", out UpdateArchitecture architecture) ||
            !ElevatedHelperProtocolV2.TryEnum(fields, "channel", out UpdateChannel channel) ||
            !ElevatedHelperProtocolV2.TryText(fields, "stableProductKey", out string stableProductKey) ||
            !ElevatedHelperProtocolV2.TryText(fields, "packageId", out string packageId) ||
            !ElevatedHelperProtocolV2.TryOptionalText(fields, "installedVersion", out string? installedVersion) ||
            !ElevatedHelperProtocolV2.TryText(fields, "targetVersion", out string targetVersion) ||
            !ElevatedHelperProtocolV2.TryText(fields, "mappingEvidenceVersion", out string mappingEvidenceVersion) ||
            !ElevatedHelperProtocolV2.TryOptionalText(fields, "agreementHash", out string? agreementHash) ||
            !ElevatedHelperProtocolV2.TryText(fields, "jobDigest", out string jobDigest) || !ElevatedHelperProtocolV2.IsDigest(jobDigest))
        {
            return Reject("item-field-invalid");
        }

        ImmutableUpdateJob job;
        try
        {
            UpdateJobSelection selection = UpdateJobSelection.Restore(operation, stableProductKey, provider, source, packageId, installedVersion, targetVersion, scope, architecture, channel, mappingEvidenceVersion, agreementHash, runsElevated);
            job = ImmutableUpdateJob.Restore(jobId, correlationId, selection, approvedAt, jobDigest);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return Reject("item-integrity-invalid");
        }

        if (items.Any(existing => existing.Job.Id == job.Id ||
            string.Equals(existing.Job.Selection.StableProductKey, job.Selection.StableProductKey, StringComparison.Ordinal) ||
            (existing.Job.Selection.Provider == job.Selection.Provider && string.Equals(existing.Job.Selection.PackageId, job.Selection.PackageId, StringComparison.OrdinalIgnoreCase))))
        {
            return Reject("item-duplicate");
        }

        items.Add(new(sequence, job, skipReason));
        return true;
    }

    private bool AcceptEnd(Dictionary<string, JsonElement> fields)
    {
        if (!ElevatedHelperProtocolV2.TryGuid(fields, "batchId", out Guid endBatch) || endBatch != batchId) return Reject("end-batch-mismatch");
        if (!ElevatedHelperProtocolV2.TryInt(fields, "itemCount", out int count) || count != expectedCount || items.Count != expectedCount) return Reject("item-truncated");
        string recomputed;
        try
        {
            recomputed = ApprovedOperationBatch.ComputeElevatedDigest(batchId, correlationId, approvedAt, batchDigest, firstSequence, [.. items.Select(item => item.Job)]);
        }
        catch (ArgumentException)
        {
            return Reject("elevated-digest-mismatch");
        }

        if (!ElevatedHelperProtocolV2.FixedEquals(recomputed, elevatedDigest)) return Reject("elevated-digest-mismatch");
        Request = new(batchId, correlationId, approvedAt, [.. items]);
        State = HelperReceiverState.Complete;
        return true;
    }

    private bool Reject(string reason)
    {
        State = HelperReceiverState.Rejected;
        Rejection = reason;
        items.Clear();
        Request = null;
        return false;
    }
}

/// <summary>
/// Desktop side: accepts helper frames only for this batch and nonce, strictly in item order. An item may be
/// reported as started at most once; a started item can only end in a provider outcome, and an unstarted one
/// only as Skipped or Cancelled. BatchComplete is accepted only after every item has a result.
/// </summary>
internal sealed class HelperBatchResponseReader(Guid batchId, string nonce, IReadOnlyList<(int Sequence, Guid JobId)> expected)
{
    private static readonly Dictionary<string, string[]> ResponseFrames = new(StringComparer.Ordinal)
    {
        ["ItemStarted"] = ElevatedHelperProtocolV2.StartedFields,
        ["ItemResult"] = ElevatedHelperProtocolV2.ResultFields,
        ["BatchComplete"] = ElevatedHelperProtocolV2.CompleteFields
    };
    private static readonly UpdateProviderAttemptStatus[] StartedOutcomes =
    [
        UpdateProviderAttemptStatus.AwaitingVerification, UpdateProviderAttemptStatus.SourceFailed, UpdateProviderAttemptStatus.DownloadFailed,
        UpdateProviderAttemptStatus.InstallerFailed, UpdateProviderAttemptStatus.TimedOut, UpdateProviderAttemptStatus.ProductFailed
    ];
    private readonly IReadOnlyList<(int Sequence, Guid JobId)> expected = expected ?? throw new ArgumentNullException(nameof(expected));
    private int next;
    private bool currentStarted;

    public bool IsComplete { get; private set; }

    /// <summary>The item the helper may be running now: started, or the next one when the stream breaks.</summary>
    public int NextIndex => next;

    public bool CurrentItemStarted => currentStarted;

    public bool TryAccept(ReadOnlySpan<byte> frame, out HelperBatchEvent? helperEvent, out string rejection)
    {
        helperEvent = null;
        if (IsComplete) { rejection = "frame-after-complete"; return false; }
        if (!ElevatedHelperProtocolV2.TryReadFrame(frame, ResponseFrames, out string kind, out Dictionary<string, JsonElement> fields, out rejection)) return false;
        if (!ElevatedHelperProtocolV2.TryGuid(fields, "batchId", out Guid frameBatch) || frameBatch != batchId ||
            !ElevatedHelperProtocolV2.TryText(fields, "nonce", out string frameNonce) || !ElevatedHelperProtocolV2.FixedEquals(frameNonce, nonce))
        {
            rejection = "response-binding-invalid";
            return false;
        }

        if (kind == "BatchComplete")
        {
            if (next != expected.Count || !ElevatedHelperProtocolV2.TryInt(fields, "itemCount", out int count) || count != expected.Count) { rejection = "complete-before-all-results"; return false; }
            IsComplete = true;
            helperEvent = new HelperBatchCompleteEvent(count);
            rejection = string.Empty;
            return true;
        }

        if (next >= expected.Count ||
            !ElevatedHelperProtocolV2.TryInt(fields, "sequence", out int sequence) || sequence != expected[next].Sequence ||
            !ElevatedHelperProtocolV2.TryGuid(fields, "jobId", out Guid jobId) || jobId != expected[next].JobId)
        {
            rejection = "response-item-out-of-order";
            return false;
        }

        if (kind == "ItemStarted")
        {
            if (currentStarted) { rejection = "item-started-twice"; return false; }
            currentStarted = true;
            helperEvent = new HelperItemStartedEvent(sequence, jobId);
            rejection = string.Empty;
            return true;
        }

        if (!ElevatedHelperProtocolV2.TryEnum(fields, "status", out UpdateProviderAttemptStatus status) ||
            !ElevatedHelperProtocolV2.TryBool(fields, "restartPending", out bool restartPending) ||
            !fields.TryGetValue("exitCode", out JsonElement exitElement) ||
            !fields.TryGetValue("sanitizedOutput", out JsonElement outputElement) || outputElement.ValueKind != JsonValueKind.String)
        {
            rejection = "result-field-invalid";
            return false;
        }

        int? exitCode = exitElement.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.Number when exitElement.TryGetInt32(out int value) => value,
            _ => int.MinValue
        };
        string output = outputElement.GetString() ?? string.Empty;
        bool statusAllowed = currentStarted
            ? StartedOutcomes.Contains(status)
            : status is UpdateProviderAttemptStatus.Skipped or UpdateProviderAttemptStatus.Cancelled;
        if (exitCode == int.MinValue || output.Length > ElevatedHelperProtocolV2.MaximumDiagnosticLength || output.Any(char.IsControl) ||
            !statusAllowed || (restartPending && status != UpdateProviderAttemptStatus.AwaitingVerification))
        {
            rejection = "result-outcome-invalid";
            return false;
        }

        helperEvent = new HelperItemResultEvent(sequence, jobId, status, exitCode, restartPending, NonElevatedUpdateRunner.Sanitize(output));
        next++;
        currentStarted = false;
        rejection = string.Empty;
        return true;
    }
}
