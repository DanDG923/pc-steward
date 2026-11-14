using System.Globalization;
using Microsoft.Data.Sqlite;
using PCSteward.Application;
using PCSteward.Domain;
using PCSteward.Persistence.Sqlite;

namespace PCSteward.Application.IntegrationTests;

/// <summary>
/// FR-AI-01/03/04, FR-BUD-01 (ADR-028 Q8 and "Disconnect", ADR-029 "missing accounting continuity"; Task 67 stage 4a): what this
/// build offers, the owner's connect, disconnect, key, limits and prices actions over the real SQLite store, and the Settings
/// section's words. The key store is a recording fake and the transport must never be called: no key, network or price is real.
/// </summary>
[TestClass]
public sealed class AdvisoryAiSetupTests
{
    private static readonly AdvisoryAiModelProfile Model = new("synthetic-model-2026-01-01", 1, 128_000, 16_384, 64, true, 64, 65_536, "default");
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 9, 0, 0, TimeSpan.Zero);
    private static readonly AdvisoryAiAvailability SetupOnly = AdvisoryAiAvailability.Evaluate([Model], AdvisoryAiPaidRequestMode.Disabled);
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private static readonly string[] PresenceThenSave = ["presence", "save"];
    private static readonly string[] SaveOnly = ["save"];
    private static readonly string[] DefaultLimitFacts = ["Input limit: 8,192 tokens", "Output limit: 1,024 tokens", "Limit per request: $0.25", "Monthly budget: $200.00"];
    private static readonly string[] EmptySpendingFacts = ["Spent in November 2026 (UTC): $0.00", "Reserved, not settled yet: $0.00"];
    private static readonly AdvisoryAiNoticeAction[] ReviewDeclarationNewMonth = [AdvisoryAiNoticeAction.AcknowledgeReview, AdvisoryAiNoticeAction.None, AdvisoryAiNoticeAction.ConfirmNewMonth];

    // ---- What this build offers --------------------------------------------------------------------------------------------------

    [TestMethod]
    [TestCategory("FR-AI-01")]
    public void ThisBuildHasNoReviewedModelSoNothingCanBeSetUp()
    {
        AdvisoryAiAvailability current = AdvisoryAiAvailability.Current;
        Assert.AreEqual(AdvisoryAiAvailabilityState.NoReviewedModel, current.State);
        Assert.IsFalse(current.CanSetUp);
        Assert.IsNull(current.Model);
        AdvisoryAiSettingsPresentation section = AdvisoryAiSettingsPresenter.PresentAvailability(current);
        Assert.AreEqual("Not available in this version", section.AvailabilityTitle);
        StringAssert.Contains(section.AvailabilityText, "everything else works without it");
        Assert.IsFalse(section.ShowsSetup);
        Assert.AreEqual(0, section.Notices.Count);
    }

    [TestMethod]
    [TestCategory("FR-AI-01")]
    public void AvailabilityFollowsTheReviewedModelAndTheBuildPolicy()
    {
        Assert.AreEqual(AdvisoryAiAvailabilityState.PaidRequestsOffInThisBuild, SetupOnly.State);
        Assert.AreSame(AdvisoryAiProviders.OpenAI, SetupOnly.Provider);
        Assert.AreEqual(AdvisoryAiAvailabilityState.FirstLiveTestOnly, AdvisoryAiAvailability.Evaluate([Model], AdvisoryAiPaidRequestMode.FirstLiveTest).State);
        Assert.AreEqual(AdvisoryAiAvailabilityState.Available, AdvisoryAiAvailability.Evaluate([Model], AdvisoryAiPaidRequestMode.Ordinary).State);
        AdvisoryAiModelProfile incoherent = Model with { ModelId = "has space" };
        Assert.AreEqual(AdvisoryAiAvailabilityState.NoReviewedModel, AdvisoryAiAvailability.Evaluate([incoherent], AdvisoryAiPaidRequestMode.Ordinary).State, "an incoherent entry is never offered");
        Assert.AreSame(Model, AdvisoryAiAvailability.Evaluate([incoherent, Model], AdvisoryAiPaidRequestMode.Ordinary).Model, "one model: the first coherent reviewed entry");
    }

    // ---- Connect -------------------------------------------------------------------------------------------------------------------

    [TestMethod]
    [TestCategory("FR-AI-03")]
    public async Task AFirstConnectionNeedsThePriorUseAnswerAndWritesNothingWithoutIt()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        using AdvisoryAiApiKey key = Key();
        AdvisoryAiConnectResult result = await fixture.Service.ConnectAsync(key, null, CancellationToken.None);
        Assert.AreEqual(AdvisoryAiConnectOutcome.NeedsPriorUseAnswer, result.Outcome);
        Assert.AreEqual(0, fixture.Credentials.Calls.Count, "the key store is not touched before the question is answered");
        Assert.AreEqual(AdvisoryAiSettings.Initial, await fixture.Store.GetSettingsAsync(CancellationToken.None));
        Assert.AreEqual(AdvisoryAiContinuity.NotEstablished, (await fixture.Store.GetAccountingStateAsync(CancellationToken.None)).Continuity);
    }

    [TestMethod]
    [TestCategory("FR-AI-03")]
    public async Task ConnectingRecordsTheDeclarationThenSavesTheKeyThenRecordsConsent()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        using AdvisoryAiApiKey key = Key();
        AdvisoryAiConnectResult result = await fixture.Service.ConnectAsync(key, AdvisoryAiPriorUseAnswer.NoPriorUse, CancellationToken.None);
        Assert.AreEqual(new AdvisoryAiConnectResult(AdvisoryAiConnectOutcome.Connected, false, null), result);
        CollectionAssert.AreEqual(PresenceThenSave, fixture.Credentials.Calls);
        Assert.AreEqual("sk-synthetic-test-key", fixture.Credentials.SavedKey);
        AdvisoryAiSettings settings = await fixture.Store.GetSettingsAsync(CancellationToken.None);
        Assert.IsTrue(settings.Connected);
        Assert.AreEqual(1, settings.ConsentVersion);
        Assert.AreEqual(1, settings.CredentialGeneration);
        AdvisoryAiAccountingState accounting = await fixture.Store.GetAccountingStateAsync(CancellationToken.None);
        Assert.AreEqual(AdvisoryAiContinuity.DeclaredNoPriorUse, accounting.Continuity);
        Assert.AreEqual(Now, accounting.LastAcceptedUtc);
        Assert.AreEqual(0, fixture.Transport.Calls, "connecting makes no request");
        StringAssert.Contains(AdvisoryAiSetupText.ConnectResult(result, AdvisoryAiProviders.OpenAI), "Nothing has been sent");
    }

    [TestMethod]
    [TestCategory("FR-AI-03")]
    [DataRow(AdvisoryAiCredentialPresence.Present)]
    [DataRow(AdvisoryAiCredentialPresence.Unknown)]
    public async Task AKeyAlreadySavedUnderPcStewardsNameAtAFirstConnectionRecordsPriorUseUnknown(AdvisoryAiCredentialPresence presence)
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        fixture.Credentials.Presence = presence;
        using AdvisoryAiApiKey key = Key();
        AdvisoryAiConnectResult result = await fixture.Service.ConnectAsync(key, AdvisoryAiPriorUseAnswer.NoPriorUse, CancellationToken.None);
        Assert.AreEqual(AdvisoryAiConnectOutcome.Connected, result.Outcome);
        Assert.IsTrue(result.ExistingKeyFound);
        AdvisoryAiAccountingState accounting = await fixture.Store.GetAccountingStateAsync(CancellationToken.None);
        Assert.AreEqual(AdvisoryAiContinuity.PriorUseUnknown, accounting.Continuity, "a found key is possible earlier use, never silent no-prior-use");
        Assert.AreEqual(AdvisoryAiPeriod.Of(Now), accounting.UnknownPriorUseSince);
        StringAssert.Contains(AdvisoryAiSetupText.ConnectResult(result, AdvisoryAiProviders.OpenAI), "earlier AI spending on this PC may be unknown");
    }

    [TestMethod]
    [TestCategory("FR-BUD-01")]
    public async Task YesOrUnsureIsRecordedAsPriorUseUnknown()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        using AdvisoryAiApiKey key = Key();
        Assert.AreEqual(AdvisoryAiConnectOutcome.Connected, (await fixture.Service.ConnectAsync(key, AdvisoryAiPriorUseAnswer.YesOrUnsure, CancellationToken.None)).Outcome);
        Assert.AreEqual(AdvisoryAiContinuity.PriorUseUnknown, (await fixture.Store.GetAccountingStateAsync(CancellationToken.None)).Continuity);
    }

    [TestMethod]
    [TestCategory("FR-AI-03")]
    public async Task ReconnectingAfterContinuityIsEstablishedAsksNothingAndChecksNoPresence()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        using AdvisoryAiApiKey key = Key();
        await fixture.Service.ConnectAsync(key, AdvisoryAiPriorUseAnswer.NoPriorUse, CancellationToken.None);
        await fixture.Service.DisconnectAsync(null, CancellationToken.None);
        fixture.Credentials.Calls.Clear();
        fixture.Credentials.Presence = AdvisoryAiCredentialPresence.Present;
        Assert.AreEqual(AdvisoryAiConnectOutcome.Connected, (await fixture.Service.ConnectAsync(key, null, CancellationToken.None)).Outcome);
        CollectionAssert.AreEqual(SaveOnly, fixture.Credentials.Calls);
        Assert.AreEqual(AdvisoryAiContinuity.DeclaredNoPriorUse, (await fixture.Store.GetAccountingStateAsync(CancellationToken.None)).Continuity);
    }

    [TestMethod]
    [TestCategory("FR-AI-03")]
    public async Task AKeyWindowsDidNotSaveLeavesAiOff()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        fixture.Credentials.Write = AdvisoryAiCredentialWrite.Failed;
        using AdvisoryAiApiKey key = Key();
        AdvisoryAiConnectResult result = await fixture.Service.ConnectAsync(key, AdvisoryAiPriorUseAnswer.NoPriorUse, CancellationToken.None);
        Assert.AreEqual(AdvisoryAiConnectOutcome.KeyNotSaved, result.Outcome);
        AdvisoryAiSettings settings = await fixture.Store.GetSettingsAsync(CancellationToken.None);
        Assert.IsFalse(settings.Connected);
        Assert.AreEqual(0, settings.ConsentVersion);
        Assert.AreEqual(AdvisoryAiCredentialRemoval.Removed, result.Rollback, "a save that wasn't confirmed is removed again");
        Assert.IsFalse(result.KeyMayRemain);
        StringAssert.EndsWith(AdvisoryAiSetupText.ConnectResult(result, AdvisoryAiProviders.OpenAI), "The key was removed again.");

        // The native write may have happened even though it wasn't confirmed: an unverified removal is disclosed, never called absent.
        fixture.Credentials.Removal = AdvisoryAiCredentialRemoval.Incomplete;
        AdvisoryAiConnectResult unverified = await fixture.Service.ConnectAsync(key, null, CancellationToken.None);
        Assert.IsTrue(unverified.KeyMayRemain);
        string text = AdvisoryAiSetupText.ConnectResult(unverified, AdvisoryAiProviders.OpenAI);
        StringAssert.Contains(text, "couldn't confirm that no key is left");
        StringAssert.Contains(text, AdvisoryAiProviders.OpenAI.CredentialTarget);
    }

    [TestMethod]
    [TestCategory("FR-AI-03")]
    public void AFailedConnectionClaimsNoKeyIsLeftOnlyWhenThatIsShown()
    {
        AdvisoryAiProvider openAi = AdvisoryAiProviders.OpenAI;
        AdvisoryAiConnectResult nothingWritten = new(AdvisoryAiConnectOutcome.NotRecorded, false, null);
        Assert.IsFalse(nothingWritten.KeyMayRemain);
        StringAssert.EndsWith(AdvisoryAiSetupText.ConnectResult(nothingWritten, openAi), "No key was saved.");

        AdvisoryAiConnectResult earlierKeyKept = new(AdvisoryAiConnectOutcome.NotRecorded, true, null);
        Assert.IsTrue(earlierKeyKept.KeyMayRemain, "a key found before is still there when nothing removed it");
        string text = AdvisoryAiSetupText.ConnectResult(earlierKeyKept, openAi);
        StringAssert.Contains(text, openAi.CredentialTarget);
        Assert.IsFalse(text.Contains("isn't kept", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains(AdvisoryAiSetupText.ExistingKeyFound, StringComparison.Ordinal), "the declaration wasn't recorded, so it isn't described");

        Assert.IsTrue(new AdvisoryAiConnectResult(AdvisoryAiConnectOutcome.NotRecorded, false, AdvisoryAiCredentialRemoval.Incomplete).KeyMayRemain);
        Assert.IsFalse(new AdvisoryAiConnectResult(AdvisoryAiConnectOutcome.NotRecorded, true, AdvisoryAiCredentialRemoval.AlreadyAbsent).KeyMayRemain);
        Assert.IsFalse(new AdvisoryAiConnectResult(AdvisoryAiConnectOutcome.Connected, true, null).KeyMayRemain);
    }

    [TestMethod]
    [TestCategory("FR-AI-03")]
    public async Task CancellingAConnectAfterTheKeyIsSavedTakesTheKeyOutAgain()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        using CancellationTokenSource cancel = new();
        fixture.Credentials.OnSave = async () =>
        {
            await cancel.CancelAsync();
        };
        using AdvisoryAiApiKey key = Key();
        bool threw = false;
        try
        {
            await fixture.Service.ConnectAsync(key, AdvisoryAiPriorUseAnswer.NoPriorUse, cancel.Token);
        }
        catch (OperationCanceledException)
        {
            threw = true;
        }
        Assert.IsTrue(threw, "the cancellation still reaches the caller");
        Assert.IsNull(fixture.Credentials.SavedKey, "no key is left behind that AI is not connected with");
        CollectionAssert.Contains(fixture.Credentials.Calls, "remove");
        Assert.IsFalse((await fixture.Store.GetSettingsAsync(CancellationToken.None)).Connected);
    }

    [TestMethod]
    [TestCategory("FR-AI-03")]
    public async Task DisconnectRunsToTheEndEvenWhenCancelled()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        using AdvisoryAiApiKey key = Key();
        await fixture.Service.ConnectAsync(key, AdvisoryAiPriorUseAnswer.NoPriorUse, CancellationToken.None);
        using CancellationTokenSource cancel = new();
        await cancel.CancelAsync();
        AdvisoryAiDisconnectResult result = await fixture.Service.DisconnectAsync(null, cancel.Token);
        Assert.AreEqual(new AdvisoryAiDisconnectResult(true, AdvisoryAiCredentialRemoval.Removed), result);
        Assert.IsNull(fixture.Credentials.SavedKey);

        await fixture.Service.ConnectAsync(key, null, CancellationToken.None);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.Service.DisconnectAsync(() => throw new InvalidOperationException("stop failed"), CancellationToken.None));
        Assert.IsNull(fixture.Credentials.SavedKey, "a failure while stopping work still removes the key");
        Assert.IsFalse((await fixture.Store.GetSettingsAsync(CancellationToken.None)).Connected);
    }

    [TestMethod]
    [TestCategory("FR-AI-03")]
    public void ADisconnectThatRecordedNothingAndRemovedNothingIsNotCalledDisconnected()
    {
        string text = AdvisoryAiSetupText.DisconnectResult(new(false, AdvisoryAiCredentialRemoval.Incomplete), AdvisoryAiProviders.OpenAI);
        StringAssert.StartsWith(text, "PC Steward couldn't turn AI explanations off");
        Assert.IsFalse(text.Contains("Disconnected", StringComparison.Ordinal));
        StringAssert.StartsWith(AdvisoryAiSetupText.DisconnectResult(new(true, AdvisoryAiCredentialRemoval.Incomplete), AdvisoryAiProviders.OpenAI), "Disconnected, but key removal is incomplete");
        StringAssert.StartsWith(AdvisoryAiSetupText.DisconnectResult(new(false, AdvisoryAiCredentialRemoval.Removed), AdvisoryAiProviders.OpenAI), "Your key was removed");
    }

    [TestMethod]
    [TestCategory("FR-AI-03")]
    public async Task ConnectIsRefusedWhenAlreadyConnectedOrWhenNothingIsReviewed()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        using AdvisoryAiApiKey key = Key();
        await fixture.Service.ConnectAsync(key, AdvisoryAiPriorUseAnswer.NoPriorUse, CancellationToken.None);
        fixture.Credentials.Calls.Clear();
        Assert.AreEqual(AdvisoryAiConnectOutcome.AlreadyConnected, (await fixture.Service.ConnectAsync(key, null, CancellationToken.None)).Outcome);
        Assert.AreEqual(0, fixture.Credentials.Calls.Count);

        AdvisoryAiSetupService none = fixture.ServiceFor(AdvisoryAiAvailability.Current);
        Assert.AreEqual(AdvisoryAiConnectOutcome.NotAvailable, (await none.ConnectAsync(key, AdvisoryAiPriorUseAnswer.NoPriorUse, CancellationToken.None)).Outcome);
        Assert.AreEqual(AdvisoryAiKeyReplaceOutcome.NotAvailable, await none.ReplaceKeyAsync(key, CancellationToken.None));
        Assert.AreEqual(0, fixture.Credentials.Calls.Count, "this build's service never touches a key store");
    }

    // ---- Disconnect and key removal ------------------------------------------------------------------------------------------------

    [TestMethod]
    [TestCategory("FR-AI-03")]
    public async Task DisconnectTurnsAiOffBeforeStoppingWorkAndBeforeRemovingTheKey()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        using AdvisoryAiApiKey key = Key();
        await fixture.Service.ConnectAsync(key, AdvisoryAiPriorUseAnswer.NoPriorUse, CancellationToken.None);
        AdvisoryAiSettings before = await fixture.Store.GetSettingsAsync(CancellationToken.None);
        bool? connectedWhenStopping = null;
        bool? connectedWhenRemoving = null;
        fixture.Credentials.OnRemove = async () => connectedWhenRemoving = (await fixture.Store.GetSettingsAsync(CancellationToken.None)).Connected;

        AdvisoryAiDisconnectResult result = await fixture.Service.DisconnectAsync(async () => connectedWhenStopping = (await fixture.Store.GetSettingsAsync(CancellationToken.None)).Connected, CancellationToken.None);

        Assert.AreEqual(new AdvisoryAiDisconnectResult(true, AdvisoryAiCredentialRemoval.Removed), result);
        Assert.IsTrue(result.KeyGone);
        Assert.IsFalse(connectedWhenStopping!.Value, "AI is durably off before active work is stopped");
        Assert.IsFalse(connectedWhenRemoving!.Value, "AI is durably off before the key is removed");
        AdvisoryAiSettings after = await fixture.Store.GetSettingsAsync(CancellationToken.None);
        Assert.IsFalse(after.Connected);
        Assert.AreEqual(before.ConsentVersion + 1, after.ConsentVersion);
        Assert.AreEqual(before.CredentialGeneration + 1, after.CredentialGeneration);
        Assert.AreEqual(0, fixture.Transport.Calls);
        StringAssert.Contains(AdvisoryAiSetupText.DisconnectResult(result, AdvisoryAiProviders.OpenAI), "still works at OpenAI until you revoke it there");
    }

    [TestMethod]
    [TestCategory("FR-AI-03")]
    public async Task AnUnverifiedRemovalIsNeverCalledRemovedAndCanBeRetried()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        using AdvisoryAiApiKey key = Key();
        await fixture.Service.ConnectAsync(key, AdvisoryAiPriorUseAnswer.NoPriorUse, CancellationToken.None);
        fixture.Credentials.Removal = AdvisoryAiCredentialRemoval.Incomplete;
        AdvisoryAiDisconnectResult result = await fixture.Service.DisconnectAsync(null, CancellationToken.None);
        Assert.IsTrue(result.Recorded);
        Assert.IsFalse(result.KeyGone);
        string text = AdvisoryAiSetupText.DisconnectResult(result, AdvisoryAiProviders.OpenAI);
        StringAssert.Contains(text, "key removal is incomplete");
        StringAssert.Contains(text, AdvisoryAiProviders.OpenAI.CredentialTarget);
        Assert.IsFalse(text.Contains("was removed", StringComparison.Ordinal));

        AdvisoryAiSettingsPresentation section = AdvisoryAiSettingsPresenter.Present(await fixture.Service.LoadAsync(CancellationToken.None), keyRemovalIncomplete: true, Invariant, TimeZoneInfo.Utc);
        Assert.AreEqual("Disconnected, key removal incomplete", section.ConnectionTitle);
        Assert.AreEqual(AdvisoryAiNoticeAction.RetryKeyRemoval, section.Notices[0].Action);

        fixture.Credentials.Removal = AdvisoryAiCredentialRemoval.AlreadyAbsent;
        long generation = (await fixture.Store.GetSettingsAsync(CancellationToken.None)).CredentialGeneration;
        Assert.AreEqual(AdvisoryAiCredentialRemoval.AlreadyAbsent, await fixture.Service.RetryKeyRemovalAsync(CancellationToken.None));
        Assert.AreEqual(generation + 1, (await fixture.Store.GetSettingsAsync(CancellationToken.None)).CredentialGeneration);
    }

    [TestMethod]
    [TestCategory("FR-AI-03")]
    public async Task WithNoComposedKeyStoreDisconnectNeverClaimsTheKeyIsGone()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        AdvisoryAiSetupService bare = new(fixture.Store, fixture.Store, new AdvisoryAiProviderAdapters(), SetupOnly, () => Now);
        AdvisoryAiDisconnectResult result = await bare.DisconnectAsync(null, CancellationToken.None);
        Assert.AreEqual(AdvisoryAiCredentialRemoval.Incomplete, result.Removal);
        Assert.IsFalse(result.KeyGone);
    }

    [TestMethod]
    [TestCategory("FR-AI-03")]
    public async Task ReplacingTheKeyInvalidatesApprovalsBeforeAndAfterTheSave()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        using AdvisoryAiApiKey key = Key();
        Assert.AreEqual(AdvisoryAiKeyReplaceOutcome.NotConnected, await fixture.Service.ReplaceKeyAsync(key, CancellationToken.None));
        await fixture.Service.ConnectAsync(key, AdvisoryAiPriorUseAnswer.NoPriorUse, CancellationToken.None);
        long generation = (await fixture.Store.GetSettingsAsync(CancellationToken.None)).CredentialGeneration;
        long? generationAtSave = null;
        fixture.Credentials.OnSave = async () => generationAtSave = (await fixture.Store.GetSettingsAsync(CancellationToken.None)).CredentialGeneration;

        Assert.AreEqual(AdvisoryAiKeyReplaceOutcome.Replaced, await fixture.Service.ReplaceKeyAsync(key, CancellationToken.None));
        Assert.AreEqual(generation + 1, generationAtSave, "an approval made with the old key is already stale when the new key is written");
        Assert.AreEqual(generation + 2, (await fixture.Store.GetSettingsAsync(CancellationToken.None)).CredentialGeneration);

        fixture.Credentials.Write = AdvisoryAiCredentialWrite.Failed;
        Assert.AreEqual(AdvisoryAiKeyReplaceOutcome.KeyNotSaved, await fixture.Service.ReplaceKeyAsync(key, CancellationToken.None));
        Assert.AreEqual(generation + 4, (await fixture.Store.GetSettingsAsync(CancellationToken.None)).CredentialGeneration);
        Assert.IsTrue((await fixture.Store.GetSettingsAsync(CancellationToken.None)).Connected);
    }

    // ---- Limits --------------------------------------------------------------------------------------------------------------------

    [TestMethod]
    [TestCategory("FR-BUD-01")]
    public void TheLimitsFormStartsFromTheSavedLimitsAndParsesExactly()
    {
        AdvisoryAiLimitsDraft draft = AdvisoryAiLimitsDraft.From(AdvisoryAiLimits.Defaults);
        Assert.AreEqual(new AdvisoryAiLimitsDraft("8192", "1024", "0.25", "200.00"), draft);
        Assert.AreEqual(AdvisoryAiLimits.Defaults, draft.Check(Model).Limits);
        Assert.AreEqual(AdvisoryAiLimits.Defaults, (draft with { RequestCap = " $0.25 " }).Check(Model).Limits, "spaces and one leading $ are dropped");

        Assert.AreEqual(AdvisoryAiLimitField.InputTokens, (draft with { InputTokens = "-1" }).Check(Model).Field);
        Assert.AreEqual(AdvisoryAiLimitField.InputTokens, (draft with { InputTokens = "8,192" }).Check(Model).Field);
        Assert.AreEqual(AdvisoryAiLimitField.OutputTokens, (draft with { OutputTokens = "1024.5" }).Check(Model).Field);
        Assert.AreEqual(AdvisoryAiLimitField.OutputTokens, (draft with { OutputTokens = "9999999999" }).Check(Model).Field);
        Assert.AreEqual(AdvisoryAiLimitField.RequestCap, (draft with { RequestCap = "0.1234567" }).Check(Model).Field, "never rounded");
        Assert.AreEqual(AdvisoryAiLimitField.MonthlyBudget, (draft with { MonthlyBudget = "£200" }).Check(Model).Field);
        Assert.IsNull((draft with { MonthlyBudget = "$$200" }).Check(Model).Limits);

        AdvisoryAiLimitsCheck capAboveMonth = (draft with { RequestCap = "300" }).Check(Model);
        Assert.AreEqual(AdvisoryAiLimitField.RequestCap, capAboveMonth.Field);
        Assert.AreEqual(AdvisoryAiLimitValidation.Explain(AdvisoryAiLimitProblem.RequestCapAboveMonthlyBudget), capAboveMonth.Message);
        Assert.AreEqual(AdvisoryAiLimitField.InputTokens, (draft with { InputTokens = "500" }).Check(Model).Field, "too small for the fixed instructions");
        Assert.AreEqual(AdvisoryAiLimitField.OutputTokens, (draft with { OutputTokens = "20000" }).Check(Model).Field, "above the model's output maximum");
        Assert.IsNotNull((draft with { RequestCap = "0" }).Check(Model).Limits, "zero turns paid requests off and is allowed");
    }

    [TestMethod]
    [TestCategory("FR-BUD-01")]
    public async Task LimitsSaveOverTheVersionTheOwnerWasShown()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        AdvisoryAiLimitsDraft draft = AdvisoryAiLimitsDraft.From(AdvisoryAiLimits.Defaults) with { MonthlyBudget = "50" };
        AdvisoryAiLimitsCheck saved = await fixture.Service.SaveLimitsAsync(draft, 0, CancellationToken.None);
        Assert.AreEqual(AdvisoryAiSetupText.LimitsSaved, saved.Message);
        Assert.AreEqual(new MicroUsd(50_000_000), (await fixture.Store.GetSettingsAsync(CancellationToken.None)).Limits.MonthlyBudget);
        AdvisoryAiLimitsCheck stale = await fixture.Service.SaveLimitsAsync(draft with { MonthlyBudget = "60" }, 0, CancellationToken.None);
        Assert.IsNull(stale.Limits);
        Assert.AreEqual(AdvisoryAiSetupText.LimitsChangedElsewhere, stale.Message);
        AdvisoryAiLimitsCheck invalid = await fixture.Service.SaveLimitsAsync(draft with { InputTokens = "x" }, 1, CancellationToken.None);
        Assert.AreEqual(AdvisoryAiLimitField.InputTokens, invalid.Field);
        Assert.AreEqual(1, (await fixture.Store.GetSettingsAsync(CancellationToken.None)).SettingsVersion, "a refused form writes nothing");
    }

    // ---- Prices --------------------------------------------------------------------------------------------------------------------

    [TestMethod]
    [TestCategory("FR-BUD-01")]
    public async Task PricesAreTypedFreshAndConfirmedForTheBuildsModelForTwentyFourHours()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        Assert.AreEqual(AdvisoryAiPricesDraft.Empty, new AdvisoryAiPricesDraft("", "", ""));
        Assert.AreEqual(nameof(AdvisoryAiPricesDraft.InputPerMillion), (await fixture.Service.ConfirmPricesAsync(AdvisoryAiPricesDraft.Empty, CancellationToken.None)).Field);
        Assert.AreEqual(nameof(AdvisoryAiPricesDraft.CachedInputPerMillion), (await fixture.Service.ConfirmPricesAsync(new("2", "0", "10"), CancellationToken.None)).Field, "a free rate is never accepted");
        Assert.AreEqual(nameof(AdvisoryAiPricesDraft.OutputPerMillion), (await fixture.Service.ConfirmPricesAsync(new("2", "0.1", "ten"), CancellationToken.None)).Field);
        Assert.IsNull(await fixture.Store.GetLatestTariffAsync(Model.ModelId, Model.ServiceTier, CancellationToken.None), "a refused form records nothing");

        AdvisoryAiPricesCheck confirmed = await fixture.Service.ConfirmPricesAsync(new("$2.00", "0.10", "10"), CancellationToken.None);
        Assert.AreEqual(AdvisoryAiSetupText.PricesConfirmed, confirmed.Message);
        Assert.AreEqual(new AdvisoryAiTariff(1, Model.ModelId, Model.ServiceTier, new(2_000_000), new(100_000), new(10_000_000), Now), confirmed.Tariff);

        AdvisoryAiSettingsPresentation fresh = AdvisoryAiSettingsPresenter.Present(await fixture.Service.LoadAsync(CancellationToken.None), false, Invariant, TimeZoneInfo.Utc);
        Assert.IsTrue(fresh.PricesCurrent);
        Assert.AreEqual("Confirmed 13 Nov 2026 · 09:00, per million tokens: input $2.00, cached input $0.10, output $10.00. Valid until 14 Nov 2026 · 09:00.", fresh.PricesText);

        AdvisoryAiSettingsPresentation later = AdvisoryAiSettingsPresenter.Present(await fixture.ServiceFor(SetupOnly, Now.AddHours(24)).LoadAsync(CancellationToken.None), false, Invariant, TimeZoneInfo.Utc);
        Assert.IsFalse(later.PricesCurrent);
        StringAssert.StartsWith(later.PricesText, "The prices you confirmed on 13 Nov 2026 · 09:00 have run out");
        StringAssert.Contains(later.PricesText, AdvisoryAiProviders.OpenAI.PricingPage.ToString());
    }

    // ---- The Settings section -----------------------------------------------------------------------------------------------------

    [TestMethod]
    [TestCategory("FR-AI-01")]
    public async Task TheSectionShowsConnectionLimitsSpendingAndTheDeadlineNote()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        AdvisoryAiSettingsPresentation disconnected = AdvisoryAiSettingsPresenter.Present(await fixture.Service.LoadAsync(CancellationToken.None), false, Invariant, TimeZoneInfo.Utc);
        Assert.IsTrue(disconnected.ShowsSetup);
        Assert.AreEqual("Set-up only in this version", disconnected.AvailabilityTitle);
        Assert.AreEqual("Not connected", disconnected.ConnectionTitle);
        Assert.AreEqual("Connect OpenAI…", disconnected.ConnectionActionLabel);
        Assert.AreEqual("No prices confirmed yet. Check https://developers.openai.com/api/docs/pricing and type today's prices before a request.", disconnected.PricesText);
        Assert.AreEqual(0, disconnected.Notices.Count, "nothing is counted before the first connection");

        using AdvisoryAiApiKey key = Key();
        await fixture.Service.ConnectAsync(key, AdvisoryAiPriorUseAnswer.NoPriorUse, CancellationToken.None);
        AdvisoryAiSettingsPresentation connected = AdvisoryAiSettingsPresenter.Present(await fixture.Service.LoadAsync(CancellationToken.None), false, Invariant, TimeZoneInfo.Utc);
        Assert.AreEqual("Connected to OpenAI", connected.ConnectionTitle);
        Assert.AreEqual("Disconnect…", connected.ConnectionActionLabel);
        Assert.AreEqual("Model: synthetic-model-2026-01-01 (default tier)", connected.ModelText);
        CollectionAssert.AreEqual(DefaultLimitFacts, connected.Limits.Select(fact => $"{fact.Label}: {fact.Value}").ToArray());
        CollectionAssert.AreEqual(new[] { AdvisoryAiSetupText.OutputDeadlineNote }, connected.LimitsNotes.ToArray());
        CollectionAssert.AreEqual(EmptySpendingFacts, connected.Spending.Select(fact => $"{fact.Label}: {fact.Value}").ToArray());
        StringAssert.Contains(connected.SpendingNote, "can differ from OpenAI's invoice");
        Assert.AreEqual(AdvisoryAiSetupText.DeclaredNoPriorUse, connected.Notices.Single().Text);
    }

    [TestMethod]
    [TestCategory("FR-BUD-01")]
    public void NoticesOfferOnlyTheActionThatApplies()
    {
        AdvisoryAiSetupSnapshot snapshot = Snapshot(new(AdvisoryAiContinuity.DeclaredNoPriorUse, Now, null, ReviewBlock: true), Now.AddDays(20));
        AdvisoryAiSettingsPresentation section = AdvisoryAiSettingsPresenter.Present(snapshot, false, Invariant, TimeZoneInfo.Utc);
        CollectionAssert.AreEqual(ReviewDeclarationNewMonth, section.Notices.Select(notice => notice.Action).ToArray(), "a review block, the declaration and a new UTC month");

        AdvisoryAiSettingsPresentation sameMonthBackwards = AdvisoryAiSettingsPresenter.Present(Snapshot(new(AdvisoryAiContinuity.DeclaredNoPriorUse, Now, null, false), Now.AddHours(-2)), false, Invariant, TimeZoneInfo.Utc);
        Assert.AreEqual(AdvisoryAiNoticeAction.AcceptClock, sameMonthBackwards.Notices[^1].Action);

        AdvisoryAiSettingsPresentation earlierMonth = AdvisoryAiSettingsPresenter.Present(Snapshot(new(AdvisoryAiContinuity.DeclaredNoPriorUse, Now, null, false), Now.AddDays(-20)), false, Invariant, TimeZoneInfo.Utc);
        Assert.AreEqual(new AdvisoryAiNotice(AdvisoryAiSetupText.ClockInEarlierMonth, AdvisoryAiNoticeAction.None, null), earlierMonth.Notices[^1], "an earlier month is never reopened");

        AdvisoryAiSettingsPresentation unknown = AdvisoryAiSettingsPresenter.Present(Snapshot(new(AdvisoryAiContinuity.PriorUseUnknown, Now, AdvisoryAiPeriod.Of(Now), false), Now), false, Invariant, TimeZoneInfo.Utc);
        Assert.AreEqual(AdvisoryAiReservationRule.Explain(AdvisoryAiReservationProblem.PriorUseUnknown), unknown.Notices.Single().Text);

        AdvisoryAiSettingsPresentation fresh = AdvisoryAiSettingsPresenter.Present(Snapshot(new(AdvisoryAiContinuity.FreshPeriodConfirmed, Now.AddDays(20), AdvisoryAiPeriod.Of(Now), false), Now.AddDays(20)), false, Invariant, TimeZoneInfo.Utc);
        StringAssert.Contains(fresh.Notices.Single().Text, "counting started fresh in December 2026 (UTC)");

        AdvisoryAiSettingsPresentation test = AdvisoryAiSettingsPresenter.Present(
            Snapshot(new(AdvisoryAiContinuity.DeclaredNoPriorUse, Now, null, false), Now, AdvisoryAiPaidRequestMode.FirstLiveTest), false, Invariant, TimeZoneInfo.Utc);
        Assert.AreEqual("First live test only", test.AvailabilityTitle);
        Assert.AreEqual("$0.30 of $1.00", test.Spending.Single(fact => fact.Label == "First live test").Value);

        AdvisoryAiSettingsPresentation off = AdvisoryAiSettingsPresenter.Present(
            Snapshot(new(AdvisoryAiContinuity.DeclaredNoPriorUse, Now, null, false), Now) with { Settings = AdvisoryAiSettings.Initial with { Limits = AdvisoryAiLimits.Defaults with { MonthlyBudget = MicroUsd.Zero } } },
            false, Invariant, TimeZoneInfo.Utc);
        Assert.AreEqual(AdvisoryAiSetupText.PaidRequestsOffBySettings, off.LimitsNotes[0]);
    }

    [TestMethod]
    [TestCategory("FR-AI-03")]
    public void TheConnectDisclosureNamesTheRouteTheKeyEntryRetentionAndItsLimits()
    {
        string disclosure = string.Join(" ", AdvisoryAiSetupText.ConnectDisclosure(AdvisoryAiProviders.OpenAI));
        StringAssert.Contains(disclosure, "Nothing is sent until you approve it");
        StringAssert.Contains(disclosure, AdvisoryAiProviders.OpenAI.Endpoint.Host);
        StringAssert.Contains(disclosure, AdvisoryAiProviders.OpenAI.CredentialTarget);
        StringAssert.Contains(disclosure, AdvisoryAiProviders.OpenAI.DataControlsPage.ToString());
        StringAssert.Contains(disclosure, "isn't a promise that OpenAI keeps nothing");
        StringAssert.Contains(disclosure, "Uninstalling PC Steward doesn't remove the key");
        StringAssert.Contains(disclosure, "saving it makes no request and costs nothing");
        StringAssert.Contains(disclosure, AdvisoryAiSetupText.ExternalRestoreLimitation);
        StringAssert.Contains(AdvisoryAiSetupText.DisconnectDisclosure(AdvisoryAiProviders.OpenAI), "Saved explanations and the spending record are kept");
        StringAssert.Contains(AdvisoryAiSetupText.PriorUseNote, "your own declaration");
    }

    [TestMethod]
    [TestCategory("FR-AI-01")]
    public void SettingsListsTheAiSectionLast()
    {
        Assert.AreEqual(AdvisoryAiSetupText.SectionHeading, SettingsPresenter.Sections[^1]);
    }

    // ---- Helpers -------------------------------------------------------------------------------------------------------------------

    private static AdvisoryAiApiKey Key()
    {
        Assert.AreEqual(AdvisoryAiKeyProblem.None, AdvisoryAiApiKey.TryCreate("sk-synthetic-test-key", out AdvisoryAiApiKey? key));
        return key!;
    }

    private static AdvisoryAiSetupSnapshot Snapshot(AdvisoryAiAccountingState accounting, DateTimeOffset readAt, AdvisoryAiPaidRequestMode mode = AdvisoryAiPaidRequestMode.Disabled) =>
        new(AdvisoryAiAvailability.Evaluate([Model], mode), AdvisoryAiSettings.Initial with { Connected = true }, accounting, null,
            new(AdvisoryAiPeriod.Of(readAt), MicroUsd.Zero, MicroUsd.Zero, MicroUsd.Zero, new(100_000), new(200_000), 0, accounting.ReviewBlock), readAt);

    private sealed class RecordingCredentials : IAdvisoryAiCredentialStore
    {
        public AdvisoryAiProviderId Provider => AdvisoryAiProviderId.OpenAI;
        public List<string> Calls { get; } = [];
        public AdvisoryAiCredentialPresence Presence { get; set; } = AdvisoryAiCredentialPresence.Absent;
        public AdvisoryAiCredentialWrite Write { get; set; } = AdvisoryAiCredentialWrite.Saved;
        public AdvisoryAiCredentialRemoval Removal { get; set; } = AdvisoryAiCredentialRemoval.Removed;
        public Func<Task>? OnSave { get; set; }
        public Func<Task>? OnRemove { get; set; }
        public string? SavedKey { get; private set; }

        public async Task<AdvisoryAiCredentialWrite> SaveAsync(AdvisoryAiApiKey key, CancellationToken cancellationToken)
        {
            Calls.Add("save");
            if (OnSave is not null) await OnSave();
            if (Write == AdvisoryAiCredentialWrite.Saved) SavedKey = new string(key.Reveal());
            return Write;
        }

        public Task<AdvisoryAiCredentialPresence> CheckPresenceAsync(CancellationToken cancellationToken)
        {
            Calls.Add("presence");
            return Task.FromResult(Presence);
        }

        public Task<AdvisoryAiApiKey?> ReadForDispatchAsync(CancellationToken cancellationToken) => throw new AssertFailedException("set-up never reads the key for a request");

        public async Task<AdvisoryAiCredentialRemoval> RemoveAndVerifyAsync(CancellationToken cancellationToken)
        {
            Calls.Add("remove");
            if (OnRemove is not null) await OnRemove();
            if (Removal != AdvisoryAiCredentialRemoval.Incomplete) SavedKey = null;
            return Removal;
        }
    }

    private sealed class CountingTransport : IAdvisoryAiTransport
    {
        public AdvisoryAiProviderId Provider => AdvisoryAiProviderId.OpenAI;
        public int Calls { get; private set; }

        public Task<AdvisoryAiTransportResult> SendOnceAsync(AdvisoryAiApproval approval, AdvisoryAiApiKey key, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(AdvisoryAiTransportResult.Of(AdvisoryAiTransportOutcome.NotSent));
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string path;

        private Fixture(string path, SqliteDatabase store)
        {
            this.path = path;
            Store = store;
            Service = ServiceFor(SetupOnly);
        }

        public SqliteDatabase Store { get; }
        public RecordingCredentials Credentials { get; } = new();
        public CountingTransport Transport { get; } = new();
        public AdvisoryAiSetupService Service { get; }

        public AdvisoryAiSetupService ServiceFor(AdvisoryAiAvailability availability, DateTimeOffset? now = null) =>
            new(Store, Store, new AdvisoryAiProviderAdapters((Credentials, Transport)), availability, () => now ?? Now);

        public static async Task<Fixture> CreateAsync()
        {
            string path = Path.Combine(Path.GetTempPath(), $"pcsteward-ai-setup-{Guid.NewGuid():N}.db");
            SqliteDatabase store = new(path);
            await store.MigrateAsync(CancellationToken.None);
            return new(path, store);
        }

        public ValueTask DisposeAsync()
        {
            SqliteConnection.ClearAllPools();
            foreach (string suffix in new[] { "", "-wal", "-shm" }) File.Delete(path + suffix);
            return ValueTask.CompletedTask;
        }
    }
}
