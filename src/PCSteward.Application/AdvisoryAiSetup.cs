using System.Globalization;
using System.Text;
using PCSteward.Domain;

namespace PCSteward.Application;

/// <summary>Whether this build can offer AI explanations at all, before anything the owner has set.</summary>
public enum AdvisoryAiAvailabilityState
{
    /// <summary>No model has its Task 69 evidence and Task 70 review in this build, so nothing can be set up or sent.</summary>
    NoReviewedModel,

    /// <summary>A reviewed model exists but this build's policy keeps paid requests off: set-up and previews work, nothing is sent.</summary>
    PaidRequestsOffInThisBuild,

    /// <summary>ADR-029 D5: only the separately approved first live test, inside its cumulative $1 envelope.</summary>
    FirstLiveTestOnly,

    Available
}

/// <summary>
/// FR-AI-01 (ADR-028 D2, addendum A1; Task 67 stage 4): what this build offers, from its reviewed models and its paid-request build
/// policy. One model and no picker (the owner's choice, 2026-11-14): the first coherent reviewed entry is the model, and its provider
/// is the one the owner connects to. Nothing here reads settings, the key or the network.
/// </summary>
public sealed record AdvisoryAiAvailability(AdvisoryAiAvailabilityState State, AdvisoryAiModelProfile? Model, AdvisoryAiProvider? Provider, AdvisoryAiPaidRequestMode Mode)
{
    /// <summary>This build: <see cref="AdvisoryAiModelProfiles.Reviewed"/> and <see cref="AdvisoryAiBuildPolicy.PaidRequests"/>.</summary>
    public static AdvisoryAiAvailability Current => Evaluate(AdvisoryAiModelProfiles.Reviewed, AdvisoryAiBuildPolicy.PaidRequests);

    public static AdvisoryAiAvailability Evaluate(IReadOnlyList<AdvisoryAiModelProfile> reviewed, AdvisoryAiPaidRequestMode mode)
    {
        ArgumentNullException.ThrowIfNull(reviewed);
        AdvisoryAiModelProfile? model = reviewed.FirstOrDefault(profile => profile is not null && profile.IsCoherent);
        if (model is null || AdvisoryAiProviders.Find(model.Provider) is not AdvisoryAiProvider provider)
            return new(AdvisoryAiAvailabilityState.NoReviewedModel, null, null, mode);
        AdvisoryAiAvailabilityState state = mode switch
        {
            AdvisoryAiPaidRequestMode.FirstLiveTest => AdvisoryAiAvailabilityState.FirstLiveTestOnly,
            AdvisoryAiPaidRequestMode.Ordinary => AdvisoryAiAvailabilityState.Available,
            _ => AdvisoryAiAvailabilityState.PaidRequestsOffInThisBuild
        };
        return new(state, model, provider, mode);
    }

    /// <summary>Connecting, limits and prices are offered only when there is a reviewed model to use them with.</summary>
    public bool CanSetUp => Model is not null && Provider is not null;
}

/// <summary>ADR-029 "missing accounting continuity": the owner's answer to the first-connection question.</summary>
public enum AdvisoryAiPriorUseAnswer
{
    /// <summary>"No, never": recorded as the owner's own declaration.</summary>
    NoPriorUse,

    /// <summary>"Yes, or I'm not sure": paid requests wait for a confirmed later UTC month.</summary>
    YesOrUnsure
}

public enum AdvisoryAiConnectOutcome
{
    Connected,

    /// <summary>Nothing was written: this is the first connection on this app state and the prior-use question has no answer.</summary>
    NeedsPriorUseAnswer,

    /// <summary>Nothing was written: no reviewed model, or no key store for its provider, in this build.</summary>
    NotAvailable,

    /// <summary>Nothing was written: AI is already connected. Replacing the key is its own action.</summary>
    AlreadyConnected,

    /// <summary>Windows did not save the key. AI stays off.</summary>
    KeyNotSaved,

    /// <summary>
    /// The connection could not be recorded, so AI stays off. When the key had already been saved, PC Steward removed it again and
    /// <see cref="AdvisoryAiConnectResult.Rollback"/> says whether that removal was verified.
    /// </summary>
    NotRecorded
}

/// <summary>
/// The result of one connect attempt. <see cref="ExistingKeyFound"/> is true when a key under PC Steward's own name was already in
/// Credential Manager at a first connection, which ADR-028/029 treat as possible earlier use: the answer is recorded as yes/unsure.
/// <see cref="Rollback"/> is the removal result when a saved key had to be taken out again.
/// </summary>
public sealed record AdvisoryAiConnectResult(AdvisoryAiConnectOutcome Outcome, bool ExistingKeyFound, AdvisoryAiCredentialRemoval? Rollback)
{
    /// <summary>
    /// True when the connection failed and PC Steward can't show that no key is left: a removal that wasn't verified, or a key found
    /// earlier that nothing removed. The app then offers to remove it again (<see cref="AdvisoryAiSetupService.RetryKeyRemovalAsync"/>).
    /// </summary>
    public bool KeyMayRemain => Outcome is (AdvisoryAiConnectOutcome.KeyNotSaved or AdvisoryAiConnectOutcome.NotRecorded) &&
        (Rollback == AdvisoryAiCredentialRemoval.Incomplete || (Rollback is null && (ExistingKeyFound || Outcome == AdvisoryAiConnectOutcome.KeyNotSaved)));
}

/// <summary>
/// FR-AI-03 (ADR-028 "Disconnect"): <see cref="Recorded"/> is true when AI was durably turned off; <see cref="Removal"/> is the verified
/// state of the key. Only <see cref="AdvisoryAiCredentialRemoval.Removed"/> or <see cref="AdvisoryAiCredentialRemoval.AlreadyAbsent"/>
/// may be described as removed; anything else is "credential removal incomplete".
/// </summary>
public sealed record AdvisoryAiDisconnectResult(bool Recorded, AdvisoryAiCredentialRemoval Removal)
{
    public bool KeyGone => Removal is AdvisoryAiCredentialRemoval.Removed or AdvisoryAiCredentialRemoval.AlreadyAbsent;
}

public enum AdvisoryAiKeyReplaceOutcome
{
    Replaced,
    NotConnected,
    NotAvailable,

    /// <summary>Windows did not save the new key. Approvals made before were still invalidated.</summary>
    KeyNotSaved,

    /// <summary>Nothing was changed: PC Steward could not record that the key is changing, so it did not change it.</summary>
    NotRecorded
}

/// <summary>Which field of the AI limits form a message is about.</summary>
public enum AdvisoryAiLimitField
{
    None,
    InputTokens,
    OutputTokens,
    RequestCap,
    MonthlyBudget
}

/// <summary>The outcome of checking a limits form: the limits when they can be saved, otherwise the field and the message to show.</summary>
public sealed record AdvisoryAiLimitsCheck(AdvisoryAiLimits? Limits, AdvisoryAiLimitField Field, string Message);

/// <summary>
/// FR-AI-04, FR-BUD-01 (ADR-028 "AI limits", ADR-029 Q7): the four limits as typed. Token limits are whole numbers of ASCII digits;
/// money is US dollars in <see cref="MicroUsd.TryParseDollars"/> form, after surrounding spaces and one leading "$" are dropped.
/// Nothing is rounded, clipped or guessed: a value that does not parse exactly is refused with the field named.
/// </summary>
public sealed record AdvisoryAiLimitsDraft(string InputTokens, string OutputTokens, string RequestCap, string MonthlyBudget)
{
    /// <summary>The most digits a token limit may have; larger values are refused before parsing.</summary>
    private const int MaximumTokenDigits = 9;

    public static AdvisoryAiLimitsDraft From(AdvisoryAiLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        return new(limits.MaximumInputTokens.ToString(CultureInfo.InvariantCulture), limits.MaximumOutputTokens.ToString(CultureInfo.InvariantCulture),
            limits.RequestCap.ToDollarString()[1..], limits.MonthlyBudget.ToDollarString()[1..]);
    }

    /// <summary>Parses and validates the form, against <paramref name="model"/> and the fixed instructions when a model is given.</summary>
    public AdvisoryAiLimitsCheck Check(AdvisoryAiModelProfile? model)
    {
        if (!TryTokens(InputTokens, out int input))
            return new(null, AdvisoryAiLimitField.InputTokens, "Enter the input limit as a whole number of tokens, for example 8192.");
        if (!TryTokens(OutputTokens, out int output))
            return new(null, AdvisoryAiLimitField.OutputTokens, "Enter the output limit as a whole number of tokens, for example 1024.");
        if (!TryDollars(RequestCap, out MicroUsd cap))
            return new(null, AdvisoryAiLimitField.RequestCap, "Enter the limit per request in US dollars, for example 0.25, with at most six decimal places.");
        if (!TryDollars(MonthlyBudget, out MicroUsd month))
            return new(null, AdvisoryAiLimitField.MonthlyBudget, "Enter the monthly budget in US dollars, for example 200, with at most six decimal places.");

        AdvisoryAiLimits limits = new(input, output, cap, month);
        int instructionTokens = Encoding.UTF8.GetByteCount(AdvisoryAiInstructions.Text);
        AdvisoryAiLimitProblem problem = AdvisoryAiLimitValidation.Validate(limits, model, instructionTokens);
        return problem == AdvisoryAiLimitProblem.None
            ? new(limits, AdvisoryAiLimitField.None, AdvisoryAiLimitValidation.Explain(problem))
            : new(null, FieldOf(problem), AdvisoryAiLimitValidation.Explain(problem));
    }

    private static AdvisoryAiLimitField FieldOf(AdvisoryAiLimitProblem problem) => problem switch
    {
        AdvisoryAiLimitProblem.InputTokensNotPositive or AdvisoryAiLimitProblem.InputAboveModelContext or AdvisoryAiLimitProblem.RequestBytesAboveMaximum or
            AdvisoryAiLimitProblem.InputTooSmallForInstructions or AdvisoryAiLimitProblem.InputPlusOutputAboveModelContext => AdvisoryAiLimitField.InputTokens,
        AdvisoryAiLimitProblem.OutputTokensNotPositive or AdvisoryAiLimitProblem.OutputAboveModelMaximum or AdvisoryAiLimitProblem.ResponseBytesAboveMaximum or
            AdvisoryAiLimitProblem.SavedItemBytesAboveMaximum => AdvisoryAiLimitField.OutputTokens,
        AdvisoryAiLimitProblem.RequestCapAboveMonthlyBudget or AdvisoryAiLimitProblem.NegativeAmount => AdvisoryAiLimitField.RequestCap,
        _ => AdvisoryAiLimitField.None
    };

    internal static bool TryTokens(string? text, out int tokens)
    {
        tokens = 0;
        string value = (text ?? string.Empty).Trim();
        return value.Length is > 0 and <= MaximumTokenDigits && value.All(char.IsAsciiDigit) &&
            int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out tokens);
    }

    internal static bool TryDollars(string? text, out MicroUsd amount)
    {
        string value = (text ?? string.Empty).Trim();
        if (value.StartsWith('$')) value = value[1..];
        return MicroUsd.TryParseDollars(value, out amount);
    }
}

/// <summary>The outcome of checking a prices form: the tariff to confirm when it is complete, otherwise the field's message.</summary>
public sealed record AdvisoryAiPricesCheck(AdvisoryAiTariff? Tariff, string? Field, string Message);

/// <summary>
/// FR-BUD-01 (ADR-029 Q7/F6): the three rates the owner reads from the provider's official price page, in US dollars per million
/// tokens. The form always starts empty: confirming prices means typing what the page says today, never re-accepting old values.
/// A confirmation is valid for 24 hours and is a new immutable version.
/// </summary>
public sealed record AdvisoryAiPricesDraft(string InputPerMillion, string CachedInputPerMillion, string OutputPerMillion)
{
    public static AdvisoryAiPricesDraft Empty { get; } = new(string.Empty, string.Empty, string.Empty);

    public AdvisoryAiPricesCheck Check(AdvisoryAiModelProfile model, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (!AdvisoryAiLimitsDraft.TryDollars(InputPerMillion, out MicroUsd input) || input.IsZero)
            return new(null, nameof(InputPerMillion), "Enter the input price per million tokens in US dollars, for example 2.00. It can't be 0.");
        if (!AdvisoryAiLimitsDraft.TryDollars(CachedInputPerMillion, out MicroUsd cached) || cached.IsZero)
            return new(null, nameof(CachedInputPerMillion), "Enter the cached input price per million tokens in US dollars, for example 0.10. It can't be 0.");
        if (!AdvisoryAiLimitsDraft.TryDollars(OutputPerMillion, out MicroUsd output) || output.IsZero)
            return new(null, nameof(OutputPerMillion), "Enter the output price per million tokens in US dollars, for example 10.00. It can't be 0.");
        // The store assigns the real version; 1 stands in so the rule that needs a positive version can check the rest.
        AdvisoryAiTariff tariff = new(1, model.ModelId, model.ServiceTier, input, cached, output, nowUtc);
        return tariff.Validate(model) == AdvisoryAiTariffProblem.None
            ? new(tariff, null, "These prices can be confirmed.")
            : new(null, null, "These prices can't be used for this model.");
    }
}

/// <summary>What the AI section reads to show itself: never the key, the credential store or the network.</summary>
public sealed record AdvisoryAiSetupSnapshot(
    AdvisoryAiAvailability Availability,
    AdvisoryAiSettings Settings,
    AdvisoryAiAccountingState Accounting,
    AdvisoryAiTariff? LatestTariff,
    AdvisoryAiSpendingSummary? Spending,
    DateTimeOffset ReadAtUtc);

/// <summary>
/// FR-AI-01/03/04, FR-BUD-01 (ADR-028 Q8 and "Disconnect", ADR-029 "missing accounting continuity"; Task 67 stage 4): the owner's
/// deliberate AI set-up actions. Only these flows touch the exact credential, and only in the ways ADR-028 lists: a presence check
/// at a first connection, a save, and removal with verified absence. No flow makes a network call or a billable "test" request.
/// The caller owns every <see cref="AdvisoryAiApiKey"/> passed in and disposes it.
/// </summary>
public sealed class AdvisoryAiSetupService
{
    private readonly IAdvisoryAiSettingsStore settings;
    private readonly IAdvisoryAiLedger ledger;
    private readonly AdvisoryAiProviderAdapters adapters;
    private readonly AdvisoryAiAvailability availability;
    private readonly Func<DateTimeOffset> utcNow;

    public AdvisoryAiSetupService(IAdvisoryAiSettingsStore settings, IAdvisoryAiLedger ledger, AdvisoryAiProviderAdapters adapters,
        AdvisoryAiAvailability availability, Func<DateTimeOffset> utcNow)
    {
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        this.ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        this.adapters = adapters ?? throw new ArgumentNullException(nameof(adapters));
        this.availability = availability ?? throw new ArgumentNullException(nameof(availability));
        this.utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
    }

    public AdvisoryAiAvailability Availability => availability;

    /// <summary>Reads what the AI section shows. Makes no credential access, so ordinary start-up never probes the store.</summary>
    public async Task<AdvisoryAiSetupSnapshot> LoadAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = utcNow();
        AdvisoryAiSettings current = await settings.GetSettingsAsync(cancellationToken).ConfigureAwait(false);
        AdvisoryAiAccountingState accounting = await ledger.GetAccountingStateAsync(cancellationToken).ConfigureAwait(false);
        AdvisoryAiTariff? tariff = null;
        AdvisoryAiSpendingSummary? spending = null;
        if (availability.Model is AdvisoryAiModelProfile model)
        {
            tariff = await settings.GetLatestTariffAsync(model.ModelId, model.ServiceTier, cancellationToken).ConfigureAwait(false);
            spending = await ledger.GetSummaryAsync(AdvisoryAiPeriod.Of(now), cancellationToken).ConfigureAwait(false);
        }
        return new(availability, current, accounting, tariff, spending, now);
    }

    /// <summary>
    /// ADR-028 Q8 and ADR-029: connects once the owner has read the disclosure and typed a key. At a first connection on this app
    /// state the prior-use answer is required and is recorded first, as the owner's declaration; a key already saved under PC
    /// Steward's name (or one whose presence can't be checked) records yes/unsure instead, because it may be earlier use. Then the
    /// key is saved, the key generation raised and consent recorded. If either record fails the key is removed again.
    /// </summary>
    public async Task<AdvisoryAiConnectResult> ConnectAsync(AdvisoryAiApiKey key, AdvisoryAiPriorUseAnswer? priorUse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (!availability.CanSetUp || !adapters.TryGet(availability.Model!.Provider, out IAdvisoryAiCredentialStore? credentials, out _))
            return new(AdvisoryAiConnectOutcome.NotAvailable, false, null);
        if ((await settings.GetSettingsAsync(cancellationToken).ConfigureAwait(false)).Connected)
            return new(AdvisoryAiConnectOutcome.AlreadyConnected, false, null);

        bool existingKey = false;
        AdvisoryAiAccountingState accounting = await ledger.GetAccountingStateAsync(cancellationToken).ConfigureAwait(false);
        if (accounting.Continuity == AdvisoryAiContinuity.NotEstablished)
        {
            if (priorUse is null) return new(AdvisoryAiConnectOutcome.NeedsPriorUseAnswer, false, null);
            existingKey = await credentials.CheckPresenceAsync(cancellationToken).ConfigureAwait(false) != AdvisoryAiCredentialPresence.Absent;
            AdvisoryAiContinuity answer = existingKey || priorUse == AdvisoryAiPriorUseAnswer.YesOrUnsure ? AdvisoryAiContinuity.PriorUseUnknown : AdvisoryAiContinuity.DeclaredNoPriorUse;
            if (await ledger.DeclareContinuityAsync(answer, utcNow(), cancellationToken).ConfigureAwait(false) == AdvisoryAiAccountingWrite.StorageFailed)
                return new(AdvisoryAiConnectOutcome.NotRecorded, existingKey, null);
        }

        // From the save on, nothing may leave a key behind that AI is not connected with: a refused record and an exception
        // (cancellation included) both take the key out again, with a token that can't be cancelled.
        try
        {
            if (await credentials.SaveAsync(key, cancellationToken).ConfigureAwait(false) != AdvisoryAiCredentialWrite.Saved)
                return new(AdvisoryAiConnectOutcome.KeyNotSaved, existingKey, await RollBackAsync(credentials, connectedMayBeRecorded: false).ConfigureAwait(false));
            if (await settings.AdvanceCredentialGenerationAsync(cancellationToken).ConfigureAwait(false) != AdvisoryAiSettingsWrite.Saved ||
                await settings.SetConnectedAsync(true, utcNow(), cancellationToken).ConfigureAwait(false) != AdvisoryAiSettingsWrite.Saved)
                return new(AdvisoryAiConnectOutcome.NotRecorded, existingKey, await RollBackAsync(credentials, connectedMayBeRecorded: false).ConfigureAwait(false));
        }
        catch
        {
            _ = await RollBackAsync(credentials, connectedMayBeRecorded: true).ConfigureAwait(false);
            throw;
        }
        return new(AdvisoryAiConnectOutcome.Connected, existingKey, null);
    }

    /// <summary>
    /// ADR-028 "Disconnect": first durably turns AI off (raising the consent version, so every approval and draft is stale and no new
    /// dispatch claim can be made), then lets the desktop app stop the AI work it is running (<paramref name="stopActiveWork"/>; a
    /// request already past its dispatch claim keeps its whole reservation, ADR-029), then removes every composed provider's exact
    /// key and verifies it is gone, then raises the key generation. A failed record still removes the keys, because a missing key
    /// also stops every send. Nothing is revoked at the provider and nothing is sent. Disconnect is not cancellable: it always runs to
    /// the end, and an exception from <paramref name="stopActiveWork"/> still removes the keys before it is rethrown.
    /// </summary>
    public async Task<AdvisoryAiDisconnectResult> DisconnectAsync(Func<Task>? stopActiveWork, CancellationToken cancellationToken)
    {
        bool recorded = await settings.SetConnectedAsync(false, utcNow(), CancellationToken.None).ConfigureAwait(false) == AdvisoryAiSettingsWrite.Saved;
        AdvisoryAiCredentialRemoval removal;
        // Once disconnect has started it runs to the end: cancelling it must not leave the key behind.
        try
        {
            if (stopActiveWork is not null) await stopActiveWork().ConfigureAwait(false);
        }
        finally
        {
            removal = await RemoveKeysAsync(CancellationToken.None).ConfigureAwait(false);
            _ = await settings.AdvanceCredentialGenerationAsync(CancellationToken.None).ConfigureAwait(false);
        }
        return new(recorded, removal);
    }

    /// <summary>The bounded retry after "credential removal incomplete": removes and verifies again, and raises the key generation.</summary>
    public async Task<AdvisoryAiCredentialRemoval> RetryKeyRemovalAsync(CancellationToken cancellationToken)
    {
        AdvisoryAiCredentialRemoval removal = await RemoveKeysAsync(cancellationToken).ConfigureAwait(false);
        _ = await settings.AdvanceCredentialGenerationAsync(cancellationToken).ConfigureAwait(false);
        return removal;
    }

    /// <summary>
    /// ADR-028 Q8: replaces the key under the same exact name. The key generation is raised before the save, so no approval made with
    /// the old key survives, and again after it. Spending already recorded is not refunded or reset.
    /// </summary>
    public async Task<AdvisoryAiKeyReplaceOutcome> ReplaceKeyAsync(AdvisoryAiApiKey key, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (!availability.CanSetUp || !adapters.TryGet(availability.Model!.Provider, out IAdvisoryAiCredentialStore? credentials, out _))
            return AdvisoryAiKeyReplaceOutcome.NotAvailable;
        if (!(await settings.GetSettingsAsync(cancellationToken).ConfigureAwait(false)).Connected) return AdvisoryAiKeyReplaceOutcome.NotConnected;
        if (await settings.AdvanceCredentialGenerationAsync(cancellationToken).ConfigureAwait(false) != AdvisoryAiSettingsWrite.Saved)
            return AdvisoryAiKeyReplaceOutcome.NotRecorded;
        AdvisoryAiCredentialWrite write = await credentials.SaveAsync(key, cancellationToken).ConfigureAwait(false);
        _ = await settings.AdvanceCredentialGenerationAsync(cancellationToken).ConfigureAwait(false);
        return write == AdvisoryAiCredentialWrite.Saved ? AdvisoryAiKeyReplaceOutcome.Replaced : AdvisoryAiKeyReplaceOutcome.KeyNotSaved;
    }

    /// <summary>FR-BUD-01: checks the form and saves it over the settings version the owner was shown. Recorded spending is untouched.</summary>
    public async Task<AdvisoryAiLimitsCheck> SaveLimitsAsync(AdvisoryAiLimitsDraft draft, long shownSettingsVersion, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);
        AdvisoryAiLimitsCheck check = draft.Check(availability.Model);
        if (check.Limits is null) return check;
        return await settings.SaveLimitsAsync(check.Limits, shownSettingsVersion, cancellationToken).ConfigureAwait(false) switch
        {
            AdvisoryAiSettingsWrite.Saved => check with { Message = AdvisoryAiSetupText.LimitsSaved },
            AdvisoryAiSettingsWrite.VersionConflict => new(null, AdvisoryAiLimitField.None, AdvisoryAiSetupText.LimitsChangedElsewhere),
            AdvisoryAiSettingsWrite.Invalid => new(null, AdvisoryAiLimitField.None, AdvisoryAiLimitValidation.Explain(AdvisoryAiLimitValidation.Validate(check.Limits))),
            _ => new(null, AdvisoryAiLimitField.None, AdvisoryAiSetupText.LimitsNotSaved)
        };
    }

    /// <summary>ADR-029 F6: records the owner's confirmation of today's prices for the build's model, as a new 24-hour version.</summary>
    public async Task<AdvisoryAiPricesCheck> ConfirmPricesAsync(AdvisoryAiPricesDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (availability.Model is not AdvisoryAiModelProfile model) return new(null, null, AdvisoryAiSetupText.NoReviewedModel);
        AdvisoryAiPricesCheck check = draft.Check(model, utcNow());
        if (check.Tariff is null) return check;
        AdvisoryAiTariff? stored = await settings.ConfirmTariffAsync(check.Tariff, cancellationToken).ConfigureAwait(false);
        return stored is null ? new(null, null, AdvisoryAiSetupText.PricesNotSaved) : new(stored, null, AdvisoryAiSetupText.PricesConfirmed);
    }

    /// <summary>ADR-029 Q7: the owner confirms a new UTC month, which after unknown earlier use starts a fresh period for new requests only.</summary>
    public Task<AdvisoryAiAccountingWrite> ConfirmNewMonthAsync(CancellationToken cancellationToken) => ledger.ConfirmNewPeriodAsync(utcNow(), cancellationToken);

    /// <summary>ADR-029 Q7: the owner reviewed a clock that went backwards inside the same month and accepts the current time.</summary>
    public Task<AdvisoryAiAccountingWrite> AcceptClockAsync(CancellationToken cancellationToken) => ledger.AcceptClockAsync(utcNow(), cancellationToken);

    /// <summary>ADR-029: the owner reviewed a recorded charge above its reservation. The recorded amounts stay as they are.</summary>
    public Task<AdvisoryAiAccountingWrite> AcknowledgeReviewAsync(CancellationToken cancellationToken) => ledger.AcknowledgeReviewAsync(cancellationToken);

    /// <summary>
    /// Undoes a connect that did not finish: turns AI off again when the connection may have been recorded, removes and verifies the
    /// key, and raises the key generation. Runs to the end whatever the caller's cancellation says.
    /// </summary>
    private async Task<AdvisoryAiCredentialRemoval> RollBackAsync(IAdvisoryAiCredentialStore credentials, bool connectedMayBeRecorded)
    {
        if (connectedMayBeRecorded) _ = await settings.SetConnectedAsync(false, utcNow(), CancellationToken.None).ConfigureAwait(false);
        AdvisoryAiCredentialRemoval removal = await credentials.RemoveAndVerifyAsync(CancellationToken.None).ConfigureAwait(false);
        _ = await settings.AdvanceCredentialGenerationAsync(CancellationToken.None).ConfigureAwait(false);
        return removal;
    }

    private async Task<AdvisoryAiCredentialRemoval> RemoveKeysAsync(CancellationToken cancellationToken)
    {
        // Without a composed key store PC Steward can't verify anything, so it never says the key was removed.
        AdvisoryAiCredentialRemoval combined = AdvisoryAiCredentialRemoval.Incomplete;
        bool any = false;
        foreach (AdvisoryAiProviderId provider in adapters.Providers.Order())
        {
            if (!adapters.TryGet(provider, out IAdvisoryAiCredentialStore? credentials, out _)) continue;
            AdvisoryAiCredentialRemoval removal = await credentials.RemoveAndVerifyAsync(cancellationToken).ConfigureAwait(false);
            combined = !any ? removal
                : combined == AdvisoryAiCredentialRemoval.Incomplete || removal == AdvisoryAiCredentialRemoval.Incomplete ? AdvisoryAiCredentialRemoval.Incomplete
                : combined == AdvisoryAiCredentialRemoval.Removed || removal == AdvisoryAiCredentialRemoval.Removed ? AdvisoryAiCredentialRemoval.Removed
                : AdvisoryAiCredentialRemoval.AlreadyAbsent;
            any = true;
        }
        return combined;
    }
}
