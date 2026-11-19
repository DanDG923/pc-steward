using System.Collections.Immutable;
using System.Text;
using Microsoft.Data.Sqlite;
using PCSteward.Application;
using PCSteward.Domain;
using PCSteward.Persistence.Sqlite;

namespace PCSteward.Application.IntegrationTests;

/// <summary>
/// FR-AI-01..05, FR-BUD-01..03 (ADR-028/029; Task 68 stage 2): the SQLite settings, tariff, ledger and saved-explanation store
/// (migration 14) and the request workflow end to end over it, with a counting fake transport and a fake credential store. No key,
/// network or price is real: the model, rates and answers are synthetic.
/// </summary>
[TestClass]
public sealed class AdvisoryAiLedgerTests
{
    private static readonly AdvisoryAiModelProfile Model = new("synthetic-model-2026-01-01", 1, 128_000, 16_384, 64, true, 64, 65_536, "default");
    private static readonly AdvisoryAiModelProfile[] Reviewed = [Model];
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 9, 0, 0, TimeSpan.Zero);
    private static readonly AdvisoryAiTariff Rates = new(0, Model.ModelId, Model.ServiceTier, new(2_000_000), new(500_000), new(8_000_000), Now);
    private const string UsageJson = "\"usage\":{\"input_tokens\":900,\"input_tokens_details\":{\"cached_tokens\":0},\"output_tokens\":300,\"output_tokens_details\":{\"reasoning_tokens\":120},\"total_tokens\":1200}";

    // ---- Settings and tariffs ---------------------------------------------------------------------------------------------------

    [TestMethod]
    [TestCategory("FR-AI-01")]
    public async Task AFreshStateIsDisconnectedWithTheDefaultLimitsAndNoContinuity()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        AdvisoryAiSettings settings = await fixture.Store.GetSettingsAsync(CancellationToken.None);
        Assert.AreEqual(AdvisoryAiSettings.Initial, settings);
        AdvisoryAiAccountingState state = await fixture.Store.GetAccountingStateAsync(CancellationToken.None);
        Assert.AreEqual(new AdvisoryAiAccountingState(AdvisoryAiContinuity.NotEstablished, null, null, false), state);
        Assert.AreEqual(16L, await fixture.ScalarAsync("SELECT MAX(version) FROM schema_migrations;"));
    }

    [TestMethod]
    [TestCategory("FR-BUD-01")]
    public async Task LimitsSaveOnlyOverTheVersionTheOwnerSawAndInvalidLimitsAreRefused()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        AdvisoryAiLimits raised = AdvisoryAiLimits.Defaults with { MonthlyBudget = new(300_000_000) };
        Assert.AreEqual(AdvisoryAiSettingsWrite.Saved, await fixture.Store.SaveLimitsAsync(raised, 0, CancellationToken.None));
        Assert.AreEqual(AdvisoryAiSettingsWrite.VersionConflict, await fixture.Store.SaveLimitsAsync(AdvisoryAiLimits.Defaults, 0, CancellationToken.None));
        Assert.AreEqual(AdvisoryAiSettingsWrite.Invalid,
            await fixture.Store.SaveLimitsAsync(AdvisoryAiLimits.Defaults with { RequestCap = new(300_000_001) }, 1, CancellationToken.None));
        AdvisoryAiSettings settings = await fixture.Store.GetSettingsAsync(CancellationToken.None);
        Assert.AreEqual(1, settings.SettingsVersion);
        Assert.AreEqual(raised, settings.Limits);
        Assert.AreEqual(AdvisoryAiSettingsWrite.Saved, await fixture.Store.SaveLimitsAsync(AdvisoryAiLimits.Defaults with { RequestCap = MicroUsd.Zero }, 1, CancellationToken.None));
        Assert.IsTrue((await fixture.Store.GetSettingsAsync(CancellationToken.None)).Limits.PaidRequestsDisabled, "zero disables paid requests and keeps the other value");
    }

    [TestMethod]
    [TestCategory("FR-AI-03")]
    public async Task ConnectingDisconnectingAndKeyChangesEachRaiseTheirVersion()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        await fixture.Store.SetConnectedAsync(true, Now, CancellationToken.None);
        await fixture.Store.AdvanceCredentialGenerationAsync(CancellationToken.None);
        AdvisoryAiSettings connected = await fixture.Store.GetSettingsAsync(CancellationToken.None);
        Assert.IsTrue(connected.Connected);
        Assert.AreEqual((1L, 1L), (connected.ConsentVersion, connected.CredentialGeneration));
        await fixture.Store.SetConnectedAsync(false, Now, CancellationToken.None);
        AdvisoryAiSettings disconnected = await fixture.Store.GetSettingsAsync(CancellationToken.None);
        Assert.IsFalse(disconnected.Connected);
        Assert.AreEqual(2, disconnected.ConsentVersion);
    }

    [TestMethod]
    [TestCategory("FR-BUD-01")]
    public async Task EachPriceConfirmationIsANewImmutableVersionForExactlyOneModelAndTier()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        AdvisoryAiTariff first = (await fixture.Store.ConfirmTariffAsync(Rates with { Version = 99 }, CancellationToken.None))!;
        AdvisoryAiTariff second = (await fixture.Store.ConfirmTariffAsync(Rates with { OutputPerMillion = new(9_000_000), ConfirmedAtUtc = Now.AddHours(1) }, CancellationToken.None))!;
        Assert.AreEqual((1L, 2L), (first.Version, second.Version));
        Assert.AreEqual(second, await fixture.Store.GetLatestTariffAsync(Model.ModelId, Model.ServiceTier, CancellationToken.None));
        Assert.IsNull(await fixture.Store.GetLatestTariffAsync(Model.ModelId, "priority", CancellationToken.None));
        Assert.IsNull(await fixture.Store.GetLatestTariffAsync("synthetic-model", Model.ServiceTier, CancellationToken.None));
        Assert.IsNull(await fixture.Store.ConfirmTariffAsync(Rates with { CachedInputPerMillion = MicroUsd.Zero }, CancellationToken.None), "a free rate is never stored");
        // T70-05: the older prices were never used by a request, so confirming again replaced them instead of growing the ledger.
        Assert.AreEqual(0L, await fixture.ScalarAsync("SELECT COUNT(*) FROM ai_tariffs WHERE version=1;"));
        Assert.AreEqual(1L, await fixture.ScalarAsync("SELECT COUNT(*) FROM ai_tariffs;"));
    }

    [TestMethod]
    [TestCategory("FR-BUD-01")]
    public async Task ConfirmingPricesEveryDayKeepsOnlyTheLatestAndThePricesARequestUsed()
    {
        await using Fixture fixture = await Fixture.CreateReadyAsync();
        AdvisoryAiTariff used = await fixture.TariffAsync();
        AdvisoryAiReservationRequest request = await fixture.ClaimedAsync(used, Now.AddMinutes(1));
        Assert.AreEqual(AdvisoryAiLedgerWrite.Done, await fixture.Store.SettleAsync(request.RequestId,
            new(AdvisoryAiRequestState.Settled, new(100), AdvisoryAiUncertainReason.None, false), new(50, 0, 0, 0, 50), "resp_used", Now, CancellationToken.None));
        for (int day = 1; day <= 30; day++)
            Assert.IsNotNull(await fixture.Store.ConfirmTariffAsync(Rates with { ConfirmedAtUtc = Now.AddDays(day) }, CancellationToken.None));

        Assert.AreEqual(2L, await fixture.ScalarAsync("SELECT COUNT(*) FROM ai_tariffs;"), "the prices the request used, and the latest");
        Assert.AreEqual(1L, await fixture.ScalarAsync("SELECT COUNT(*) FROM ai_tariffs WHERE version=$v;", ("$v", used.Version)));
        Assert.AreEqual(31L, (await fixture.TariffAsync()).Version);
    }

    [TestMethod]
    [TestCategory("FR-BUD-03")]
    public async Task SettledDetailOlderThanTwelveMonthsIsCompactedWithEveryTotalAndResponseIdKept()
    {
        await using Fixture fixture = await Fixture.CreateReadyAsync();
        AdvisoryAiTariff tariff = await fixture.TariffAsync();
        // 2024-10 is older than the current month (2026-11) and the 12 before it; 2025-11 is the oldest month kept in detail.
        await fixture.ExecuteAsync("""
            INSERT INTO ai_requests(request_id,period,model_id,model_profile_version,tariff_version,settings_version,input_token_bound,max_output_tokens,reservation,committed,first_live_test,state,reason,above_reservation,reserved_at,provider_response_id,
                input_tokens,cached_input_tokens,output_tokens,reasoning_tokens,total_tokens) VALUES
            ('cccccccc-0000-0000-0000-000000000001',202410,'synthetic-model-2026-01-01',1,1,0,10,10,500,100,0,'Settled','None',0,'2024-10-02T00:00:00.0000000Z','resp_old_1',50,0,0,0,50),
            ('cccccccc-0000-0000-0000-000000000002',202410,'synthetic-model-2026-01-01',1,1,0,10,10,500,100,1,'Settled','None',0,'2024-10-03T00:00:00.0000000Z','resp_old_2',50,0,0,0,50),
            ('cccccccc-0000-0000-0000-000000000003',202410,'synthetic-model-2026-01-01',1,1,0,10,10,500,100,0,'Settled','None',0,'2024-10-04T00:00:00.0000000Z',NULL,50,0,0,0,50),
            ('cccccccc-0000-0000-0000-000000000005',202511,'synthetic-model-2026-01-01',1,1,0,10,10,500,100,0,'Settled','None',0,'2025-11-02T00:00:00.0000000Z','resp_kept',50,0,0,0,50);
            INSERT INTO ai_requests(request_id,period,model_id,model_profile_version,tariff_version,settings_version,input_token_bound,max_output_tokens,reservation,committed,first_live_test,state,reason,above_reservation,reserved_at) VALUES
            ('cccccccc-0000-0000-0000-000000000004',202410,'synthetic-model-2026-01-01',1,1,0,10,10,500,0,0,'NotSent','NotSent',0,'2024-10-05T00:00:00.0000000Z'),
            ('cccccccc-0000-0000-0000-000000000006',202409,'synthetic-model-2026-01-01',1,1,0,10,10,700,700,1,'Uncertain','Interrupted',0,'2024-09-05T00:00:00.0000000Z');
            """);
        AdvisoryAiSpendingSummary before = await fixture.Store.GetSummaryAsync(AdvisoryAiPeriod.Of(Now), CancellationToken.None);

        AdvisoryAiRecovery recovery = await fixture.Store.RecoverInterruptedAsync(Now.AddMinutes(1), CancellationToken.None);

        Assert.AreEqual(4, recovery.Compacted, "three settled and one released request from 2024-10");
        Assert.AreEqual(2L, await fixture.ScalarAsync("SELECT COUNT(*) FROM ai_requests;"), "the unresolved 2024-09 request and the kept 2025-11 one");
        Assert.AreEqual("Uncertain", await fixture.ScalarAsync("SELECT state FROM ai_requests WHERE period=202409;"));
        Assert.AreEqual("300|100|3|1", await fixture.ScalarAsync("SELECT settled||'|'||test_settled||'|'||settled_requests||'|'||released_requests FROM ai_monthly_totals WHERE period=202410;"));
        Assert.AreEqual(3L, await fixture.ScalarAsync("SELECT COUNT(*) FROM ai_compacted_requests WHERE period=202410 AND tariff_version=1;"));
        AdvisoryAiSpendingSummary after = await fixture.Store.GetSummaryAsync(AdvisoryAiPeriod.Of(Now), CancellationToken.None);
        Assert.AreEqual(before.TestSettled, after.TestSettled, "the first-live-test envelope still counts the compacted test charge");
        Assert.AreEqual(new MicroUsd(100), after.TestSettled);
        Assert.AreEqual(before.TestOutstanding, after.TestOutstanding);
        Assert.AreEqual(before.UncertainInEarlierMonths, after.UncertainInEarlierMonths);
        Assert.IsFalse(after.ReviewBlock);
        Assert.AreEqual(0, (await fixture.Store.RecoverInterruptedAsync(Now.AddMinutes(2), CancellationToken.None)).Compacted, "nothing left to compact");

        // A compacted provider response can never settle another request.
        AdvisoryAiReservationRequest replay = await fixture.ClaimedAsync(tariff, Now.AddMinutes(3));
        Assert.AreEqual(AdvisoryAiLedgerWrite.Conflict, await fixture.Store.SettleAsync(replay.RequestId,
            new(AdvisoryAiRequestState.Settled, new(100), AdvisoryAiUncertainReason.None, false), new(50, 0, 0, 0, 50), "resp_old_1", Now, CancellationToken.None));
        Assert.IsTrue((await fixture.Store.GetAccountingStateAsync(CancellationToken.None)).ReviewBlock);
    }

    [TestMethod]
    [TestCategory("FR-BUD-03")]
    [DataRow("review", DisplayName = "paused for review")]
    [DataRow("month", DisplayName = "new month not yet confirmed")]
    public async Task NothingIsCompactedWhileTheLedgerIsPausedOrTheClockIsNotAccepted(string condition)
    {
        await using Fixture fixture = await Fixture.CreateReadyAsync();
        await fixture.ExecuteAsync("""
            INSERT INTO ai_requests(request_id,period,model_id,model_profile_version,tariff_version,settings_version,input_token_bound,max_output_tokens,reservation,committed,first_live_test,state,reason,above_reservation,reserved_at,
                input_tokens,cached_input_tokens,output_tokens,reasoning_tokens,total_tokens)
            VALUES ('dddddddd-0000-0000-0000-000000000001',202410,'synthetic-model-2026-01-01',1,1,0,10,10,500,100,0,'Settled','None',0,'2024-10-02T00:00:00.0000000Z',50,0,0,0,50);
            """);
        DateTimeOffset at = Now.AddMinutes(1);
        if (condition == "review") await fixture.ExecuteAsync("UPDATE ai_accounting_state SET review_block=1;");
        else at = Now.AddMonths(1);

        Assert.AreEqual(0, (await fixture.Store.RecoverInterruptedAsync(at, CancellationToken.None)).Compacted);
        Assert.AreEqual(1L, await fixture.ScalarAsync("SELECT COUNT(*) FROM ai_requests WHERE period=202410;"));
        Assert.AreEqual(0L, await fixture.ScalarAsync("SELECT COUNT(*) FROM ai_monthly_totals;"));
    }

    [TestMethod]
    [TestCategory("FR-BUD-02")]
    public async Task AMonthlyTotalThatDoesNotAddUpStopsPaidRequestsForReview()
    {
        await using Fixture fixture = await Fixture.CreateReadyAsync();
        AdvisoryAiTariff tariff = await fixture.TariffAsync();
        await fixture.ExecuteAsync("PRAGMA ignore_check_constraints=ON; INSERT INTO ai_monthly_totals VALUES (202410,100,200,1,0);");
        Assert.AreEqual(AdvisoryAiReservationProblem.LedgerNeedsReview, (await fixture.ReserveAsync(tariff, Now.AddMinutes(1))).Problem);
        Assert.IsTrue((await fixture.Store.GetAccountingStateAsync(CancellationToken.None)).ReviewBlock);
    }

    [TestMethod]
    [TestCategory("FR-BUD-02")]
    [DataRow("UPDATE ai_monthly_totals SET test_settled=0;", DisplayName = "compacted test total lowered to zero")]
    [DataRow("UPDATE ai_monthly_totals SET settled=settled-1;", DisplayName = "compacted month total lowered")]
    [DataRow("UPDATE ai_monthly_totals SET settled_requests=settled_requests-1;", DisplayName = "compacted request count changed")]
    [DataRow("DELETE FROM ai_compacted_requests WHERE request_id='eeeeeeee-0000-0000-0000-000000000002';", DisplayName = "compacted request removed")]
    [DataRow("UPDATE ai_compacted_requests SET committed=1 WHERE first_live_test=1;", DisplayName = "compacted charge no longer matches its usage")]
    [DataRow("UPDATE ai_compacted_requests SET first_live_test=0;", DisplayName = "compacted test flag cleared")]
    [DataRow("PRAGMA foreign_keys=OFF; DELETE FROM ai_monthly_totals;", DisplayName = "compacted month total removed")]
    public async Task CompactedSpendingThatDoesNotAddUpStopsPaidRequestsForReview(string tamper)
    {
        // Task 70B T70B-01: once compacted, a first-live-test charge still counts towards the $1 envelope, and lowering any part of the
        // compacted record on its own is caught on every later read, including after a restart.
        await using Fixture fixture = await Fixture.CreateReadyAsync();
        AdvisoryAiTariff tariff = await fixture.TariffAsync();
        await fixture.ExecuteAsync("""
            INSERT INTO ai_requests(request_id,period,model_id,model_profile_version,tariff_version,settings_version,input_token_bound,max_output_tokens,reservation,committed,first_live_test,state,reason,above_reservation,reserved_at,provider_response_id,
                input_tokens,cached_input_tokens,output_tokens,reasoning_tokens,total_tokens) VALUES
            ('eeeeeeee-0000-0000-0000-000000000001',202410,'synthetic-model-2026-01-01',1,1,0,10,10,1000000,990000,1,'Settled','None',0,'2024-10-02T00:00:00.0000000Z','resp_test',495000,0,0,0,495000),
            ('eeeeeeee-0000-0000-0000-000000000002',202410,'synthetic-model-2026-01-01',1,1,0,10,10,500,100,0,'Settled','None',0,'2024-10-03T00:00:00.0000000Z',NULL,50,0,0,0,50);
            """);
        Assert.AreEqual(2, (await fixture.Store.RecoverInterruptedAsync(Now.AddMinutes(1), CancellationToken.None)).Compacted);
        Assert.AreEqual(new MicroUsd(990_000), (await fixture.Store.GetSummaryAsync(AdvisoryAiPeriod.Of(Now), CancellationToken.None)).TestSettled);
        Assert.AreEqual(AdvisoryAiReservationProblem.AboveFirstLiveTestEnvelope,
            (await fixture.ReserveAsync(tariff, Now.AddMinutes(2), AdvisoryAiPaidRequestMode.FirstLiveTest)).Problem, "the compacted test charge still counts");

        await fixture.ExecuteAsync(tamper);

        Assert.AreEqual(AdvisoryAiReservationProblem.LedgerNeedsReview, (await fixture.ReserveAsync(tariff, Now.AddMinutes(3), AdvisoryAiPaidRequestMode.FirstLiveTest)).Problem);
        Assert.AreEqual(0L, await fixture.ScalarAsync("SELECT COUNT(*) FROM ai_requests;"), "nothing is reserved");
        AdvisoryAiSpendingSummary summary = await fixture.NewStore().GetSummaryAsync(AdvisoryAiPeriod.Of(Now), CancellationToken.None);
        Assert.IsTrue(summary.ReviewBlock, "the block is stored and shown after a restart");
    }

    [TestMethod]
    [TestCategory("FR-BUD-02")]
    public async Task TheSixteenMebibyteCeilingCountsCompactedRequestsAndPricesToo()
    {
        await using Fixture fixture = await Fixture.CreateReadyAsync();
        AdvisoryAiTariff tariff = await fixture.TariffAsync();
        // 32,766 compacted IDs (512 bytes each), one month total and one price row leave less than one request row (1,024 bytes) of room.
        await fixture.ExecuteAsync("""
            INSERT INTO ai_monthly_totals VALUES (202410,0,0,32766,0);
            WITH RECURSIVE n(i) AS (SELECT 1 UNION ALL SELECT i+1 FROM n WHERE i < 32766)
            INSERT INTO ai_compacted_requests(request_id,period,tariff_version,provider_response_id,committed,first_live_test,input_tokens,cached_input_tokens,output_tokens,reasoning_tokens,total_tokens)
            SELECT printf('%08d-0000-0000-0000-000000000000', i),202410,1,NULL,0,0,0,0,0,0,0 FROM n;
            """);
        Assert.AreEqual(448L, IAdvisoryAiLedger.MaximumLogicalBytes - IAdvisoryAiLedger.LogicalBytes(0, 1, 32_766, 1));
        Assert.AreEqual(AdvisoryAiReservationProblem.LedgerFull, (await fixture.ReserveAsync(tariff, Now.AddMinutes(1))).Problem);
        Assert.AreEqual(0L, await fixture.ScalarAsync("SELECT COUNT(*) FROM ai_requests;"));

        // Two fewer compacted IDs free 1,024 bytes more: 1,472 bytes of room, enough for one request row.
        await fixture.ExecuteAsync("DELETE FROM ai_compacted_requests WHERE request_id IN ('00000001-0000-0000-0000-000000000000','00000002-0000-0000-0000-000000000000'); UPDATE ai_monthly_totals SET settled_requests=32764;");
        Assert.AreEqual(AdvisoryAiReservationProblem.None, (await fixture.ReserveAsync(tariff, Now.AddMinutes(2))).Problem, "one request row fits again");
    }

    // ---- Ledger ----------------------------------------------------------------------------------------------------------------

    [TestMethod]
    [TestCategory("FR-BUD-01")]
    public async Task NothingIsReservedUntilTheOwnerAnswersThePriorUseQuestion()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        await fixture.Store.SetConnectedAsync(true, Now, CancellationToken.None);
        AdvisoryAiTariff tariff = await fixture.ConfirmAsync();
        Assert.AreEqual(AdvisoryAiReservationProblem.ContinuityNotEstablished, (await fixture.ReserveAsync(tariff, Now.AddMinutes(1))).Problem);
        Assert.AreEqual(AdvisoryAiAccountingWrite.Done, await fixture.Store.DeclareContinuityAsync(AdvisoryAiContinuity.DeclaredNoPriorUse, Now, CancellationToken.None));
        Assert.AreEqual(AdvisoryAiAccountingWrite.NotApplicable, await fixture.Store.DeclareContinuityAsync(AdvisoryAiContinuity.PriorUseUnknown, Now, CancellationToken.None), "answered once");
        Assert.IsTrue((await fixture.ReserveAsync(tariff, Now.AddMinutes(1))).Reserved);
        Assert.AreEqual(0L, await fixture.ScalarAsync("SELECT COUNT(*) FROM ai_requests WHERE state <> 'Reserved';"));
    }

    [TestMethod]
    [TestCategory("FR-BUD-03")]
    public async Task OnlyOneRequestIsInFlightAcrossConnectionsAndProcesses()
    {
        await using Fixture fixture = await Fixture.CreateReadyAsync();
        AdvisoryAiTariff tariff = (await fixture.Store.GetLatestTariffAsync(Model.ModelId, Model.ServiceTier, CancellationToken.None))!;
        // Separate store instances use separate connections, as two app processes would.
        AdvisoryAiApprovalBinding binding = await fixture.BindingAsync(tariff);
        AdvisoryAiReserveResult[] results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => fixture.NewStore().ReserveAsync(
            Request(tariff, binding, Now.AddMinutes(1)), AdvisoryAiLimits.Defaults, AdvisoryAiPaidRequestMode.Ordinary, tariff, Model, CancellationToken.None))));
        Assert.AreEqual(1, results.Count(result => result.Reserved));
        Assert.IsTrue(results.Where(result => !result.Reserved).All(result => result.Problem == AdvisoryAiReservationProblem.AnotherRequestInFlight), string.Join(",", results.Select(r => r.Problem)));
        Assert.AreEqual(1L, await fixture.ScalarAsync("SELECT COUNT(*) FROM ai_requests;"));
    }

    [TestMethod]
    [TestCategory("FR-BUD-02")]
    public async Task AClaimedRequestIsNeverReleasedAndAReleasedOneIsNeverClaimed()
    {
        await using Fixture fixture = await Fixture.CreateReadyAsync();
        AdvisoryAiTariff tariff = await fixture.TariffAsync();
        AdvisoryAiApprovalBinding binding = await fixture.BindingAsync(tariff);
        AdvisoryAiReservationRequest claimed = Request(tariff, binding, Now.AddMinutes(1));
        Assert.IsTrue((await fixture.Store.ReserveAsync(claimed, AdvisoryAiLimits.Defaults, AdvisoryAiPaidRequestMode.Ordinary, tariff, Model, CancellationToken.None)).Reserved);
        Assert.AreEqual(AdvisoryAiLedgerWrite.Done, await fixture.Store.ClaimDispatchAsync(claimed.RequestId, binding, Now, CancellationToken.None));
        Assert.AreEqual(AdvisoryAiLedgerWrite.AlreadyDone, await fixture.Store.ClaimDispatchAsync(claimed.RequestId, binding, Now, CancellationToken.None));
        Assert.AreEqual(AdvisoryAiLedgerWrite.WrongState, await fixture.Store.ReleaseUnclaimedAsync(claimed.RequestId, Now, CancellationToken.None));
        Assert.AreEqual(AdvisoryAiLedgerWrite.Done, await fixture.SettleAsync(claimed.RequestId, AdvisoryAiSettlementRule.Interrupted(claimed.Reservation)));

        AdvisoryAiReservationRequest released = Request(tariff, binding, Now.AddMinutes(2));
        Assert.IsTrue((await fixture.Store.ReserveAsync(released, AdvisoryAiLimits.Defaults, AdvisoryAiPaidRequestMode.Ordinary, tariff, Model, CancellationToken.None)).Reserved);
        Assert.AreEqual(AdvisoryAiLedgerWrite.Done, await fixture.Store.ReleaseUnclaimedAsync(released.RequestId, Now, CancellationToken.None));
        Assert.AreEqual(AdvisoryAiLedgerWrite.WrongState, await fixture.Store.ClaimDispatchAsync(released.RequestId, binding, Now, CancellationToken.None));
        Assert.AreEqual(AdvisoryAiLedgerWrite.NotFound, await fixture.Store.ClaimDispatchAsync(Guid.NewGuid(), binding, Now, CancellationToken.None));

        AdvisoryAiSpendingSummary summary = await fixture.Store.GetSummaryAsync(AdvisoryAiPeriod.Of(Now), CancellationToken.None);
        Assert.AreEqual(claimed.Reservation, summary.Outstanding, "the uncertain request keeps its reservation; the released one costs nothing");
        Assert.AreEqual(MicroUsd.Zero, summary.Settled);
    }

    [TestMethod]
    [TestCategory("FR-BUD-02")]
    public async Task SettlementReplacesTheReservationOnceAndAConflictingRepeatStopsPaidRequests()
    {
        await using Fixture fixture = await Fixture.CreateReadyAsync();
        AdvisoryAiTariff tariff = await fixture.TariffAsync();
        AdvisoryAiReservationRequest request = await fixture.ClaimedAsync(tariff, Now.AddMinutes(1));
        AdvisoryAiSettlement settled = new(AdvisoryAiRequestState.Settled, new(4_200), AdvisoryAiUncertainReason.None, false);
        AdvisoryAiUsage usage = new(900, 0, 300, 120, 1200);
        Assert.AreEqual(AdvisoryAiLedgerWrite.Done, await fixture.Store.SettleAsync(request.RequestId, settled, usage, "resp_one", Now, CancellationToken.None));
        Assert.AreEqual(AdvisoryAiLedgerWrite.AlreadyDone, await fixture.Store.SettleAsync(request.RequestId, settled, usage, "resp_one", Now, CancellationToken.None));
        Assert.AreEqual(4_200, (await fixture.Store.GetSummaryAsync(AdvisoryAiPeriod.Of(Now), CancellationToken.None)).Settled.Value);
        Assert.AreEqual(900L, await fixture.ScalarAsync("SELECT input_tokens FROM ai_requests;"));

        Assert.AreEqual(AdvisoryAiLedgerWrite.Conflict, await fixture.Store.SettleAsync(request.RequestId, settled with { Committed = new(1) }, usage, "resp_one", Now, CancellationToken.None));
        Assert.AreEqual(4_200, (await fixture.Store.GetSummaryAsync(AdvisoryAiPeriod.Of(Now), CancellationToken.None)).Settled.Value, "a conflicting repeat changes nothing");
        Assert.AreEqual(AdvisoryAiReservationProblem.LedgerNeedsReview, (await fixture.ReserveAsync(tariff, Now.AddMinutes(2))).Problem);
        Assert.AreEqual(AdvisoryAiAccountingWrite.Done, await fixture.Store.AcknowledgeReviewAsync(CancellationToken.None));
        Assert.IsTrue((await fixture.ReserveAsync(tariff, Now.AddMinutes(3))).Reserved);
    }

    [TestMethod]
    [TestCategory("FR-BUD-02")]
    [DataRow("usage", DisplayName = "different token counts, same rounded charge")]
    [DataRow("no-usage", DisplayName = "usage missing on the repeat")]
    [DataRow("above", DisplayName = "repeat newly marked above the reservation")]
    [DataRow("reason", DisplayName = "different reason")]
    public async Task ARepeatWithTheSameChargeButDifferentEvidenceIsAConflict(string difference)
    {
        // ADR-029: only an identical repeat is harmless; different usage can round to the same micro-dollar charge.
        await using Fixture fixture = await Fixture.CreateReadyAsync();
        AdvisoryAiTariff tariff = await fixture.TariffAsync();
        AdvisoryAiReservationRequest request = await fixture.ClaimedAsync(tariff, Now.AddMinutes(1));
        AdvisoryAiSettlement settled = new(AdvisoryAiRequestState.Settled, new(4_200), AdvisoryAiUncertainReason.None, false);
        AdvisoryAiUsage usage = new(900, 0, 300, 120, 1200);
        Assert.AreEqual(AdvisoryAiLedgerWrite.Done, await fixture.Store.SettleAsync(request.RequestId, settled, usage, "resp_one", Now, CancellationToken.None));
        (AdvisoryAiSettlement repeat, AdvisoryAiUsage? repeatUsage) = difference switch
        {
            "usage" => (settled, usage with { InputTokens = 901, TotalTokens = 1201 }),
            "no-usage" => (settled, (AdvisoryAiUsage?)null),
            "above" => (settled with { AboveReservation = true }, usage),
            _ => (settled with { Reason = AdvisoryAiUncertainReason.UsageMissingOrUnreliable }, usage),
        };
        Assert.AreEqual(AdvisoryAiLedgerWrite.Conflict, await fixture.Store.SettleAsync(request.RequestId, repeat, repeatUsage, "resp_one", Now, CancellationToken.None));
        Assert.IsTrue((await fixture.Store.GetAccountingStateAsync(CancellationToken.None)).ReviewBlock, "paid requests stop for review");
        Assert.AreEqual(900L, await fixture.ScalarAsync("SELECT input_tokens FROM ai_requests;"), "the first evidence is kept");
    }

    [TestMethod]
    [TestCategory("FR-BUD-02")]
    public async Task AChargeAboveTheReservationIsRecordedInFullAndBlocksTheNextRequest()
    {
        await using Fixture fixture = await Fixture.CreateReadyAsync();
        AdvisoryAiTariff tariff = await fixture.TariffAsync();
        AdvisoryAiReservationRequest request = await fixture.ClaimedAsync(tariff, Now.AddMinutes(1));
        AdvisoryAiUsage usage = new(2_000, 0, 1_025, 0, 3_025);
        long above = AdvisoryAiCost.Actual(usage, tariff).Value;
        Assert.IsGreaterThan(request.Reservation.Value, above);
        Assert.AreEqual(AdvisoryAiLedgerWrite.Done, await fixture.Store.SettleAsync(request.RequestId, new(AdvisoryAiRequestState.Settled, new(above), AdvisoryAiUncertainReason.None, true), usage, null, Now, CancellationToken.None));
        Assert.AreEqual(above, (await fixture.Store.GetSummaryAsync(AdvisoryAiPeriod.Of(Now), CancellationToken.None)).Settled.Value);
        Assert.IsTrue((await fixture.Store.GetAccountingStateAsync(CancellationToken.None)).ReviewBlock);
        Assert.AreEqual(1L, await fixture.ScalarAsync("SELECT above_reservation FROM ai_requests;"));
    }

    [TestMethod]
    [TestCategory("FR-BUD-02")]
    public async Task OneProviderResponseCannotSettleTwoRequests()
    {
        await using Fixture fixture = await Fixture.CreateReadyAsync();
        AdvisoryAiTariff tariff = await fixture.TariffAsync();
        AdvisoryAiReservationRequest first = await fixture.ClaimedAsync(tariff, Now.AddMinutes(1));
        AdvisoryAiSettlement settled = new(AdvisoryAiRequestState.Settled, new(100), AdvisoryAiUncertainReason.None, false);
        AdvisoryAiUsage usage = new(50, 0, 0, 0, 50);
        Assert.AreEqual(AdvisoryAiLedgerWrite.Done, await fixture.Store.SettleAsync(first.RequestId, settled, usage, "resp_same", Now, CancellationToken.None));
        AdvisoryAiReservationRequest second = await fixture.ClaimedAsync(tariff, Now.AddMinutes(2));
        Assert.AreEqual(AdvisoryAiLedgerWrite.Conflict, await fixture.Store.SettleAsync(second.RequestId, settled, usage, "resp_same", Now, CancellationToken.None));
        Assert.AreEqual("Uncertain", await fixture.ScalarAsync("SELECT state FROM ai_requests WHERE request_id=$id;", ("$id", second.RequestId.ToString("D"))));
        Assert.AreEqual(second.Reservation.Value, await fixture.ScalarAsync("SELECT committed FROM ai_requests WHERE request_id=$id;", ("$id", second.RequestId.ToString("D"))));
        Assert.IsTrue((await fixture.Store.GetAccountingStateAsync(CancellationToken.None)).ReviewBlock);
    }

    [TestMethod]
    [TestCategory("FR-BUD-02")]
    public async Task RecoveryReleasesUnclaimedRequestsAndKeepsClaimedOnesUncertainWithoutResending()
    {
        await using Fixture fixture = await Fixture.CreateReadyAsync();
        AdvisoryAiTariff tariff = await fixture.TariffAsync();
        AdvisoryAiReservationRequest claimed = await fixture.ClaimedAsync(tariff, Now.AddMinutes(1));
        await fixture.ExecuteAsync("UPDATE ai_requests SET state='Reserved' WHERE request_id <> $id;", ("$id", claimed.RequestId.ToString("D")));
        await fixture.ExecuteAsync("""
            INSERT INTO ai_requests(request_id,period,model_id,model_profile_version,tariff_version,settings_version,input_token_bound,max_output_tokens,reservation,committed,first_live_test,state,reason,above_reservation,reserved_at)
            VALUES ('aaaaaaaa-0000-0000-0000-000000000001',202611,'synthetic-model-2026-01-01',1,1,0,10,10,500,500,0,'Reserved','None',0,'2026-11-13T09:00:00.0000000Z');
            """);
        Assert.AreEqual(new AdvisoryAiRecovery(1, 1), await fixture.Store.RecoverInterruptedAsync(Now.AddMinutes(5), CancellationToken.None));
        Assert.AreEqual("Interrupted", await fixture.ScalarAsync("SELECT reason FROM ai_requests WHERE request_id=$id;", ("$id", claimed.RequestId.ToString("D"))));
        AdvisoryAiSpendingSummary summary = await fixture.Store.GetSummaryAsync(AdvisoryAiPeriod.Of(Now), CancellationToken.None);
        Assert.AreEqual(claimed.Reservation, summary.Outstanding);
        Assert.AreEqual(new AdvisoryAiRecovery(0, 0), await fixture.Store.RecoverInterruptedAsync(Now.AddMinutes(6), CancellationToken.None));
    }

    [TestMethod]
    [TestCategory("FR-BUD-03")]
    public async Task ANewMonthIsConfirmedBeforeUseAndEarlierUncertaintyStaysInItsOwnMonth()
    {
        await using Fixture fixture = await Fixture.CreateReadyAsync();
        AdvisoryAiTariff november = await fixture.TariffAsync();
        AdvisoryAiReservationRequest request = await fixture.ClaimedAsync(november, Now.AddMinutes(1));
        await fixture.SettleAsync(request.RequestId, AdvisoryAiSettlementRule.Interrupted(request.Reservation));

        DateTimeOffset december = new(2026, 12, 1, 0, 0, 1, TimeSpan.Zero);
        AdvisoryAiTariff decemberRates = (await fixture.Store.ConfirmTariffAsync(Rates with { ConfirmedAtUtc = december }, CancellationToken.None))!;
        Assert.AreEqual(AdvisoryAiReservationProblem.NewMonthNeedsConfirmation, (await fixture.ReserveAsync(decemberRates, december)).Problem);
        Assert.AreEqual(AdvisoryAiAccountingWrite.Done, await fixture.Store.ConfirmNewPeriodAsync(december, CancellationToken.None));
        Assert.IsTrue((await fixture.ReserveAsync(decemberRates, december.AddSeconds(1))).Reserved);

        AdvisoryAiSpendingSummary decemberSummary = await fixture.Store.GetSummaryAsync(new(2026, 12), CancellationToken.None);
        Assert.AreEqual(request.Reservation, decemberSummary.UncertainInEarlierMonths, "shown separately, never refunded");
        AdvisoryAiSpendingSummary novemberSummary = await fixture.Store.GetSummaryAsync(new(2026, 11), CancellationToken.None);
        Assert.AreEqual(request.Reservation, novemberSummary.Outstanding);
    }

    [TestMethod]
    [TestCategory("FR-BUD-01")]
    public async Task UnknownPriorUseWaitsForALaterConfirmedMonthAndNeverReopensTheTest()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        await fixture.Store.SetConnectedAsync(true, Now, CancellationToken.None);
        await fixture.Store.DeclareContinuityAsync(AdvisoryAiContinuity.PriorUseUnknown, Now, CancellationToken.None);
        AdvisoryAiTariff tariff = await fixture.ConfirmAsync();
        Assert.AreEqual(AdvisoryAiReservationProblem.PriorUseUnknown, (await fixture.ReserveAsync(tariff, Now.AddMinutes(1))).Problem);
        Assert.AreEqual(AdvisoryAiAccountingWrite.NotApplicable, await fixture.Store.ConfirmNewPeriodAsync(Now.AddDays(1), CancellationToken.None), "the same month is not a new period");

        DateTimeOffset december = new(2026, 12, 2, 8, 0, 0, TimeSpan.Zero);
        AdvisoryAiTariff decemberRates = (await fixture.Store.ConfirmTariffAsync(Rates with { ConfirmedAtUtc = december }, CancellationToken.None))!;
        Assert.AreEqual(AdvisoryAiAccountingWrite.Done, await fixture.Store.ConfirmNewPeriodAsync(december, CancellationToken.None));
        Assert.AreEqual(AdvisoryAiContinuity.FreshPeriodConfirmed, (await fixture.Store.GetAccountingStateAsync(CancellationToken.None)).Continuity);
        Assert.AreEqual(AdvisoryAiReservationProblem.TestSpendingUnknown, (await fixture.ReserveAsync(decemberRates, december.AddMinutes(1), AdvisoryAiPaidRequestMode.FirstLiveTest)).Problem);
        Assert.IsTrue((await fixture.ReserveAsync(decemberRates, december.AddMinutes(2))).Reserved);
    }

    [TestMethod]
    [TestCategory("FR-BUD-03")]
    public async Task AClockThatMovedBackBlocksUntilReviewedAndAnEarlierMonthIsNeverReopened()
    {
        await using Fixture fixture = await Fixture.CreateReadyAsync();
        AdvisoryAiTariff tariff = await fixture.TariffAsync();
        Assert.IsTrue((await fixture.ReserveAsync(tariff, Now.AddHours(2), release: true)).Reserved);
        Assert.AreEqual(AdvisoryAiReservationProblem.ClockMovedBackwards, (await fixture.ReserveAsync(tariff, Now.AddHours(1))).Problem);
        Assert.AreEqual(AdvisoryAiAccountingWrite.NotApplicable, await fixture.Store.AcceptClockAsync(new(2026, 10, 31, 0, 0, 0, TimeSpan.Zero), CancellationToken.None));
        Assert.AreEqual(AdvisoryAiAccountingWrite.Done, await fixture.Store.AcceptClockAsync(Now.AddHours(1), CancellationToken.None));
        Assert.IsTrue((await fixture.ReserveAsync(tariff, Now.AddHours(1).AddSeconds(1))).Reserved);
    }

    [TestMethod]
    [TestCategory("FR-BUD-01")]
    public async Task TheFirstLiveTestEnvelopeCountsUncertainTestRequestsAcrossMonths()
    {
        await using Fixture fixture = await Fixture.CreateReadyAsync();
        AdvisoryAiTariff tariff = await fixture.TariffAsync();
        await fixture.ExecuteAsync("""
            INSERT INTO ai_requests(request_id,period,model_id,model_profile_version,tariff_version,settings_version,input_token_bound,max_output_tokens,reservation,committed,first_live_test,state,reason,above_reservation,reserved_at)
            VALUES ('aaaaaaaa-0000-0000-0000-000000000002',202610,'synthetic-model-2026-01-01',1,1,0,10,10,999000,999000,1,'Uncertain','TimedOut',0,'2026-10-13T09:00:00.0000000Z');
            """);
        AdvisoryAiReserveResult result = await fixture.ReserveAsync(tariff, Now.AddMinutes(1), AdvisoryAiPaidRequestMode.FirstLiveTest);
        Assert.AreEqual(AdvisoryAiReservationProblem.AboveFirstLiveTestEnvelope, result.Problem);
        Assert.AreEqual(999_000, result.Snapshot!.TestOutstanding.Value);
        Assert.IsTrue((await fixture.ReserveAsync(tariff, Now.AddMinutes(2))).Reserved, "ordinary mode uses the monthly budget instead");
    }

    [TestMethod]
    [TestCategory("FR-BUD-01")]
    public async Task AFullLedgerBlocksNewRequestsInsteadOfDeletingAnything()
    {
        await using Fixture fixture = await Fixture.CreateReadyAsync();
        AdvisoryAiTariff tariff = await fixture.TariffAsync();
        await fixture.ExecuteAsync("""
            WITH RECURSIVE n(i) AS (SELECT 1 UNION ALL SELECT i+1 FROM n WHERE i < 10000)
            INSERT INTO ai_requests(request_id,period,model_id,model_profile_version,tariff_version,settings_version,input_token_bound,max_output_tokens,reservation,committed,first_live_test,state,reason,above_reservation,reserved_at,
                input_tokens,cached_input_tokens,output_tokens,reasoning_tokens,total_tokens)
            SELECT printf('%08d-0000-0000-0000-000000000000', i),202611,'synthetic-model-2026-01-01',1,1,0,10,10,1,1,0,'Settled','None',0,'2026-11-01T00:00:00.0000000Z',1,1,0,0,1 FROM n;
            """);
        Assert.AreEqual(AdvisoryAiReservationProblem.LedgerFull, (await fixture.ReserveAsync(tariff, Now.AddMinutes(1))).Problem);
        Assert.AreEqual(10_000L, await fixture.ScalarAsync("SELECT COUNT(*) FROM ai_requests;"));
    }

    [TestMethod]
    [TestCategory("FR-BUD-02")]
    public async Task AFullLedgerWithEveryValueAtItsLongestStaysWithinTheLogicalCeilingOnDisk()
    {
        // T70-05 storage evidence for the accounting record alone: 10,000 settled rows with every text at its longest allowed length.
        await using Fixture fixture = await Fixture.CreateReadyAsync();
        AdvisoryAiTariff tariff = await fixture.TariffAsync();
        long empty = fixture.BytesOnDisk();
        await fixture.ExecuteAsync("""
            WITH RECURSIVE n(i) AS (SELECT 1 UNION ALL SELECT i+1 FROM n WHERE i < 10000)
            INSERT INTO ai_requests(request_id,period,model_id,model_profile_version,tariff_version,settings_version,input_token_bound,max_output_tokens,reservation,committed,first_live_test,state,reason,above_reservation,
                reserved_at,claimed_at,finished_at,provider_response_id,input_tokens,cached_input_tokens,output_tokens,reasoning_tokens,total_tokens)
            SELECT printf('%08d-0000-0000-0000-000000000000', i),202611,replace(hex(zeroblob(64)),'0','m'),2147483647,1,9223372036854775807,2147483647,2147483647,
                9223372036854775807,100,0,'Settled',replace(hex(zeroblob(32)),'0','r'),0,'2026-11-01T00:00:00.0000000Z','2026-11-01T00:00:00.0000000Z','2026-11-01T00:00:00.0000000Z',
                printf('%08d', i)||substr(replace(hex(zeroblob(124)),'0','x'),1,248),50,0,0,0,50 FROM n;
            PRAGMA wal_checkpoint(TRUNCATE);
            """);
        long full = fixture.BytesOnDisk();
        Assert.AreEqual(AdvisoryAiReservationProblem.LedgerFull, (await fixture.ReserveAsync(tariff, Now.AddMinutes(1))).Problem);
        Assert.IsTrue(full - empty <= IAdvisoryAiLedger.MaximumLogicalBytes, $"{full - empty} bytes on disk for a full ledger");
        Console.WriteLine($"LEDGER-DISK empty={empty} full={full} added={full - empty}");
    }

    [TestMethod]
    [TestCategory("FR-BUD-02")]
    public async Task AnAlteredLedgerRowBlocksPaidRequestsInsteadOfGrantingThem()
    {
        await using Fixture fixture = await Fixture.CreateReadyAsync();
        AdvisoryAiTariff tariff = await fixture.TariffAsync();
        await fixture.ExecuteAsync("PRAGMA ignore_check_constraints=ON; UPDATE ai_accounting_state SET continuity='Anything';");
        Assert.AreEqual(AdvisoryAiReservationProblem.LedgerNeedsReview, (await fixture.ReserveAsync(tariff, Now.AddMinutes(1))).Problem);
        Assert.IsTrue((await fixture.Store.GetAccountingStateAsync(CancellationToken.None)).ReviewBlock);
    }

    [TestMethod]
    [TestCategory("FR-BUD-02")]
    [DataRow("UPDATE ai_requests SET committed=0 WHERE state='Uncertain';", false, DisplayName = "uncertain charge lowered to zero")]
    [DataRow("UPDATE ai_requests SET committed=committed-1 WHERE state='Uncertain';", false, DisplayName = "uncertain charge lowered below its reservation")]
    [DataRow("UPDATE ai_requests SET committed=0 WHERE state='Uncertain';", true, DisplayName = "first live test charge lowered to zero")]
    [DataRow("UPDATE ai_requests SET committed=1 WHERE state='Settled';", false, DisplayName = "settled charge no longer matches its usage")]
    [DataRow("UPDATE ai_requests SET input_tokens=NULL WHERE state='Settled';", false, DisplayName = "settled usage removed")]
    [DataRow("UPDATE ai_requests SET committed=0 WHERE state='DispatchClaimed';", false, DisplayName = "claimed charge lowered to zero")]
    [DataRow("UPDATE ai_requests SET committed=5 WHERE state='NotSent';", false, DisplayName = "released request holds a charge")]
    public async Task ALedgerRowThatDoesNotAddUpStopsPaidRequestsForReview(string tamper, bool firstLiveTest)
    {
        // Task 70 T70-01: a row whose charge doesn't match its state is never trusted for a smaller figure, even through valid columns.
        await using Fixture fixture = await Fixture.CreateReadyAsync();
        AdvisoryAiTariff tariff = await fixture.TariffAsync();
        AdvisoryAiPaidRequestMode mode = firstLiveTest ? AdvisoryAiPaidRequestMode.FirstLiveTest : AdvisoryAiPaidRequestMode.Ordinary;
        AdvisoryAiReservationRequest uncertain = Request(tariff, await fixture.BindingAsync(tariff), Now.AddMinutes(1), firstLiveTest);
        Assert.IsTrue((await fixture.Store.ReserveAsync(uncertain, AdvisoryAiLimits.Defaults, mode, tariff, Model, CancellationToken.None)).Reserved);
        Assert.AreEqual(AdvisoryAiLedgerWrite.Done, await fixture.Store.ClaimDispatchAsync(uncertain.RequestId, uncertain.Binding, Now.AddMinutes(1), CancellationToken.None));
        Assert.AreEqual(AdvisoryAiLedgerWrite.Done, await fixture.SettleAsync(uncertain.RequestId, new(AdvisoryAiRequestState.Uncertain, uncertain.Reservation, AdvisoryAiUncertainReason.TimedOut, false)));
        if (!firstLiveTest)
        {
            AdvisoryAiReservationRequest settled = await fixture.ClaimedAsync(tariff, Now.AddMinutes(2));
            Assert.AreEqual(AdvisoryAiLedgerWrite.Done, await fixture.Store.SettleAsync(settled.RequestId, new(AdvisoryAiRequestState.Settled, new(4_200), AdvisoryAiUncertainReason.None, false),
                new(900, 0, 300, 120, 1200), "resp_settled", Now, CancellationToken.None));
            Assert.IsTrue((await fixture.ReserveAsync(tariff, Now.AddMinutes(3), release: true)).Reserved, "an intact ledger admits");
            _ = await fixture.ClaimedAsync(tariff, Now.AddMinutes(4));
        }
        Assert.IsFalse((await fixture.Store.GetSummaryAsync(AdvisoryAiPeriod.Of(Now), CancellationToken.None)).ReviewBlock);

        await fixture.ExecuteAsync(tamper);
        Assert.IsTrue((await fixture.Store.GetSummaryAsync(AdvisoryAiPeriod.Of(Now), CancellationToken.None)).ReviewBlock, "the summary shows the review block");
        Assert.AreEqual(AdvisoryAiReservationProblem.LedgerNeedsReview, (await fixture.ReserveAsync(tariff, Now.AddMinutes(5), mode)).Problem);
        Assert.IsTrue((await fixture.Store.GetAccountingStateAsync(CancellationToken.None)).ReviewBlock, "the block is stored");

        // A restart (a new store and recovery) doesn't repair it, and acknowledging the review doesn't trust the row either.
        SqliteDatabase restarted = fixture.NewStore();
        _ = await restarted.RecoverInterruptedAsync(Now.AddMinutes(6), CancellationToken.None);
        Assert.IsTrue((await restarted.GetAccountingStateAsync(CancellationToken.None)).ReviewBlock);
        Assert.AreEqual(AdvisoryAiAccountingWrite.Done, await restarted.AcknowledgeReviewAsync(CancellationToken.None));
        AdvisoryAiReservationRequest again = Request(tariff, (await restarted.GetSettingsAsync(CancellationToken.None)).BindingFor(tariff), Now.AddMinutes(7), firstLiveTest);
        Assert.AreEqual(AdvisoryAiReservationProblem.LedgerNeedsReview, (await restarted.ReserveAsync(again, AdvisoryAiLimits.Defaults, mode, tariff, Model, CancellationToken.None)).Problem);
    }

    // ---- Saved explanations -----------------------------------------------------------------------------------------------------

    [TestMethod]
    [TestCategory("FR-AI-05")]
    public async Task EachClearRemovesOnlyItsOwnKindOfExplanationAndNeverTheLedger()
    {
        await using Fixture fixture = await Fixture.CreateReadyAsync();
        AdvisoryAiTariff tariff = await fixture.TariffAsync();
        AdvisoryAiReservationRequest request = await fixture.ClaimedAsync(tariff, Now.AddMinutes(1));
        await fixture.Store.SettleAsync(request.RequestId, new(AdvisoryAiRequestState.Settled, new(10), AdvisoryAiUncertainReason.None, false), new(5, 0, 0, 0, 5), null, Now, CancellationToken.None);
        AdvisoryAiSavedExplanation update = Explanation(AdvisoryAiSourceKind.FailedUpdate, "job-1", Now);
        AdvisoryAiSavedExplanation timeline = Explanation(AdvisoryAiSourceKind.DiagnosticTimeline, "incident-1", Now);
        Assert.AreEqual(AdvisoryAiExplanationSave.Saved, await fixture.Store.SaveExplanationAsync(update, Now, CancellationToken.None));
        Assert.AreEqual(AdvisoryAiExplanationSave.Saved, await fixture.Store.SaveExplanationAsync(timeline, Now, CancellationToken.None));

        Assert.IsTrue((await fixture.Store.ClearScanDataAsync(CancellationToken.None)).Succeeded);
        Assert.HasCount(0, await fixture.Store.ListExplanationsAsync(AdvisoryAiSourceKind.DiagnosticTimeline, "incident-1", Now, CancellationToken.None));
        Assert.HasCount(1, await fixture.Store.ListExplanationsAsync(AdvisoryAiSourceKind.FailedUpdate, "job-1", Now, CancellationToken.None));

        Assert.IsTrue((await fixture.Store.ClearUpdateHistoryAsync(CancellationToken.None)).Succeeded);
        Assert.HasCount(0, await fixture.Store.ListExplanationsAsync(AdvisoryAiSourceKind.FailedUpdate, "job-1", Now, CancellationToken.None));
        Assert.AreEqual(10, (await fixture.Store.GetSummaryAsync(AdvisoryAiPeriod.Of(Now), CancellationToken.None)).Settled.Value, "spending survives every clear");
    }

    [TestMethod]
    [TestCategory("FR-AI-05")]
    public async Task ExplanationsExpireAfterThirtyDaysAndRespectTheSizeBounds()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        AdvisoryAiSavedExplanation old = Explanation(AdvisoryAiSourceKind.FailedUpdate, "job-1", Now.AddDays(-30).AddSeconds(1));
        Assert.AreEqual(AdvisoryAiExplanationSave.Saved, await fixture.Store.SaveExplanationAsync(old, Now, CancellationToken.None));
        Assert.HasCount(1, await fixture.Store.ListExplanationsAsync(AdvisoryAiSourceKind.FailedUpdate, "job-1", Now, CancellationToken.None));
        Assert.HasCount(0, await fixture.Store.ListExplanationsAsync(AdvisoryAiSourceKind.FailedUpdate, "job-1", Now.AddSeconds(1), CancellationToken.None));
        Assert.AreEqual(0L, await fixture.ScalarAsync("SELECT COUNT(*) FROM ai_explanations;"), "listing removed the expired row, not only hid it");
        Assert.AreEqual(AdvisoryAiExplanationSave.Invalid, await fixture.Store.SaveExplanationAsync(Explanation(AdvisoryAiSourceKind.FailedUpdate, "job-1", Now.AddDays(-31)), Now, CancellationToken.None));

        string huge = new('a', (int)AdvisoryAiResourcePolicy.MaximumSavedItemBytes);
        Assert.AreEqual(AdvisoryAiExplanationSave.TooLarge, await fixture.Store.SaveExplanationAsync(Explanation(AdvisoryAiSourceKind.FailedUpdate, "job-2", Now) with { Text = huge }, Now, CancellationToken.None));

        string large = new('b', 3 * 1024 * 1024);
        for (int index = 0; index < 3; index++)
            Assert.AreEqual(AdvisoryAiExplanationSave.Saved, await fixture.Store.SaveExplanationAsync(Explanation(AdvisoryAiSourceKind.FailedUpdate, $"big-{index}", Now.AddMinutes(index)) with { Text = large }, Now.AddMinutes(index), CancellationToken.None));
        Assert.HasCount(0, await fixture.Store.ListExplanationsAsync(AdvisoryAiSourceKind.FailedUpdate, "big-0", Now.AddMinutes(3), CancellationToken.None), "the oldest went to keep 8 MiB");
        Assert.HasCount(1, await fixture.Store.ListExplanationsAsync(AdvisoryAiSourceKind.FailedUpdate, "big-2", Now.AddMinutes(3), CancellationToken.None));
        Assert.IsLessThanOrEqualTo(AdvisoryAiResourcePolicy.MaximumSavedContentBytes, (long)(await fixture.ScalarAsync("SELECT SUM(item_bytes) FROM ai_explanations;"))!);
    }

    [TestMethod]
    [TestCategory("FR-AI-06")]
    public async Task ExpiredExplanationsAreRemovedWithoutAnotherSaveOrListing()
    {
        // FR-AI-06: the 30 days hold when the owner stops saving or disconnects; start-up removes what has expired.
        await using Fixture fixture = await Fixture.CreateAsync();
        Assert.AreEqual(AdvisoryAiExplanationSave.Saved, await fixture.Store.SaveExplanationAsync(Explanation(AdvisoryAiSourceKind.FailedUpdate, "job-1", Now.AddDays(-29)), Now, CancellationToken.None));
        Assert.AreEqual(AdvisoryAiExplanationSave.Saved, await fixture.Store.SaveExplanationAsync(Explanation(AdvisoryAiSourceKind.DiagnosticTimeline, "incident-1", Now), Now, CancellationToken.None));
        Assert.AreEqual(0, await fixture.Store.RemoveExpiredExplanationsAsync(Now, CancellationToken.None));
        Assert.AreEqual(1, await fixture.Store.RemoveExpiredExplanationsAsync(Now.AddDays(1), CancellationToken.None));
        Assert.AreEqual(1L, await fixture.ScalarAsync("SELECT COUNT(*) FROM ai_explanations;"));
        Assert.AreEqual(1, await fixture.Store.RemoveExpiredExplanationsAsync(Now.AddDays(30), CancellationToken.None));
        Assert.AreEqual(0L, await fixture.ScalarAsync("SELECT COUNT(*) FROM ai_explanations;"));
    }

    [TestMethod]
    [TestCategory("FR-AI-05")]
    public async Task ASavedAnswerIsRedactedAgainWhenRead()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        AdvisoryAiSavedExplanation saved = Explanation(AdvisoryAiSourceKind.FailedUpdate, "job-1", Now);
        await fixture.Store.SaveExplanationAsync(saved, Now, CancellationToken.None);
        await fixture.ExecuteAsync("UPDATE ai_explanations SET answer='Open https://example.com/fix now';");
        AdvisoryAiSavedExplanation read = (await fixture.Store.ListExplanationsAsync(AdvisoryAiSourceKind.FailedUpdate, "job-1", Now, CancellationToken.None)).Single();
        Assert.AreEqual("Open [link] now", read.Text);
        Assert.IsTrue(await fixture.Store.DeleteExplanationAsync(saved.Id, CancellationToken.None));
        Assert.IsFalse(await fixture.Store.DeleteExplanationAsync(saved.Id, CancellationToken.None));
    }

    // ---- The workflow over the real store --------------------------------------------------------------------------------------

    [TestMethod]
    [TestCategory("FR-AI-05")]
    public async Task AnAnswerThatGrowsWhenRedactedIsKeptWhole()
    {
        // The raw answer fills the admitted output; redaction makes every line longer. The redacted answer is shown and saved whole.
        await using Fixture fixture = await Fixture.CreateReadyAsync();
        int rawBound = AdvisoryAiLimits.Defaults.MaximumOutputTokens * Model.MaximumTokenUtf8Bytes;
        const string line = "password=a\n";
        string text = string.Concat(Enumerable.Repeat(line, (rawBound / line.Length) - 1)) + "end";
        FakeTransport transport = new(_ => Received(Body(text)));
        (AdvisoryAiRequestWorkflow workflow, _) = fixture.Workflow(transport);
        (AdvisoryAiApproval approval, AdvisoryAiRequestPreview preview) = await fixture.ApproveAsync();
        AdvisoryAiSendOutcome outcome = await workflow.SendAsync(approval, preview, null, CancellationToken.None);
        Assert.AreEqual(AdvisoryAiAnswerStatus.Completed, outcome.AnswerStatus);
        Assert.IsFalse(outcome.AnswerTruncated, "nothing was cut");
        Assert.IsNotNull(outcome.AnswerText);
        Assert.IsGreaterThan(text.Length, outcome.AnswerText.Length, "redaction made it longer");
        Assert.DoesNotContain("password=a", outcome.AnswerText);
        Assert.EndsWith("end", outcome.AnswerText);
        Assert.AreEqual(AdvisoryAiExplanationSave.Saved, outcome.Saved);
    }

    [TestMethod]
    [TestCategory("FR-BUD-03")]
    [DataRow(25 * 60, 1, true, DisplayName = "25 wall hours in one running minute")]
    [DataRow(48 * 60, 48 * 60, false, DisplayName = "a two-day sleep: both clocks moved")]
    [DataRow(23 * 60, 0, false, DisplayName = "less than a day ahead")]
    public async Task AClockThatJumpsForwardWhileRunningStopsPaidRequestsForReview(int wallMinutes, int tickMinutes, bool jumped)
    {
        // Task 70 T70-04: the guard runs on the real admission path. A jump is stored, survives a restart and needs the owner's review.
        await using Fixture fixture = await Fixture.CreateReadyAsync();
        DateTimeOffset wall = Now;
        TimeSpan tick = TimeSpan.FromHours(5);
        AdvisoryAiClockWatch watch = new(() => wall, () => tick);
        wall = wall.AddMinutes(wallMinutes);
        tick += TimeSpan.FromMinutes(tickMinutes);
        FakeTransport transport = new(_ => Received(Body("Synthetic advice.")));
        (AdvisoryAiRequestWorkflow workflow, FakeCredentials credentials) = fixture.Workflow(transport, clockWatch: watch);
        (AdvisoryAiApproval approval, AdvisoryAiRequestPreview preview) = await fixture.ApproveAsync();

        AdvisoryAiSendOutcome outcome = await workflow.SendAsync(approval, preview, null, CancellationToken.None);
        if (!jumped)
        {
            Assert.IsTrue(outcome.Dispatched);
            return;
        }
        Assert.AreEqual(AdvisoryAiSendRefusal.ReservationRefused, outcome.Refusal);
        Assert.AreEqual(AdvisoryAiReservationProblem.LedgerNeedsReview, outcome.ReservationProblem);
        Assert.AreEqual(0, transport.Calls, "nothing is sent");
        Assert.AreEqual(0, credentials.Reads, "the key isn't read");
        Assert.AreEqual(0L, await fixture.ScalarAsync("SELECT COUNT(*) FROM ai_requests;"), "nothing is reserved");
        Assert.IsTrue((await fixture.NewStore().GetAccountingStateAsync(CancellationToken.None)).ReviewBlock, "the block is stored for after a restart");
    }

    [TestMethod]
    [TestCategory("FR-BUD-03")]
    public async Task AClockJumpWhoseBlockCannotBeStoredIsNotForgotten()
    {
        await using Fixture fixture = await Fixture.CreateReadyAsync();
        DateTimeOffset wall = Now;
        TimeSpan tick = TimeSpan.FromHours(5);
        AdvisoryAiClockWatch watch = new(() => wall, () => tick);
        wall = wall.AddHours(25);
        tick += TimeSpan.FromMinutes(1);
        FakeTransport transport = new(_ => Received(Body("Synthetic advice.")));
        (AdvisoryAiRequestWorkflow workflow, _) = fixture.Workflow(transport, clockWatch: watch);
        await fixture.ExecuteAsync("CREATE TRIGGER fail_review_block BEFORE UPDATE ON ai_accounting_state WHEN NEW.review_block=1 BEGIN SELECT RAISE(ABORT,'fixture write fault'); END;");
        (AdvisoryAiApproval approval, AdvisoryAiRequestPreview preview) = await fixture.ApproveAsync();

        Assert.AreEqual(AdvisoryAiSendRefusal.LedgerUnavailable, (await workflow.SendAsync(approval, preview, null, CancellationToken.None)).Refusal);

        // Storage recovers and time moves on normally: the jump is still pending, so it is stored now and the send is refused for review.
        await fixture.ExecuteAsync("DROP TRIGGER fail_review_block;");
        wall = wall.AddMinutes(1);
        tick += TimeSpan.FromMinutes(1);
        (approval, preview) = await fixture.ApproveAsync();
        AdvisoryAiSendOutcome outcome = await workflow.SendAsync(approval, preview, null, CancellationToken.None);
        Assert.AreEqual(AdvisoryAiReservationProblem.LedgerNeedsReview, outcome.ReservationProblem);
        Assert.AreEqual(0, transport.Calls, "nothing is sent");
        Assert.IsTrue((await fixture.Store.GetAccountingStateAsync(CancellationToken.None)).ReviewBlock);
        Assert.IsFalse(watch.JumpedForward(), "once stored, the watch starts fresh");
    }

    [TestMethod]
    [TestCategory("FR-AI-04")]
    public async Task AnApprovedRequestIsSentOnceSettledRedactedAndSaved()
    {
        await using Fixture fixture = await Fixture.CreateReadyAsync();
        FakeTransport transport = new(_ => Received(Body("The installer at C:\\Users\\alex\\setup.exe failed with 1603.")));
        (AdvisoryAiRequestWorkflow workflow, FakeCredentials credentials) = fixture.Workflow(transport);
        (AdvisoryAiApproval approval, AdvisoryAiRequestPreview preview) = await fixture.ApproveAsync();

        AdvisoryAiSendOutcome outcome = await workflow.SendAsync(approval, preview, null, CancellationToken.None);
        Assert.AreEqual(AdvisoryAiSendRefusal.None, outcome.Refusal);
        Assert.IsTrue(outcome.Dispatched);
        Assert.AreEqual(1, transport.Calls);
        Assert.AreEqual(1, credentials.Reads);
        Assert.IsTrue(transport.SawApprovedBytes);
        Assert.AreEqual(AdvisoryAiRequestState.Settled, outcome.Settlement!.State);
        Assert.AreEqual(900 * 2 + 300 * 8, outcome.Settlement.Committed.Value, "900 input tokens at $2/M and 300 output tokens at $8/M");
        Assert.AreEqual("The installer at [path] failed with 1603.", outcome.AnswerText);
        Assert.AreEqual(AdvisoryAiExplanationSave.Saved, outcome.Saved);
        AdvisoryAiSavedExplanation saved = (await fixture.Store.ListExplanationsAsync(AdvisoryAiSourceKind.FailedUpdate, approval.Preview.Draft.OriginId, Now.AddMinutes(1), CancellationToken.None)).Single();
        Assert.AreEqual(outcome.AnswerText, saved.Text);
        Assert.IsTrue(credentials.LastKeyDisposed);
        Assert.AreEqual("Settled", await fixture.ScalarAsync("SELECT state FROM ai_requests;"));
    }

    [TestMethod]
    [TestCategory("FR-AI-02")]
    public async Task NothingIsReservedOrSentWhenTheApprovalNoLongerMatches()
    {
        await using Fixture fixture = await Fixture.CreateReadyAsync();
        FakeTransport transport = new(_ => Received(Body("x")));
        (AdvisoryAiRequestWorkflow workflow, FakeCredentials credentials) = fixture.Workflow(transport);
        (AdvisoryAiApproval approval, AdvisoryAiRequestPreview preview) = await fixture.ApproveAsync();
        await fixture.Store.SaveLimitsAsync(AdvisoryAiLimits.Defaults, 0, CancellationToken.None);

        Assert.AreEqual(AdvisoryAiSendRefusal.ApprovalStale, (await workflow.SendAsync(approval, preview, null, CancellationToken.None)).Refusal);
        Assert.AreEqual(0, transport.Calls);
        Assert.AreEqual(0, credentials.Reads);
        Assert.AreEqual(0L, await fixture.ScalarAsync("SELECT COUNT(*) FROM ai_requests;"));
    }

    [TestMethod]
    [TestCategory("FR-AI-01")]
    public async Task ADisconnectedUnreviewedOrDisabledSetupNeverReachesTheTransport()
    {
        await using Fixture fixture = await Fixture.CreateReadyAsync();
        FakeTransport transport = new(_ => Received(Body("x")));
        (AdvisoryAiApproval approval, AdvisoryAiRequestPreview preview) = await fixture.ApproveAsync();

        AdvisoryAiSendOutcome disabled = await fixture.Workflow(transport, mode: AdvisoryAiPaidRequestMode.Disabled).Workflow.SendAsync(approval, preview, null, CancellationToken.None);
        Assert.AreEqual((AdvisoryAiSendRefusal.ReservationRefused, AdvisoryAiReservationProblem.PaidRequestsDisabledInThisBuild), (disabled.Refusal, disabled.ReservationProblem));
        Assert.AreEqual(AdvisoryAiSendRefusal.ModelNotReviewed,
            (await fixture.Workflow(transport, reviewed: []).Workflow.SendAsync(approval, preview, null, CancellationToken.None)).Refusal);
        await fixture.Store.SetConnectedAsync(false, Now, CancellationToken.None);
        Assert.AreEqual(AdvisoryAiSendRefusal.NotConnected, (await fixture.Workflow(transport).Workflow.SendAsync(approval, preview, null, CancellationToken.None)).Refusal);
        Assert.AreEqual(0, transport.Calls);
        Assert.AreEqual(0L, await fixture.ScalarAsync("SELECT COUNT(*) FROM ai_requests WHERE state <> 'NotSent';"));
    }

    [TestMethod]
    [TestCategory("FR-AI-03")]
    public async Task WithoutAKeyTheReservationIsReleasedAndNothingIsSent()
    {
        await using Fixture fixture = await Fixture.CreateReadyAsync();
        FakeTransport transport = new(_ => Received(Body("x")));
        (AdvisoryAiRequestWorkflow workflow, _) = fixture.Workflow(transport, key: null);
        (AdvisoryAiApproval approval, AdvisoryAiRequestPreview preview) = await fixture.ApproveAsync();
        Assert.AreEqual(AdvisoryAiSendRefusal.NoKey, (await workflow.SendAsync(approval, preview, null, CancellationToken.None)).Refusal);
        Assert.AreEqual(0, transport.Calls);
        Assert.AreEqual("NotSent", await fixture.ScalarAsync("SELECT state FROM ai_requests;"));
    }

    [TestMethod]
    [TestCategory("FR-BUD-02")]
    [DataRow(AdvisoryAiTransportOutcome.TimedOut, "TimedOut")]
    [DataRow(AdvisoryAiTransportOutcome.ConnectFailed, "ConnectFailed")]
    [DataRow(AdvisoryAiTransportOutcome.Redirected, "Redirected")]
    [DataRow(AdvisoryAiTransportOutcome.NotSent, "NotSentAfterClaim")]
    public async Task AFailedAttemptKeepsTheWholeReservationAndIsNeverRetried(AdvisoryAiTransportOutcome failure, string reason)
    {
        await using Fixture fixture = await Fixture.CreateReadyAsync();
        FakeTransport transport = new(_ => AdvisoryAiTransportResult.Of(failure));
        (AdvisoryAiRequestWorkflow workflow, _) = fixture.Workflow(transport);
        (AdvisoryAiApproval approval, AdvisoryAiRequestPreview preview) = await fixture.ApproveAsync();
        AdvisoryAiSendOutcome outcome = await workflow.SendAsync(approval, preview, null, CancellationToken.None);
        Assert.IsTrue(outcome.Dispatched);
        Assert.AreEqual(1, transport.Calls);
        Assert.AreEqual(AdvisoryAiRequestState.Uncertain, outcome.Settlement!.State);
        Assert.AreEqual(outcome.Reservation, outcome.Settlement.Committed);
        Assert.AreEqual(reason, await fixture.ScalarAsync("SELECT reason FROM ai_requests;"));
        Assert.AreEqual(outcome.Reservation.Value, await fixture.ScalarAsync("SELECT committed FROM ai_requests;"));
        Assert.IsNull(outcome.AnswerText);
    }

    [TestMethod]
    [TestCategory("FR-BUD-02")]
    public async Task ATransportThatThrowsOrIsCancelledAfterTheClaimIsRecordedAsUncertain()
    {
        await using Fixture fixture = await Fixture.CreateReadyAsync();
        (AdvisoryAiApproval approval, AdvisoryAiRequestPreview preview) = await fixture.ApproveAsync();
        AdvisoryAiSendOutcome thrown = await fixture.Workflow(new FakeTransport(_ => throw new HttpRequestException("synthetic"))).Workflow.SendAsync(approval, preview, null, CancellationToken.None);
        Assert.AreEqual(AdvisoryAiUncertainReason.TransportFailed, thrown.Settlement!.Reason);

        (AdvisoryAiApproval second, AdvisoryAiRequestPreview secondPreview) = await fixture.ApproveAsync();
        using CancellationTokenSource cancel = new();
        FakeTransport cancelling = new(_ => { cancel.Cancel(); throw new OperationCanceledException(cancel.Token); });
        AdvisoryAiSendOutcome cancelled = await fixture.Workflow(cancelling).Workflow.SendAsync(second, secondPreview, null, cancel.Token);
        Assert.AreEqual(AdvisoryAiUncertainReason.Cancelled, cancelled.Settlement!.Reason);
        Assert.AreEqual(2L, await fixture.ScalarAsync("SELECT COUNT(*) FROM ai_requests WHERE state='Uncertain';"));
    }

    [TestMethod]
    [TestCategory("FR-BUD-01")]
    [DataRow("limits", DisplayName = "limit lowered to zero")]
    [DataRow("disconnect", DisplayName = "disconnected")]
    [DataRow("key", DisplayName = "key replaced")]
    [DataRow("prices", DisplayName = "new prices confirmed")]
    public async Task AChangeCommittedAfterTheApprovalIsSeenInsideTheReservationAndTheClaim(string change)
    {
        await using Fixture fixture = await Fixture.CreateReadyAsync();
        AdvisoryAiTariff tariff = await fixture.TariffAsync();
        AdvisoryAiApprovalBinding binding = await fixture.BindingAsync(tariff);

        // Reserve: the caller read the settings before another connection changed them.
        AdvisoryAiReservationRequest stale = Request(tariff, binding, Now.AddMinutes(1));
        await fixture.ChangeAsync(change);
        AdvisoryAiReserveResult refused = await fixture.Store.ReserveAsync(stale, AdvisoryAiLimits.Defaults, AdvisoryAiPaidRequestMode.Ordinary, tariff, Model, CancellationToken.None);
        Assert.AreEqual(AdvisoryAiReservationProblem.ApprovalOutdated, refused.Problem);
        Assert.AreEqual(0L, await fixture.ScalarAsync("SELECT COUNT(*) FROM ai_requests;"));
    }

    [TestMethod]
    [TestCategory("FR-BUD-01")]
    [DataRow("limits", DisplayName = "limit lowered to zero")]
    [DataRow("disconnect", DisplayName = "disconnected")]
    [DataRow("key", DisplayName = "key replaced")]
    [DataRow("prices", DisplayName = "new prices confirmed")]
    public async Task AChangeAfterTheReservationStopsTheClaimAndTheSend(string change)
    {
        await using Fixture fixture = await Fixture.CreateReadyAsync();
        AdvisoryAiTariff tariff = await fixture.TariffAsync();
        AdvisoryAiApprovalBinding binding = await fixture.BindingAsync(tariff);
        AdvisoryAiReservationRequest request = Request(tariff, binding, Now.AddMinutes(1));
        Assert.IsTrue((await fixture.Store.ReserveAsync(request, AdvisoryAiLimits.Defaults, AdvisoryAiPaidRequestMode.Ordinary, tariff, Model, CancellationToken.None)).Reserved);
        await fixture.ChangeAsync(change);
        Assert.AreEqual(AdvisoryAiLedgerWrite.Outdated, await fixture.Store.ClaimDispatchAsync(request.RequestId, binding, Now.AddMinutes(1), CancellationToken.None));
        Assert.AreEqual("Reserved", await fixture.ScalarAsync("SELECT state FROM ai_requests;"), "nothing was claimed");

        // End to end: the change lands while the key is read, after the reservation and before the claim.
        await using Fixture second = await Fixture.CreateReadyAsync();
        FakeTransport transport = new(_ => Received(Body("x")));
        (AdvisoryAiRequestWorkflow workflow, _) = second.Workflow(transport, onRead: () => second.ChangeAsync(change).GetAwaiter().GetResult());
        (AdvisoryAiApproval approval, AdvisoryAiRequestPreview preview) = await second.ApproveAsync();
        AdvisoryAiSendOutcome outcome = await workflow.SendAsync(approval, preview, null, CancellationToken.None);
        Assert.AreEqual(AdvisoryAiSendRefusal.ApprovalStale, outcome.Refusal);
        Assert.AreEqual(0, transport.Calls, "nothing reached the transport");
        Assert.AreEqual("NotSent", await second.ScalarAsync("SELECT state FROM ai_requests;"));
    }

    [TestMethod]
    [TestCategory("FR-BUD-01")]
    public async Task PricesThatRunOutAfterTheReservationStopTheClaimAndTheSend()
    {
        // ADR-029: owner-confirmed prices last 24 hours. A reservation made just inside the window cannot be claimed once it has passed.
        await using Fixture fixture = await Fixture.CreateReadyAsync();
        AdvisoryAiTariff tariff = await fixture.TariffAsync();
        AdvisoryAiApprovalBinding binding = await fixture.BindingAsync(tariff);
        DateTimeOffset lastMinute = tariff.ExpiresAtUtc.AddMinutes(-1);
        AdvisoryAiReservationRequest request = Request(tariff, binding, lastMinute);
        Assert.IsTrue((await fixture.Store.ReserveAsync(request, AdvisoryAiLimits.Defaults, AdvisoryAiPaidRequestMode.Ordinary, tariff, Model, CancellationToken.None)).Reserved);
        Assert.AreEqual(AdvisoryAiLedgerWrite.Outdated, await fixture.Store.ClaimDispatchAsync(request.RequestId, binding, tariff.ExpiresAtUtc, CancellationToken.None));
        Assert.AreEqual(AdvisoryAiLedgerWrite.Outdated, await fixture.Store.ClaimDispatchAsync(request.RequestId, binding, tariff.ConfirmedAtUtc.AddSeconds(-1), CancellationToken.None),
            "a clock before the confirmation is not current either");
        Assert.AreEqual("Reserved", await fixture.ScalarAsync("SELECT state FROM ai_requests;"), "nothing was claimed");
        Assert.AreEqual(AdvisoryAiLedgerWrite.Done, await fixture.Store.ClaimDispatchAsync(request.RequestId, binding, lastMinute, CancellationToken.None));

        // End to end: the window closes while the key is read, after the reservation and before the claim.
        await using Fixture second = await Fixture.CreateReadyAsync();
        AdvisoryAiTariff secondTariff = await second.TariffAsync();
        DateTimeOffset clock = secondTariff.ExpiresAtUtc.AddMinutes(-1);
        FakeTransport transport = new(_ => Received(Body("x")));
        (AdvisoryAiRequestWorkflow workflow, _) = second.Workflow(transport, onRead: () => clock = secondTariff.ExpiresAtUtc.AddMinutes(1), utcNow: () => clock);
        (AdvisoryAiApproval approval, AdvisoryAiRequestPreview preview) = await second.ApproveAsync();
        AdvisoryAiSendOutcome outcome = await workflow.SendAsync(approval, preview, null, CancellationToken.None);
        Assert.AreEqual(AdvisoryAiSendRefusal.ApprovalStale, outcome.Refusal);
        Assert.AreEqual(0, transport.Calls, "nothing reached the transport");
        Assert.AreEqual("NotSent", await second.ScalarAsync("SELECT state FROM ai_requests;"));
    }

    [TestMethod]
    [TestCategory("FR-AI-04")]
    public async Task AModelWhoseProviderHasNoAdaptersIsRefusedBeforeAnythingIsReserved()
    {
        // ADR-028 addendum A1: the workflow uses only the key store and transport composed for the model's provider.
        await using Fixture fixture = await Fixture.CreateReadyAsync();
        (AdvisoryAiApproval approval, AdvisoryAiRequestPreview preview) = await fixture.ApproveAsync();
        AdvisoryAiRequestWorkflow workflow = new(fixture.Store, fixture.Store, new AdvisoryAiProviderAdapters(), fixture.Store, Reviewed,
            AdvisoryAiPaidRequestMode.Ordinary, () => Now.AddMinutes(1));
        AdvisoryAiSendOutcome outcome = await workflow.SendAsync(approval, preview, null, CancellationToken.None);
        Assert.AreEqual(AdvisoryAiSendRefusal.ProviderUnavailable, outcome.Refusal);
        Assert.IsFalse(outcome.Dispatched);
        Assert.AreEqual(0L, await fixture.ScalarAsync("SELECT COUNT(*) FROM ai_requests;"), "nothing was reserved");
        Assert.ThrowsExactly<ArgumentException>(() => new AdvisoryAiProviderAdapters(
            (new FakeCredentials("sk-a", null), new FakeTransport(_ => Received(Body("x")))),
            (new FakeCredentials("sk-b", null), new FakeTransport(_ => Received(Body("x"))))), "one pair per provider");
    }

    [TestMethod]
    [TestCategory("FR-BUD-02")]
    public async Task CancellingBeforeTheClaimSendsNothingAndCostsNothing()
    {
        await using Fixture fixture = await Fixture.CreateReadyAsync();
        FakeTransport transport = new(_ => Received(Body("x")));
        using CancellationTokenSource cancel = new();
        (AdvisoryAiRequestWorkflow workflow, FakeCredentials credentials) = fixture.Workflow(transport, onRead: cancel.Cancel);
        (AdvisoryAiApproval approval, AdvisoryAiRequestPreview preview) = await fixture.ApproveAsync();
        AdvisoryAiSendOutcome outcome = await workflow.SendAsync(approval, preview, null, cancel.Token);
        Assert.AreEqual(AdvisoryAiSendRefusal.ClaimNotRecorded, outcome.Refusal);
        Assert.AreEqual(0, transport.Calls);
        Assert.AreEqual("NotSent", await fixture.ScalarAsync("SELECT state FROM ai_requests;"));
        Assert.IsTrue(credentials.LastKeyDisposed);
    }

    [TestMethod]
    [TestCategory("FR-AI-05")]
    public async Task AMalformedResponseIsNotShownAndItsCostStaysUncertain()
    {
        await using Fixture fixture = await Fixture.CreateReadyAsync();
        string tool = "{\"id\":\"resp_t\",\"object\":\"response\",\"status\":\"completed\",\"model\":\"" + Model.ModelId + "\",\"store\":false,\"tools\":[],\"output\":[{\"type\":\"function_call\",\"name\":\"run\",\"arguments\":\"{}\"}]," + UsageJson + "}";
        (AdvisoryAiRequestWorkflow workflow, _) = fixture.Workflow(new FakeTransport(_ => Received(tool)));
        (AdvisoryAiApproval approval, AdvisoryAiRequestPreview preview) = await fixture.ApproveAsync();
        AdvisoryAiSendOutcome outcome = await workflow.SendAsync(approval, preview, null, CancellationToken.None);
        Assert.AreEqual(AdvisoryAiAnswerStatus.Malformed, outcome.AnswerStatus);
        Assert.IsNull(outcome.AnswerText);
        Assert.IsNull(outcome.Saved);
        Assert.AreEqual(AdvisoryAiRequestState.Uncertain, outcome.Settlement!.State);
    }

    // ---- Fixtures -----------------------------------------------------------------------------------------------------------------

    private static AdvisoryAiReservationRequest Request(AdvisoryAiTariff tariff, AdvisoryAiApprovalBinding binding, DateTimeOffset at, bool test = false) =>
        new(Guid.NewGuid(), Model.ModelId, Model.ProfileVersion, binding, 2_000, 1_024, AdvisoryAiCost.Reservation(2_000, 1_024, tariff), test, at);

    private static AdvisoryAiSavedExplanation Explanation(AdvisoryAiSourceKind source, string origin, DateTimeOffset created) =>
        new(Guid.NewGuid(), Guid.NewGuid(), source, origin, Model.ModelId, AdvisoryAiAnswerStatus.Completed, AdvisoryAiIncompleteReason.None, "Synthetic explanation.", false, created);

    private static string Body(string text) =>
        "{\"id\":\"resp_" + Guid.NewGuid().ToString("N") + "\",\"object\":\"response\",\"status\":\"completed\",\"model\":\"" + Model.ModelId + "\",\"store\":false,\"tools\":[],\"output\":[{\"type\":\"message\",\"role\":\"assistant\",\"content\":[{\"type\":\"output_text\",\"text\":" +
        System.Text.Json.JsonSerializer.Serialize(text) + ",\"annotations\":[]}]}]," + UsageJson + "}";

    private static AdvisoryAiTransportResult Received(string body) => new(AdvisoryAiTransportOutcome.ResponseReceived, 200, [.. Encoding.UTF8.GetBytes(body)]);

    private sealed class FakeTransport(Func<AdvisoryAiApproval, AdvisoryAiTransportResult> respond) : IAdvisoryAiTransport
    {
        public AdvisoryAiProviderId Provider => AdvisoryAiProviderId.OpenAI;
        public int Calls { get; private set; }
        public bool SawApprovedBytes { get; private set; }

        public Task<AdvisoryAiTransportResult> SendOnceAsync(AdvisoryAiApproval approval, AdvisoryAiApiKey key, CancellationToken cancellationToken)
        {
            Calls++;
            SawApprovedBytes = approval.MatchesWireBody(approval.Body.AsSpan()) && key.Length > 0;
            return Task.FromResult(respond(approval));
        }
    }

    private sealed class FakeCredentials(string? key, Action? onRead) : IAdvisoryAiCredentialStore
    {
        public AdvisoryAiProviderId Provider => AdvisoryAiProviderId.OpenAI;
        private AdvisoryAiApiKey? last;
        public int Reads { get; private set; }
        public bool LastKeyDisposed
        {
            get
            {
                try
                {
                    _ = last!.Length;
                    return false;
                }
                catch (ObjectDisposedException)
                {
                    return true;
                }
            }
        }

        public Task<AdvisoryAiApiKey?> ReadForDispatchAsync(CancellationToken cancellationToken)
        {
            Reads++;
            onRead?.Invoke();
            if (key is null) return Task.FromResult<AdvisoryAiApiKey?>(null);
            AdvisoryAiApiKey.TryCreate(key, out last);
            return Task.FromResult(last);
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

        /// <summary>Bytes on disk for the database and its WAL and shared-memory files.</summary>
        private static readonly string[] Suffixes = ["", "-wal", "-shm"];

        public long BytesOnDisk() => Suffixes.Select(suffix => new FileInfo(path + suffix)).Where(file => file.Exists).Sum(file => file.Length);

        public static async Task<Fixture> CreateAsync()
        {
            string path = Path.Combine(Path.GetTempPath(), $"pcsteward-ai-{Guid.NewGuid():N}.db");
            SqliteDatabase store = new(path);
            await store.MigrateAsync(CancellationToken.None);
            return new(path, store);
        }

        /// <summary>Connected, prices confirmed and no prior use declared: the state a first paid request needs.</summary>
        public static async Task<Fixture> CreateReadyAsync()
        {
            Fixture fixture = await CreateAsync();
            await fixture.Store.SetConnectedAsync(true, Now, CancellationToken.None);
            await fixture.ConfirmAsync();
            await fixture.Store.DeclareContinuityAsync(AdvisoryAiContinuity.DeclaredNoPriorUse, Now, CancellationToken.None);
            return fixture;
        }

        public SqliteDatabase NewStore() => new(path);

        public async Task<AdvisoryAiTariff> ConfirmAsync() => (await Store.ConfirmTariffAsync(Rates, CancellationToken.None))!;

        /// <summary>One committed change that makes an earlier approval stale, as another app window or process would make it.</summary>
        public async Task ChangeAsync(string change)
        {
            AdvisoryAiSettings settings = await Store.GetSettingsAsync(CancellationToken.None);
            switch (change)
            {
                case "limits":
                    Assert.AreEqual(AdvisoryAiSettingsWrite.Saved, await Store.SaveLimitsAsync(AdvisoryAiLimits.Defaults with { RequestCap = new(0) }, settings.SettingsVersion, CancellationToken.None));
                    break;
                case "disconnect":
                    Assert.AreEqual(AdvisoryAiSettingsWrite.Saved, await Store.SetConnectedAsync(false, Now, CancellationToken.None));
                    break;
                case "key":
                    Assert.AreEqual(AdvisoryAiSettingsWrite.Saved, await Store.AdvanceCredentialGenerationAsync(CancellationToken.None));
                    break;
                case "prices":
                    Assert.IsNotNull(await Store.ConfirmTariffAsync(Rates with { ConfirmedAtUtc = Now.AddMinutes(1) }, CancellationToken.None));
                    break;
                default:
                    Assert.Fail(change);
                    break;
            }
        }

        public async Task<AdvisoryAiApprovalBinding> BindingAsync(AdvisoryAiTariff tariff) => (await Store.GetSettingsAsync(CancellationToken.None)).BindingFor(tariff);

        public async Task<AdvisoryAiTariff> TariffAsync() => (await Store.GetLatestTariffAsync(Model.ModelId, Model.ServiceTier, CancellationToken.None))!;

        public async Task<AdvisoryAiReserveResult> ReserveAsync(AdvisoryAiTariff tariff, DateTimeOffset at, AdvisoryAiPaidRequestMode mode = AdvisoryAiPaidRequestMode.Ordinary, bool release = false)
        {
            AdvisoryAiReservationRequest request = Request(tariff, await BindingAsync(tariff), at, mode == AdvisoryAiPaidRequestMode.FirstLiveTest);
            AdvisoryAiReserveResult result = await Store.ReserveAsync(request, AdvisoryAiLimits.Defaults, mode, tariff, Model, CancellationToken.None);
            if (release && result.Reserved) await Store.ReleaseUnclaimedAsync(request.RequestId, at, CancellationToken.None);
            return result;
        }

        public async Task<AdvisoryAiReservationRequest> ClaimedAsync(AdvisoryAiTariff tariff, DateTimeOffset at)
        {
            AdvisoryAiReservationRequest request = Request(tariff, await BindingAsync(tariff), at);
            Assert.IsTrue((await Store.ReserveAsync(request, AdvisoryAiLimits.Defaults, AdvisoryAiPaidRequestMode.Ordinary, tariff, Model, CancellationToken.None)).Reserved);
            Assert.AreEqual(AdvisoryAiLedgerWrite.Done, await Store.ClaimDispatchAsync(request.RequestId, request.Binding, at, CancellationToken.None));
            return request;
        }

        public Task<AdvisoryAiLedgerWrite> SettleAsync(Guid requestId, AdvisoryAiSettlement settlement) =>
            Store.SettleAsync(requestId, settlement, null, null, Now, CancellationToken.None);

        public async Task<(AdvisoryAiApproval, AdvisoryAiRequestPreview)> ApproveAsync()
        {
            AdvisoryAiSettings settings = await Store.GetSettingsAsync(CancellationToken.None);
            AdvisoryAiTariff tariff = await TariffAsync();
            AdvisoryAiFailedUpdateFacts facts = new("Contoso Editor", "WinGet", "1.2.0", "1.3.0", "InstallerFailed", 1603,
                [new(Now.AddMinutes(-5), "InstallerFailed", 1603, "Installer failed with 1603.")]);
            AdvisoryAiInputAllowance allowance = AdvisoryAiInputAllowance.For(settings.Limits, Model, AdvisoryAiInstructions.Text);
            Assert.AreEqual(AdvisoryAiDraftProblem.None, AdvisoryAiFailedUpdateDraftBuilder.TryBuild(Guid.NewGuid(), facts, allowance, null, out AdvisoryAiEvidenceDraft? draft));
            Assert.AreEqual(AdvisoryAiPreviewProblem.None, AdvisoryAiRequestBuilder.TryBuild(draft!, Model, settings.Limits, out AdvisoryAiRequestPreview? preview));
            return (AdvisoryAiApproval.Approve(preview!, settings.BindingFor(tariff), Now), preview!);
        }

        public (AdvisoryAiRequestWorkflow Workflow, FakeCredentials Credentials) Workflow(IAdvisoryAiTransport transport, string? key = "sk-synthetic-not-a-real-key",
            AdvisoryAiPaidRequestMode mode = AdvisoryAiPaidRequestMode.Ordinary, IReadOnlyList<AdvisoryAiModelProfile>? reviewed = null, Action? onRead = null,
            Func<DateTimeOffset>? utcNow = null, AdvisoryAiClockWatch? clockWatch = null)
        {
            FakeCredentials credentials = new(key, onRead);
            DateTimeOffset clock = Now.AddMinutes(1);
            return (new AdvisoryAiRequestWorkflow(Store, Store, new AdvisoryAiProviderAdapters((credentials, transport)), Store, reviewed ?? Reviewed, mode,
                utcNow ?? (() => clock = clock.AddSeconds(1)), clockWatch), credentials);
        }

        public async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
        {
            await using SqliteConnection connection = new($"Data Source={path};Pooling=False");
            await connection.OpenAsync();
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = sql;
            foreach ((string name, object value) in parameters) command.Parameters.AddWithValue(name, value);
            await command.ExecuteNonQueryAsync();
        }

        public async Task<object?> ScalarAsync(string sql, params (string Name, object Value)[] parameters)
        {
            await using SqliteConnection connection = new($"Data Source={path};Pooling=False");
            await connection.OpenAsync();
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = sql;
            foreach ((string name, object value) in parameters) command.Parameters.AddWithValue(name, value);
            return await command.ExecuteScalarAsync();
        }

        public ValueTask DisposeAsync()
        {
            SqliteConnection.ClearAllPools();
            foreach (string suffix in new[] { "", "-wal", "-shm" }) File.Delete(path + suffix);
            return ValueTask.CompletedTask;
        }
    }
}
