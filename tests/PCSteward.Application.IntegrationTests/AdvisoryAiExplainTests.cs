using System.Text;
using Microsoft.Data.Sqlite;
using PCSteward.Application;
using PCSteward.Domain;
using PCSteward.Persistence.Sqlite;

namespace PCSteward.Application.IntegrationTests;

/// <summary>
/// FR-AI-02/04/05/06, FR-BUD-02/03 (ADR-028 consent items 4–5, ADR-029; Task 67 stage 4c): "Explain with AI…" over the real SQLite
/// store. A draft is made only while connected, a review needs today's prices and shows the exact request and its reservation, and
/// only an approved, unchanged review is sent, once. The transport and key store are fakes; no key, network or price is real.
/// </summary>
[TestClass]
public sealed class AdvisoryAiExplainTests
{
    private static readonly AdvisoryAiModelProfile Model = new("synthetic-model-2026-01-01", 1, 128_000, 16_384, 64, true, 64, 65_536, "default");
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 9, 0, 0, TimeSpan.Zero);
    private static readonly AdvisoryAiTariff Rates = new(0, Model.ModelId, Model.ServiceTier, new(2_000_000), new(500_000), new(8_000_000), Now);
    private static readonly AdvisoryAiAvailability Ordinary = AdvisoryAiAvailability.Evaluate([Model], AdvisoryAiPaidRequestMode.Ordinary);
    private const string UsageJson = "\"usage\":{\"input_tokens\":900,\"input_tokens_details\":{\"cached_tokens\":0},\"output_tokens\":300,\"output_tokens_details\":{\"reasoning_tokens\":120},\"total_tokens\":1200}";

    private static readonly AdvisoryAiFailedUpdateFacts Facts = new("Contoso Editor", "WinGet", "1.2.0", "1.3.0", "InstallerFailed", 1603,
        [new(Now.AddMinutes(-5), "InstallerFailed", 1603, "Installer failed with 1603.")]);

    [TestMethod]
    [TestCategory("FR-AI-01")]
    public async Task NothingIsDraftedOrReviewedWithoutAReviewedModelOrAConnection()
    {
        await using Fixture fixture = await Fixture.CreateAsync(connected: false, prices: true);
        AdvisoryAiExplainService none = fixture.Service(AdvisoryAiAvailability.Current);
        Assert.AreEqual(AdvisoryAiExplainBlock.NotAvailable, (await none.DraftForFailedUpdateAsync(Guid.NewGuid(), Facts, CancellationToken.None)).Block);

        AdvisoryAiDraftResult notConnected = await fixture.Service().DraftForFailedUpdateAsync(Guid.NewGuid(), Facts, CancellationToken.None);
        Assert.AreEqual(AdvisoryAiExplainBlock.NotConnected, notConnected.Block);
        Assert.IsNull(notConnected.Draft);
        StringAssert.Contains(AdvisoryAiExplainText.Blocked(notConnected.Block), "Nothing has been sent");
        Assert.AreEqual(0, fixture.Transport.Calls);
        Assert.AreEqual(0, fixture.Credentials.Reads);
    }

    [TestMethod]
    [TestCategory("FR-AI-02")]
    public async Task TheReviewNeedsTodaysPricesAndShowsTheExactRequestAndItsReservation()
    {
        await using Fixture fixture = await Fixture.CreateAsync(connected: true, prices: false);
        AdvisoryAiExplainService service = fixture.Service();
        AdvisoryAiEvidenceDraft draft = (await service.DraftForFailedUpdateAsync(Guid.NewGuid(), Facts, CancellationToken.None)).Draft!;
        Assert.AreEqual(AdvisoryAiEvidenceRole.FailedUpdateSummary, draft.Items[0].Role);

        AdvisoryAiReview noPrices = await service.ReviewAsync(draft, CancellationToken.None);
        Assert.AreEqual(AdvisoryAiExplainBlock.PricesNeeded, noPrices.Block);
        Assert.IsFalse(noPrices.CanApprove);

        await fixture.Store.ConfirmTariffAsync(Rates, CancellationToken.None);
        AdvisoryAiReview review = await service.ReviewAsync(draft, CancellationToken.None);
        Assert.IsTrue(review.CanApprove);
        StringAssert.Contains(review.Preview!.BodyText, "Contoso Editor");
        StringAssert.Contains(review.Preview.BodyText, "\"store\":false");
        Assert.AreEqual(AdvisoryAiCost.Reservation(review.Preview.InputTokenUpperBound, review.Preview.Limits.MaximumOutputTokens, review.Tariff!), review.Reservation);
        StringAssert.Contains(AdvisoryAiExplainText.Cost(review, AdvisoryAiProviders.OpenAI), review.Reservation.ToDollarString());
        string route = AdvisoryAiExplainText.Route(review.Preview);
        StringAssert.Contains(route, AdvisoryAiProviders.OpenAI.Endpoint.ToString());
        StringAssert.Contains(route, AdvisoryAiProviders.OpenAI.AuthorizationPreview);
        Assert.AreEqual(0, fixture.Credentials.Reads, "a review reads no key");
        Assert.AreEqual(0, fixture.Transport.Calls, "a review sends nothing");
    }

    [TestMethod]
    [TestCategory("FR-AI-04")]
    public async Task AnApprovedReviewIsSentOnceSettledAndSaved()
    {
        await using Fixture fixture = await Fixture.CreateAsync(connected: true, prices: true);
        AdvisoryAiExplainService service = fixture.Service();
        Guid job = Guid.NewGuid();
        AdvisoryAiEvidenceDraft draft = (await service.DraftForFailedUpdateAsync(job, Facts, CancellationToken.None)).Draft!;
        AdvisoryAiReview review = await service.ReviewAsync(draft, CancellationToken.None);

        AdvisoryAiSendOutcome outcome = await service.SendAsync(review, CancellationToken.None);

        Assert.IsTrue(outcome.Dispatched);
        Assert.AreEqual(1, fixture.Transport.Calls);
        Assert.AreEqual(AdvisoryAiRequestState.Settled, outcome.Settlement!.State);
        Assert.AreEqual(AdvisoryAiExplanationSave.Saved, outcome.Saved);
        string text = AdvisoryAiExplainText.Outcome(outcome);
        StringAssert.Contains(text, "Sent once.");
        StringAssert.Contains(text, "It cost " + outcome.Settlement.Committed.ToDollarString());
        StringAssert.Contains(text, "saved for 30 days");
        IReadOnlyList<AdvisoryAiSavedExplanation> saved = await service.SavedAsync(AdvisoryAiSourceKind.FailedUpdate, job.ToString("D"), CancellationToken.None);
        Assert.AreEqual(1, saved.Count);
        Assert.AreEqual("Synthetic explanation.", saved[0].Text);
        Assert.IsTrue(await service.DeleteSavedAsync(saved[0].Id, CancellationToken.None));
        Assert.AreEqual(0, (await service.SavedAsync(AdvisoryAiSourceKind.FailedUpdate, job.ToString("D"), CancellationToken.None)).Count);
    }

    [TestMethod]
    [TestCategory("FR-AI-04")]
    public async Task AChangeAfterTheReviewRefusesTheSendBeforeAnythingIsReservedOrRead()
    {
        await using Fixture fixture = await Fixture.CreateAsync(connected: true, prices: true);
        AdvisoryAiExplainService service = fixture.Service();
        AdvisoryAiEvidenceDraft draft = (await service.DraftForFailedUpdateAsync(Guid.NewGuid(), Facts, CancellationToken.None)).Draft!;
        AdvisoryAiReview review = await service.ReviewAsync(draft, CancellationToken.None);
        AdvisoryAiSettings settings = await fixture.Store.GetSettingsAsync(CancellationToken.None);
        Assert.AreEqual(AdvisoryAiSettingsWrite.Saved, await fixture.Store.SaveLimitsAsync(settings.Limits with { MaximumOutputTokens = 2_048 }, settings.SettingsVersion, CancellationToken.None));

        AdvisoryAiSendOutcome outcome = await service.SendAsync(review, CancellationToken.None);

        Assert.IsFalse(outcome.Dispatched);
        Assert.AreEqual(AdvisoryAiSendRefusal.ApprovalStale, outcome.Refusal);
        Assert.AreEqual(0, fixture.Transport.Calls);
        Assert.AreEqual(0, fixture.Credentials.Reads);
        StringAssert.StartsWith(AdvisoryAiExplainText.Outcome(outcome), "Nothing was sent and nothing was charged.");

        AdvisoryAiSendOutcome blocked = await service.SendAsync(new AdvisoryAiReview(null, null, null, MicroUsd.Zero, AdvisoryAiExplainBlock.PricesNeeded, AdvisoryAiPreviewProblem.None), CancellationToken.None);
        Assert.IsFalse(blocked.Dispatched);
        Assert.AreEqual(0, fixture.Transport.Calls);
    }

    [TestMethod]
    [TestCategory("FR-AI-02")]
    public async Task EditsAreRedactedAgainAndRemovalsKeepAtLeastOneItem()
    {
        await using Fixture fixture = await Fixture.CreateAsync(connected: true, prices: true);
        AdvisoryAiExplainService service = fixture.Service(redaction: new(["OWNER-PC"]));
        AdvisoryAiEvidenceDraft draft = (await service.DraftForFailedUpdateAsync(Guid.NewGuid(), Facts, CancellationToken.None)).Draft!;
        Assert.AreEqual(2, draft.Items.Count);

        Assert.AreEqual(AdvisoryAiDraftProblem.None, service.ApplyEdits(draft, ["The update failed on OWNER-PC.", null], out AdvisoryAiEvidenceDraft edited));
        Assert.AreEqual(1, edited.Items.Count);
        Assert.IsFalse(edited.Items[0].Text.Contains("OWNER-PC", StringComparison.OrdinalIgnoreCase), "an edit is redacted again");
        Assert.IsTrue(edited.Items[0].EditedByOwner);
        Assert.IsGreaterThan(draft.Version, edited.Version);

        Assert.AreEqual(AdvisoryAiDraftProblem.None, service.ApplyEdits(draft, [.. draft.Items.Select(item => (string?)item.Text)], out AdvisoryAiEvidenceDraft unchanged));
        Assert.AreSame(draft, unchanged, "no change makes no new version");
        Assert.AreEqual(AdvisoryAiDraftProblem.NoEvidenceLeft, service.ApplyEdits(draft, [null, null], out _));
        Assert.AreEqual(AdvisoryAiDraftProblem.EditIsEmpty, service.ApplyEdits(draft, ["   ", draft.Items[1].Text], out _));
        Assert.AreEqual(AdvisoryAiDraftProblem.NoSuchItem, service.ApplyEdits(draft, ["only one"], out _));
    }

    [TestMethod]
    [TestCategory("FR-BUD-03")]
    public void OutcomeWordsNeverCallAnUncertainCostKnown()
    {
        AdvisoryAiSendOutcome uncertain = new(AdvisoryAiSendRefusal.None, AdvisoryAiReservationProblem.None, true, new(40_000),
            new(AdvisoryAiRequestState.Uncertain, new(40_000), AdvisoryAiUncertainReason.TimedOut, false), AdvisoryAiLedgerWrite.Done, null,
            AdvisoryAiIncompleteReason.None, null, false, null);
        string text = AdvisoryAiExplainText.Outcome(uncertain);
        StringAssert.Contains(text, "can't tell what it cost");
        StringAssert.Contains(text, "$0.04");
        StringAssert.Contains(text, "No answer came back.");

        AdvisoryAiSendOutcome above = uncertain with { Settlement = new(AdvisoryAiRequestState.Settled, new(90_000), AdvisoryAiUncertainReason.None, true), AnswerStatus = AdvisoryAiAnswerStatus.Incomplete, IncompleteReason = AdvisoryAiIncompleteReason.OutputLimit, Saved = AdvisoryAiExplanationSave.StorageFailed };
        string aboveText = AdvisoryAiExplainText.Outcome(above);
        StringAssert.Contains(aboveText, "paid requests are paused");
        StringAssert.Contains(aboveText, "stopped at your output limit");
        StringAssert.Contains(aboveText, "couldn't be saved");

        AdvisoryAiSendOutcome unrecorded = uncertain with { SettlementWrite = AdvisoryAiLedgerWrite.WrongState };
        StringAssert.Contains(AdvisoryAiExplainText.Outcome(unrecorded), "stays counted at its whole reservation");
    }

    [TestMethod]
    [TestCategory("FR-AI-02")]
    public void BlockedWordsAndRoleLabelsCoverEveryCase()
    {
        foreach (AdvisoryAiExplainBlock block in Enum.GetValues<AdvisoryAiExplainBlock>().Where(block => block != AdvisoryAiExplainBlock.None))
            Assert.IsFalse(string.IsNullOrWhiteSpace(AdvisoryAiExplainText.Blocked(block)), block.ToString());
        foreach (AdvisoryAiEvidenceRole role in Enum.GetValues<AdvisoryAiEvidenceRole>())
            Assert.IsFalse(string.IsNullOrWhiteSpace(AdvisoryAiExplainText.RoleLabel(role)), role.ToString());
        StringAssert.Contains(AdvisoryAiExplainText.Blocked(AdvisoryAiExplainBlock.DraftNotPossible, AdvisoryAiDraftProblem.RequiredEvidenceDoesNotFit), "input limit");
        StringAssert.Contains(AdvisoryAiExplainText.Blocked(AdvisoryAiExplainBlock.PreviewNotPossible, previewProblem: AdvisoryAiPreviewProblem.DraftDoesNotFitInputLimit), "Remove some items");
    }

    // ---- Helpers -------------------------------------------------------------------------------------------------------------------

    private static string Body(string text) =>
        "{\"id\":\"resp_" + Guid.NewGuid().ToString("N") + "\",\"object\":\"response\",\"status\":\"completed\",\"model\":\"" + Model.ModelId + "\",\"store\":false,\"tools\":[],\"output\":[{\"type\":\"message\",\"role\":\"assistant\",\"content\":[{\"type\":\"output_text\",\"text\":" +
        System.Text.Json.JsonSerializer.Serialize(text) + ",\"annotations\":[]}]}]," + UsageJson + "}";

    private sealed class FakeTransport : IAdvisoryAiTransport
    {
        public AdvisoryAiProviderId Provider => AdvisoryAiProviderId.OpenAI;
        public int Calls { get; private set; }

        public Task<AdvisoryAiTransportResult> SendOnceAsync(AdvisoryAiApproval approval, AdvisoryAiApiKey key, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new AdvisoryAiTransportResult(AdvisoryAiTransportOutcome.ResponseReceived, 200, [.. Encoding.UTF8.GetBytes(Body("Synthetic explanation."))]));
        }
    }

    private sealed class FakeCredentials : IAdvisoryAiCredentialStore
    {
        public AdvisoryAiProviderId Provider => AdvisoryAiProviderId.OpenAI;
        public int Reads { get; private set; }

        public Task<AdvisoryAiApiKey?> ReadForDispatchAsync(CancellationToken cancellationToken)
        {
            Reads++;
            AdvisoryAiApiKey.TryCreate("sk-synthetic-not-a-real-key", out AdvisoryAiApiKey? key);
            return Task.FromResult(key);
        }

        public Task<AdvisoryAiCredentialWrite> SaveAsync(AdvisoryAiApiKey key, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AdvisoryAiCredentialPresence> CheckPresenceAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AdvisoryAiCredentialRemoval> RemoveAndVerifyAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string path;
        private Fixture(string path, SqliteDatabase store) { this.path = path; Store = store; }
        public SqliteDatabase Store { get; }
        public FakeTransport Transport { get; } = new();
        public FakeCredentials Credentials { get; } = new();

        public static async Task<Fixture> CreateAsync(bool connected, bool prices)
        {
            string path = Path.Combine(Path.GetTempPath(), $"pcsteward-ai-explain-{Guid.NewGuid():N}.db");
            SqliteDatabase store = new(path);
            await store.MigrateAsync(CancellationToken.None);
            if (connected) await store.SetConnectedAsync(true, Now, CancellationToken.None);
            if (prices) await store.ConfirmTariffAsync(Rates, CancellationToken.None);
            await store.DeclareContinuityAsync(AdvisoryAiContinuity.DeclaredNoPriorUse, Now, CancellationToken.None);
            return new(path, store);
        }

        public AdvisoryAiExplainService Service(AdvisoryAiAvailability? availability = null, DiagnosticRedactionContext? redaction = null)
        {
            AdvisoryAiAvailability chosen = availability ?? Ordinary;
            DateTimeOffset clock = Now.AddMinutes(1);
            Func<DateTimeOffset> utcNow = () => clock = clock.AddSeconds(1);
            AdvisoryAiRequestWorkflow workflow = new(Store, Store, new AdvisoryAiProviderAdapters((Credentials, Transport)), Store,
                chosen.Model is null ? [] : [chosen.Model], chosen.Mode, utcNow);
            return new AdvisoryAiExplainService(Store, Store, Store, workflow, chosen, utcNow, redaction);
        }

        public ValueTask DisposeAsync()
        {
            SqliteConnection.ClearAllPools();
            foreach (string suffix in new[] { "", "-wal", "-shm" }) File.Delete(path + suffix);
            return ValueTask.CompletedTask;
        }
    }
}
