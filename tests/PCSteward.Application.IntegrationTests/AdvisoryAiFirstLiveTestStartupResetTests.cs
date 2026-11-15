using System.Text.Json;
using Microsoft.Data.Sqlite;
using PCSteward.Domain;
using PCSteward.Persistence.Sqlite;
using PCSteward.Storage;

namespace PCSteward.Application.IntegrationTests;

/// <summary>
/// Task 71 review T71-01: the AI test build shares the installed copy's data folder, database (with the AI ledger), scheduled task and
/// key. At start-up it must leave any reset state alone: no schedule or key callback, no deletion and no change to the folder, so a
/// reset the installed copy started can never wipe the ledger the one-request test is measured against. An ordinary build still
/// completes and rechecks a reset exactly as before. Every fixture is a disposable temporary root with synthetic data and fake callbacks.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class AdvisoryAiFirstLiveTestStartupResetTests
{
    private static readonly AdvisoryAiModelProfile Model = new("synthetic-model-2026-01-01", 1, 128_000, 16_384, 64, true, 64, 65_536, "default");
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 9, 0, 0, TimeSpan.Zero);
    private static readonly AdvisoryAiTariff Rates = new(0, Model.ModelId, Model.ServiceTier, new(2_000_000), new(500_000), new(8_000_000), Now);

    [TestMethod]
    [DataRow("ResetPending")]
    [DataRow("Partial")]
    [DataRow("OnboardingPending")]
    [DataRow("TransitionPending")]
    [DataRow("Invalid")]
    public async Task TheTestBuildLeavesEveryResetStateAndTheLedgerAlone(string stateName)
    {
        FactoryResetMarkerState state = Enum.Parse<FactoryResetMarkerState>(stateName);
        string root = NewRoot();
        try
        {
            await PopulateLedgerAsync(root);
            WriteMarker(root, state);
            Assert.AreEqual(state, FactoryReset.GetMarkerState(root));
            string before = Snapshot(root);
            int schedule = 0, key = 0;

            FactoryResetStartup startup = FactoryReset.RecoverAtStartupForTesting(root,
                _ => { schedule++; return Task.CompletedTask; }, _ => { key++; return Task.FromResult(true); }, refuseRecovery: true);

            Assert.AreEqual(FactoryResetStartupKind.RefusedInTestBuild, startup.Kind);
            Assert.IsFalse(startup.Result.Succeeded);
            Assert.AreEqual("TestBuildLeavesResetAlone", startup.Result.Residuals.Single().Reason);
            Assert.AreEqual(0, schedule, "no scheduled-task callback");
            Assert.AreEqual(0, key, "no Credential Manager callback");
            Assert.AreEqual(before, Snapshot(root), "nothing in the data folder changed, the database and its ledger included");
            Assert.AreEqual(state, FactoryReset.GetMarkerState(root), "the reset is left for the installed copy");
            Assert.AreEqual(new MicroUsd(990_000), await TestSettledAsync(root), "the first-live-test spending is still recorded");
        }
        finally { Remove(root); }
    }

    [TestMethod]
    public async Task TheTestBuildStartsNormallyWhenNoResetIsPending()
    {
        string root = NewRoot();
        try
        {
            await PopulateLedgerAsync(root);
            string before = Snapshot(root);
            int callbacks = 0;

            FactoryResetStartup startup = FactoryReset.RecoverAtStartupForTesting(root,
                _ => { callbacks++; return Task.CompletedTask; }, _ => { callbacks++; return Task.FromResult(true); }, refuseRecovery: true);

            Assert.AreEqual(FactoryResetStartupKind.Continue, startup.Kind);
            Assert.AreEqual(0, callbacks);
            Assert.AreEqual(before, Snapshot(root));
        }
        finally { Remove(root); }
    }

    [TestMethod]
    public async Task AnOrdinaryBuildStillCompletesAPendingResetAndRechecksTheKey()
    {
        string root = NewRoot();
        try
        {
            await PopulateLedgerAsync(root);
            FactoryReset.ArmForTesting(root);
            int schedule = 0, key = 0;

            FactoryResetStartup startup = FactoryReset.RecoverAtStartupForTesting(root,
                _ => { schedule++; return Task.CompletedTask; }, _ => { key++; return Task.FromResult(true); }, refuseRecovery: false);

            Assert.AreEqual(FactoryResetStartupKind.Onboarding, startup.Kind, string.Join("; ", startup.Result.Residuals.Select(item => item.Reason)));
            Assert.AreEqual(1, schedule);
            Assert.AreEqual(2, key, "removed during the reset, then rechecked before first-run setup");
            Assert.IsFalse(File.Exists(Path.Combine(root, "pcsteward.db")));
            Assert.AreEqual(FactoryResetMarkerState.OnboardingPending, FactoryReset.GetMarkerState(root));
        }
        finally { Remove(root); }
    }

    [TestMethod]
    public void AnOrdinaryBuildStillStopsOnAnInvalidMarker()
    {
        string root = NewRoot();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "reset"));
            int callbacks = 0;

            FactoryResetStartup startup = FactoryReset.RecoverAtStartupForTesting(root,
                _ => { callbacks++; return Task.CompletedTask; }, _ => { callbacks++; return Task.FromResult(true); }, refuseRecovery: false);

            Assert.AreEqual(FactoryResetStartupKind.NeedsAttention, startup.Kind);
            Assert.AreEqual("InvalidMarker", startup.Result.Residuals.Single().Reason);
            Assert.AreEqual(0, callbacks);
        }
        finally { Remove(root); }
    }

    private static string NewRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "pcsteward-reset-ai-test-build-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    /// <summary>A migrated database whose ledger holds one settled first-live-test charge of $0.99.</summary>
    private static async Task PopulateLedgerAsync(string root)
    {
        string path = Path.Combine(root, "pcsteward.db");
        SqliteDatabase store = new(path);
        await store.MigrateAsync(CancellationToken.None);
        await store.SetConnectedAsync(true, Now, CancellationToken.None);
        Assert.IsNotNull(await store.ConfirmTariffAsync(Rates, CancellationToken.None));
        await using (SqliteConnection connection = new($"Data Source={path};Pooling=False"))
        {
            await connection.OpenAsync();
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO ai_requests(request_id,period,model_id,model_profile_version,tariff_version,settings_version,input_token_bound,max_output_tokens,reservation,committed,first_live_test,state,reason,above_reservation,reserved_at,provider_response_id,
                    input_tokens,cached_input_tokens,output_tokens,reasoning_tokens,total_tokens) VALUES
                ('eeeeeeee-0000-0000-0000-000000000071',202611,'synthetic-model-2026-01-01',1,1,0,10,10,1000000,990000,1,'Settled','None',0,'2026-11-13T09:00:00.0000000Z','resp_test',495000,0,0,0,495000);
                """;
            await command.ExecuteNonQueryAsync();
        }
        Assert.AreEqual(new MicroUsd(990_000), await TestSettledAsync(root));
        SqliteConnection.ClearAllPools();
    }

    private static async Task<MicroUsd> TestSettledAsync(string root)
    {
        SqliteDatabase store = new(Path.Combine(root, "pcsteward.db"));
        MicroUsd settled = (await store.GetSummaryAsync(AdvisoryAiPeriod.Of(Now), CancellationToken.None)).TestSettled;
        SqliteConnection.ClearAllPools();
        return settled;
    }

    private static void WriteMarker(string root, FactoryResetMarkerState state)
    {
        string control = Path.Combine(root, "reset");
        switch (state)
        {
            case FactoryResetMarkerState.ResetPending:
                FactoryReset.ArmForTesting(root);
                break;
            case FactoryResetMarkerState.Partial or FactoryResetMarkerState.OnboardingPending:
                Directory.CreateDirectory(control);
                File.WriteAllText(Path.Combine(control, FactoryReset.MarkerFileName), Marker(state.ToString()));
                break;
            case FactoryResetMarkerState.TransitionPending:
                Directory.CreateDirectory(control);
                File.WriteAllText(Path.Combine(control, FactoryReset.MarkerFileName), Marker("ResetPending"));
                File.WriteAllText(Path.Combine(control, FactoryReset.TemporaryMarkerFileName), Marker("OnboardingPending"));
                break;
            case FactoryResetMarkerState.Invalid:
                Directory.CreateDirectory(control);
                break;
            default:
                Assert.Fail(state.ToString());
                break;
        }
    }

    private static string Marker(string state) => JsonSerializer.Serialize(new
    {
        schemaVersion = 1,
        resetId = "11111111-1111-1111-1111-111111111111",
        requestedUtc = "2026-08-01T00:00:00.0000000+00:00",
        attempt = 0,
        state,
        categories = new { ScheduledCheck = "Pending", Downloads = "Pending", Logs = "Pending", Database = "Pending", OwnedRoot = "Pending" },
    });

    private static string Snapshot(string root) => string.Join("\n", Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
        .Order(StringComparer.Ordinal)
        .Select(path => File.Exists(path)
            ? $"{Path.GetRelativePath(root, path)}|{Convert.ToHexString(File.ReadAllBytes(path))}|{File.GetLastWriteTimeUtc(path):O}"
            : $"{Path.GetRelativePath(root, path)}/|{Directory.GetLastWriteTimeUtc(path):O}"));

    private static void Remove(string root)
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
