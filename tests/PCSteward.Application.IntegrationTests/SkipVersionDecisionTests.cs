using System.Globalization;
using PCSteward.Application;
using PCSteward.Domain;
using PCSteward.Persistence.Sqlite;

namespace PCSteward.Application.IntegrationTests;

/// <summary>
/// Owner decision 2026-11-02 (FR-POL-01/03/04/05; Task 73 Phase 6): Skip this version is a reversible per-product decision stored in
/// the existing stable_decisions table (no schema change), listed with the other decisions and never granting authority.
/// </summary>
[TestClass]
public sealed class SkipVersionDecisionTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    [TestCategory("FR-POL-01")]
    [TestCategory("FR-POL-05")]
    public async Task SkipRoundTripsThroughSqliteReplacesTheEarlierSkipAndIsReversible()
    {
        string path = Path.Combine(Path.GetTempPath(), $"pcsteward-skip-{Guid.NewGuid():N}.db");
        try
        {
            SqliteDatabase store = new(path);
            await store.MigrateAsync(CancellationToken.None);
            UserDecisionService service = new(store);
            string reason = new('r', 600);

            await service.SkipVersionAsync("winget:contoso.widget", " 1.1.0 ", "Known regression", CancellationToken.None);
            await service.SkipVersionAsync("winget:contoso.widget", "1.2.0", reason, CancellationToken.None);
            await service.PinCurrentVersionAsync("winget:contoso.other", "3.0", null, CancellationToken.None);

            StableDecision[] skips = (await store.GetAllAsync(CancellationToken.None)).Where(decision => decision.Kind == UserDecisionService.SkipVersionKind).ToArray();
            Assert.HasCount(1, skips, "skipping again replaces the product's earlier skip");
            Assert.AreEqual("1.2.0", skips[0].Value);
            Assert.AreEqual(512, skips[0].Reason!.Length, "the optional reason is bounded like other decisions (FR-POL-03)");
            Assert.IsNull(skips[0].ExpiresAt);
            IReadOnlyDictionary<string, ProductUpdatePolicy> policies = await service.GetPoliciesAsync(CancellationToken.None);
            Assert.AreEqual("1.2.0", policies["winget:contoso.widget"].SkippedVersion);
            Assert.IsFalse(policies["winget:contoso.widget"].IsPinned || policies["winget:contoso.widget"].IsIgnored, "a skip is neither a pin nor an ignore");
            Assert.IsNull(policies["winget:contoso.other"].SkippedVersion);
            Assert.IsTrue((await service.GetActiveDecisionsAsync(CancellationToken.None)).Any(decision => decision.Id == skips[0].Id), "listed on Decisions (FR-POL-04)");
            ProductTimelineEntry timeline = (await service.GetTimelineAsync("winget:contoso.widget", CancellationToken.None)).Single();
            Assert.AreEqual("Skipped version 1.2.0", AppDetailPresenter.Timeline([timeline]).Single().Title);

            // A fresh connection to the same file (no migration needed) still reads it.
            SqliteDatabase reopened = new(path);
            await reopened.MigrateAsync(CancellationToken.None);
            Assert.AreEqual("1.2.0", (await new UserDecisionService(reopened).GetPoliciesAsync(CancellationToken.None))["winget:contoso.widget"].SkippedVersion);

            await service.ReverseDecisionAsync(skips[0].Id, CancellationToken.None);
            Assert.IsNull((await service.GetPoliciesAsync(CancellationToken.None)).GetValueOrDefault("winget:contoso.widget")?.SkippedVersion);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    [TestMethod]
    [TestCategory("FR-POL-01")]
    public async Task UnknownDecisionKindsHideAndEnableNothingAndAreShownAsUnrecognised()
    {
        MemoryStore store = new();
        await store.SaveAsync(new StableDecision(Guid.NewGuid(), "product:widget", "SkipMajorBranch", "2", Now), CancellationToken.None);
        UserDecisionService service = new(store);

        ProductUpdatePolicy policy = (await service.GetPoliciesAsync(CancellationToken.None))["product:widget"];
        Assert.AreEqual(new ProductUpdatePolicy(), policy, "an unknown kind changes no policy");

        DecisionRow row = DecisionsPresenter.Build(await store.GetAllAsync(CancellationToken.None), [], [], CultureInfo.InvariantCulture, TimeZoneInfo.Utc).Single();
        Assert.AreEqual(DecisionKind.Other, row.Kind, "never shown as a pin it may not be");
        Assert.AreEqual("Saved decision (not recognised)", row.Title);
        StringAssert.StartsWith(row.RemoveAccessibleName, row.RemoveLabel);
    }

    [TestMethod]
    [TestCategory("FR-POL-04")]
    public void DecisionsListTheSkipWithDistinctWordingAndItsReversal()
    {
        InventoryRow[] rows = [new(Guid.NewGuid(), "Widget", "Contoso", "winget", "User", "1.0", "Skipped", "Application", [], "product:widget")];
        StableDecision skip = new(Guid.NewGuid(), "product:widget", UserDecisionService.SkipVersionKind, "1.1.0", Now, "Known regression");

        DecisionRow row = DecisionsPresenter.Build([skip], [], rows, CultureInfo.InvariantCulture, TimeZoneInfo.Utc).Single();

        Assert.AreEqual(DecisionKind.Skip, row.Kind);
        Assert.AreEqual("Skipping version 1.1.0", row.Title);
        Assert.AreEqual("Stop skipping 1.1.0", row.RemoveLabel);
        StringAssert.StartsWith(row.RemoveAccessibleName, row.RemoveLabel, "label-in-name");
        StringAssert.Contains(row.RemoveAccessibleName, "Widget");
        Assert.AreEqual("Known regression", row.Reason);
        StringAssert.Contains(row.Detail, "A newer version will be offered again");
        Assert.IsFalse(row.Title.Contains("Ignore", StringComparison.OrdinalIgnoreCase), "distinct from Ignore updates for this app");
        Assert.AreEqual("Skipped versions (1)", DecisionsPresenter.GroupHeading(DecisionKind.Skip, 1));
        Assert.AreEqual("Skipped", StatusVocabulary.For(UpdateIntelligenceStatus.Skipped).Label);
        Assert.AreEqual(StatusTone.Neutral, StatusVocabulary.For(UpdateIntelligenceStatus.Skipped).Tone);
    }

    private sealed class MemoryStore : IUserDecisionStore
    {
        private readonly List<StableDecision> decisions = [];
        public Task SaveAsync(StableDecision decision, CancellationToken cancellationToken) { decisions.Add(decision); return Task.CompletedTask; }
        public Task<IReadOnlyCollection<StableDecision>> GetForProductAsync(string stableProductKey, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<StableDecision>>(decisions.Where(decision => decision.StableProductKey == stableProductKey).ToArray());
        public Task<IReadOnlyCollection<StableDecision>> GetAllAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<StableDecision>>(decisions.ToArray());
        public Task DeleteAsync(Guid decisionId, CancellationToken cancellationToken) { decisions.RemoveAll(decision => decision.Id == decisionId); return Task.CompletedTask; }
        public Task SaveManualMappingAsync(ManualMapping mapping, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyCollection<ManualMapping>> GetManualMappingsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<ManualMapping>>([]);
        public Task DeleteManualMappingAsync(Guid mappingId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyCollection<ProductTimelineEntry>> GetTimelineAsync(string stableProductKey, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<ProductTimelineEntry>>([]);
    }
}
