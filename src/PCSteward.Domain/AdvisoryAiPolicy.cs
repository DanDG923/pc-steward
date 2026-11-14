using System.Globalization;

namespace PCSteward.Domain;

/// <summary>
/// FR-BUD-01 (ADR-029 Q7): an amount of US dollars in checked fixed-point micro-dollars (1,000,000 per dollar). No floating point
/// is used anywhere in AI money arithmetic; overflow throws rather than wrapping.
/// </summary>
public readonly record struct MicroUsd(long Value) : IComparable<MicroUsd>
{
    public const long PerDollar = 1_000_000;
    public const int MaximumFractionDigits = 6;

    public static MicroUsd Zero { get; } = new(0);

    public bool IsZero => Value == 0;

    public int CompareTo(MicroUsd other) => Value.CompareTo(other.Value);

    public static bool operator <(MicroUsd left, MicroUsd right) => left.Value < right.Value;
    public static bool operator >(MicroUsd left, MicroUsd right) => left.Value > right.Value;
    public static bool operator <=(MicroUsd left, MicroUsd right) => left.Value <= right.Value;
    public static bool operator >=(MicroUsd left, MicroUsd right) => left.Value >= right.Value;

    /// <summary>Checked sum. Throws <see cref="OverflowException"/> rather than wrapping.</summary>
    public static MicroUsd Add(MicroUsd left, MicroUsd right) => new(checked(left.Value + right.Value));

    /// <summary>
    /// ADR-029 Q7: parses a plain non-negative dollar amount such as <c>0.25</c> or <c>200</c>. Only ASCII digits and at most one
    /// decimal point with at most six fraction digits are accepted: no sign, exponent, currency symbol, grouping or whitespace.
    /// A value needing more precision, or too large to hold, is refused rather than rounded or clipped.
    /// </summary>
    public static bool TryParseDollars(string? text, out MicroUsd amount)
    {
        amount = Zero;
        if (string.IsNullOrEmpty(text) || text.Length > 32) return false;
        int point = text.IndexOf('.', StringComparison.Ordinal);
        string whole = point < 0 ? text : text[..point];
        string fraction = point < 0 ? string.Empty : text[(point + 1)..];
        if (whole.Length == 0 || (point >= 0 && fraction.Length == 0) || fraction.Length > MaximumFractionDigits) return false;
        if (!whole.All(char.IsAsciiDigit) || !fraction.All(char.IsAsciiDigit)) return false;
        try
        {
            long dollars = long.Parse(whole, NumberStyles.None, CultureInfo.InvariantCulture);
            long micros = fraction.Length == 0 ? 0 : long.Parse(fraction.PadRight(MaximumFractionDigits, '0'), NumberStyles.None, CultureInfo.InvariantCulture);
            amount = new(checked(dollars * PerDollar + micros));
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    /// <summary>Formats as dollars with at least two decimals and no trailing zeros beyond them, e.g. <c>$0.25</c>, <c>$0.000125</c>.</summary>
    public string ToDollarString()
    {
        if (Value < 0) return "-" + new MicroUsd(checked(-Value)).ToDollarString();
        long dollars = Value / PerDollar;
        string fraction = (Value % PerDollar).ToString("D6", CultureInfo.InvariantCulture).TrimEnd('0');
        if (fraction.Length < 2) fraction = fraction.PadRight(2, '0');
        return "$" + dollars.ToString(CultureInfo.InvariantCulture) + "." + fraction;
    }

    public override string ToString() => ToDollarString();
}

/// <summary>
/// FR-AI-04, FR-BUD-01 (ADR-028 "AI limits", ADR-029 Q7): the four owner-editable AI limits. Token limits and money limits are
/// independent: an edit to one never changes the other. Either money value at zero disables paid requests and keeps the other.
/// </summary>
public sealed record AdvisoryAiLimits(int MaximumInputTokens, int MaximumOutputTokens, MicroUsd RequestCap, MicroUsd MonthlyBudget)
{
    /// <summary>The accepted initial defaults: 8,192 input tokens, 1,024 billable output tokens, $0.25 per request, $200 per month.</summary>
    public static AdvisoryAiLimits Defaults { get; } = new(8192, 1024, new(250_000), new(200_000_000));

    /// <summary>FR-BUD-01: zero in either spending limit disables paid requests; the other value and the ledger are kept.</summary>
    public bool PaidRequestsDisabled => RequestCap.IsZero || MonthlyBudget.IsZero;
}

/// <summary>Why a set of AI limits cannot be saved or used. The first failing rule is reported.</summary>
public enum AdvisoryAiLimitProblem
{
    None,
    InputTokensNotPositive,
    OutputTokensNotPositive,
    NegativeAmount,
    RequestCapAboveMonthlyBudget,
    InputAboveModelContext,
    OutputAboveModelMaximum,
    InputPlusOutputAboveModelContext,
    RequestBytesAboveMaximum,
    ResponseBytesAboveMaximum,
    SavedItemBytesAboveMaximum,
    InputTooSmallForInstructions
}

/// <summary>
/// FR-AI-04 (ADR-028 Q1 and review F1): the accepted v1 hard resource maxima. MiB means 1,048,576 bytes. These bound every
/// admitted token setting; they are not performance evidence and are never silently reduced after an approval.
/// </summary>
public static class AdvisoryAiResourcePolicy
{
    public const int PolicyVersion = 1;
    public const long MaximumRequestBytes = 4L * 1024 * 1024;
    public const long MaximumResponseBytes = 4L * 1024 * 1024;
    public const long MaximumSavedItemBytes = 4L * 1024 * 1024;
    public const long MaximumSavedContentBytes = 8L * 1024 * 1024;

    /// <summary>
    /// The most UTF-8 bytes one byte of text can become inside a JSON string: a character written as a six-byte <c>\u00XX</c>
    /// escape. Applied to every text field of the request and the response, so the bounds hold whatever the text contains.
    /// </summary>
    public const int JsonEscapeExpansion = 6;

    /// <summary>The request envelope outside its two text fields: the fixed property names, the model ID (at most 128 bytes) and numbers.</summary>
    public const int RequestEnvelopeBytes = 1024;

    /// <summary>The most a sanitized answer can grow: a one-letter account name replaced by the ten-byte <c>[redacted]</c>.</summary>
    public const int AnswerSanitizerExpansion = 10;

    /// <summary>A saved explanation's fixed fields beside its text: IDs, source kind, times.</summary>
    public const int SavedItemMetadataBytes = 512;

    public static TimeSpan OverallDeadline { get; } = TimeSpan.FromSeconds(45);
    public static TimeSpan ConnectDeadline { get; } = TimeSpan.FromSeconds(10);
    public static TimeSpan SavedExplanationRetention { get; } = TimeSpan.FromDays(30);
}

/// <summary>
/// FR-AI-01/04 (ADR-028 F1/F7/F11): the reviewed, evidence-backed profile of one exact model on the one approved route. Only a
/// profile in <see cref="AdvisoryAiModelProfiles.Reviewed"/> can be used; a model without evidence stays unavailable.
/// </summary>
/// <param name="ModelId">The exact model revision sent and expected back, never an alias resolved later.</param>
/// <param name="ProfileVersion">Changes whenever any value here changes, so an approval bound to an older profile is invalid.</param>
/// <param name="ContextWindowTokens">The model's context window: input plus output must fit.</param>
/// <param name="MaximumOutputTokens">The most output tokens the model accepts for <c>max_output_tokens</c>.</param>
/// <param name="FramingAllowanceTokens">An evidenced upper bound on tokens the route adds around the instructions and input.</param>
/// <param name="TokenizerIsByteLevel">
/// True only when the model's tokenizer is evidenced to be byte-level BPE, so every token covers at least one UTF-8 byte and a
/// text's UTF-8 byte count is a proven upper bound on its tokens (ADR-028 F7: a conservative local upper bound, not a guess).
/// </param>
/// <param name="MaximumTokenUtf8Bytes">The most UTF-8 bytes any one output token can decode to.</param>
/// <param name="ResponseEnvelopeBytes">An evidenced bound on the response object outside its text fields.</param>
/// <param name="ServiceTier">The exact service tier requested, whose prices the tariff states.</param>
/// <remarks>
/// ADR-028 addendum A1: a profile is one registry entry. Besides the values above it names its <see cref="Provider"/> and the
/// <see cref="ReasoningEffort"/> it requests, so each model carries its own evidence, limits, reasoning setting and (through its model
/// ID and tier) its own owner-confirmed prices. Offering a second model is adding a second reviewed entry, not new code paths.
/// </remarks>
public sealed record AdvisoryAiModelProfile(
    string ModelId,
    int ProfileVersion,
    int ContextWindowTokens,
    int MaximumOutputTokens,
    int FramingAllowanceTokens,
    bool TokenizerIsByteLevel,
    int MaximumTokenUtf8Bytes,
    int ResponseEnvelopeBytes,
    string ServiceTier)
{
    /// <summary>The provider whose route, key and wire format this model uses.</summary>
    public AdvisoryAiProviderId Provider { get; init; } = AdvisoryAiProviderId.OpenAI;

    /// <summary>
    /// The reasoning effort requested, exactly as the provider spells it, or null to send none. Fixed per reviewed profile so billed
    /// reasoning cannot silently use up the output allowance (Task 69 measures it).
    /// </summary>
    public string? ReasoningEffort { get; init; }

    /// <summary>ADR-028 F1: the whole request, every text byte assumed to need the longest JSON escape.</summary>
    public static long RequestBytes(int maximumInputTokens) =>
        checked(AdvisoryAiResourcePolicy.RequestEnvelopeBytes + (long)AdvisoryAiResourcePolicy.JsonEscapeExpansion * maximumInputTokens);

    /// <summary>
    /// ADR-028 F1: the whole response. The response object can echo the instructions, so the input allowance is counted as well
    /// as every output token at its longest, escaped.
    /// </summary>
    public long ResponseBytes(int maximumInputTokens, int maximumOutputTokens) =>
        checked(ResponseEnvelopeBytes + (long)AdvisoryAiResourcePolicy.JsonEscapeExpansion * maximumInputTokens +
            (long)AdvisoryAiResourcePolicy.JsonEscapeExpansion * maximumOutputTokens * MaximumTokenUtf8Bytes);

    /// <summary>ADR-028 F1: one saved explanation, its text at its longest after sanitizing, plus its fixed fields.</summary>
    public long SavedItemBytes(int maximumOutputTokens) =>
        checked(AdvisoryAiResourcePolicy.SavedItemMetadataBytes + (long)AdvisoryAiResourcePolicy.AnswerSanitizerExpansion * maximumOutputTokens * MaximumTokenUtf8Bytes);

    /// <summary>A profile is usable only when its own values are coherent and its token bound is proven.</summary>
    public bool IsCoherent =>
        !string.IsNullOrWhiteSpace(ModelId) && ModelId.Length <= 128 && ModelId.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '.' or '_') &&
        ProfileVersion > 0 && ContextWindowTokens > 0 && MaximumOutputTokens > 0 && MaximumOutputTokens < ContextWindowTokens &&
        FramingAllowanceTokens >= 0 && TokenizerIsByteLevel && MaximumTokenUtf8Bytes > 0 && ResponseEnvelopeBytes > 0 &&
        !string.IsNullOrWhiteSpace(ServiceTier) && ServiceTier.All(character => char.IsAsciiLetterLower(character) || character == '_') &&
        AdvisoryAiProviders.Find(Provider) is not null &&
        (ReasoningEffort is null || (ReasoningEffort.Length is >= 1 and <= 16 && ReasoningEffort.All(char.IsAsciiLetterLower)));
}

/// <summary>
/// FR-AI-01 (ADR-028 F1/F7/F11, Task 67 exit): the reviewed models. Empty on purpose: no model is chosen from memory. A profile is
/// added only with the Task 69 evidence for that exact model (tokenizer, framing, response size, current prices), and paid
/// requests stay disabled by <see cref="AdvisoryAiBuildPolicy"/> until the Task 70 review passes.
/// </summary>
public static class AdvisoryAiModelProfiles
{
    public static IReadOnlyList<AdvisoryAiModelProfile> Reviewed { get; } = [];

    public static AdvisoryAiModelProfile? Find(string? modelId) =>
        Reviewed.FirstOrDefault(profile => string.Equals(profile.ModelId, modelId, StringComparison.Ordinal));
}

/// <summary>
/// ADR-029 "Ending first-live-test mode" (R6): whether this build may make paid requests at all is a reviewed build policy, not an
/// app-data setting. Task 67 builds the path with mocks only, so paid requests are disabled.
/// </summary>
public static class AdvisoryAiBuildPolicy
{
    public static AdvisoryAiPaidRequestMode PaidRequests => AdvisoryAiPaidRequestMode.Disabled;
}

public enum AdvisoryAiPaidRequestMode
{
    /// <summary>No request can reach a provider. The whole AI path works with mocks only.</summary>
    Disabled,

    /// <summary>ADR-029: the separately authorized first live test, inside the cumulative $1 envelope.</summary>
    FirstLiveTest,

    /// <summary>Ordinary use, only after the owner separately ends first-live-test mode.</summary>
    Ordinary
}

/// <summary>FR-AI-04, FR-BUD-01 (ADR-028/029): validates the four limits together, and against a model profile when one is chosen.</summary>
public static class AdvisoryAiLimitValidation
{
    public static AdvisoryAiLimitProblem Validate(AdvisoryAiLimits limits, AdvisoryAiModelProfile? model = null, int instructionTokens = 0)
    {
        ArgumentNullException.ThrowIfNull(limits);
        if (limits.MaximumInputTokens <= 0) return AdvisoryAiLimitProblem.InputTokensNotPositive;
        if (limits.MaximumOutputTokens <= 0) return AdvisoryAiLimitProblem.OutputTokensNotPositive;
        if (limits.RequestCap.Value < 0 || limits.MonthlyBudget.Value < 0) return AdvisoryAiLimitProblem.NegativeAmount;
        if (!limits.PaidRequestsDisabled && limits.RequestCap > limits.MonthlyBudget) return AdvisoryAiLimitProblem.RequestCapAboveMonthlyBudget;
        if (model is null) return AdvisoryAiLimitProblem.None;

        if (limits.MaximumInputTokens > model.ContextWindowTokens) return AdvisoryAiLimitProblem.InputAboveModelContext;
        if (limits.MaximumOutputTokens > model.MaximumOutputTokens) return AdvisoryAiLimitProblem.OutputAboveModelMaximum;
        if ((long)limits.MaximumInputTokens + limits.MaximumOutputTokens > model.ContextWindowTokens) return AdvisoryAiLimitProblem.InputPlusOutputAboveModelContext;
        if (AdvisoryAiModelProfile.RequestBytes(limits.MaximumInputTokens) > AdvisoryAiResourcePolicy.MaximumRequestBytes) return AdvisoryAiLimitProblem.RequestBytesAboveMaximum;
        if (model.ResponseBytes(limits.MaximumInputTokens, limits.MaximumOutputTokens) > AdvisoryAiResourcePolicy.MaximumResponseBytes) return AdvisoryAiLimitProblem.ResponseBytesAboveMaximum;
        long saved = model.SavedItemBytes(limits.MaximumOutputTokens);
        if (saved > AdvisoryAiResourcePolicy.MaximumSavedItemBytes || saved > AdvisoryAiResourcePolicy.MaximumSavedContentBytes) return AdvisoryAiLimitProblem.SavedItemBytesAboveMaximum;
        if ((long)model.FramingAllowanceTokens + instructionTokens >= limits.MaximumInputTokens) return AdvisoryAiLimitProblem.InputTooSmallForInstructions;
        return AdvisoryAiLimitProblem.None;
    }

    /// <summary>The binding limit in words, for the settings page and the request review.</summary>
    public static string Explain(AdvisoryAiLimitProblem problem) => problem switch
    {
        AdvisoryAiLimitProblem.None => "These limits can be used.",
        AdvisoryAiLimitProblem.InputTokensNotPositive => "The input limit must be a whole number above zero.",
        AdvisoryAiLimitProblem.OutputTokensNotPositive => "The output limit must be a whole number above zero.",
        AdvisoryAiLimitProblem.NegativeAmount => "Spending limits can't be negative. Set one to 0 to turn paid requests off.",
        AdvisoryAiLimitProblem.RequestCapAboveMonthlyBudget => "The limit per request can't be more than the monthly budget.",
        AdvisoryAiLimitProblem.InputAboveModelContext => "The input limit is more than this model can read.",
        AdvisoryAiLimitProblem.OutputAboveModelMaximum => "The output limit is more than this model can write in one answer.",
        AdvisoryAiLimitProblem.InputPlusOutputAboveModelContext => "Input and output together are more than this model can handle in one request.",
        AdvisoryAiLimitProblem.RequestBytesAboveMaximum => "A request this large would be over PC Steward's 4 MiB request limit.",
        AdvisoryAiLimitProblem.ResponseBytesAboveMaximum => "An answer this long could be over PC Steward's 4 MiB response limit.",
        AdvisoryAiLimitProblem.SavedItemBytesAboveMaximum => "An answer this long could be over PC Steward's 4 MiB limit for one saved explanation.",
        AdvisoryAiLimitProblem.InputTooSmallForInstructions => "The input limit is too small to hold PC Steward's fixed instructions and any evidence.",
        _ => "These limits can't be used."
    };
}
