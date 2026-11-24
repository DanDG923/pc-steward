using System.Text;
using System.Text.Json;

namespace PCSteward.Domain;

/// <summary>FR-AI-05/06: the closed outcome of reading one response body.</summary>
public enum AdvisoryAiAnswerStatus
{
    /// <summary>The provider finished the answer.</summary>
    Completed,

    /// <summary>The provider stopped early, for example at the output limit. Usage can still be complete and billable (ADR-029 F4).</summary>
    Incomplete,

    /// <summary>The model declined to answer.</summary>
    Refused,

    /// <summary>The provider reported a failure in a well-formed response.</summary>
    ProviderFailed,

    /// <summary>The body was not a well-formed response for the approved request; nothing in it is trusted.</summary>
    Malformed
}

public enum AdvisoryAiIncompleteReason
{
    None,
    OutputLimit,
    ContentFilter,
    Other
}

/// <summary>Why a body was treated as malformed. Never derived from provider text.</summary>
public enum AdvisoryAiMalformedReason
{
    None,
    TooLarge,
    NotJson,
    NotAResponseObject,
    DuplicateProperty,
    MissingOrInvalidId,
    ModelMismatch,
    UnknownStatus,
    StoredResponse,
    ToolsPresent,
    UnexpectedOutputItem,
    InvalidOutputText,

    /// <summary>Independent review F2: the body carries background, continuation or conversation state the request never asked for.</summary>
    UnexpectedState,

    /// <summary>
    /// Task 71C review 71C-01: the body parsed as JSON, but a field name or string in it couldn't be decoded (for example an unpaired
    /// escaped surrogate), so its billing fields couldn't all be checked. Such a reply is never trusted and always stops paid requests
    /// for review.
    /// </summary>
    UndecodableText
}

/// <summary>
/// Task 70E T70E-01: what the body said about its service tier, in closed terms. The provider's own text is never kept: a named tier
/// is one of <see cref="AdvisoryAiResponseReader.KnownServiceTiers"/>, held as PC Steward's own constant.
/// </summary>
public enum AdvisoryAiServiceTierReport
{
    /// <summary>No tier, or one null tier.</summary>
    NotReported,

    /// <summary>One known tier name; <see cref="AdvisoryAiResponse.ServiceTier"/> holds it.</summary>
    Named,

    /// <summary>More than one tier field, null ones included.</summary>
    Repeated,

    /// <summary>One tier field that isn't a known tier name.</summary>
    Unrecognised
}

/// <summary>
/// Independent review F1/F2: ways a body departs from the request that was approved and priced. Each one makes usage untrusted and
/// stops paid requests for review, even in a body refused for another reason, as a cache write does (Task 70B T70B-02).
/// </summary>
[Flags]
public enum AdvisoryAiResponseDeparture
{
    None = 0,

    /// <summary>The body names a service tier other than the one requested and priced, or names one more than once or not as a plain name.</summary>
    ServiceTier = 1,

    /// <summary>A usage or usage-details object holds a key the reader doesn't know, which could be an unpriced billing category.</summary>
    UnknownUsageField = 2,

    /// <summary>Cached input is reported, or isn't a plain zero, although the request asked for no prompt cache.</summary>
    PromptCacheUsed = 4,

    /// <summary>The body says it ran in the background or continued an earlier response or a conversation.</summary>
    ConversationState = 8
}

/// <summary>
/// First live test (Task 71): where a usage key the reader doesn't know sat, in PC Steward's own closed terms. The provider's key name
/// is never kept or shown (as for the service tier, Task 70E T70E-01), so this is what lets an unknown counter be traced without
/// another paid request.
/// </summary>
[Flags]
public enum AdvisoryAiUsageFieldLocation
{
    None = 0,

    /// <summary>Directly in <c>usage</c>, beside the input, output and total counts.</summary>
    UsageTotals = 1,

    /// <summary>In <c>usage.input_tokens_details</c>.</summary>
    InputDetails = 2,

    /// <summary>In <c>usage.output_tokens_details</c>.</summary>
    OutputDetails = 4,

    /// <summary>In <c>usage.input_tokens_details.cache_write_tokens_details</c>, the cache-write breakdown by retention.</summary>
    CacheWriteDetails = 8
}

/// <summary>ADR-029: provider-reported token usage. Only <see cref="AdvisoryAiResponse.UsageReliable"/> usage can settle a charge.</summary>
public sealed record AdvisoryAiUsage(long InputTokens, long CachedInputTokens, long OutputTokens, long ReasoningTokens, long TotalTokens);

/// <summary>
/// FR-AI-05/06, ADR-029 F4: one response read strictly and boundedly. <see cref="Text"/> is the provider's raw text: it is never
/// saved or shown until <see cref="AdvisoryAiRedaction.SanitizeAnswer"/> has redacted it. Answer completeness and usage reliability
/// are independent: an incomplete or empty answer can carry complete billable usage.
/// </summary>
public sealed record AdvisoryAiResponse(
    AdvisoryAiAnswerStatus Status,
    AdvisoryAiIncompleteReason IncompleteReason,
    AdvisoryAiMalformedReason MalformedReason,
    string? ResponseId,
    string? Text,
    AdvisoryAiUsage? Usage,
    bool UsageReliable,
    bool UsageAboveApprovedBounds)
{
    /// <summary>
    /// Task 70 T70-07: the exact <c>cache_write_tokens</c> count when the provider reported one as a count; null when it reported none
    /// or <see cref="CacheWriteUnreadable"/>. Kept for the owner to see, never for settlement: any write already makes usage untrusted.
    /// </summary>
    public long? CacheWriteTokens { get; init; }

    /// <summary>The provider reported <c>cache_write_tokens</c> but not as one plain count.</summary>
    public bool CacheWriteUnreadable { get; init; }

    /// <summary>
    /// Independent review F1, Task 70E T70E-01: the known service tier the body named, as PC Steward's own constant from
    /// <see cref="AdvisoryAiResponseReader.KnownServiceTiers"/>; null otherwise. Any tier other than the requested one, a repeated tier
    /// or an unknown one is a <see cref="Departures"/> entry.
    /// </summary>
    public string? ServiceTier { get; init; }

    /// <summary>Task 70E T70E-01: what the body said about its tier, in closed terms.</summary>
    public AdvisoryAiServiceTierReport ServiceTierReport { get; init; }

    /// <summary>Independent review F1/F2: what in the body departs from the approved, priced request.</summary>
    public AdvisoryAiResponseDeparture Departures { get; init; }

    /// <summary>First live test: where any unknown usage key sat; set whenever <see cref="AdvisoryAiResponseDeparture.UnknownUsageField"/> is.</summary>
    public AdvisoryAiUsageFieldLocation UnknownUsageFieldLocations { get; init; }

    public static AdvisoryAiResponse Malformed(AdvisoryAiMalformedReason reason) =>
        new(AdvisoryAiAnswerStatus.Malformed, AdvisoryAiIncompleteReason.None, reason, null, null, null, false, false);
}

/// <summary>
/// FR-AI-04/05 (ADR-028 Q1, ADR-029 F4): reads a Responses API body for one approved request. It refuses anything over the admitted
/// response size, deeper than needed, with duplicate properties where it reads, for another model, stored, with tools, or with any
/// output item other than a message or reasoning. Unknown top-level fields are ignored, not trusted, except the service tier and
/// conversation state, which are checked. Usage is reliable only when every count is present, non-negative and consistent; a body that is malformed has no reliable usage, so its cost stays uncertain.
/// </summary>
public static class AdvisoryAiResponseReader
{
    public const int MaximumDepth = 16;
    public const int MaximumOutputItems = 64;
    public const int MaximumIdLength = 128;

    /// <summary>
    /// Task 70E T70E-01: the service tier names PC Steward knows (Task 69 §3, and earlier API names). Only these are ever shown, as
    /// these constants; any other value is reported as unrecognised. Which one the request needs is the model profile's, so knowing a
    /// name is never accepting it.
    /// </summary>
    public static readonly IReadOnlyList<string> KnownServiceTiers = ["auto", "default", "flex", "batch", "scale", "priority", "fast", "ultrafast"];

    public static AdvisoryAiResponse Read(ReadOnlySpan<byte> body, AdvisoryAiApproval approval)
    {
        ArgumentNullException.ThrowIfNull(approval);
        AdvisoryAiRequestPreview preview = approval.Preview;
        return Read(body, preview.Model.ModelId, AdmittedBytes(approval), preview.InputTokenUpperBound, preview.Limits.MaximumOutputTokens,
            preview.Model.ServiceTier, preview.Model.RequestNoPromptCache, preview.Model.MaximumTokenUtf8Bytes, MinimumInputTokens(preview));
    }

    /// <summary>
    /// Codex review of bf637b5, 71C-02: the fewest input tokens the approved text can be. The tokenizer is byte-level, and its
    /// longest token decodes to at most <see cref="AdvisoryAiModelProfile.MaximumTokenUtf8Bytes"/>, so the instructions and input
    /// sent need at least their UTF-8 bytes divided by that, rounded up. Framing the route adds is left out, so this only ever
    /// errs low.
    /// </summary>
    public static long MinimumInputTokens(AdvisoryAiRequestPreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        long bytes = (long)Encoding.UTF8.GetByteCount(AdvisoryAiInstructions.Text) + Encoding.UTF8.GetByteCount(preview.InputText);
        int perToken = preview.Model.MaximumTokenUtf8Bytes;
        return perToken <= 0 ? 1 : Math.Max(1, (bytes + perToken - 1) / perToken);
    }

    /// <summary>
    /// ADR-028 F1: the most response bytes this approved request admits. The transport stops reading past it and the reader refuses
    /// a longer body, so both apply the same bound.
    /// </summary>
    public static long AdmittedBytes(AdvisoryAiApproval approval)
    {
        ArgumentNullException.ThrowIfNull(approval);
        AdvisoryAiRequestPreview preview = approval.Preview;
        return Math.Min(AdvisoryAiResourcePolicy.MaximumResponseBytes,
            preview.Model.ResponseBytes(preview.Limits.MaximumInputTokens, preview.Limits.MaximumOutputTokens));
    }

    /// <summary>
    /// Reads one body for <paramref name="expectedModel"/>. <paramref name="expectedServiceTier"/> is the tier requested and priced: a
    /// body naming another is a departure (null checks only that a named tier is readable). When <paramref name="noPromptCache"/> is
    /// set, the request asked for no prompt cache, so any cached input is a departure. When <paramref name="maximumTokenUtf8Bytes"/> is
    /// above 0, any text under output longer than the reported output tokens (or the approved output) could decode to contradicts the usage.
    /// </summary>
    public static AdvisoryAiResponse Read(ReadOnlySpan<byte> body, string expectedModel, long maximumBytes, long approvedInputTokens, long approvedOutputTokens,
        string? expectedServiceTier = null, bool noPromptCache = false, int maximumTokenUtf8Bytes = 0, long minimumInputTokens = 1)
    {
        ArgumentNullException.ThrowIfNull(expectedModel);
        if (body.Length > maximumBytes) return AdvisoryAiResponse.Malformed(AdvisoryAiMalformedReason.TooLarge);
        try
        {
            // Task 71C review 71C-R1: decode every name and string first, including the ones the reader below never visits.
            if (!DecodesInFull(body)) return Undecodable;
            using JsonDocument document = JsonDocument.Parse(body.ToArray(),
                new JsonDocumentOptions { MaxDepth = MaximumDepth, CommentHandling = JsonCommentHandling.Disallow, AllowTrailingCommas = false });
            AdvisoryAiResponse response = Read(document.RootElement, expectedModel, approvedInputTokens, approvedOutputTokens, minimumInputTokens);
            (long? written, bool unreadable) = CacheWriteReport(document.RootElement);
            (string? tier, AdvisoryAiServiceTierReport tierReport, AdvisoryAiResponseDeparture departures, AdvisoryAiUsageFieldLocation unknownAt) =
                DepartureReport(document.RootElement, expectedServiceTier, noPromptCache);
            // Task 70B T70B-02: a reported cache write (or one that isn't a plain count) is an unexpected billing category even when the
            // body is refused earlier for another reason, so it always stops paid requests for review. Independent review F1/F2: so is
            // another service tier, an unknown usage counter, cached input on a no-cache request, or conversation state. Usage is then
            // never trusted, so the whole reservation is kept.
            // Codex review of ce0eaf3, C1, the independent review of 4056e3f, G2, and Codex review of cd04620, 71C-03: output text longer
            // than the reported output tokens could decode to contradicts the usage, whatever else is wrong with the body, so the check
            // runs here, after every early return.
            // Codex review of bf637b5, 71C-01: so is a usage count above the approved bounds, read on its own, so a body refused
            // early (stored, another model, no id) can't hide a charge above the reservation.
            bool unexpected = unreadable || written > 0 || departures != AdvisoryAiResponseDeparture.None ||
                OutputContradictsUsage(document.RootElement, approvedOutputTokens, maximumTokenUtf8Bytes) ||
                CountsAboveApprovedBounds(document.RootElement, approvedInputTokens, approvedOutputTokens) ||
                // Independent review of a99b79a, AC1: a body that isn't a response object but carries usage or output is a reply that
                // couldn't be read in its place, so its tier, cache write, state and output length were never checked. It pauses.
                // Independent review of dcef10c, AE1: so does usage or output anywhere but directly on a response object, whatever
                // the body is refused for (<see cref="HoldsAReplyOutOfPlace"/>).
                HoldsAReplyOutOfPlace(document.RootElement) ||
                // Independent review of a4db871, AG1: so does a response that shows something the request never allowed and the
                // reservation never priced (another model, storage, tools, an unsupported status, a tool call item), whatever the
                // body is refused for. The owner's review of a4db871, 71C-01, found the same and added the unsupported status
                // (<see cref="ShowsWhatTheRequestNeverAllowed"/>).
                ShowsWhatTheRequestNeverAllowed(document.RootElement, expectedModel);
            // A body that continues other state isn't an answer to this request, so its text is never shown or saved.
            if (departures.HasFlag(AdvisoryAiResponseDeparture.ConversationState) && response.Status != AdvisoryAiAnswerStatus.Malformed)
                response = response with { Status = AdvisoryAiAnswerStatus.Malformed, IncompleteReason = AdvisoryAiIncompleteReason.None,
                    MalformedReason = AdvisoryAiMalformedReason.UnexpectedState, Text = null };
            return response with
            {
                CacheWriteTokens = written,
                CacheWriteUnreadable = unreadable,
                ServiceTier = tier,
                ServiceTierReport = tierReport,
                Departures = departures,
                UnknownUsageFieldLocations = unknownAt,
                UsageReliable = response.UsageReliable && !unexpected,
                UsageAboveApprovedBounds = response.UsageAboveApprovedBounds || unexpected
            };
        }
        catch (JsonException)
        {
            return AdvisoryAiResponse.Malformed(AdvisoryAiMalformedReason.NotJson);
        }
        catch (InvalidOperationException)
        {
            // Task 71C review 71C-01: System.Text.Json parses a string token whose escaped contents it can't later decode, and then
            // throws here when a name or value is read. The billing scan may not have finished, so this is an untrusted reply.
            return Undecodable;
        }
    }

    /// <summary>
    /// Task 71C review 71C-R1: true when every property name and string value in <paramref name="body"/> decodes, wherever it sits.
    /// System.Text.Json parses a string token and decodes its escapes and UTF-8 only when it is read, so a reply could otherwise
    /// carry undecodable text in metadata, a reasoning item or a nested name that nothing reads, and still have its usage trusted.
    /// This one pass reads the body with the same depth and syntax rules as the parse, so its work is bounded by the admitted bytes.
    /// It keeps nothing it decodes. A syntax error throws JsonException, which the caller already treats as not JSON.
    /// </summary>
    public static bool DecodesInFull(ReadOnlySpan<byte> body)
    {
        Utf8JsonReader reader = new(body, new JsonReaderOptions { MaxDepth = MaximumDepth, CommentHandling = JsonCommentHandling.Disallow, AllowTrailingCommas = false });
        char[] buffer = [];
        try
        {
            while (reader.Read())
            {
                if (reader.TokenType is not (JsonTokenType.String or JsonTokenType.PropertyName)) continue;
                // A decoded value never has more UTF-16 units than its bytes in the body.
                int length = reader.HasValueSequence ? checked((int)reader.ValueSequence.Length) : reader.ValueSpan.Length;
                if (buffer.Length < length) buffer = new char[Math.Max(length, Math.Min(2 * buffer.Length, body.Length))];
                reader.CopyString(buffer);
            }
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        finally
        {
            Array.Clear(buffer);
        }
    }

    /// <summary>
    /// Task 71C review 71C-01: a reply PC Steward couldn't read in full. It is never shown or saved, its usage is never trusted, so the
    /// whole reservation is kept, and it counts as a departure from the approved request, so paid requests stop for review.
    /// </summary>
    public static AdvisoryAiResponse Undecodable { get; } =
        AdvisoryAiResponse.Malformed(AdvisoryAiMalformedReason.UndecodableText) with { UsageAboveApprovedBounds = true };

    private static AdvisoryAiResponse Read(JsonElement root, string expectedModel, long approvedInputTokens, long approvedOutputTokens, long minimumInputTokens)
    {
        if (root.ValueKind != JsonValueKind.Object) return AdvisoryAiResponse.Malformed(AdvisoryAiMalformedReason.NotAResponseObject);
        if (HasDuplicate(root)) return AdvisoryAiResponse.Malformed(AdvisoryAiMalformedReason.DuplicateProperty);
        if (!IsString(root, "object", "response")) return AdvisoryAiResponse.Malformed(AdvisoryAiMalformedReason.NotAResponseObject);
        if (!root.TryGetProperty("id", out JsonElement idElement) || idElement.ValueKind != JsonValueKind.String || !IsValidId(idElement.GetString()))
            return AdvisoryAiResponse.Malformed(AdvisoryAiMalformedReason.MissingOrInvalidId);
        string id = idElement.GetString()!;
        if (!IsString(root, "model", expectedModel)) return AdvisoryAiResponse.Malformed(AdvisoryAiMalformedReason.ModelMismatch);

        (AdvisoryAiUsage? usage, bool reliable, bool cacheWritten) = ReadUsage(root);
        // Independent review F2: an answer always has input (the instructions at least), so usage reporting none can't settle it at $0.
        // Codex review of bf637b5, 71C-02: nor can usage reporting fewer input tokens than the approved text could be.
        if (reliable && usage!.InputTokens < Math.Max(1, minimumInputTokens) && !IsString(root, "status", "failed") && !IsString(root, "status", "cancelled"))
            reliable = false;
        // Task 69: a reported cache write was never requested and is billed above the input rate, so the usage can't settle the
        // request and paid requests stop for review, as for usage above the approved bounds.
        bool above = cacheWritten || reliable && (usage!.InputTokens > approvedInputTokens || usage.OutputTokens > approvedOutputTokens);

        // Codex review of bf637b5, 71C-01: a body for this request and model that is refused here keeps its usage for the ledger, so a
        // charge above the reservation is recorded in full and stops paid requests, as for a body refused for its output.
        if (root.TryGetProperty("store", out JsonElement store) && store.ValueKind != JsonValueKind.False) return Unbound(AdvisoryAiMalformedReason.StoredResponse, usage, reliable, above, id);
        if (root.TryGetProperty("tools", out JsonElement tools) && !(tools.ValueKind == JsonValueKind.Array && tools.GetArrayLength() == 0))
            return Unbound(AdvisoryAiMalformedReason.ToolsPresent, usage, reliable, above, id);

        if (!root.TryGetProperty("status", out JsonElement statusElement) || statusElement.ValueKind != JsonValueKind.String)
            return Unbound(AdvisoryAiMalformedReason.UnknownStatus, usage, reliable, above, id);
        AdvisoryAiAnswerStatus? status = statusElement.GetString() switch
        {
            "completed" => AdvisoryAiAnswerStatus.Completed,
            "incomplete" => AdvisoryAiAnswerStatus.Incomplete,
            "failed" or "cancelled" => AdvisoryAiAnswerStatus.ProviderFailed,
            _ => null
        };
        if (status is null) return Unbound(AdvisoryAiMalformedReason.UnknownStatus, usage, reliable, above, id);

        AdvisoryAiIncompleteReason incomplete = AdvisoryAiIncompleteReason.None;
        if (status == AdvisoryAiAnswerStatus.Incomplete)
        {
            string? reason = root.TryGetProperty("incomplete_details", out JsonElement details) && details.ValueKind == JsonValueKind.Object &&
                !HasDuplicate(details) && details.TryGetProperty("reason", out JsonElement reasonElement) && reasonElement.ValueKind == JsonValueKind.String ? reasonElement.GetString() : null;
            incomplete = reason switch
            {
                "max_output_tokens" => AdvisoryAiIncompleteReason.OutputLimit,
                "content_filter" => AdvisoryAiIncompleteReason.ContentFilter,
                _ => AdvisoryAiIncompleteReason.Other
            };
        }

        StringBuilder text = new();
        bool refused = false;
        if (root.TryGetProperty("output", out JsonElement output))
        {
            if (output.ValueKind != JsonValueKind.Array || output.GetArrayLength() > MaximumOutputItems) return Unbound(AdvisoryAiMalformedReason.UnexpectedOutputItem, usage, reliable, above, id);
            foreach (JsonElement item in output.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object && HasDuplicate(item))
                    return Unbound(AdvisoryAiMalformedReason.DuplicateProperty, usage, reliable, above, id);
                if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("type", out JsonElement type) || type.ValueKind != JsonValueKind.String)
                    return Unbound(AdvisoryAiMalformedReason.UnexpectedOutputItem, usage, reliable, above, id);
                switch (type.GetString())
                {
                    case "reasoning":
                        continue;
                    case "message":
                        if (!item.TryGetProperty("content", out JsonElement content) || content.ValueKind != JsonValueKind.Array)
                            return Unbound(AdvisoryAiMalformedReason.UnexpectedOutputItem, usage, reliable, above, id);
                        foreach (JsonElement part in content.EnumerateArray())
                        {
                            if (part.ValueKind == JsonValueKind.Object && HasDuplicate(part))
                                return Unbound(AdvisoryAiMalformedReason.DuplicateProperty, usage, reliable, above, id);
                            if (part.ValueKind != JsonValueKind.Object || !part.TryGetProperty("type", out JsonElement partType) || partType.ValueKind != JsonValueKind.String)
                                return Unbound(AdvisoryAiMalformedReason.InvalidOutputText, usage, reliable, above, id);
                            // Independent review of c80cb60, AI1: a citation is made only by a hosted tool, so text that carries one isn't an
                            // answer to a request that allowed no tools.
                            if (HoldsAnnotations(part)) return Unbound(AdvisoryAiMalformedReason.InvalidOutputText, usage, reliable, above, id);
                            if (partType.ValueEquals("output_text") && part.TryGetProperty("text", out JsonElement value) && value.ValueKind == JsonValueKind.String)
                                text.Append(value.GetString());
                            else if (partType.ValueEquals("refusal")) refused = true;
                            else return Unbound(AdvisoryAiMalformedReason.InvalidOutputText, usage, reliable, above, id);
                        }
                        if (text.Length > 0 && !text.ToString().EndsWith('\n')) text.Append('\n');
                        break;
                    default:
                        // A function, tool, search, file or computer item: the request allowed none, so nothing in this body is trusted.
                        return Unbound(AdvisoryAiMalformedReason.UnexpectedOutputItem, usage, reliable, above, id);
                }
            }
        }
        else if (status != AdvisoryAiAnswerStatus.ProviderFailed) return Unbound(AdvisoryAiMalformedReason.UnexpectedOutputItem, usage, reliable, above, id);

        AdvisoryAiAnswerStatus final = refused && text.Length == 0 && status == AdvisoryAiAnswerStatus.Completed ? AdvisoryAiAnswerStatus.Refused : status.Value;
        string? answer = text.Length == 0 ? null : text.ToString().TrimEnd('\n');
        return new(final, incomplete, AdvisoryAiMalformedReason.None, id, answer, usage, reliable, above);
    }

    /// <summary>
    /// Codex review of ce0eaf3, C1, the independent review of 4056e3f, G2, and Codex review of cd04620, 71C-02 and 71C-03: every output
    /// token decodes to at most <paramref name="maximumTokenUtf8Bytes"/>, so text under output longer than the reported output tokens
    /// could produce contradicts the usage. The reported count is used whenever one is a plain count, even when other usage figures
    /// can't be trusted (the smallest, if it is repeated); only when there is none is the approved output used. Either way it is never
    /// more than the approved output.
    /// </summary>
    private static bool OutputContradictsUsage(JsonElement root, long approvedOutputTokens, int maximumTokenUtf8Bytes)
    {
        if (maximumTokenUtf8Bytes <= 0 || root.ValueKind != JsonValueKind.Object) return false;
        long tokens = approvedOutputTokens;
        long bytes = 0;
        foreach (JsonProperty property in root.EnumerateObject())
        {
            if (property.NameEquals("output")) bytes += OutputTextBytes(property.Value);
            else if (property.NameEquals("usage") && property.Value.ValueKind == JsonValueKind.Object)
                foreach (JsonProperty count in property.Value.EnumerateObject())
                    if (count.NameEquals("output_tokens") && count.Value.ValueKind == JsonValueKind.Number && count.Value.TryGetInt64(out long reported) && reported >= 0)
                        tokens = Math.Min(tokens, reported);
        }
        return bytes > tokens * maximumTokenUtf8Bytes;
    }

    /// <summary>
    /// Independent review of dcef10c, AE1: true when a non-null "usage" or "output" sits anywhere in the body except directly on a root
    /// object whose "object" is "response". The reader only checks the tier, cache write, state and output length of a reply in that
    /// place, so a reply anywhere else (a wrapper that repeats a name, a response whose own reply sits one level down, a wrapped list
    /// whose "object" is "response") pauses, whatever the body is refused for. An error envelope, <c>{}</c>, <c>[]</c> and a bare value
    /// don't. A response that is read in its place but also holds usage or output deeper down pauses too, which costs a review of a
    /// body the service shouldn't send.
    /// </summary>
    private static bool HoldsAReplyOutOfPlace(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !IsString(root, "object", "response")) return CarriesAReply(root);
        foreach (JsonProperty property in root.EnumerateObject())
            if (CarriesAReply(property.Value)) return true;
        return false;
    }

    /// <summary>
    /// Independent review of a4db871, AG1: true when a response object shows something the approved request never allowed and the
    /// reservation never priced: a model other than the approved one, a stored response, tools, a status the reader doesn't support
    /// (owner's review of a4db871, 71C-01), an output item that is neither
    /// reasoning nor a message (a search, file, code, image, computer, remote or function call), or a message part that is neither
    /// output text nor a refusal (audio, an image). The reader refuses each of these, but a refusal alone only keeps the
    /// reservation, and a hosted tool or another model is charged outside it. Read on its own and over every occurrence, like the
    /// departures, so a body refused earlier for another reason (a repeated name, no id, an unknown status) can't hide one. A body
    /// that isn't a response object (an error envelope, <c>{}</c>) shows nothing here.
    /// </summary>
    private static bool ShowsWhatTheRequestNeverAllowed(JsonElement root, string expectedModel)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !Occurrences(root, "object").Any(value => value.ValueKind == JsonValueKind.String && value.ValueEquals("response"))) return false;
        foreach (JsonProperty property in root.EnumerateObject())
        {
            JsonElement value = property.Value;
            if (property.NameEquals("model") && !(value.ValueKind == JsonValueKind.String && value.ValueEquals(expectedModel))) return true;
            if (property.NameEquals("store") && value.ValueKind != JsonValueKind.False) return true;
            if (property.NameEquals("tools") && !(value.ValueKind == JsonValueKind.Array && value.GetArrayLength() == 0)) return true;
            // Owner's review of a4db871, 71C-01: a status the reader doesn't support (queued, in progress, anything else) says the
            // provider may still be working on the request, so it pauses too. A missing status is no status.
            if (property.NameEquals("status") && value.ValueKind != JsonValueKind.Null && !(value.ValueKind == JsonValueKind.String &&
                (value.ValueEquals("completed") || value.ValueEquals("incomplete") || value.ValueEquals("failed") || value.ValueEquals("cancelled")))) return true;
            if (property.NameEquals("output") && HoldsMoreThanAnAnswer(value)) return true;
        }
        return false;
    }

    /// <summary>
    /// True when <paramref name="output"/> holds anything but reasoning items and messages made of output text and refusals. Every
    /// occurrence of an item's or a part's "type" counts, so a second type can't hide a tool call. null is no output.
    /// </summary>
    private static bool HoldsMoreThanAnAnswer(JsonElement output)
    {
        if (output.ValueKind == JsonValueKind.Null) return false;
        if (output.ValueKind != JsonValueKind.Array) return true;
        foreach (JsonElement item in output.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) return true;
            bool typed = false, message = false;
            foreach (JsonElement type in Occurrences(item, "type"))
            {
                typed = true;
                if (type.ValueKind != JsonValueKind.String) return true;
                if (type.ValueEquals("message")) message = true;
                else if (!type.ValueEquals("reasoning")) return true;
            }
            if (!typed) return true;
            if (!message) continue;
            bool content = false;
            foreach (JsonElement parts in Occurrences(item, "content"))
            {
                content = true;
                if (parts.ValueKind != JsonValueKind.Array) return true;
                foreach (JsonElement part in parts.EnumerateArray())
                {
                    if (part.ValueKind != JsonValueKind.Object) return true;
                    bool partTyped = false;
                    foreach (JsonElement type in Occurrences(part, "type"))
                    {
                        partTyped = true;
                        if (type.ValueKind != JsonValueKind.String || !(type.ValueEquals("output_text") || type.ValueEquals("refusal"))) return true;
                    }
                    if (!partTyped) return true;
                    if (HoldsAnnotations(part)) return true;
                }
            }
            if (!content) return true;
        }
        return false;
    }

    /// <summary>
    /// Independent review of c80cb60, AI1: true when a message part carries annotations that aren't an empty list. A link, file or
    /// container citation is made only by a hosted tool (search, files, code), so it is a trace of tool use even when no tool call
    /// item and no tools list is reported. Every occurrence counts. null is no annotations.
    /// </summary>
    private static bool HoldsAnnotations(JsonElement part) =>
        Occurrences(part, "annotations").Any(value => value.ValueKind != JsonValueKind.Null && !(value.ValueKind == JsonValueKind.Array && value.GetArrayLength() == 0));

    /// <summary>
    /// Independent review of a99b79a, AC1, and of dcef10c, AE1: true when a property named "usage" or "output" with any value but null
    /// sits anywhere in <paramref name="element"/>, so a response wrapped in an array, an event or a list counts as a reply.
    /// </summary>
    private static bool CarriesAReply(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement child in element.EnumerateArray())
                if (CarriesAReply(child)) return true;
            return false;
        }
        if (element.ValueKind != JsonValueKind.Object) return false;
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Null && (property.NameEquals("usage") || property.NameEquals("output"))) return true;
            if (CarriesAReply(property.Value)) return true;
        }
        return false;
    }

    /// <summary>
    /// Codex review of bf637b5, 71C-01: true when any plain count under any "usage" object reports more input, output or total tokens
    /// than the approved request allows, whether or not the usage as a whole can be trusted and whatever else refuses the body.
    /// Independent review of be08c02, W1: a "usage" object at any depth counts, so a response wrapped in an array, an event or a list
    /// (refused as not a response object) can't hide a charge above the reservation. The parse bounds the depth, and each element is
    /// visited once, and once more for each "usage" object above it (at most 16). Codex review of 2cdfcc4, 71C-01: a count that isn't a
    /// plain whole number counts as above the bounds. Independent review of 2de2ec6, AA1: every count under a "usage" object is read,
    /// not only the three totals (<see cref="UsageAbove"/>).
    /// </summary>
    private static bool CountsAboveApprovedBounds(JsonElement element, long approvedInputTokens, long approvedOutputTokens)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement child in element.EnumerateArray())
                if (CountsAboveApprovedBounds(child, approvedInputTokens, approvedOutputTokens)) return true;
            return false;
        }
        if (element.ValueKind != JsonValueKind.Object) return false;
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (CountsAboveApprovedBounds(property.Value, approvedInputTokens, approvedOutputTokens)) return true;
            if (property.NameEquals("usage") && UsageAbove(property.Value, approvedInputTokens, approvedOutputTokens)) return true;
        }
        return false;
    }

    /// <summary>
    /// Independent review of 2de2ec6, AA1: under a "usage" object every count is bounded by its place: the input bound for input_tokens
    /// and anything under input_tokens_details, the output bound for output_tokens and anything under output_tokens_details, and the
    /// total for every other name. So reasoning tokens above the output approval, a count written as a string, and a cached or other
    /// count in a wrapped body all stop paid requests. A value where a count belongs that isn't a plain whole number within its bound
    /// (a fraction, a string, true, false, an array) counts as above. null is no count, as a missing one is. Independent review of
    /// dcef10c, AE1: a "usage" array counts as above, and only a name ending in "_details" may hold an object, so a cache write or an
    /// unknown counter can't hide inside an array or under input_tokens.
    /// </summary>
    private static bool UsageAbove(JsonElement usage, long approvedInputTokens, long approvedOutputTokens)
    {
        if (usage.ValueKind == JsonValueKind.Array) return true;
        if (usage.ValueKind != JsonValueKind.Object) return false;
        foreach (JsonProperty count in usage.EnumerateObject())
        {
            long bound = count.NameEquals("input_tokens") || count.NameEquals("input_tokens_details") ? approvedInputTokens
                : count.NameEquals("output_tokens") || count.NameEquals("output_tokens_details") ? approvedOutputTokens
                : approvedInputTokens + approvedOutputTokens;
            if (AnyCountAbove(count, bound)) return true;
        }
        return false;
    }

    /// <summary>
    /// True when the value of <paramref name="count"/> isn't a plain whole number within <paramref name="bound"/>. Only a name ending
    /// in "_details" may hold an object, and every value inside it is held to the same rule. null is no count.
    /// </summary>
    private static bool AnyCountAbove(JsonProperty count, long bound)
    {
        switch (count.Value.ValueKind)
        {
            case JsonValueKind.Null: return false;
            case JsonValueKind.Number: return !count.Value.TryGetInt64(out long whole) || whole > bound;
            case JsonValueKind.Object when count.Name.EndsWith("_details", StringComparison.Ordinal):
                foreach (JsonProperty inner in count.Value.EnumerateObject())
                    if (AnyCountAbove(inner, bound)) return true;
                return false;
            default: return true;
        }
    }

    /// <summary>
    /// The UTF-8 bytes of every string value under <paramref name="element"/>, except the plain string kinds, ids, roles and statuses,
    /// which the provider writes rather than the model. Codex review of cd04620, 71C-02: a value under one of those names that isn't a
    /// string (an object or array that could hold text) is counted in full.
    /// </summary>
    private static long OutputTextBytes(JsonElement element)
    {
        long bytes = 0;
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                return Encoding.UTF8.GetByteCount(element.GetString()!);
            case JsonValueKind.Array:
                foreach (JsonElement child in element.EnumerateArray()) bytes += OutputTextBytes(child);
                return bytes;
            case JsonValueKind.Object:
                foreach (JsonProperty property in element.EnumerateObject())
                    if (property.Value.ValueKind != JsonValueKind.String || property.Name is not ("type" or "id" or "role" or "status"))
                        bytes += OutputTextBytes(property.Value);
                return bytes;
            default:
                return 0;
        }
    }

    /// <summary>
    /// ADR-029 F4: a body that is a valid response for this model but carries something the request never allowed. Its text is
    /// discarded. Its usage is kept for the ledger (a charge above the reservation must be recorded in full) but is never treated
    /// as reliable settlement evidence.
    /// </summary>
    private static AdvisoryAiResponse Unbound(AdvisoryAiMalformedReason reason, AdvisoryAiUsage? usage, bool reliable, bool above, string id) =>
        new(AdvisoryAiAnswerStatus.Malformed, AdvisoryAiIncompleteReason.None, reason, id, null, usage, false, above);

    private static (AdvisoryAiUsage? Usage, bool Reliable, bool CacheWritten) ReadUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out JsonElement usage) || usage.ValueKind != JsonValueKind.Object || HasDuplicate(usage)) return (null, false, false);
        bool complete = TryCount(usage, "input_tokens", out long input) & TryCount(usage, "output_tokens", out long output) & TryCount(usage, "total_tokens", out long total);
        long cached = 0, reasoning = 0;
        bool inputObject = usage.TryGetProperty("input_tokens_details", out JsonElement inputDetails) && inputDetails.ValueKind == JsonValueKind.Object;
        bool details = inputObject && !HasDuplicate(inputDetails) && TryCount(inputDetails, "cached_tokens", out cached);
        details &= usage.TryGetProperty("output_tokens_details", out JsonElement outputDetails) && outputDetails.ValueKind == JsonValueKind.Object &&
            !HasDuplicate(outputDetails) && TryCount(outputDetails, "reasoning_tokens", out reasoning);
        // Task 69: cache_write_tokens is optional; when present it must be a count, and any write makes the usage untrusted. It is
        // checked on its own, whatever else is missing, so a write is never hidden by incomplete details.
        bool written = false;
        if (inputObject && inputDetails.TryGetProperty("cache_write_tokens", out _))
        {
            long writeCount = 0;
            bool counted = !HasDuplicate(inputDetails) && TryCount(inputDetails, "cache_write_tokens", out writeCount);
            details &= counted;
            written = !counted || writeCount > 0;
        }
        if (!complete) return (null, false, written);
        AdvisoryAiUsage value = new(input, cached, output, reasoning, total);
        bool consistent = details && !written && input >= 0 && output >= 0 && cached <= input && reasoning <= output && total == input + output;
        return (value, consistent, written);
    }

    /// <summary>
    /// T70-07: what the body says about cache writes, read on its own so a malformed body still reports it. Task 70C T70C-01: every
    /// <c>usage</c>, <c>input_tokens_details</c> and <c>cache_write_tokens</c> occurrence is looked at, not only the last one, so a
    /// repeated container can't hide an earlier write. A count is exact only when it is the single plain count on a single path; any
    /// repetition next to a cache-write field, or a value that isn't a count, is unreadable and stops paid requests for review.
    /// First live test: <c>cache_write_tokens_details</c>, the breakdown of cache writes by retention, is read here too. It may be
    /// absent or null; otherwise it must be a single object whose known counts are plain counts that add up to the reported
    /// <c>cache_write_tokens</c> (zero when that is absent). Anything else is unreadable. Unknown keys inside it are departures.
    /// </summary>
    private static (long? Tokens, bool Unreadable) CacheWriteReport(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return (null, false);
        int fields = 0, breakdowns = 0;
        bool ambiguous = false, notACount = false;
        long count = 0, breakdownTotal = 0;
        List<JsonElement> usages = [.. Occurrences(root, "usage")];
        ambiguous |= usages.Count > 1;
        foreach (JsonElement usage in usages)
        {
            if (usage.ValueKind != JsonValueKind.Object) continue;
            List<JsonElement> details = [.. Occurrences(usage, "input_tokens_details")];
            ambiguous |= details.Count > 1;
            foreach (JsonElement detail in details)
            {
                if (detail.ValueKind != JsonValueKind.Object) continue;
                foreach (JsonElement value in Occurrences(detail, "cache_write_tokens"))
                {
                    fields++;
                    if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long tokens) && tokens >= 0) count = tokens;
                    else notACount = true;
                }
                foreach (JsonElement breakdown in Occurrences(detail, "cache_write_tokens_details"))
                {
                    breakdowns++;
                    if (breakdown.ValueKind == JsonValueKind.Null) continue;
                    if (breakdown.ValueKind != JsonValueKind.Object || HasDuplicate(breakdown)) { notACount = true; continue; }
                    foreach (JsonProperty part in breakdown.EnumerateObject())
                    {
                        // A key the reader doesn't know is a departure (DepartureReport); only the known counts are added up here.
                        if (!CacheWriteDetailKeys.Contains(part.Name, StringComparer.Ordinal)) continue;
                        if (part.Value.ValueKind == JsonValueKind.Number && part.Value.TryGetInt64(out long tokens) && tokens >= 0 && tokens <= long.MaxValue - breakdownTotal)
                            breakdownTotal += tokens;
                        else notACount = true;
                    }
                }
            }
        }
        if (fields == 0 && breakdowns == 0) return (null, false);
        if (fields > 1 || breakdowns > 1 || ambiguous || notACount) return (null, true);
        // A breakdown that doesn't add up to the reported writes (or reports writes with no total) can't be trusted either way.
        if (breakdowns == 1 && breakdownTotal != (fields == 1 ? count : 0)) return (null, true);
        return fields == 1 ? (count, false) : (null, false);
    }

    private static readonly string[] UsageKeys = ["input_tokens", "input_tokens_details", "output_tokens", "output_tokens_details", "total_tokens"];
    private static readonly string[] InputDetailKeys = ["cached_tokens", "cache_write_tokens", "cache_write_tokens_details"];
    private static readonly string[] OutputDetailKeys = ["reasoning_tokens"];

    /// <summary>First live test: the cache-write retention counts OpenAI documents. Any other key here is still an unknown usage field.</summary>
    private static readonly string[] CacheWriteDetailKeys = ["ttl_12h_tokens", "ttl_30m_tokens"];

    /// <summary>
    /// Independent review F1/F2: the reported service tier and every departure from the approved request, read on its own like
    /// <see cref="CacheWriteReport"/> so a body refused for another reason still reports them, and over every occurrence so a repeated
    /// field can't hide one. One absent or null tier is accepted and shown as not reported.
    /// </summary>
    private static (string? Tier, AdvisoryAiServiceTierReport Report, AdvisoryAiResponseDeparture Departures, AdvisoryAiUsageFieldLocation UnknownAt) DepartureReport(
        JsonElement root, string? expectedTier, bool noPromptCache)
    {
        if (root.ValueKind != JsonValueKind.Object) return (null, AdvisoryAiServiceTierReport.NotReported, AdvisoryAiResponseDeparture.None, AdvisoryAiUsageFieldLocation.None);
        AdvisoryAiResponseDeparture departures = AdvisoryAiResponseDeparture.None;
        AdvisoryAiUsageFieldLocation unknownAt = AdvisoryAiUsageFieldLocation.None;

        // Task 70E T70E-02: every occurrence counts, null ones included, so a null copy can't hide a repeated tier.
        List<JsonElement> tiers = [.. Occurrences(root, "service_tier")];
        string? tier = null;
        AdvisoryAiServiceTierReport report = AdvisoryAiServiceTierReport.NotReported;
        if (tiers.Count > 1) report = AdvisoryAiServiceTierReport.Repeated;
        else if (tiers.Count == 1 && tiers[0].ValueKind != JsonValueKind.Null)
        {
            // Task 70E T70E-01: only a known name is kept, and then as this app's own constant, never the provider's string.
            tier = tiers[0].ValueKind == JsonValueKind.String ? KnownServiceTiers.FirstOrDefault(known => tiers[0].ValueEquals(known)) : null;
            report = tier is null ? AdvisoryAiServiceTierReport.Unrecognised : AdvisoryAiServiceTierReport.Named;
        }
        if (report is AdvisoryAiServiceTierReport.Repeated or AdvisoryAiServiceTierReport.Unrecognised ||
            report == AdvisoryAiServiceTierReport.Named && expectedTier is not null && !string.Equals(tier, expectedTier, StringComparison.Ordinal))
            departures |= AdvisoryAiResponseDeparture.ServiceTier;

        foreach (JsonElement usage in Occurrences(root, "usage"))
        {
            if (usage.ValueKind != JsonValueKind.Object) continue;
            if (HasUnknownKey(usage, UsageKeys)) unknownAt |= AdvisoryAiUsageFieldLocation.UsageTotals;
            foreach (JsonElement details in Occurrences(usage, "input_tokens_details"))
            {
                if (details.ValueKind != JsonValueKind.Object) continue;
                if (HasUnknownKey(details, InputDetailKeys)) unknownAt |= AdvisoryAiUsageFieldLocation.InputDetails;
                foreach (JsonElement breakdown in Occurrences(details, "cache_write_tokens_details"))
                    if (breakdown.ValueKind == JsonValueKind.Object && HasUnknownKey(breakdown, CacheWriteDetailKeys)) unknownAt |= AdvisoryAiUsageFieldLocation.CacheWriteDetails;
                if (noPromptCache && Occurrences(details, "cached_tokens").Any(value => !(value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long cached) && cached == 0)))
                    departures |= AdvisoryAiResponseDeparture.PromptCacheUsed;
            }
            foreach (JsonElement details in Occurrences(usage, "output_tokens_details"))
                if (details.ValueKind == JsonValueKind.Object && HasUnknownKey(details, OutputDetailKeys)) unknownAt |= AdvisoryAiUsageFieldLocation.OutputDetails;
        }
        if (unknownAt != AdvisoryAiUsageFieldLocation.None) departures |= AdvisoryAiResponseDeparture.UnknownUsageField;

        bool state = Occurrences(root, "background").Any(value => value.ValueKind != JsonValueKind.False) ||
            Occurrences(root, "previous_response_id").Any(value => value.ValueKind != JsonValueKind.Null) ||
            Occurrences(root, "conversation").Any(value => value.ValueKind != JsonValueKind.Null);
        if (state) departures |= AdvisoryAiResponseDeparture.ConversationState;
        return (tier, report, departures, unknownAt);
    }

    private static bool HasUnknownKey(JsonElement element, string[] known) =>
        element.EnumerateObject().Any(property => !known.Contains(property.Name, StringComparer.Ordinal));

    private static IEnumerable<JsonElement> Occurrences(JsonElement element, string name) =>
        element.EnumerateObject().Where(property => property.NameEquals(name)).Select(property => property.Value);

    private static bool TryCount(JsonElement element, string name, out long value)
    {
        value = 0;
        return element.TryGetProperty(name, out JsonElement count) && count.ValueKind == JsonValueKind.Number && count.TryGetInt64(out value) && value >= 0;
    }

    private static bool IsString(JsonElement element, string name, string expected) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String && value.ValueEquals(expected);

    private static bool IsValidId(string? value) =>
        !string.IsNullOrEmpty(value) && value.Length <= MaximumIdLength && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-');

    private static bool HasDuplicate(JsonElement element)
    {
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (JsonProperty property in element.EnumerateObject())
            if (!seen.Add(property.Name)) return true;
        return false;
    }
}
