using System.Text;
using Microsoft.Data.Sqlite;
using PCSteward.Application;
using PCSteward.Catalog;
using PCSteward.Domain;
using PCSteward.Persistence.Sqlite;

namespace PCSteward.Application.IntegrationTests;

/// <summary>Task 48: FR-INS-01/02/04/05/06, AC-V3-02 — install preparation and two-authority install verification.</summary>
[TestClass]
public sealed class InstallWorkflowTests
{
    private const string CatalogJson = """
    {
      "schemaVersion": 1,
      "catalogVersion": "2026.10.09.1",
      "entries": [
        {
          "id": "pcs:7zip", "name": "7-Zip", "publisher": "Igor Pavlov", "category": "Compression",
          "providers": [
            { "provider": "winget", "source": "winget", "packageId": "7zip.7zip", "scopes": ["machine"], "default": true },
            { "provider": "chocolatey", "source": "chocolatey-community", "packageId": "7zip", "scopes": ["machine"], "default": false }
          ],
          "detection": { "arpDisplayName": "^7-Zip [0-9.]{1,32}( \\((x64|arm64)\\))?$", "arpPublisher": "^Igor Pavlov$" }
        },
        {
          "id": "pcs:firefox", "name": "Mozilla Firefox", "publisher": "Mozilla", "category": "Browsers",
          "providers": [ { "provider": "winget", "source": "winget", "packageId": "Mozilla.Firefox", "scopes": ["user", "machine"], "default": true } ],
          "detection": { "arpDisplayName": "^Mozilla Firefox( \\((x64|x86) [a-z]{2,3}(-[A-Z]{2})?\\))?$", "arpPublisher": "^Mozilla$" }
        }
      ]
    }
    """;

    private static PersonalCatalog Catalog() => CatalogValidator.Validate(Encoding.UTF8.GetBytes(CatalogJson)).Catalog ?? throw new InvalidOperationException("fixture catalog invalid");

    [TestMethod]
    public async Task NotInstalledEntryIsResolvedLiveThroughItsDefaultProvider()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        await EnableTrustedSourcesAsync(database);
        RecordingAuthority authority = new();
        using InventoryWorkflow workflow = Workflow(database, [], authority);

        InstallSelectionPreparation prepared = await workflow.PrepareInstallSelectionAsync("pcs:firefox", null, UpdateScope.User, UpdateArchitecture.X64, CancellationToken.None);

        Assert.IsNotNull(prepared.Selection, prepared.Message);
        InstallPreflightRequest request = authority.Requests.Single();
        Assert.AreEqual(new InstallPreflightRequest("pcs:firefox", UpdateProvider.WinGet, UpdateSource.WinGet, "Mozilla.Firefox", UpdateScope.User, UpdateArchitecture.X64), request);
        Assert.IsFalse(request.IsRevalidation);
    }

    [TestMethod]
    public async Task ChocolateyOverrideIsAnExplicitMachineScopeNeutralRequest()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        await EnableTrustedSourcesAsync(database);
        RecordingAuthority authority = new();
        using InventoryWorkflow workflow = Workflow(database, [], authority);

        InstallSelectionPreparation prepared = await workflow.PrepareInstallSelectionAsync("pcs:7zip", CatalogProviderKind.Chocolatey, UpdateScope.Machine, UpdateArchitecture.X64, CancellationToken.None);

        Assert.IsNotNull(prepared.Selection, prepared.Message);
        Assert.AreEqual(new InstallPreflightRequest("pcs:7zip", UpdateProvider.Chocolatey, UpdateSource.ChocolateyCommunity, "7zip", UpdateScope.Machine, UpdateArchitecture.Neutral), authority.Requests.Single());
    }

    [TestMethod]
    public async Task UnlistedChoicesDetectedAppsAndMissingConsentNeverReachTheProvider()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        RecordingAuthority authority = new();
        using InventoryWorkflow workflow = Workflow(database, [Registry("Mozilla Firefox (x64 en-GB)", "Mozilla")], authority);
        using InventoryWorkflow userScopeOnly = Workflow(database, [], authority, UpdateExecutionCapabilities.UserScopeOnly);
        using InventoryWorkflow noPreflight = Workflow(database, [], null);

        Assert.IsNull((await workflow.PrepareInstallSelectionAsync("pcs:7zip", null, UpdateScope.Machine, UpdateArchitecture.X64, CancellationToken.None)).Selection, "Sources are not enabled.");
        await EnableTrustedSourcesAsync(database);

        Assert.IsNull((await workflow.PrepareInstallSelectionAsync("pcs:unknown", null, UpdateScope.User, UpdateArchitecture.X64, CancellationToken.None)).Selection);
        Assert.IsNull((await workflow.PrepareInstallSelectionAsync("pcs:7zip", null, UpdateScope.User, UpdateArchitecture.X64, CancellationToken.None)).Selection, "7-Zip lists machine scope only.");
        Assert.IsNull((await workflow.PrepareInstallSelectionAsync("pcs:firefox", CatalogProviderKind.Chocolatey, UpdateScope.Machine, UpdateArchitecture.X64, CancellationToken.None)).Selection, "Firefox lists no Chocolatey provider.");
        Assert.IsNull((await userScopeOnly.PrepareInstallSelectionAsync("pcs:7zip", null, UpdateScope.Machine, UpdateArchitecture.X64, CancellationToken.None)).Selection, "Machine scope disabled by deployment.");
        Assert.IsNull((await noPreflight.PrepareInstallSelectionAsync("pcs:firefox", null, UpdateScope.User, UpdateArchitecture.X64, CancellationToken.None)).Selection);
        InstallSelectionPreparation detected = await workflow.PrepareInstallSelectionAsync("pcs:firefox", null, UpdateScope.User, UpdateArchitecture.X64, CancellationToken.None);
        Assert.IsNull(detected.Selection);
        StringAssert.Contains(detected.Message, "detected this app");
        Assert.HasCount(0, authority.Requests);
    }

    [TestMethod]
    public async Task NameOnlyDetectionStillBlocksAnInstall()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        await EnableTrustedSourcesAsync(database);
        RecordingAuthority authority = new();
        using InventoryWorkflow workflow = Workflow(database, [Registry("Mozilla Firefox", null)], authority);

        Assert.IsNull((await workflow.PrepareInstallSelectionAsync("pcs:firefox", null, UpdateScope.User, UpdateArchitecture.X64, CancellationToken.None)).Selection);
        Assert.HasCount(0, authority.Requests);
    }

    [TestMethod]
    public async Task NonActionablePreflightOffersNoSelection()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        await EnableTrustedSourcesAsync(database);
        using InventoryWorkflow workflow = Workflow(database, [], new RecordingAuthority(InstallPreflightResult.NotActionable("package-not-unique")));

        InstallSelectionPreparation prepared = await workflow.PrepareInstallSelectionAsync("pcs:firefox", null, UpdateScope.User, UpdateArchitecture.X64, CancellationToken.None);

        Assert.IsNull(prepared.Selection);
        StringAssert.Contains(prepared.Message, "package-not-unique");
    }

    [TestMethod]
    public async Task InstallSuccessNeedsTheProviderListingAndAnExactLocalCatalogMatch()
    {
        ImmutableUpdateJob job = new UpdateJobReview(Guid.NewGuid(), Guid.NewGuid(), UpdateJobSelection.ForInstall("pcs:firefox", UpdateProvider.WinGet, UpdateSource.WinGet, "Mozilla.Firefox", "131.0", UpdateScope.User, UpdateArchitecture.X64, UpdateChannel.Stable, "winget-com-v1", null, false)).Confirm(DateTimeOffset.UtcNow);

        Assert.IsTrue((await VerifyAsync(job, listed: true, Registry("Mozilla Firefox (x64 en-GB)", "Mozilla"))).ExpectedVersionDetected);
        Assert.IsFalse((await VerifyAsync(job, listed: false, Registry("Mozilla Firefox (x64 en-GB)", "Mozilla"))).ExpectedVersionDetected, "No provider listing.");
        Assert.IsFalse((await VerifyAsync(job, listed: true)).ExpectedVersionDetected, "Nothing found locally.");
        Assert.IsFalse((await VerifyAsync(job, listed: true, Registry("Mozilla Firefox (x64 en-GB)", null))).ExpectedVersionDetected, "Name-only detection is not an exact match.");
        Assert.IsFalse((await VerifyAsync(job, listed: true, Registry("Mozilla Firefox (x64 en-GB)", "Someone Else"))).ExpectedVersionDetected, "Publisher decoy.");
        Assert.IsFalse((await VerifyAsync(job, listed: true, WingetOnly("Mozilla.Firefox"))).ExpectedVersionDetected, "The provider's own list is not independent local evidence.");
        Assert.IsFalse((await VerifyAsync(job, listed: true, Registry("7-Zip 24.09 (x64)", "Igor Pavlov"))).ExpectedVersionDetected, "A different catalog entry.");
    }

    private static async Task<PostUpdateInventoryResult> VerifyAsync(ImmutableUpdateJob job, bool listed, params ProviderScanResult[] scans)
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        ProviderScanResult merged = new([.. scans.SelectMany(scan => scan.Products)], [.. scans.SelectMany(scan => scan.Instances)], [], []);
        using InventoryWorkflow workflow = new(database.Store, database.Store, database.Store, [new FixedProvider(merged)], decisions: database.Store, postUpdatePackageInventory: new FixedListing(listed), catalog: Catalog(), userCatalog: database.Store);
        return await workflow.ReInventoryAsync(job, CancellationToken.None);
    }

    private static ProviderScanResult Registry(string displayName, string? publisher)
    {
        CanonicalProduct product = new(Guid.NewGuid(), "registry:" + displayName.ToLowerInvariant().Replace(' ', '-'), displayName);
        return new([product], [new(Guid.NewGuid(), product.Id, "registry", displayName, "131.0", "User", "x64", publisher, Channel: "Stable", Classification: "Application")], [], []);
    }

    private static ProviderScanResult WingetOnly(string packageId)
    {
        CanonicalProduct product = new(Guid.NewGuid(), "winget:winget:" + packageId, "Mozilla Firefox (x64 en-GB)");
        return new([product], [new(Guid.NewGuid(), product.Id, "winget", packageId, "131.0", "User", "x64", null, Channel: "Stable", Classification: "Application", SourceId: "winget")], [], []);
    }

    private static InventoryWorkflow Workflow(TestDatabase database, IReadOnlyCollection<ProviderScanResult> scans, IInstallPreflightAuthority? authority, UpdateExecutionCapabilities? capabilities = null)
    {
        ILocalInventoryProvider[] providers = scans.Count == 0 ? [] : [new FixedProvider(new([.. scans.SelectMany(scan => scan.Products)], [.. scans.SelectMany(scan => scan.Instances)], [], []))];
        return new(database.Store, database.Store, database.Store, providers, decisions: database.Store, executionCapabilities: capabilities, catalog: Catalog(), userCatalog: database.Store, installPreflight: authority);
    }

    private static Task EnableTrustedSourcesAsync(TestDatabase database) => database.Store.SaveConnectorStatesAsync(
        new Dictionary<string, ConnectorState>
        {
            [TrustedSourceConnectorConsent.InstalledInventoryConnector] = new(true, true),
            [TrustedSourceConnectorConsent.PackageSourcesConnector] = new(true, true)
        },
        CancellationToken.None);

    private sealed class RecordingAuthority(InstallPreflightResult? fixedResult = null) : IInstallPreflightAuthority
    {
        public List<InstallPreflightRequest> Requests { get; } = [];

        public Task<InstallPreflightResult> PreflightAsync(InstallPreflightRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (fixedResult is not null) return Task.FromResult(fixedResult);
            UpdateJobSelection selection = UpdateJobSelection.ForInstall(request.CatalogEntryKey, request.Provider, request.Source, request.PackageId, "1.0.0", request.Scope, request.Architecture, request.Channel, "fixture-v1", null, false);
            return Task.FromResult(InstallPreflightResult.Actionable(selection, [], InstallerElevation.Unknown));
        }
    }

    private sealed class FixedProvider(ProviderScanResult result) : ILocalInventoryProvider
    {
        public string Name => "fixture";
        public TimeSpan Timeout => TimeSpan.FromSeconds(1);
        public Task<ProviderScanResult> ScanAsync(CancellationToken cancellationToken) => Task.FromResult(result);
    }

    private sealed class FixedListing(bool listed) : IPostUpdatePackageInventory
    {
        public Task<bool> HasExactExpectedVersionAsync(ImmutableUpdateJob job, CancellationToken cancellationToken) => Task.FromResult(listed);
    }

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly string path;
        private TestDatabase(string path, SqliteDatabase store) { this.path = path; Store = store; }
        public SqliteDatabase Store { get; }
        public static async Task<TestDatabase> CreateAsync()
        {
            string path = Path.Combine(Path.GetTempPath(), $"pcsteward-install-{Guid.NewGuid():N}.db");
            SqliteDatabase store = new(path); await store.MigrateAsync(CancellationToken.None); return new TestDatabase(path, store);
        }
        public ValueTask DisposeAsync() { SqliteConnection.ClearAllPools(); File.Delete(path); return ValueTask.CompletedTask; }
    }
}
