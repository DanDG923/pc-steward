using System.Text;
using Microsoft.Data.Sqlite;
using PCSteward.Application;
using PCSteward.Catalog;
using PCSteward.Domain;
using PCSteward.Persistence.Sqlite;
using PCSteward.Provider.Chocolatey;
using PCSteward.Provider.WinGet;

namespace PCSteward.Application.IntegrationTests;

/// <summary>Task 2: CRD-03/08, FR-INS-02/04/06; all discovery and inventory are injected fakes.</summary>
[TestClass]
public sealed class InstallChoiceTests
{
    [TestMethod]
    public async Task ChoicesUseLiveApplicableInstallerNotHostArchitecture()
    {
        ChoiceFixtures.Authority live = new(request => request.Architecture == UpdateArchitecture.X86
            ? ChoiceFixtures.Ready(request)
            : InstallPreflightResult.NotActionable("no-applicable-installer"));
        CatalogEntry entry = ChoiceFixtures.Catalog().Entries.Single(item => item.Id == "pcs:tool");

        IReadOnlyList<InstallPreflightResult> choices = await new WingetInstallChoiceAuthority(live).PreflightChoicesAsync(entry, entry.DefaultProvider, CancellationToken.None);

        Assert.HasCount(1, choices, "The x64 host still offers the live applicable x86 installer.");
        Assert.AreEqual(UpdateArchitecture.X86, choices[0].Selection!.Architecture);
        Assert.IsTrue(live.Requests.Any(request => request.Architecture == UpdateArchitecture.X86));
        Assert.IsFalse(live.Requests.Any(request => request.Architecture == UpdateArchitecture.Arm64));
        Assert.IsTrue(live.Requests.All(request => request.PackageId == "Vendor.Tool" && request.Source == UpdateSource.WinGet && request.Scope == UpdateScope.Machine));
    }

    [TestMethod]
    public async Task NeutralChoicesArePreservedAndIdenticalSelectionsDeduplicated()
    {
        ChoiceFixtures.Authority live = new(request => ChoiceFixtures.Ready(request with { Architecture = UpdateArchitecture.Neutral }));
        CatalogEntry entry = ChoiceFixtures.Catalog().Entries.Single(item => item.Id == "pcs:tool");

        IReadOnlyList<InstallPreflightResult> choices = await new WingetInstallChoiceAuthority(live).PreflightChoicesAsync(entry, entry.DefaultProvider, CancellationToken.None);

        Assert.HasCount(1, choices, "Identical applicable neutral selections must be deduplicated.");
        Assert.AreEqual(UpdateArchitecture.Neutral, choices[0].Selection!.Architecture);
        Assert.AreEqual(3, live.Requests.Count, "The supported x64/x86/neutral choices are queried live.");
    }

    [TestMethod]
    public async Task DiscoveryNeverHasMoreThanTwoQueriesInFlight()
    {
        ChoiceFixtures.ConcurrentAuthority live = new();
        CatalogEntry entry = ChoiceFixtures.Catalog().Entries.Single(item => item.Id == "pcs:tool");
        WingetInstallChoiceAuthority choices = new(live);

        await Task.WhenAll(choices.PreflightChoicesAsync(entry, entry.DefaultProvider, CancellationToken.None), choices.PreflightChoicesAsync(entry, entry.DefaultProvider, CancellationToken.None));

        Assert.AreEqual(6, live.Calls, "Both requests discover all three supported architectures.");
        Assert.IsTrue(live.Peak <= 2, $"Discovery exceeded its two-query bound: {live.Peak}.");
    }

    [TestMethod]
    public async Task UnsupportedAndUncertainProviderChoicesAreWithheld()
    {
        ChoiceFixtures.Authority live = new(_ => InstallPreflightResult.NotActionable("provider-unavailable"));
        CatalogEntry entry = ChoiceFixtures.Catalog().Entries.Single(item => item.Id == "pcs:tool");
        IReadOnlyList<InstallPreflightResult> choices = await new WingetInstallChoiceAuthority(live).PreflightChoicesAsync(entry, entry.DefaultProvider, CancellationToken.None);
        Assert.HasCount(0, choices);
    }

    [TestMethod]
    public async Task WorkflowDiscoversOnlyAfterValidatedConsentAndReliableAbsence()
    {
        await using ChoiceFixtures.Database database = await ChoiceFixtures.Database.CreateAsync();
        await database.EnableSourcesAsync();
        ChoiceFixtures.Choices authority = new();
        using InventoryWorkflow workflow = database.Workflow(choices: authority);

        IReadOnlyList<InstallPreflightResult> choices = await workflow.PrepareInstallChoicesAsync("pcs:tool", null, CancellationToken.None);

        Assert.HasCount(1, choices);
        Assert.AreEqual("Vendor.Tool", authority.Providers.Single().PackageId);
        Assert.AreEqual(CatalogProviderKind.WinGet, authority.Providers.Single().Provider);
        Assert.AreEqual(1, database.Local.Scans);
    }

    [TestMethod]
    public async Task WorkflowRefusesMissingConsentInvalidCatalogInstalledAndReservedEntry()
    {
        await using ChoiceFixtures.Database database = await ChoiceFixtures.Database.CreateAsync();
        ChoiceFixtures.Choices authority = new();
        using InventoryWorkflow workflow = database.Workflow(choices: authority);
        Assert.HasCount(0, await workflow.PrepareInstallChoicesAsync("pcs:tool", null, CancellationToken.None));
        await database.EnableSourcesAsync();
        Assert.HasCount(0, await workflow.PrepareInstallChoicesAsync("pcs:unknown", null, CancellationToken.None));
        Assert.HasCount(0, await workflow.PrepareInstallChoicesAsync(ChocolateySetupPreparation.CatalogEntryId, null, CancellationToken.None));
        Assert.HasCount(0, await workflow.PrepareInstallChoicesAsync("pcs:tool", CatalogProviderKind.Chocolatey, CancellationToken.None));
        using InventoryWorkflow invalidCatalog = database.Workflow(choices: authority, catalogAvailable: false);
        Assert.HasCount(0, await invalidCatalog.PrepareInstallChoicesAsync("pcs:tool", null, CancellationToken.None));
        database.Local.Result = ChoiceFixtures.Registry("Fixture Tool", "Vendor");
        Assert.HasCount(0, await workflow.PrepareInstallChoicesAsync("pcs:tool", null, CancellationToken.None));
        Assert.HasCount(0, authority.Providers);
    }

    [TestMethod]
    public async Task PartialLocalInventoryCannotOfferInstall()
    {
        await using ChoiceFixtures.Database database = await ChoiceFixtures.Database.CreateAsync();
        await database.EnableSourcesAsync();
        database.Local.Result = new([], [], [], ["partial"]);
        ChoiceFixtures.Authority preflight = new(ChoiceFixtures.Ready);
        using InventoryWorkflow workflow = database.Workflow(preflight: preflight, choices: new ChoiceFixtures.Choices());

        InstallSelectionPreparation final = await workflow.PrepareInstallSelectionAsync("pcs:tool", null, UpdateScope.Machine, UpdateArchitecture.X64, CancellationToken.None);

        Assert.IsNull(final.Selection, "CRD-03: a published partial scan is not reliable absence.");
        Assert.HasCount(0, preflight.Requests);
        Assert.HasCount(0, await workflow.PrepareInstallChoicesAsync("pcs:tool", null, CancellationToken.None));
    }

    [TestMethod]
    public async Task CompositeChoicesRoutesOnlyTheListedProvider()
    {
        CatalogEntry entry = ChoiceFixtures.Catalog().Entries.Single(item => item.Id == "pcs:tool");
        ChoiceFixtures.Choices authority = new();
        CompositeInstallChoiceAuthority composite = new(new Dictionary<CatalogProviderKind, IInstallChoiceAuthority> { [CatalogProviderKind.WinGet] = authority });
        Assert.HasCount(1, await composite.PreflightChoicesAsync(entry, entry.DefaultProvider, CancellationToken.None));
        Assert.HasCount(1, authority.Providers);
    }

    [TestMethod]
    public async Task ChocolateyChoicesRequireStrictCurrentTrustAndRemainMachineNeutral()
    {
        CatalogEntry entry = ChoiceFixtures.Catalog().Entries.Single(item => item.Id == "pcs:choco-tool");
        ChoiceFixtures.Authority live = new(ChoiceFixtures.Ready);
        ChoiceFixtures.Health health = new(ChoiceFixtures.Report(CatalogProviderKind.Chocolatey, ProviderPresence.Present, ProviderTrust.Trusted));
        IReadOnlyList<InstallPreflightResult> choices = await new ChocolateyInstallChoiceAuthority(live, health).PreflightChoicesAsync(entry, entry.DefaultProvider, CancellationToken.None);
        Assert.HasCount(1, choices);
        Assert.AreEqual(UpdateScope.Machine, choices[0].Selection!.Scope);
        Assert.AreEqual(UpdateArchitecture.Neutral, choices[0].Selection!.Architecture);
        Assert.AreEqual(UpdateSource.ChocolateyCommunity, choices[0].Selection!.Source);
        Assert.AreEqual(1, health.Calls);
        Assert.HasCount(1, live.Requests);
    }

    [TestMethod]
    [DataRow(ProviderTrust.Untrusted, ProviderProbeState.Current)]
    [DataRow(ProviderTrust.Unknown, ProviderProbeState.Current)]
    [DataRow(ProviderTrust.Trusted, ProviderProbeState.Partial)]
    public async Task ChocolateyChoicesRefuseUntrustedOrUncertainHealth(ProviderTrust trust, ProviderProbeState state)
    {
        CatalogEntry entry = ChoiceFixtures.Catalog().Entries.Single(item => item.Id == "pcs:choco-tool");
        ChoiceFixtures.Authority live = new(ChoiceFixtures.Ready);
        ChoiceFixtures.Health health = new(ChoiceFixtures.Report(CatalogProviderKind.Chocolatey, ProviderPresence.Present, trust) with { State = state });
        Assert.HasCount(0, await new ChocolateyInstallChoiceAuthority(live, health).PreflightChoicesAsync(entry, entry.DefaultProvider, CancellationToken.None));
        Assert.HasCount(0, live.Requests);
    }
}

/// <summary>Shared fake-only Task 2 fixtures; no provider/client/executor/launcher is composed.</summary>
internal static class ChoiceFixtures
{
    private const string CatalogJson = """
    { "schemaVersion": 1, "catalogVersion": "2026.10.09.2", "entries": [
      { "id": "pcs:tool", "name": "Fixture Tool", "publisher": "Vendor", "category": "Utilities",
        "providers": [{ "provider": "winget", "source": "winget", "packageId": "Vendor.Tool", "scopes": ["machine"], "default": true }],
        "detection": { "arpDisplayName": "^Fixture Tool$", "arpPublisher": "^Vendor$" } },
      { "id": "pcs:choco-tool", "name": "Choco Tool", "publisher": "Vendor", "category": "Utilities",
        "providers": [{ "provider": "chocolatey", "source": "chocolatey-community", "packageId": "fixture-tool", "scopes": ["machine"], "default": true }], "detection": {} },
      { "id": "pcs:chocolatey-setup", "name": "Chocolatey setup", "publisher": "Chocolatey Software", "category": "Utilities",
        "providers": [{ "provider": "winget", "source": "winget", "packageId": "Chocolatey.Chocolatey", "scopes": ["machine"], "default": true }],
        "detection": { "arpDisplayName": "^Chocolatey$", "arpPublisher": "^Chocolatey Software$" }, "flags": { "channel": "Stable", "automationAllowed": false } }
    ] }
    """;

    public static PersonalCatalog Catalog()
    {
        CatalogLoadResult loaded = CatalogValidator.Validate(Encoding.UTF8.GetBytes(CatalogJson));
        Assert.IsTrue(loaded.IsLoaded, string.Join(", ", loaded.Errors));
        return loaded.Catalog!;
    }

    public static ProviderHealthReport Report(CatalogProviderKind provider, ProviderPresence presence, ProviderTrust trust) =>
        new(provider, presence, trust, ProviderProbeState.Current, presence == ProviderPresence.Present ? "2.0.0" : null, [], DateTimeOffset.UtcNow, "fixture");

    public static InstallPreflightResult Ready(InstallPreflightRequest request)
    {
        PackageAgreementText[] agreements = request.PackageId == ChocolateySetupPreparation.PackageId ? [new("License", "Fixture setup agreement", "https://example.invalid/license")] : [];
        PackageAgreementDigest.TryCompute(agreements, out string? hash, out _);
        return InstallPreflightResult.Actionable(UpdateJobSelection.ForInstall(request.CatalogEntryKey, request.Provider, request.Source, request.PackageId, "2.0.0", request.Scope, request.Architecture, request.Channel, "fixture-v1", hash, false), agreements, InstallerElevation.Unknown);
    }

    public static ProviderScanResult Registry(string name, string publisher)
    {
        CanonicalProduct product = new(Guid.NewGuid(), "registry:fixture", name);
        return new([product], [new(Guid.NewGuid(), product.Id, "registry", name, "2.0.0", "Machine", "x86", publisher, Channel: "Stable", Classification: "Application")], [], []);
    }

    internal sealed class Authority(Func<InstallPreflightRequest, InstallPreflightResult> result) : IInstallPreflightAuthority
    {
        private readonly object sync = new();
        public List<InstallPreflightRequest> Requests { get; } = [];
        public Task<InstallPreflightResult> PreflightAsync(InstallPreflightRequest request, CancellationToken cancellationToken)
        {
            lock (sync) Requests.Add(request);
            return Task.FromResult(result(request));
        }
    }

    internal sealed class ConcurrentAuthority : IInstallPreflightAuthority
    {
        private int active;
        private int calls;
        private readonly object sync = new();
        public int Calls => calls;
        public int Peak { get; private set; }
        public async Task<InstallPreflightResult> PreflightAsync(InstallPreflightRequest request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref calls);
            int count = Interlocked.Increment(ref active);
            lock (sync) Peak = Math.Max(Peak, count);
            try { await Task.Delay(20, cancellationToken); return Ready(request); }
            finally { Interlocked.Decrement(ref active); }
        }
    }

    internal sealed class Choices : IInstallChoiceAuthority
    {
        public List<CatalogProviderIdentity> Providers { get; } = [];
        public Task<IReadOnlyList<InstallPreflightResult>> PreflightChoicesAsync(CatalogEntry entry, CatalogProviderIdentity provider, CancellationToken cancellationToken)
        {
            Providers.Add(provider);
            return Task.FromResult<IReadOnlyList<InstallPreflightResult>>([Ready(new(entry.Id, UpdateProvider.WinGet, UpdateSource.WinGet, provider.PackageId, UpdateScope.Machine, UpdateArchitecture.X86))]);
        }
    }

    internal sealed class Health(ProviderHealthReport report) : IProviderHealthProbe
    {
        public ProviderHealthReport Report { get; set; } = report;
        public int Calls { get; private set; }
        public Task<ProviderHealthReport> ProbeAsync(CancellationToken cancellationToken) { Calls++; return Task.FromResult(Report); }
    }

    internal sealed class Local : ILocalInventoryProvider
    {
        public string Name => "fixture-local";
        public TimeSpan Timeout => TimeSpan.FromSeconds(1);
        public ProviderScanResult Result { get; set; } = new([], [], [], []);
        public int Scans { get; private set; }
        public Task<ProviderScanResult> ScanAsync(CancellationToken cancellationToken) { Scans++; return Task.FromResult(Result); }
    }

    internal sealed class Database : IAsyncDisposable
    {
        private readonly string path;
        private Database(string path, SqliteDatabase store) { this.path = path; Store = store; }
        public SqliteDatabase Store { get; }
        public Local Local { get; } = new();
        public static async Task<Database> CreateAsync()
        {
            string path = Path.Combine(Path.GetTempPath(), $"pcsteward-choices-{Guid.NewGuid():N}.db");
            SqliteDatabase store = new(path);
            await store.MigrateAsync(CancellationToken.None);
            return new(path, store);
        }
        public Task EnableSourcesAsync() => Store.SaveConnectorStatesAsync(new Dictionary<string, ConnectorState>
        {
            [TrustedSourceConnectorConsent.InstalledInventoryConnector] = new(true, true),
            [TrustedSourceConnectorConsent.PackageSourcesConnector] = new(true, true)
        }, CancellationToken.None);
        public InventoryWorkflow Workflow(IInstallPreflightAuthority? preflight = null, IInstallChoiceAuthority? choices = null, UpdateExecutionCapabilities? capabilities = null, bool catalogAvailable = true) =>
            new(Store, Store, Store, [Local], decisions: Store, executionCapabilities: capabilities, catalog: catalogAvailable ? Catalog() : null, userCatalog: Store, installPreflight: preflight, installChoices: choices);
        public ValueTask DisposeAsync() { SqliteConnection.ClearAllPools(); File.Delete(path); return ValueTask.CompletedTask; }
    }
}
