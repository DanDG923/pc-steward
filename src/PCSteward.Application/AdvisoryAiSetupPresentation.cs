using System.Globalization;
using PCSteward.Domain;

namespace PCSteward.Application;

/// <summary>An action a notice in the AI section offers. Each maps to one deliberate owner action; none sends anything.</summary>
public enum AdvisoryAiNoticeAction
{
    None,
    ConfirmNewMonth,
    AcceptClock,
    AcknowledgeReview,
    RetryKeyRemoval
}

/// <summary>One notice in the AI section, with the one action it offers, if any.</summary>
public sealed record AdvisoryAiNotice(string Text, AdvisoryAiNoticeAction Action, string? ActionLabel);

/// <summary>
/// FR-AI-01/03/04, FR-BUD-01 (Task 67 stage 4): the "AI explanations" section of Settings. <see cref="ShowsSetup"/> is false while
/// this build has no reviewed model, and then only the availability line is shown.
/// </summary>
public sealed record AdvisoryAiSettingsPresentation(
    string AvailabilityTitle,
    string AvailabilityText,
    bool ShowsSetup,
    bool IsConnected,
    string ConnectionTitle,
    string ConnectionText,
    string ConnectionActionLabel,
    string ModelText,
    IReadOnlyList<DetailFact> Limits,
    IReadOnlyList<string> LimitsNotes,
    bool PricesCurrent,
    string PricesText,
    IReadOnlyList<DetailFact> Spending,
    string SpendingNote,
    IReadOnlyList<AdvisoryAiNotice> Notices);

/// <summary>
/// FR-AI-01/03 (ADR-028 Q8, "Disconnect" and "Provider storage disclosure"; ADR-029 "missing accounting continuity", attribution and
/// external restores): the fixed words of AI set-up. Provider-specific words come from the provider registry, so a second provider
/// needs no new text here, only its own reviewed disclosure (ADR-028 addendum A1).
/// </summary>
public static class AdvisoryAiSetupText
{
    public const string SectionHeading = "AI explanations";

    public const string SectionDescription = "Optional. Ask an AI service to explain a problem PC Steward recorded. You see and approve exactly what is sent, every time.";

    public const string NoReviewedModel = "This version of PC Steward has no reviewed AI model, so AI explanations can't be set up yet.";

    /// <summary>ADR-028 R5: beside the output limit and again in the request review.</summary>
    public const string OutputDeadlineNote = "A higher output limit doesn't give an answer more than 45 seconds. A longer answer may not finish in time, and a request that times out may still be charged, so its whole reservation is kept.";

    public const string PaidRequestsOffBySettings = "A spending limit is set to 0, so paid requests are off. The other limits are kept.";

    public const string LimitsSaved = "Saved. Requests you approve from now on use these limits. A request you already approved needs a fresh review.";

    public const string LimitsChangedElsewhere = "These limits were changed somewhere else first, so nothing was saved. Check the current limits and try again.";

    public const string LimitsNotSaved = "PC Steward couldn't save the limits. Nothing changed.";

    public const string PricesConfirmed = "Prices confirmed. They last 24 hours.";

    public const string PricesNotSaved = "PC Steward couldn't record the prices. Nothing changed.";

    /// <summary>ADR-029: the first-connection question. The answer is labelled as the owner's own declaration.</summary>
    public const string PriorUseQuestion = "Has PC Steward made AI requests on this PC before? For example, before a reset, a restore or a reinstall.";

    public const string PriorUseNo = "No, never";

    public const string PriorUseYesOrUnsure = "Yes, or I'm not sure";

    public const string PriorUseNote = "Your answer is recorded as your own declaration. If you say yes or aren't sure, paid requests wait until a new month starts, because PC Steward can't know what was spent before.";

    /// <summary>ADR-029 "External file restores": the limitation disclosed in AI set-up.</summary>
    public const string ExternalRestoreLimitation = "PC Steward can't tell when its files are put back from a backup, OneDrive or a system restore. After one, its spending record may be out of date.";

    public const string ReviewBlockNotice = "A recorded charge was more than PC Steward reserved for it, or didn't add up, so paid requests are paused. The recorded amounts stay as they are.";

    public const string ReviewBlockAction = "I've reviewed this";

    public const string ConfirmNewMonthAction = "Confirm new month";

    public const string AcceptClockAction = "Use the current time";

    public const string RetryKeyRemovalAction = "Try again";

    public const string ClockInEarlierMonth = "The PC's clock is set to an earlier month than the last AI request, so paid requests wait until the clock is right. An earlier month is never reopened.";

    public const string DeclaredNoPriorUse = "Spending is counted from your declaration that PC Steward made no AI requests on this PC before.";

    public const string ExistingKeyFound = "PC Steward found a key it had saved before, so earlier AI spending on this PC may be unknown. Paid requests wait until a new month starts.";

    /// <summary>ADR-028 Q8 and "Provider storage disclosure": what the connect dialog says before the key box.</summary>
    public static IReadOnlyList<string> ConnectDisclosure(AdvisoryAiProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return
        [
            $"Nothing is sent until you approve it. Each request shows exactly what goes to {provider.Name} at {provider.Endpoint.Host}, and you can edit or remove any of it first.",
            $"Each request is charged to your own {provider.Name} account. PC Steward reserves the most a request could cost against your limits before sending it.",
            $"PC Steward asks {provider.Name} not to store the answer. That isn't a promise that {provider.Name} keeps nothing: it may keep data for abuse monitoring. Read {provider.Name}'s data controls at {provider.DataControlsPage}.",
            $"Your key is saved in Windows Credential Manager for your Windows account on this PC, as {provider.CredentialTarget}. It is never shown, and saving it makes no request and costs nothing.",
            "Uninstalling PC Steward doesn't remove the key. Disconnect, or use Factory reset, before removing the app. Removing the key here doesn't cancel it at " + provider.Name + ".",
            ExternalRestoreLimitation
        ];
    }

    /// <summary>ADR-028 "Disconnect": disclosed before the owner confirms.</summary>
    public static string DisconnectDisclosure(AdvisoryAiProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return $"Disconnecting turns AI explanations off and removes your key from Credential Manager, then checks it's gone. Saved explanations and the spending record are kept. Your key still works at {provider.Name} until you revoke it there.";
    }

    public static string ConnectResult(AdvisoryAiConnectResult result, AdvisoryAiProvider provider)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(provider);
        string text = result.Outcome switch
        {
            AdvisoryAiConnectOutcome.Connected => $"Connected to {provider.Name}. Nothing has been sent.",
            AdvisoryAiConnectOutcome.NeedsPriorUseAnswer => "Answer the question about earlier AI requests first.",
            AdvisoryAiConnectOutcome.NotAvailable => NoReviewedModel,
            AdvisoryAiConnectOutcome.AlreadyConnected => $"PC Steward is already connected to {provider.Name}.",
            AdvisoryAiConnectOutcome.KeyNotSaved => "Windows didn't save the key, so AI explanations stay off.",
            _ => result.Rollback is AdvisoryAiCredentialRemoval.Removed or AdvisoryAiCredentialRemoval.AlreadyAbsent or null
                ? "PC Steward couldn't record the connection, so AI explanations stay off. Your key isn't kept."
                : $"PC Steward couldn't record the connection, so AI explanations stay off, and it couldn't confirm your key was removed. Disconnect to try again, or remove {provider.CredentialTarget} in Credential Manager."
        };
        return result.ExistingKeyFound ? text + " " + ExistingKeyFound : text;
    }

    public static string DisconnectResult(AdvisoryAiDisconnectResult result, AdvisoryAiProvider provider)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(provider);
        if (result.KeyGone)
            return result.Recorded
                ? $"Disconnected. Your key was removed from Credential Manager. It still works at {provider.Name} until you revoke it there."
                : "Your key was removed from Credential Manager, but PC Steward couldn't record the change. Without a key nothing can be sent.";
        return KeyRemovalIncomplete(provider);
    }

    public static string KeyRemovalIncomplete(AdvisoryAiProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return $"Disconnected, but key removal is incomplete: PC Steward couldn't confirm your key is gone from Credential Manager. Try again, or remove {provider.CredentialTarget} there yourself.";
    }

    public static string ReplaceResult(AdvisoryAiKeyReplaceOutcome outcome) => outcome switch
    {
        AdvisoryAiKeyReplaceOutcome.Replaced => "Key replaced. Requests you approve from now on use the new key. Spending already recorded is kept.",
        AdvisoryAiKeyReplaceOutcome.NotConnected => "Connect first. There's no key to replace.",
        AdvisoryAiKeyReplaceOutcome.NotAvailable => NoReviewedModel,
        AdvisoryAiKeyReplaceOutcome.KeyNotSaved => "Windows didn't save the new key. Check the key in Credential Manager, or replace it again.",
        _ => "PC Steward couldn't record the change, so the key wasn't replaced."
    };
}

/// <summary>
/// FR-AI-01/03/04, FR-BUD-01 (Task 67 stage 4): builds the AI section from what <see cref="AdvisoryAiSetupService.LoadAsync"/> read.
/// Presentation only: it reads no key and changes nothing. Amounts are US dollars; months are UTC calendar months (ADR-029 Q7).
/// </summary>
public static class AdvisoryAiSettingsPresenter
{
    /// <summary>The section while only the build is known (no stored state read), as shown when no model is reviewed.</summary>
    public static AdvisoryAiSettingsPresentation PresentAvailability(AdvisoryAiAvailability availability)
    {
        ArgumentNullException.ThrowIfNull(availability);
        (string title, string text) = Availability(availability);
        return new(title, text, false, false, string.Empty, string.Empty, string.Empty, string.Empty, [], [], false, string.Empty, [], string.Empty, []);
    }

    /// <param name="snapshot">What the service read.</param>
    /// <param name="keyRemovalIncomplete">True after a disconnect or retry in this session that could not verify the key is gone.</param>
    /// <param name="culture">The culture for numbers and dates; the user's own when null.</param>
    /// <param name="zone">The time zone for dates; the PC's own when null.</param>
    public static AdvisoryAiSettingsPresentation Present(AdvisoryAiSetupSnapshot snapshot, bool keyRemovalIncomplete, CultureInfo? culture = null, TimeZoneInfo? zone = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        CultureInfo format = culture ?? CultureInfo.CurrentCulture;
        if (!snapshot.Availability.CanSetUp || snapshot.Availability.Model is not AdvisoryAiModelProfile model || snapshot.Availability.Provider is not AdvisoryAiProvider provider)
            return PresentAvailability(snapshot.Availability);

        (string title, string text) = Availability(snapshot.Availability);
        bool connected = snapshot.Settings.Connected;
        List<AdvisoryAiNotice> notices = [];
        string connectionTitle;
        string connectionText;
        if (connected)
        {
            connectionTitle = $"Connected to {provider.Name}";
            connectionText = "Your key is kept in Windows Credential Manager for your Windows account and is never shown.";
        }
        else if (keyRemovalIncomplete)
        {
            connectionTitle = "Disconnected, key removal incomplete";
            connectionText = AdvisoryAiSetupText.KeyRemovalIncomplete(provider);
            notices.Add(new(connectionText, AdvisoryAiNoticeAction.RetryKeyRemoval, AdvisoryAiSetupText.RetryKeyRemovalAction));
        }
        else
        {
            connectionTitle = "Not connected";
            connectionText = $"Connecting asks for your {provider.Name} API key, and shows what is shared and what it costs first.";
        }

        AdvisoryAiLimits limits = snapshot.Settings.Limits;
        DetailFact[] limitFacts =
        [
            new("Input limit", Tokens(limits.MaximumInputTokens, format)),
            new("Output limit", Tokens(limits.MaximumOutputTokens, format)),
            new("Limit per request", limits.RequestCap.ToDollarString()),
            new("Monthly budget", limits.MonthlyBudget.ToDollarString())
        ];
        List<string> limitNotes = [AdvisoryAiSetupText.OutputDeadlineNote];
        if (limits.PaidRequestsDisabled) limitNotes.Insert(0, AdvisoryAiSetupText.PaidRequestsOffBySettings);

        bool pricesCurrent = snapshot.LatestTariff is AdvisoryAiTariff current && current.IsCurrent(snapshot.ReadAtUtc) && current.Validate(model) == AdvisoryAiTariffProblem.None;
        string prices = Prices(snapshot.LatestTariff, pricesCurrent, provider, format, zone);

        AdvisoryAiPeriod period = AdvisoryAiPeriod.Of(snapshot.ReadAtUtc);
        List<DetailFact> spending = [];
        if (snapshot.Spending is AdvisoryAiSpendingSummary summary)
        {
            string month = period.StartUtc.ToString("MMMM yyyy", format);
            spending.Add(new($"Spent in {month} (UTC)", summary.Settled.ToDollarString()));
            spending.Add(new("Reserved, not settled yet", summary.Outstanding.ToDollarString()));
            if (!summary.UncertainInEarlierMonths.IsZero) spending.Add(new("Uncertain, from earlier months", summary.UncertainInEarlierMonths.ToDollarString()));
            if (snapshot.Availability.Mode == AdvisoryAiPaidRequestMode.FirstLiveTest)
                spending.Add(new("First live test", $"{Sum(summary.TestSettled, summary.TestOutstanding).ToDollarString()} of {AdvisoryAiReservationRule.FirstLiveTestEnvelope.ToDollarString()}"));
        }
        string spendingNote = $"PC Steward counts each request in the UTC month it was approved in, so its totals can differ from {provider.Name}'s invoice. A request whose cost is uncertain keeps its whole reservation.";

        AddAccountingNotices(notices, snapshot, format);
        string modelText = model.ReasoningEffort is string effort
            ? $"Model: {model.ModelId} ({model.ServiceTier} tier, {effort} reasoning)"
            : $"Model: {model.ModelId} ({model.ServiceTier} tier)";
        return new(title, text, true, connected, connectionTitle, connectionText, connected ? "Disconnect…" : $"Connect {provider.Name}…", modelText,
            limitFacts, limitNotes, pricesCurrent, prices, spending, spendingNote, notices);
    }

    private static (string Title, string Text) Availability(AdvisoryAiAvailability availability) => availability.State switch
    {
        AdvisoryAiAvailabilityState.NoReviewedModel => ("Not available in this version",
            "AI explanations need a model that has been checked and reviewed for PC Steward, and this version has none yet. Nothing is sent anywhere, and everything else works without it."),
        AdvisoryAiAvailabilityState.PaidRequestsOffInThisBuild => ("Set-up only in this version",
            "You can connect and preview a request, but this version of PC Steward sends nothing: paid requests stay off until the review is finished."),
        AdvisoryAiAvailabilityState.FirstLiveTestOnly => ("First live test only",
            $"This version allows only the first live test: at most {AdvisoryAiReservationRule.FirstLiveTestEnvelope.ToDollarString()} in total, across every request and restart."),
        _ => ("Off until you connect",
            $"You approve every request before it is sent, and each one is paid from your own {availability.Provider?.Name ?? "provider"} account.")
    };

    private static string Prices(AdvisoryAiTariff? tariff, bool current, AdvisoryAiProvider provider, CultureInfo format, TimeZoneInfo? zone)
    {
        string check = $"Check {provider.PricingPage} and type today's prices before a request.";
        if (tariff is null) return "No prices confirmed yet. " + check;
        if (!current) return $"The prices you confirmed on {DisplayFormat.DateTime(tariff.ConfirmedAtUtc, format, zone)} have run out: confirmed prices last 24 hours. " + check;
        return $"Confirmed {DisplayFormat.DateTime(tariff.ConfirmedAtUtc, format, zone)}, per million tokens: input {tariff.InputPerMillion.ToDollarString()}, " +
            $"cached input {tariff.CachedInputPerMillion.ToDollarString()}, output {tariff.OutputPerMillion.ToDollarString()}. Valid until {DisplayFormat.DateTime(tariff.ExpiresAtUtc, format, zone)}.";
    }

    private static void AddAccountingNotices(List<AdvisoryAiNotice> notices, AdvisoryAiSetupSnapshot snapshot, CultureInfo format)
    {
        AdvisoryAiAccountingState accounting = snapshot.Accounting;
        if (accounting.ReviewBlock || snapshot.Spending?.ReviewBlock == true)
            notices.Add(new(AdvisoryAiSetupText.ReviewBlockNotice, AdvisoryAiNoticeAction.AcknowledgeReview, AdvisoryAiSetupText.ReviewBlockAction));

        switch (accounting.Continuity)
        {
            case AdvisoryAiContinuity.DeclaredNoPriorUse:
                notices.Add(new(AdvisoryAiSetupText.DeclaredNoPriorUse, AdvisoryAiNoticeAction.None, null));
                break;
            case AdvisoryAiContinuity.PriorUseUnknown:
                notices.Add(new(AdvisoryAiReservationRule.Explain(AdvisoryAiReservationProblem.PriorUseUnknown), AdvisoryAiNoticeAction.None, null));
                break;
            case AdvisoryAiContinuity.FreshPeriodConfirmed when accounting.LastAcceptedUtc is DateTimeOffset started:
                notices.Add(new($"Earlier AI use on this PC was unknown, so counting started fresh in {AdvisoryAiPeriod.Of(started).StartUtc.ToString("MMMM yyyy", format)} (UTC). Anything spent before then isn't included.",
                    AdvisoryAiNoticeAction.None, null));
                break;
            default:
                // Not established: the connect flow asks the prior-use question; nothing is counted yet.
                return;
        }

        if (accounting.LastAcceptedUtc is not DateTimeOffset last) return;
        switch (AdvisoryAiClock.Evaluate(last, snapshot.ReadAtUtc))
        {
            case AdvisoryAiClockVerdict.NewPeriodNeedsConfirmation:
                notices.Add(new(AdvisoryAiReservationRule.Explain(AdvisoryAiReservationProblem.NewMonthNeedsConfirmation), AdvisoryAiNoticeAction.ConfirmNewMonth, AdvisoryAiSetupText.ConfirmNewMonthAction));
                break;
            case AdvisoryAiClockVerdict.MovedBackwards when AdvisoryAiPeriod.Of(snapshot.ReadAtUtc) < AdvisoryAiPeriod.Of(last):
                notices.Add(new(AdvisoryAiSetupText.ClockInEarlierMonth, AdvisoryAiNoticeAction.None, null));
                break;
            case AdvisoryAiClockVerdict.MovedBackwards:
                notices.Add(new(AdvisoryAiReservationRule.Explain(AdvisoryAiReservationProblem.ClockMovedBackwards), AdvisoryAiNoticeAction.AcceptClock, AdvisoryAiSetupText.AcceptClockAction));
                break;
        }
    }

    private static string Tokens(int tokens, CultureInfo format) => tokens.ToString("N0", format) + (tokens == 1 ? " token" : " tokens");

    private static MicroUsd Sum(MicroUsd left, MicroUsd right)
    {
        try
        {
            return MicroUsd.Add(left, right);
        }
        catch (OverflowException)
        {
            return new(long.MaxValue);
        }
    }
}
