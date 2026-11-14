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
    UnexpectedState
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
            preview.Model.ServiceTier, preview.Model.RequestNoPromptCache);
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
    /// set, the request asked for no prompt cache, so any cached input is a departure.
    /// </summary>
    public static AdvisoryAiResponse Read(ReadOnlySpan<byte> body, string expectedModel, long maximumBytes, long approvedInputTokens, long approvedOutputTokens,
        string? expectedServiceTier = null, bool noPromptCache = false)
    {
        ArgumentNullException.ThrowIfNull(expectedModel);
        if (body.Length > maximumBytes) return AdvisoryAiResponse.Malformed(AdvisoryAiMalformedReason.TooLarge);
        try
        {
            using JsonDocument document = JsonDocument.Parse(body.ToArray(),
                new JsonDocumentOptions { MaxDepth = MaximumDepth, CommentHandling = JsonCommentHandling.Disallow, AllowTrailingCommas = false });
            AdvisoryAiResponse response = Read(document.RootElement, expectedModel, approvedInputTokens, approvedOutputTokens);
            (long? written, bool unreadable) = CacheWriteReport(document.RootElement);
            (string? tier, AdvisoryAiServiceTierReport tierReport, AdvisoryAiResponseDeparture departures) = DepartureReport(document.RootElement, expectedServiceTier, noPromptCache);
            // Task 70B T70B-02: a reported cache write (or one that isn't a plain count) is an unexpected billing category even when the
            // body is refused earlier for another reason, so it always stops paid requests for review. Independent review F1/F2: so is
            // another service tier, an unknown usage counter, cached input on a no-cache request, or conversation state. Usage is then
            // never trusted, so the whole reservation is kept.
            bool unexpected = unreadable || written > 0 || departures != AdvisoryAiResponseDeparture.None;
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
                UsageReliable = response.UsageReliable && !unexpected,
                UsageAboveApprovedBounds = response.UsageAboveApprovedBounds || unexpected
            };
        }
        catch (JsonException)
        {
            return AdvisoryAiResponse.Malformed(AdvisoryAiMalformedReason.NotJson);
        }
    }

    private static AdvisoryAiResponse Read(JsonElement root, string expectedModel, long approvedInputTokens, long approvedOutputTokens)
    {
        if (root.ValueKind != JsonValueKind.Object) return AdvisoryAiResponse.Malformed(AdvisoryAiMalformedReason.NotAResponseObject);
        if (HasDuplicate(root)) return AdvisoryAiResponse.Malformed(AdvisoryAiMalformedReason.DuplicateProperty);
        if (!IsString(root, "object", "response")) return AdvisoryAiResponse.Malformed(AdvisoryAiMalformedReason.NotAResponseObject);
        if (!root.TryGetProperty("id", out JsonElement idElement) || idElement.ValueKind != JsonValueKind.String || !IsValidId(idElement.GetString()))
            return AdvisoryAiResponse.Malformed(AdvisoryAiMalformedReason.MissingOrInvalidId);
        string id = idElement.GetString()!;
        if (!IsString(root, "model", expectedModel)) return AdvisoryAiResponse.Malformed(AdvisoryAiMalformedReason.ModelMismatch);
        if (root.TryGetProperty("store", out JsonElement store) && store.ValueKind != JsonValueKind.False) return AdvisoryAiResponse.Malformed(AdvisoryAiMalformedReason.StoredResponse);
        if (root.TryGetProperty("tools", out JsonElement tools) && !(tools.ValueKind == JsonValueKind.Array && tools.GetArrayLength() == 0))
            return AdvisoryAiResponse.Malformed(AdvisoryAiMalformedReason.ToolsPresent);

        if (!root.TryGetProperty("status", out JsonElement statusElement) || statusElement.ValueKind != JsonValueKind.String)
            return AdvisoryAiResponse.Malformed(AdvisoryAiMalformedReason.UnknownStatus);
        AdvisoryAiAnswerStatus? status = statusElement.GetString() switch
        {
            "completed" => AdvisoryAiAnswerStatus.Completed,
            "incomplete" => AdvisoryAiAnswerStatus.Incomplete,
            "failed" or "cancelled" => AdvisoryAiAnswerStatus.ProviderFailed,
            _ => null
        };
        if (status is null) return AdvisoryAiResponse.Malformed(AdvisoryAiMalformedReason.UnknownStatus);

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

        (AdvisoryAiUsage? usage, bool reliable, bool cacheWritten) = ReadUsage(root);
        // Independent review F2: an answer always has input (the instructions at least), so usage reporting none can't settle it at $0.
        if (reliable && usage!.InputTokens == 0 && status != AdvisoryAiAnswerStatus.ProviderFailed) reliable = false;
        // Task 69: a reported cache write was never requested and is billed above the input rate, so the usage can't settle the
        // request and paid requests stop for review, as for usage above the approved bounds.
        bool above = cacheWritten || reliable && (usage!.InputTokens > approvedInputTokens || usage.OutputTokens > approvedOutputTokens);

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
    /// </summary>
    private static (long? Tokens, bool Unreadable) CacheWriteReport(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return (null, false);
        int fields = 0;
        bool ambiguous = false, notACount = false;
        long count = 0;
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
            }
        }
        if (fields == 0) return (null, false);
        return fields == 1 && !ambiguous && !notACount ? (count, false) : (null, true);
    }

    private static readonly string[] UsageKeys = ["input_tokens", "input_tokens_details", "output_tokens", "output_tokens_details", "total_tokens"];
    private static readonly string[] InputDetailKeys = ["cached_tokens", "cache_write_tokens"];
    private static readonly string[] OutputDetailKeys = ["reasoning_tokens"];

    /// <summary>
    /// Independent review F1/F2: the reported service tier and every departure from the approved request, read on its own like
    /// <see cref="CacheWriteReport"/> so a body refused for another reason still reports them, and over every occurrence so a repeated
    /// field can't hide one. One absent or null tier is accepted and shown as not reported.
    /// </summary>
    private static (string? Tier, AdvisoryAiServiceTierReport Report, AdvisoryAiResponseDeparture Departures) DepartureReport(JsonElement root, string? expectedTier, bool noPromptCache)
    {
        if (root.ValueKind != JsonValueKind.Object) return (null, AdvisoryAiServiceTierReport.NotReported, AdvisoryAiResponseDeparture.None);
        AdvisoryAiResponseDeparture departures = AdvisoryAiResponseDeparture.None;

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
            if (HasUnknownKey(usage, UsageKeys)) departures |= AdvisoryAiResponseDeparture.UnknownUsageField;
            foreach (JsonElement details in Occurrences(usage, "input_tokens_details"))
            {
                if (details.ValueKind != JsonValueKind.Object) continue;
                if (HasUnknownKey(details, InputDetailKeys)) departures |= AdvisoryAiResponseDeparture.UnknownUsageField;
                if (noPromptCache && Occurrences(details, "cached_tokens").Any(value => !(value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long cached) && cached == 0)))
                    departures |= AdvisoryAiResponseDeparture.PromptCacheUsed;
            }
            foreach (JsonElement details in Occurrences(usage, "output_tokens_details"))
                if (details.ValueKind == JsonValueKind.Object && HasUnknownKey(details, OutputDetailKeys)) departures |= AdvisoryAiResponseDeparture.UnknownUsageField;
        }

        bool state = Occurrences(root, "background").Any(value => value.ValueKind != JsonValueKind.False) ||
            Occurrences(root, "previous_response_id").Any(value => value.ValueKind != JsonValueKind.Null) ||
            Occurrences(root, "conversation").Any(value => value.ValueKind != JsonValueKind.Null);
        if (state) departures |= AdvisoryAiResponseDeparture.ConversationState;
        return (tier, report, departures);
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
