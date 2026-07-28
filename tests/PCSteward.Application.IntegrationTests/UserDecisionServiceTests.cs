using PCSteward.Application;
using PCSteward.Domain;

namespace PCSteward.Application.IntegrationTests;

[TestClass]
public sealed class UserDecisionServiceTests
{
    [TestMethod]
    public async Task MappingOnlyPersistsAnExactPackageProvidedByAnEnabledTrustedConnector()
    {
        MemoryDecisionStore store = new(); UserDecisionService service = new(store);
        SourcePackageRecord package = new("winget", "Contoso.Widget", "Widget", "Contoso", "1.0", "1.1", "Machine", "x64", "Stable");
        ManualMappingValidation accepted = await service.CreateManualMappingAsync("product:widget", package, [new SourceSnapshot("winget", "v1", DateTimeOffset.UtcNow, SourceSnapshotState.Live, [package], [])], "same app", CancellationToken.None);
        ManualMappingValidation rejected = await service.CreateManualMappingAsync("product:other", package, [new SourceSnapshot("winget", "v1", DateTimeOffset.UtcNow, SourceSnapshotState.LocalOnly, [package], [])], null, CancellationToken.None);
        Assert.IsTrue(accepted.IsValid); Assert.IsFalse(rejected.IsValid); Assert.HasCount(1, await service.GetManualMappingsAsync(CancellationToken.None));
    }

    [TestMethod]
    public async Task PinsIgnoresAndReversalProduceStableReappliedPolicies()
    {
        MemoryDecisionStore store = new(); UserDecisionService service = new(store);
        await service.PinCurrentVersionAsync("product:widget", "1.0", "hold", CancellationToken.None); await service.IgnoreAsync("product:widget", DateTimeOffset.UtcNow.AddDays(1), "busy", CancellationToken.None);
        IReadOnlyDictionary<string, ProductUpdatePolicy> policies = await service.GetPoliciesAsync(CancellationToken.None);
        Assert.IsTrue(policies["product:widget"].IsPinned); Assert.IsTrue(policies["product:widget"].IsIgnoreActive(DateTimeOffset.UtcNow));
        StableDecision ignore = (await service.GetActiveDecisionsAsync(CancellationToken.None)).Single(decision => decision.Kind == "Ignore"); await service.ReverseDecisionAsync(ignore.Id, CancellationToken.None);
        Assert.IsFalse((await service.GetPoliciesAsync(CancellationToken.None))["product:widget"].IsIgnored);
    }

    private sealed class MemoryDecisionStore : IUserDecisionStore
    {
        private readonly List<StableDecision> decisions = []; private readonly List<ManualMapping> mappings = [];
        public Task SaveAsync(StableDecision decision, CancellationToken cancellationToken) { decisions.Add(decision); return Task.CompletedTask; }
        public Task<IReadOnlyCollection<StableDecision>> GetForProductAsync(string key, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<StableDecision>>(decisions.Where(decision => decision.StableProductKey == key).ToArray());
        public Task<IReadOnlyCollection<StableDecision>> GetAllAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<StableDecision>>(decisions.ToArray());
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken) { decisions.RemoveAll(decision => decision.Id == id); return Task.CompletedTask; }
        public Task SaveManualMappingAsync(ManualMapping mapping, CancellationToken cancellationToken) { mappings.Add(mapping); return Task.CompletedTask; }
        public Task<IReadOnlyCollection<ManualMapping>> GetManualMappingsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<ManualMapping>>(mappings.ToArray());
        public Task DeleteManualMappingAsync(Guid id, CancellationToken cancellationToken) { mappings.RemoveAll(mapping => mapping.Id == id); return Task.CompletedTask; }
        public Task<IReadOnlyCollection<ProductTimelineEntry>> GetTimelineAsync(string key, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<ProductTimelineEntry>>([]);
    }
}
