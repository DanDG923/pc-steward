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
    InvalidOutputText
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
    public static AdvisoryAiResponse Malformed(AdvisoryAiMalformedReason reason) =>
        new(AdvisoryAiAnswerStatus.Malformed, AdvisoryAiIncompleteReason.None, reason, null, null, null, false, false);
}

/// <summary>
/// FR-AI-04/05 (ADR-028 Q1, ADR-029 F4): reads a Responses API body for one approved request. It refuses anything over the admitted
/// response size, deeper than needed, with duplicate properties where it reads, for another model, stored, with tools, or with any
/// output item other than a message or reasoning. Unknown top-level fields are ignored, not trusted. Usage is reliable only when every
/// count is present, non-negative and consistent; a body that is malformed has no reliable usage, so its cost stays uncertain.
/// </summary>
public static class AdvisoryAiResponseReader
{
    public const int MaximumDepth = 16;
    public const int MaximumOutputItems = 64;
    public const int MaximumIdLength = 128;

    public static AdvisoryAiResponse Read(ReadOnlySpan<byte> body, AdvisoryAiApproval approval)
    {
        ArgumentNullException.ThrowIfNull(approval);
        AdvisoryAiRequestPreview preview = approval.Preview;
        return Read(body, preview.Model.ModelId, AdmittedBytes(approval), preview.InputTokenUpperBound, preview.Limits.MaximumOutputTokens);
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

    public static AdvisoryAiResponse Read(ReadOnlySpan<byte> body, string expectedModel, long maximumBytes, long approvedInputTokens, long approvedOutputTokens)
    {
        ArgumentNullException.ThrowIfNull(expectedModel);
        if (body.Length > maximumBytes) return AdvisoryAiResponse.Malformed(AdvisoryAiMalformedReason.TooLarge);
        try
        {
            using JsonDocument document = JsonDocument.Parse(body.ToArray(),
                new JsonDocumentOptions { MaxDepth = MaximumDepth, CommentHandling = JsonCommentHandling.Disallow, AllowTrailingCommas = false });
            return Read(document.RootElement, expectedModel, approvedInputTokens, approvedOutputTokens);
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

        (AdvisoryAiUsage? usage, bool reliable) = ReadUsage(root);
        bool above = reliable && (usage!.InputTokens > approvedInputTokens || usage.OutputTokens > approvedOutputTokens);

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
        new(AdvisoryAiAnswerStatus.Malformed, AdvisoryAiIncompleteReason.None, reason, id, null, usage, false, reliable && above);

    private static (AdvisoryAiUsage? Usage, bool Reliable) ReadUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out JsonElement usage) || usage.ValueKind != JsonValueKind.Object || HasDuplicate(usage)) return (null, false);
        bool complete = TryCount(usage, "input_tokens", out long input) & TryCount(usage, "output_tokens", out long output) & TryCount(usage, "total_tokens", out long total);
        long cached = 0, reasoning = 0;
        bool details = usage.TryGetProperty("input_tokens_details", out JsonElement inputDetails) && inputDetails.ValueKind == JsonValueKind.Object &&
            !HasDuplicate(inputDetails) && TryCount(inputDetails, "cached_tokens", out cached);
        details &= usage.TryGetProperty("output_tokens_details", out JsonElement outputDetails) && outputDetails.ValueKind == JsonValueKind.Object &&
            !HasDuplicate(outputDetails) && TryCount(outputDetails, "reasoning_tokens", out reasoning);
        if (!complete) return (null, false);
        AdvisoryAiUsage value = new(input, cached, output, reasoning, total);
        bool consistent = details && input >= 0 && output >= 0 && cached <= input && reasoning <= output && total == input + output;
        return (value, consistent);
    }

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
