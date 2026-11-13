using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;
using PCSteward.Application;
using PCSteward.Domain;

namespace PCSteward.Persistence.Sqlite;

/// <summary>
/// FR-AI-01/03/05, FR-BUD-01/02/03 (Task 68, migration 14; ADR-015/027/028/029): AI settings, owner-confirmed tariffs, the request
/// ledger and saved explanations. Every ledger change is one immediate (write-locked) transaction, so a reservation's commitment
/// check and insert cannot interleave with another process. Ledger rows store only the ADR-029 minimal record: no prompt, evidence,
/// body hash, key or identity. Explanations have no foreign key to the ledger, so clearing or expiring one never touches spending.
/// Every value read back is validated; a row that fails is treated as a reason to block, never as authority to send.
/// </summary>
public sealed partial class SqliteDatabase : IAdvisoryAiSettingsStore, IAdvisoryAiLedger, IAdvisoryAiExplanationStore
{
    internal static readonly string[] AdvisoryAiTables = ["ai_explanations", "ai_requests", "ai_tariffs", "ai_accounting_state", "ai_settings"];

    private static async Task CreateAdvisoryAiSchemaAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        await ExecuteAsync(connection, """
            CREATE TABLE ai_settings (
                id INTEGER NOT NULL PRIMARY KEY CHECK(id=1),
                connected INTEGER NOT NULL CHECK(connected IN (0,1)),
                settings_version INTEGER NOT NULL CHECK(settings_version >= 0),
                consent_version INTEGER NOT NULL CHECK(consent_version >= 0),
                credential_generation INTEGER NOT NULL CHECK(credential_generation >= 0),
                max_input_tokens INTEGER NOT NULL CHECK(max_input_tokens > 0),
                max_output_tokens INTEGER NOT NULL CHECK(max_output_tokens > 0),
                request_cap INTEGER NOT NULL CHECK(request_cap >= 0),
                monthly_budget INTEGER NOT NULL CHECK(monthly_budget >= 0),
                connection_changed_at TEXT CHECK(connection_changed_at IS NULL OR length(connection_changed_at)=28)
            );
            CREATE TABLE ai_accounting_state (
                id INTEGER NOT NULL PRIMARY KEY CHECK(id=1),
                continuity TEXT NOT NULL CHECK(continuity IN ('NotEstablished','DeclaredNoPriorUse','PriorUseUnknown','FreshPeriodConfirmed')),
                last_accepted_at TEXT CHECK(last_accepted_at IS NULL OR length(last_accepted_at)=28),
                unknown_since_period INTEGER CHECK(unknown_since_period IS NULL OR unknown_since_period BETWEEN 200001 AND 999912),
                review_block INTEGER NOT NULL CHECK(review_block IN (0,1))
            );
            CREATE TABLE ai_tariffs (
                version INTEGER NOT NULL PRIMARY KEY CHECK(version >= 1),
                model_id TEXT NOT NULL CHECK(length(model_id) BETWEEN 1 AND 128),
                service_tier TEXT NOT NULL CHECK(length(service_tier) BETWEEN 1 AND 64),
                input_rate INTEGER NOT NULL CHECK(input_rate > 0),
                cached_input_rate INTEGER NOT NULL CHECK(cached_input_rate > 0),
                output_rate INTEGER NOT NULL CHECK(output_rate > 0),
                confirmed_at TEXT NOT NULL CHECK(length(confirmed_at)=28)
            );
            CREATE TABLE ai_requests (
                request_id TEXT NOT NULL PRIMARY KEY CHECK(length(request_id)=36),
                period INTEGER NOT NULL CHECK(period BETWEEN 200001 AND 999912),
                model_id TEXT NOT NULL CHECK(length(model_id) BETWEEN 1 AND 128),
                model_profile_version INTEGER NOT NULL CHECK(model_profile_version >= 1),
                tariff_version INTEGER NOT NULL REFERENCES ai_tariffs(version),
                settings_version INTEGER NOT NULL CHECK(settings_version >= 0),
                input_token_bound INTEGER NOT NULL CHECK(input_token_bound > 0),
                max_output_tokens INTEGER NOT NULL CHECK(max_output_tokens > 0),
                reservation INTEGER NOT NULL CHECK(reservation >= 0),
                committed INTEGER NOT NULL CHECK(committed >= 0),
                first_live_test INTEGER NOT NULL CHECK(first_live_test IN (0,1)),
                state TEXT NOT NULL CHECK(state IN ('Reserved','NotSent','DispatchClaimed','Settled','Uncertain')),
                reason TEXT NOT NULL CHECK(length(reason) BETWEEN 1 AND 64),
                above_reservation INTEGER NOT NULL CHECK(above_reservation IN (0,1)),
                reserved_at TEXT NOT NULL CHECK(length(reserved_at)=28),
                claimed_at TEXT CHECK(claimed_at IS NULL OR length(claimed_at)=28),
                finished_at TEXT CHECK(finished_at IS NULL OR length(finished_at)=28),
                provider_response_id TEXT UNIQUE CHECK(provider_response_id IS NULL OR length(provider_response_id) BETWEEN 1 AND 256),
                input_tokens INTEGER CHECK(input_tokens IS NULL OR input_tokens >= 0),
                cached_input_tokens INTEGER CHECK(cached_input_tokens IS NULL OR cached_input_tokens >= 0),
                output_tokens INTEGER CHECK(output_tokens IS NULL OR output_tokens >= 0),
                reasoning_tokens INTEGER CHECK(reasoning_tokens IS NULL OR reasoning_tokens >= 0),
                total_tokens INTEGER CHECK(total_tokens IS NULL OR total_tokens >= 0)
            );
            CREATE INDEX ix_ai_requests_period ON ai_requests(period, state);
            CREATE TABLE ai_explanations (
                id TEXT NOT NULL PRIMARY KEY CHECK(length(id)=36),
                request_id TEXT NOT NULL CHECK(length(request_id)=36),
                source TEXT NOT NULL CHECK(source IN ('FailedUpdate','DiagnosticTimeline')),
                origin_id TEXT NOT NULL CHECK(length(origin_id) BETWEEN 1 AND 128),
                model_id TEXT NOT NULL CHECK(length(model_id) BETWEEN 1 AND 128),
                status TEXT NOT NULL CHECK(length(status) BETWEEN 1 AND 32),
                incomplete_reason TEXT NOT NULL CHECK(length(incomplete_reason) BETWEEN 1 AND 32),
                answer TEXT CHECK(answer IS NULL OR (typeof(answer)='text' AND length(CAST(answer AS BLOB)) <= 4194304)),
                answer_truncated INTEGER NOT NULL CHECK(answer_truncated IN (0,1)),
                item_bytes INTEGER NOT NULL CHECK(item_bytes BETWEEN 1 AND 4194304),
                created_at TEXT NOT NULL CHECK(length(created_at)=28),
                expires_at TEXT NOT NULL CHECK(length(expires_at)=28)
            );
            CREATE INDEX ix_ai_explanations_origin ON ai_explanations(source, origin_id, created_at);
            CREATE INDEX ix_ai_explanations_created ON ai_explanations(created_at, id);
            INSERT INTO ai_accounting_state(id,continuity,last_accepted_at,unknown_since_period,review_block) VALUES (1,'NotEstablished',NULL,NULL,0);
            INSERT INTO schema_migrations(version) VALUES (14);
            """, cancellationToken, transaction).ConfigureAwait(false);
        AdvisoryAiLimits defaults = AdvisoryAiLimits.Defaults;
        await AiExecuteAsync(connection, transaction, "INSERT INTO ai_settings VALUES (1,0,0,0,0,$in,$out,$cap,$month,NULL);", cancellationToken,
            ("$in", defaults.MaximumInputTokens), ("$out", defaults.MaximumOutputTokens), ("$cap", defaults.RequestCap.Value), ("$month", defaults.MonthlyBudget.Value)).ConfigureAwait(false);
    }

    private static async Task ValidateAdvisoryAiSchemaAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken) =>
        await ExecuteAsync(connection, "SELECT connected FROM ai_settings LIMIT 0; SELECT continuity FROM ai_accounting_state LIMIT 0; SELECT version FROM ai_tariffs LIMIT 0; SELECT state, committed FROM ai_requests LIMIT 0; SELECT answer FROM ai_explanations LIMIT 0;",
            cancellationToken, transaction).ConfigureAwait(false);

    // ---- Settings and tariffs -------------------------------------------------------------------------------------------------

    public async Task<AdvisoryAiSettings> GetSettingsAsync(CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenAiAsync(cancellationToken).ConfigureAwait(false);
        return await ReadSettingsAsync(connection, null, cancellationToken).ConfigureAwait(false) ?? DisconnectedFallback;
    }

    /// <summary>A settings row that fails validation reads as disconnected, so nothing can be sent until the owner reconnects.</summary>
    private static AdvisoryAiSettings DisconnectedFallback => AdvisoryAiSettings.Initial;

    private static async Task<AdvisoryAiSettings?> ReadSettingsAsync(SqliteConnection connection, SqliteTransaction? transaction, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = AiCommand(connection, transaction,
            "SELECT connected,settings_version,consent_version,credential_generation,max_input_tokens,max_output_tokens,request_cap,monthly_budget FROM ai_settings WHERE id=1;");
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
        if (!TryLong(reader, 0, out long connected) || connected is not (0 or 1) || !TryLong(reader, 1, out long settingsVersion) || !TryLong(reader, 2, out long consentVersion) ||
            !TryLong(reader, 3, out long generation) || !TryLong(reader, 4, out long input) || !TryLong(reader, 5, out long output) ||
            !TryLong(reader, 6, out long cap) || !TryLong(reader, 7, out long month) || settingsVersion < 0 || consentVersion < 0 || generation < 0 ||
            input is <= 0 or > int.MaxValue || output is <= 0 or > int.MaxValue || cap < 0 || month < 0)
            return null;
        return new(connected == 1, settingsVersion, consentVersion, generation, new((int)input, (int)output, new(cap), new(month)));
    }

    public async Task<AdvisoryAiSettingsWrite> SaveLimitsAsync(AdvisoryAiLimits limits, long expectedSettingsVersion, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(limits);
        if (AdvisoryAiLimitValidation.Validate(limits) != AdvisoryAiLimitProblem.None) return AdvisoryAiSettingsWrite.Invalid;
        return await AiWriteAsync(async (connection, transaction) =>
        {
            int changed = await AiExecuteAsync(connection, transaction,
                "UPDATE ai_settings SET max_input_tokens=$in,max_output_tokens=$out,request_cap=$cap,monthly_budget=$month,settings_version=settings_version+1 WHERE id=1 AND settings_version=$expected;",
                cancellationToken, ("$in", limits.MaximumInputTokens), ("$out", limits.MaximumOutputTokens), ("$cap", limits.RequestCap.Value), ("$month", limits.MonthlyBudget.Value),
                ("$expected", expectedSettingsVersion)).ConfigureAwait(false);
            return changed == 1 ? AdvisoryAiSettingsWrite.Saved : AdvisoryAiSettingsWrite.VersionConflict;
        }, AdvisoryAiSettingsWrite.StorageFailed, cancellationToken).ConfigureAwait(false);
    }

    public Task<AdvisoryAiSettingsWrite> SetConnectedAsync(bool connected, DateTimeOffset nowUtc, CancellationToken cancellationToken) =>
        AiWriteAsync(async (connection, transaction) =>
        {
            int changed = await AiExecuteAsync(connection, transaction, "UPDATE ai_settings SET connected=$connected,consent_version=consent_version+1,connection_changed_at=$now WHERE id=1;",
                cancellationToken, ("$connected", connected ? 1 : 0), ("$now", Utc(nowUtc))).ConfigureAwait(false);
            return changed == 1 ? AdvisoryAiSettingsWrite.Saved : AdvisoryAiSettingsWrite.StorageFailed;
        }, AdvisoryAiSettingsWrite.StorageFailed, cancellationToken);

    public Task<AdvisoryAiSettingsWrite> AdvanceCredentialGenerationAsync(CancellationToken cancellationToken) =>
        AiWriteAsync(async (connection, transaction) =>
        {
            int changed = await AiExecuteAsync(connection, transaction, "UPDATE ai_settings SET credential_generation=credential_generation+1 WHERE id=1;", cancellationToken).ConfigureAwait(false);
            return changed == 1 ? AdvisoryAiSettingsWrite.Saved : AdvisoryAiSettingsWrite.StorageFailed;
        }, AdvisoryAiSettingsWrite.StorageFailed, cancellationToken);

    public async Task<AdvisoryAiTariff?> ConfirmTariffAsync(AdvisoryAiTariff tariff, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tariff);
        if (string.IsNullOrEmpty(tariff.ModelId) || tariff.ModelId.Length > 128 || string.IsNullOrEmpty(tariff.ServiceTier) || tariff.ServiceTier.Length > 64 ||
            tariff.InputPerMillion.Value <= 0 || tariff.CachedInputPerMillion.Value <= 0 || tariff.OutputPerMillion.Value <= 0)
            return null;
        return await AiWriteAsync<AdvisoryAiTariff?>(async (connection, transaction) =>
        {
            await using SqliteCommand next = AiCommand(connection, transaction, "SELECT COALESCE(MAX(version),0)+1 FROM ai_tariffs;");
            long version = Convert.ToInt64(await next.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
            AdvisoryAiTariff stored = tariff with { Version = version, ConfirmedAtUtc = AtUtcPrecision(tariff.ConfirmedAtUtc) };
            await AiExecuteAsync(connection, transaction, "INSERT INTO ai_tariffs VALUES ($version,$model,$tier,$in,$cached,$out,$confirmed);", cancellationToken,
                ("$version", version), ("$model", stored.ModelId), ("$tier", stored.ServiceTier), ("$in", stored.InputPerMillion.Value),
                ("$cached", stored.CachedInputPerMillion.Value), ("$out", stored.OutputPerMillion.Value), ("$confirmed", Utc(stored.ConfirmedAtUtc))).ConfigureAwait(false);
            return stored;
        }, null, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AdvisoryAiTariff?> GetLatestTariffAsync(string modelId, string serviceTier, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(modelId);
        ArgumentNullException.ThrowIfNull(serviceTier);
        await using SqliteConnection connection = await OpenAiAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = AiCommand(connection, null,
            "SELECT version,model_id,service_tier,input_rate,cached_input_rate,output_rate,confirmed_at FROM ai_tariffs WHERE model_id=$model AND service_tier=$tier ORDER BY version DESC LIMIT 1;",
            ("$model", modelId), ("$tier", serviceTier));
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
        if (!TryLong(reader, 0, out long version) || version < 1 || reader.GetValue(1) is not string model || reader.GetValue(2) is not string tier ||
            !TryLong(reader, 3, out long input) || !TryLong(reader, 4, out long cached) || !TryLong(reader, 5, out long output) ||
            reader.GetValue(6) is not string confirmedText || !TryParseUtc(confirmedText, out DateTimeOffset confirmed) ||
            !string.Equals(model, modelId, StringComparison.Ordinal) || !string.Equals(tier, serviceTier, StringComparison.Ordinal) || input <= 0 || cached <= 0 || output <= 0)
            return null;
        return new(version, model, tier, new(input), new(cached), new(output), confirmed);
    }

    // ---- Ledger -------------------------------------------------------------------------------------------------------------------

    public async Task<AdvisoryAiReserveResult> ReserveAsync(AdvisoryAiReservationRequest request, AdvisoryAiLimits limits, AdvisoryAiPaidRequestMode mode,
        AdvisoryAiTariff tariff, AdvisoryAiModelProfile model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(tariff);
        ArgumentNullException.ThrowIfNull(model);
        if (request.RequestId == Guid.Empty || request.Reservation.Value < 0 || request.InputTokenUpperBound <= 0 || request.MaximumOutputTokens <= 0 ||
            !string.Equals(request.ModelId, model.ModelId, StringComparison.Ordinal) || request.TariffVersion != tariff.Version ||
            request.FirstLiveTest != (mode == AdvisoryAiPaidRequestMode.FirstLiveTest))
            throw new ArgumentException("The reservation does not match its model, tariff or mode.", nameof(request));
        try
        {
            await using SqliteConnection connection = await OpenAiAsync(cancellationToken).ConfigureAwait(false);
            await using SqliteTransaction transaction = connection.BeginTransaction(deferred: false);
            AdvisoryAiLedgerSnapshot? snapshot = await SnapshotAsync(connection, transaction, request.Period, request.ReservedAtUtc, cancellationToken).ConfigureAwait(false);
            if (snapshot is null) return new(AdvisoryAiReservationProblem.LedgerNeedsReview, null);
            AdvisoryAiReservationProblem problem = AdvisoryAiReservationRule.Check(request.Reservation, limits, snapshot, mode, tariff, model, request.ReservedAtUtc);
            if (problem != AdvisoryAiReservationProblem.None) return new(problem, snapshot);
            await AiExecuteAsync(connection, transaction, """
                INSERT INTO ai_requests(request_id,period,model_id,model_profile_version,tariff_version,settings_version,input_token_bound,max_output_tokens,
                    reservation,committed,first_live_test,state,reason,above_reservation,reserved_at)
                VALUES ($id,$period,$model,$profile,$tariff,$settings,$in,$out,$reservation,$reservation,$test,'Reserved','None',0,$at);
                """, cancellationToken, ("$id", Id(request.RequestId)), ("$period", request.Period.Key), ("$model", request.ModelId), ("$profile", request.ModelProfileVersion),
                ("$tariff", request.TariffVersion), ("$settings", request.SettingsVersion), ("$in", request.InputTokenUpperBound), ("$out", request.MaximumOutputTokens),
                ("$reservation", request.Reservation.Value), ("$test", request.FirstLiveTest ? 1 : 0), ("$at", Utc(request.ReservedAtUtc))).ConfigureAwait(false);
            await AiExecuteAsync(connection, transaction, "UPDATE ai_accounting_state SET last_accepted_at=$at WHERE id=1 AND (last_accepted_at IS NULL OR last_accepted_at < $at);",
                cancellationToken, ("$at", Utc(request.ReservedAtUtc))).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new(AdvisoryAiReservationProblem.None, snapshot);
        }
        catch (SqliteException)
        {
            return new(AdvisoryAiReservationProblem.None, null, StorageFailed: true);
        }
    }

    /// <summary>The commitments and ledger-wide gates, read inside the reservation's write transaction. Null when a row is invalid.</summary>
    private static async Task<AdvisoryAiLedgerSnapshot?> SnapshotAsync(SqliteConnection connection, SqliteTransaction transaction, AdvisoryAiPeriod period, DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        AdvisoryAiAccountingState? state = await ReadAccountingStateAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        if (state is null) return null;
        long settled = 0, outstanding = 0, testSettled = 0, testOutstanding = 0, rows = 0;
        bool inFlight = false;
        await using (SqliteCommand command = AiCommand(connection, transaction, "SELECT period,state,committed,first_live_test FROM ai_requests;"))
        await using (SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                rows++;
                if (!TryLong(reader, 0, out long key) || !AdvisoryAiPeriod.TryParseKey(key, out AdvisoryAiPeriod rowPeriod) || !TryName(reader.GetValue(1), out AdvisoryAiRequestState rowState) ||
                    !TryLong(reader, 2, out long committed) || committed < 0 || !TryLong(reader, 3, out long test) || test is not (0 or 1))
                    return null;
                try
                {
                    checked
                    {
                        bool holds = AdvisoryAiRequestStates.HoldsReservation(rowState);
                        if (rowState == AdvisoryAiRequestState.Settled && rowPeriod == period) settled += committed;
                        if (holds && rowPeriod == period) outstanding += committed;
                        if (rowState == AdvisoryAiRequestState.Settled && test == 1) testSettled += committed;
                        if (holds && test == 1) testOutstanding += committed;
                    }
                }
                catch (OverflowException)
                {
                    return null;
                }
                inFlight |= AdvisoryAiRequestStates.IsInFlight(rowState);
            }
        }
        return new(new(settled), new(outstanding), new(testSettled), new(testOutstanding), inFlight, state.ReviewBlock, rows >= IAdvisoryAiLedger.MaximumDetailedRows,
            state.Continuity, AdvisoryAiClock.Evaluate(state.LastAcceptedUtc, nowUtc));
    }

    public Task<AdvisoryAiLedgerWrite> ClaimDispatchAsync(Guid requestId, DateTimeOffset nowUtc, CancellationToken cancellationToken) =>
        MoveAsync(requestId, AdvisoryAiRequestState.Reserved, AdvisoryAiRequestState.DispatchClaimed,
            "UPDATE ai_requests SET state='DispatchClaimed',claimed_at=$now WHERE request_id=$id AND state='Reserved';", nowUtc, cancellationToken);

    public Task<AdvisoryAiLedgerWrite> ReleaseUnclaimedAsync(Guid requestId, DateTimeOffset nowUtc, CancellationToken cancellationToken) =>
        MoveAsync(requestId, AdvisoryAiRequestState.Reserved, AdvisoryAiRequestState.NotSent,
            "UPDATE ai_requests SET state='NotSent',committed=0,reason='NotSent',finished_at=$now WHERE request_id=$id AND state='Reserved';", nowUtc, cancellationToken);

    private Task<AdvisoryAiLedgerWrite> MoveAsync(Guid requestId, AdvisoryAiRequestState from, AdvisoryAiRequestState to, string sql, DateTimeOffset nowUtc, CancellationToken cancellationToken) =>
        AiWriteAsync(async (connection, transaction) =>
        {
            int changed = await AiExecuteAsync(connection, transaction, sql, cancellationToken, ("$id", Id(requestId)), ("$now", Utc(nowUtc))).ConfigureAwait(false);
            if (changed == 1) return AdvisoryAiLedgerWrite.Done;
            AdvisoryAiRequestState? current = await RequestStateAsync(connection, transaction, requestId, cancellationToken).ConfigureAwait(false);
            return current is null ? AdvisoryAiLedgerWrite.NotFound : current == to && from != to ? AdvisoryAiLedgerWrite.AlreadyDone : AdvisoryAiLedgerWrite.WrongState;
        }, AdvisoryAiLedgerWrite.StorageFailed, cancellationToken);

    public async Task<AdvisoryAiLedgerWrite> SettleAsync(Guid requestId, AdvisoryAiSettlement settlement, AdvisoryAiUsage? usage, string? providerResponseId,
        DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settlement);
        if (settlement.State is not (AdvisoryAiRequestState.Settled or AdvisoryAiRequestState.Uncertain) || settlement.Committed.Value < 0)
            throw new ArgumentException("A settlement ends in Settled or Uncertain with a non-negative amount.", nameof(settlement));
        string? responseId = providerResponseId is { Length: >= 1 and <= 256 } ? providerResponseId : null;
        AdvisoryAiUsage? counts = usage is { InputTokens: >= 0, CachedInputTokens: >= 0, OutputTokens: >= 0, ReasoningTokens: >= 0, TotalTokens: >= 0 } ? usage : null;
        return await AiWriteAsync(async (connection, transaction) =>
        {
            (AdvisoryAiRequestState State, long Committed, string? ResponseId)? row = await SettlementRowAsync(connection, transaction, requestId, cancellationToken).ConfigureAwait(false);
            if (row is null) return AdvisoryAiLedgerWrite.NotFound;
            if (row.Value.State is AdvisoryAiRequestState.Settled or AdvisoryAiRequestState.Uncertain)
            {
                if (row.Value.State == settlement.State && row.Value.Committed == settlement.Committed.Value && string.Equals(row.Value.ResponseId, responseId, StringComparison.Ordinal))
                    return AdvisoryAiLedgerWrite.AlreadyDone;
                await SetReviewBlockAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
                return AdvisoryAiLedgerWrite.Conflict;
            }
            if (row.Value.State != AdvisoryAiRequestState.DispatchClaimed)
            {
                // A result for a request that was never claimed, or was released, is unbound evidence: stop paid requests for review.
                await SetReviewBlockAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
                return row.Value.State == AdvisoryAiRequestState.NotSent ? AdvisoryAiLedgerWrite.Conflict : AdvisoryAiLedgerWrite.WrongState;
            }

            AdvisoryAiSettlement applied = settlement;
            AdvisoryAiLedgerWrite result = AdvisoryAiLedgerWrite.Done;
            if (responseId is not null && await ResponseIdUsedAsync(connection, transaction, responseId, cancellationToken).ConfigureAwait(false))
            {
                // The same provider response cannot settle two requests: keep the larger of the two figures as uncertain and stop.
                long kept = Math.Max(row.Value.Committed, settlement.Committed.Value);
                applied = new(AdvisoryAiRequestState.Uncertain, new(kept), AdvisoryAiUncertainReason.UsageMissingOrUnreliable, true);
                responseId = null;
                result = AdvisoryAiLedgerWrite.Conflict;
            }
            await AiExecuteAsync(connection, transaction, """
                UPDATE ai_requests SET state=$state,committed=$committed,reason=$reason,above_reservation=$above,finished_at=$now,provider_response_id=$response,
                    input_tokens=$in,cached_input_tokens=$cached,output_tokens=$out,reasoning_tokens=$reasoning,total_tokens=$total
                WHERE request_id=$id AND state='DispatchClaimed';
                """, cancellationToken, ("$state", applied.State.ToString()), ("$committed", applied.Committed.Value), ("$reason", applied.Reason.ToString()),
                ("$above", applied.AboveReservation ? 1 : 0), ("$now", Utc(nowUtc)), ("$response", responseId), ("$in", counts?.InputTokens), ("$cached", counts?.CachedInputTokens),
                ("$out", counts?.OutputTokens), ("$reasoning", counts?.ReasoningTokens), ("$total", counts?.TotalTokens), ("$id", Id(requestId))).ConfigureAwait(false);
            if (applied.NeedsReview) await SetReviewBlockAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            return result;
        }, AdvisoryAiLedgerWrite.StorageFailed, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AdvisoryAiRecovery> RecoverInterruptedAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenAiAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = connection.BeginTransaction(deferred: false);
        int released = await AiExecuteAsync(connection, transaction,
            "UPDATE ai_requests SET state='NotSent',committed=0,reason='NotSent',finished_at=$now WHERE state='Reserved';", cancellationToken, ("$now", Utc(nowUtc))).ConfigureAwait(false);
        int uncertain = await AiExecuteAsync(connection, transaction,
            "UPDATE ai_requests SET state='Uncertain',reason='Interrupted',finished_at=$now WHERE state='DispatchClaimed';", cancellationToken, ("$now", Utc(nowUtc))).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new(released, uncertain);
    }

    public async Task<AdvisoryAiAccountingState> GetAccountingStateAsync(CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenAiAsync(cancellationToken).ConfigureAwait(false);
        // An unreadable state row blocks paid requests: it reads as review-blocked with no established continuity.
        return await ReadAccountingStateAsync(connection, null, cancellationToken).ConfigureAwait(false)
            ?? new(AdvisoryAiContinuity.NotEstablished, null, null, true);
    }

    private static async Task<AdvisoryAiAccountingState?> ReadAccountingStateAsync(SqliteConnection connection, SqliteTransaction? transaction, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = AiCommand(connection, transaction, "SELECT continuity,last_accepted_at,unknown_since_period,review_block FROM ai_accounting_state WHERE id=1;");
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false) || !TryName(reader.GetValue(0), out AdvisoryAiContinuity continuity) ||
            !TryLong(reader, 3, out long review) || review is not (0 or 1))
            return null;
        DateTimeOffset? last = null;
        if (!reader.IsDBNull(1))
        {
            if (reader.GetValue(1) is not string text || !TryParseUtc(text, out DateTimeOffset parsed)) return null;
            last = parsed;
        }
        AdvisoryAiPeriod? since = null;
        if (!reader.IsDBNull(2))
        {
            if (!TryLong(reader, 2, out long key) || !AdvisoryAiPeriod.TryParseKey(key, out AdvisoryAiPeriod parsed)) return null;
            since = parsed;
        }
        return new(continuity, last, since, review == 1);
    }

    public Task<AdvisoryAiAccountingWrite> DeclareContinuityAsync(AdvisoryAiContinuity answer, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        if (answer is not (AdvisoryAiContinuity.DeclaredNoPriorUse or AdvisoryAiContinuity.PriorUseUnknown))
            throw new ArgumentOutOfRangeException(nameof(answer), "The owner answers no prior use, or yes/unsure.");
        return AiWriteAsync(async (connection, transaction) =>
        {
            int changed = await AiExecuteAsync(connection, transaction,
                "UPDATE ai_accounting_state SET continuity=$answer,last_accepted_at=$now,unknown_since_period=$since WHERE id=1 AND continuity='NotEstablished';", cancellationToken,
                ("$answer", answer.ToString()), ("$now", Utc(nowUtc)), ("$since", answer == AdvisoryAiContinuity.PriorUseUnknown ? AdvisoryAiPeriod.Of(nowUtc).Key : null)).ConfigureAwait(false);
            return changed == 1 ? AdvisoryAiAccountingWrite.Done : AdvisoryAiAccountingWrite.NotApplicable;
        }, AdvisoryAiAccountingWrite.StorageFailed, cancellationToken);
    }

    public Task<AdvisoryAiAccountingWrite> ConfirmNewPeriodAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken) =>
        AiWriteAsync(async (connection, transaction) =>
        {
            AdvisoryAiAccountingState? state = await ReadAccountingStateAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            if (state?.LastAcceptedUtc is not DateTimeOffset last || AdvisoryAiClock.Evaluate(last, nowUtc) != AdvisoryAiClockVerdict.NewPeriodNeedsConfirmation)
                return AdvisoryAiAccountingWrite.NotApplicable;
            AdvisoryAiPeriod now = AdvisoryAiPeriod.Of(nowUtc);
            AdvisoryAiContinuity continuity = state.Continuity == AdvisoryAiContinuity.PriorUseUnknown && state.UnknownPriorUseSince is AdvisoryAiPeriod since && now > since
                ? AdvisoryAiContinuity.FreshPeriodConfirmed
                : state.Continuity;
            await AiExecuteAsync(connection, transaction, "UPDATE ai_accounting_state SET last_accepted_at=$now,continuity=$continuity WHERE id=1;", cancellationToken,
                ("$now", Utc(nowUtc)), ("$continuity", continuity.ToString())).ConfigureAwait(false);
            return AdvisoryAiAccountingWrite.Done;
        }, AdvisoryAiAccountingWrite.StorageFailed, cancellationToken);

    public Task<AdvisoryAiAccountingWrite> AcceptClockAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken) =>
        AiWriteAsync(async (connection, transaction) =>
        {
            AdvisoryAiAccountingState? state = await ReadAccountingStateAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            if (state?.LastAcceptedUtc is not DateTimeOffset last || AdvisoryAiClock.Evaluate(last, nowUtc) != AdvisoryAiClockVerdict.MovedBackwards ||
                AdvisoryAiPeriod.Of(nowUtc) != AdvisoryAiPeriod.Of(last))
                return AdvisoryAiAccountingWrite.NotApplicable;
            await AiExecuteAsync(connection, transaction, "UPDATE ai_accounting_state SET last_accepted_at=$now WHERE id=1;", cancellationToken, ("$now", Utc(nowUtc))).ConfigureAwait(false);
            return AdvisoryAiAccountingWrite.Done;
        }, AdvisoryAiAccountingWrite.StorageFailed, cancellationToken);

    public Task<AdvisoryAiAccountingWrite> AcknowledgeReviewAsync(CancellationToken cancellationToken) =>
        AiWriteAsync(async (connection, transaction) =>
        {
            int changed = await AiExecuteAsync(connection, transaction, "UPDATE ai_accounting_state SET review_block=0 WHERE id=1 AND review_block=1;", cancellationToken).ConfigureAwait(false);
            return changed == 1 ? AdvisoryAiAccountingWrite.Done : AdvisoryAiAccountingWrite.NotApplicable;
        }, AdvisoryAiAccountingWrite.StorageFailed, cancellationToken);

    public async Task<AdvisoryAiSpendingSummary> GetSummaryAsync(AdvisoryAiPeriod period, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenAiAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = connection.BeginTransaction(deferred: true);
        AdvisoryAiLedgerSnapshot? snapshot = await SnapshotAsync(connection, transaction, period, period.StartUtc, cancellationToken).ConfigureAwait(false);
        long earlier = 0;
        int rows = 0;
        await using (SqliteCommand command = AiCommand(connection, transaction,
            "SELECT (SELECT COALESCE(SUM(committed),0) FROM ai_requests WHERE state='Uncertain' AND period < $period), (SELECT COUNT(*) FROM ai_requests);", ("$period", period.Key)))
        await using (SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                earlier = TryLong(reader, 0, out long value) ? value : 0;
                rows = TryLong(reader, 1, out long count) ? (int)Math.Min(int.MaxValue, count) : 0;
            }
        }
        if (snapshot is null) return new(period, MicroUsd.Zero, MicroUsd.Zero, new(earlier), MicroUsd.Zero, MicroUsd.Zero, rows, true);
        return new(period, snapshot.SettledInPeriod, snapshot.OutstandingInPeriod, new(earlier), snapshot.TestSettled, snapshot.TestOutstanding, rows, snapshot.ReviewBlock);
    }

    private static async Task SetReviewBlockAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken) =>
        await AiExecuteAsync(connection, transaction, "UPDATE ai_accounting_state SET review_block=1 WHERE id=1;", cancellationToken).ConfigureAwait(false);

    private static async Task<AdvisoryAiRequestState?> RequestStateAsync(SqliteConnection connection, SqliteTransaction transaction, Guid requestId, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = AiCommand(connection, transaction, "SELECT state FROM ai_requests WHERE request_id=$id;", ("$id", Id(requestId)));
        object? value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value is not null && TryName(value, out AdvisoryAiRequestState state) ? state : null;
    }

    private static async Task<(AdvisoryAiRequestState, long, string?)?> SettlementRowAsync(SqliteConnection connection, SqliteTransaction transaction, Guid requestId, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = AiCommand(connection, transaction, "SELECT state,committed,provider_response_id FROM ai_requests WHERE request_id=$id;", ("$id", Id(requestId)));
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false) || !TryName(reader.GetValue(0), out AdvisoryAiRequestState state) || !TryLong(reader, 1, out long committed))
            return null;
        return (state, committed, reader.IsDBNull(2) ? null : reader.GetValue(2) as string);
    }

    private static async Task<bool> ResponseIdUsedAsync(SqliteConnection connection, SqliteTransaction transaction, string responseId, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = AiCommand(connection, transaction, "SELECT COUNT(*) FROM ai_requests WHERE provider_response_id=$response;", ("$response", responseId));
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture) > 0;
    }

    // ---- Saved explanations -----------------------------------------------------------------------------------------------------

    public async Task<AdvisoryAiExplanationSave> SaveExplanationAsync(AdvisoryAiSavedExplanation explanation, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(explanation);
        if (explanation.Id == Guid.Empty || explanation.RequestId == Guid.Empty || string.IsNullOrEmpty(explanation.OriginId) || explanation.OriginId.Length > 128 ||
            string.IsNullOrEmpty(explanation.ModelId) || explanation.ModelId.Length > 128 || !Enum.IsDefined(explanation.Source) || !Enum.IsDefined(explanation.Status) ||
            !Enum.IsDefined(explanation.IncompleteReason) || explanation.ExpiresAtUtc <= nowUtc)
            return AdvisoryAiExplanationSave.Invalid;
        long itemBytes = (long)AdvisoryAiResourcePolicy.SavedItemMetadataBytes + (explanation.Text is null ? 0 : Encoding.UTF8.GetByteCount(explanation.Text));
        if (itemBytes > AdvisoryAiResourcePolicy.MaximumSavedItemBytes) return AdvisoryAiExplanationSave.TooLarge;
        return await AiWriteAsync(async (connection, transaction) =>
        {
            await AiExecuteAsync(connection, transaction, "DELETE FROM ai_explanations WHERE expires_at <= $now;", cancellationToken, ("$now", Utc(nowUtc))).ConfigureAwait(false);
            string id = Id(explanation.Id);
            await AiExecuteAsync(connection, transaction, "INSERT INTO ai_explanations VALUES ($id,$request,$source,$origin,$model,$status,$incomplete,$answer,$truncated,$bytes,$created,$expires);",
                cancellationToken, ("$id", id), ("$request", Id(explanation.RequestId)), ("$source", explanation.Source.ToString()), ("$origin", explanation.OriginId),
                ("$model", explanation.ModelId), ("$status", explanation.Status.ToString()), ("$incomplete", explanation.IncompleteReason.ToString()), ("$answer", explanation.Text),
                ("$truncated", explanation.TextTruncated ? 1 : 0), ("$bytes", itemBytes), ("$created", Utc(explanation.CreatedAtUtc)), ("$expires", Utc(explanation.ExpiresAtUtc))).ConfigureAwait(false);
            // ADR-028: 8 MiB of saved content in total. The oldest other explanations go first; the new one always fits on its own.
            while (true)
            {
                await using SqliteCommand total = AiCommand(connection, transaction, "SELECT COALESCE(SUM(item_bytes),0) FROM ai_explanations;");
                if (Convert.ToInt64(await total.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture) <= AdvisoryAiResourcePolicy.MaximumSavedContentBytes) break;
                int removed = await AiExecuteAsync(connection, transaction,
                    "DELETE FROM ai_explanations WHERE id = (SELECT id FROM ai_explanations WHERE id <> $id ORDER BY created_at ASC, id ASC LIMIT 1);", cancellationToken, ("$id", id)).ConfigureAwait(false);
                if (removed == 0) break;
            }
            return AdvisoryAiExplanationSave.Saved;
        }, AdvisoryAiExplanationSave.StorageFailed, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AdvisoryAiSavedExplanation>> ListExplanationsAsync(AdvisoryAiSourceKind source, string originId, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(originId);
        List<AdvisoryAiSavedExplanation> result = [];
        await using SqliteConnection connection = await OpenAiAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = AiCommand(connection, null,
            "SELECT id,request_id,model_id,status,incomplete_reason,answer,answer_truncated,created_at,expires_at FROM ai_explanations WHERE source=$source AND origin_id=$origin AND expires_at > $now ORDER BY created_at DESC, id DESC LIMIT 100;",
            ("$source", source.ToString()), ("$origin", originId), ("$now", Utc(nowUtc)));
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (reader.GetValue(0) is not string idText || !Guid.TryParseExact(idText, "D", out Guid id) || reader.GetValue(1) is not string requestText ||
                !Guid.TryParseExact(requestText, "D", out Guid request) || reader.GetValue(2) is not string model || !TryName(reader.GetValue(3), out AdvisoryAiAnswerStatus status) ||
                !TryName(reader.GetValue(4), out AdvisoryAiIncompleteReason incomplete) || !TryLong(reader, 6, out long truncated) || truncated is not (0 or 1) ||
                reader.GetValue(7) is not string createdText || !TryParseUtc(createdText, out DateTimeOffset created) ||
                reader.GetValue(8) is not string expiresText || !TryParseUtc(expiresText, out DateTimeOffset expires))
                continue;
            string? answer = reader.IsDBNull(5) ? null : reader.GetValue(5) as string;
            AdvisoryAiSavedExplanation item = new(id, request, source, originId, model, status, incomplete, answer, truncated == 1, created);
            if (item.ExpiresAtUtc != expires || expires <= nowUtc) continue;
            // Saved text was redacted before it was written; it is redacted again on read in case the row was altered.
            if (answer is not null && AdvisoryAiRedaction.SanitizeAnswer(answer, int.MaxValue).Value is string clean && !string.Equals(clean, answer, StringComparison.Ordinal))
                item = item with { Text = clean };
            result.Add(item);
        }
        return result;
    }

    public async Task<bool> DeleteExplanationAsync(Guid id, CancellationToken cancellationToken) =>
        await AiWriteAsync(async (connection, transaction) =>
            await AiExecuteAsync(connection, transaction, "DELETE FROM ai_explanations WHERE id=$id;", cancellationToken, ("$id", Id(id))).ConfigureAwait(false) == 1,
            false, cancellationToken).ConfigureAwait(false);

    // ---- Helpers ------------------------------------------------------------------------------------------------------------------

    private async Task<SqliteConnection> OpenAiAsync(CancellationToken cancellationToken)
    {
        SqliteConnection connection = new(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await RefuseFutureSchemaAsync(connection, cancellationToken).ConfigureAwait(false);
            await ExecuteAsync(connection, "PRAGMA busy_timeout = 5000;", cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>One immediate write transaction; a storage failure returns <paramref name="failed"/> and nothing is committed.</summary>
    private async Task<T> AiWriteAsync<T>(Func<SqliteConnection, SqliteTransaction, Task<T>> work, T failed, CancellationToken cancellationToken)
    {
        try
        {
            await using SqliteConnection connection = await OpenAiAsync(cancellationToken).ConfigureAwait(false);
            await using SqliteTransaction transaction = connection.BeginTransaction(deferred: false);
            T result = await work(connection, transaction).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch (SqliteException)
        {
            return failed;
        }
    }

    private static SqliteCommand AiCommand(SqliteConnection connection, SqliteTransaction? transaction, string sql, params (string Name, object? Value)[] parameters)
    {
        SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach ((string name, object? value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command;
    }

    private static async Task<int> AiExecuteAsync(SqliteConnection connection, SqliteTransaction? transaction, string sql, CancellationToken cancellationToken, params (string Name, object? Value)[] parameters)
    {
        await using SqliteCommand command = AiCommand(connection, transaction, sql, parameters);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static bool TryLong(SqliteDataReader reader, int ordinal, out long value)
    {
        value = 0;
        if (reader.IsDBNull(ordinal) || reader.GetValue(ordinal) is not long number) return false;
        value = number;
        return true;
    }

    private static string Id(Guid value) => value.ToString("D", CultureInfo.InvariantCulture);

    /// <summary>Stored times keep 100-nanosecond precision, so a value read back equals the value written.</summary>
    private static DateTimeOffset AtUtcPrecision(DateTimeOffset value) => new(value.UtcDateTime, TimeSpan.Zero);
}
