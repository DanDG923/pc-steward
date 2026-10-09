using PCSteward.Application;
using PCSteward.Catalog;
using PCSteward.Domain;
using PCSteward.Provider.WinGet;

namespace PCSteward.Application.IntegrationTests;

/// <summary>Task 2: CRD-03/05/08, FR-PRV-01/03, FR-INS-02/03/05/06; injected fakes only.</summary>
[TestClass]
public sealed class ProviderSetupTests
{
    [TestMethod]
    public async Task SetupRequiresPositiveAbsenceConsentAndMachineCapability()
    {
        await using ChoiceFixtures.Database database = await ChoiceFixtures.Database.CreateAsync();
        ChoiceFixtures.Authority authority = new(ChoiceFixtures.Ready);
        ChoiceFixtures.Health winget = TrustedWinget();
        ChoiceFixtures.Health chocolatey = AbsentChocolatey();
        using InventoryWorkflow workflow = database.Workflow(preflight: authority);
        ChocolateySetupPreparation setup = new(workflow, winget, chocolatey, UpdateExecutionCapabilities.UserAndMachine);
        Assert.IsNull((await setup.PrepareAsync(CancellationToken.None)).Selection, "No persisted source consent.");
        await database.EnableSourcesAsync();
        using InventoryWorkflow userOnly = database.Workflow(preflight: authority, capabilities: UpdateExecutionCapabilities.UserScopeOnly);
        Assert.IsNull((await new ChocolateySetupPreparation(userOnly, winget, chocolatey, UpdateExecutionCapabilities.UserScopeOnly).PrepareAsync(CancellationToken.None)).Selection);
        chocolatey.Report = chocolatey.Report with { Presence = ProviderPresence.Unknown, State = ProviderProbeState.Unavailable };
        Assert.IsNull((await setup.PrepareAsync(CancellationToken.None)).Selection, "Unknown is never positive absence.");
        chocolatey.Report = AbsentChocolatey().Report;
        winget.Report = winget.Report with { Trust = ProviderTrust.Untrusted };
        Assert.IsNull((await setup.PrepareAsync(CancellationToken.None)).Selection);
        winget.Report = TrustedWinget().Report;
        chocolatey.Report = chocolatey.Report with { State = ProviderProbeState.Partial };
        Assert.IsNull((await setup.PrepareAsync(CancellationToken.None)).Selection);
        Assert.HasCount(0, authority.Requests, "Rejected health, consent and capability must precede discovery.");
        chocolatey.Report = AbsentChocolatey().Report;

        Assert.IsNotNull((await setup.PrepareAsync(CancellationToken.None)).Selection, "All required fresh positive gates permit review preparation.");
        Assert.HasCount(1, authority.Requests);
    }

    [TestMethod]
    public async Task SetupUsesExactWingetMachineX86Selection()
    {
        await using ChoiceFixtures.Database database = await ChoiceFixtures.Database.CreateAsync();
        await database.EnableSourcesAsync();
        ChoiceFixtures.Authority authority = new(ChoiceFixtures.Ready);
        ChoiceFixtures.Health winget = TrustedWinget();
        ChoiceFixtures.Health chocolatey = AbsentChocolatey();
        using InventoryWorkflow workflow = database.Workflow(preflight: authority);
        ChocolateySetupPreparation setup = new(workflow, winget, chocolatey, UpdateExecutionCapabilities.UserAndMachine);

        InstallSelectionPreparation prepared = await setup.PrepareAsync(CancellationToken.None);

        Assert.IsNotNull(prepared.Selection, prepared.Message);
        Assert.AreEqual("Chocolatey.Chocolatey", prepared.Selection!.PackageId);
        Assert.AreEqual(UpdateArchitecture.X86, prepared.Selection.Architecture);
        Assert.AreEqual(UpdateScope.Machine, prepared.Selection.Scope);
        Assert.AreEqual(UpdateProvider.WinGet, prepared.Selection.Provider);
        Assert.AreEqual(UpdateSource.WinGet, prepared.Selection.Source);
        Assert.AreEqual(PackageOperationKind.Install, prepared.Selection.Operation);
        Assert.AreEqual(ChocolateySetupPreparation.CatalogEntryId, prepared.Selection.StableProductKey);
        Assert.IsTrue(prepared.Selection.RunsElevated);
        Assert.AreEqual(1, winget.Calls);
        Assert.AreEqual(1, chocolatey.Calls);
        Assert.HasCount(1, authority.Requests);
        Assert.HasCount(1, prepared.Preflight!.Agreements);
        Assert.IsNotNull(prepared.Selection.AgreementHash);
        Assert.IsFalse(await database.Store.HasAgreementAcceptanceAsync(prepared.Selection.Source, prepared.Selection.PackageId, prepared.Selection.CandidateVersion, prepared.Selection.AgreementHash!, CancellationToken.None), "Preparation does not check or persist agreement acceptance.");
        OperationBatchReview review = new(Guid.NewGuid(), Guid.NewGuid(), [prepared.Selection]);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => new OperationBatchApprovalService(database.Store, database.Store).ConfirmAsync(review, DateTimeOffset.UtcNow, CancellationToken.None));
        // No executor, launcher or installer port is composed; both probes and preflight are fakes.
        Assert.IsNull(await database.Store.GetApprovedBatchAsync(review.BatchId, CancellationToken.None));
    }

    [TestMethod]
    [DataRow(ProviderPresence.Present, ProviderTrust.Trusted)]
    [DataRow(ProviderPresence.Present, ProviderTrust.Untrusted)]
    [DataRow(ProviderPresence.Unknown, ProviderTrust.Unknown)]
    public async Task ExistingOrUntrustedChocolateyCannotOfferReinstall(ProviderPresence presence, ProviderTrust trust)
    {
        await using ChoiceFixtures.Database database = await ChoiceFixtures.Database.CreateAsync();
        await database.EnableSourcesAsync();
        ChoiceFixtures.Authority authority = new(ChoiceFixtures.Ready);
        using InventoryWorkflow workflow = database.Workflow(preflight: authority);
        ChoiceFixtures.Health chocolatey = new(ChoiceFixtures.Report(CatalogProviderKind.Chocolatey, presence, trust));
        ChocolateySetupPreparation setup = new(workflow, TrustedWinget(), chocolatey, UpdateExecutionCapabilities.UserAndMachine);
        Assert.IsNull((await setup.PrepareAsync(CancellationToken.None)).Selection);
        Assert.HasCount(0, authority.Requests);
    }

    [TestMethod]
    public async Task SetupVerificationRequiresStrictPostInstallTrust()
    {
        await using ChoiceFixtures.Database database = await ChoiceFixtures.Database.CreateAsync();
        using InventoryWorkflow workflow = database.Workflow();
        ChoiceFixtures.Health health = new(ChoiceFixtures.Report(CatalogProviderKind.Chocolatey, ProviderPresence.Present, ProviderTrust.Untrusted));
        ChocolateySetupPreparation setup = new(workflow, TrustedWinget(), health, UpdateExecutionCapabilities.UserAndMachine);

        ChocolateySetupVerification untrusted = await setup.VerifyAsync(new(true, false), CancellationToken.None);

        Assert.IsFalse(untrusted.IsReady, "Ordinary package/local verification cannot grant Chocolatey trust.");
        Assert.AreEqual("Needs attention", untrusted.Message);
        Assert.AreEqual(1, health.Calls, "Fresh strict post-install probe is required even after ordinary verification passes.");
        health.Report = health.Report with { Trust = ProviderTrust.Trusted };
        Assert.IsFalse((await setup.VerifyAsync(new(false, false), CancellationToken.None)).IsReady, "Health alone does not replace provider + independent local verification.");
        Assert.AreEqual(1, health.Calls, "Failed ordinary verification cannot proceed to provider use.");
        Assert.IsTrue((await setup.VerifyAsync(new(true, false), CancellationToken.None)).IsReady, "Both ordinary verification and fresh trust are required.");
        Assert.AreEqual(2, health.Calls);
    }

    [TestMethod]
    public async Task SetupPartialLocalInventoryIsNotPositiveAbsence()
    {
        await using ChoiceFixtures.Database database = await ChoiceFixtures.Database.CreateAsync();
        await database.EnableSourcesAsync();
        database.Local.Result = new([], [], [], ["access-denied"]);
        ChoiceFixtures.Authority authority = new(ChoiceFixtures.Ready);
        using InventoryWorkflow workflow = database.Workflow(preflight: authority);
        Assert.IsNull((await new ChocolateySetupPreparation(workflow, TrustedWinget(), AbsentChocolatey(), UpdateExecutionCapabilities.UserAndMachine).PrepareAsync(CancellationToken.None)).Selection);
        Assert.HasCount(0, authority.Requests);
    }

    [TestMethod]
    public void WrapperArchitectureDoesNotWeakenOrdinaryArchitectureOrScopeChecks()
    {
        InstallPreflightRequest request = new("pcs:tool", UpdateProvider.WinGet, UpdateSource.WinGet, "Vendor.Tool", UpdateScope.Machine, UpdateArchitecture.X64);
        WinGetInstallObservation observation = new("winget", true, true, true, 1, false, "Vendor.Tool", false, "2.0.0", "winget", null, new(UpdateScope.Machine, UpdateArchitecture.X86, InstallerElevation.Unknown, "Exe"), []);
        Assert.IsFalse(WingetInstallPreflightAuthority.Assess(request, observation).IsActionable);
        Assert.IsFalse(WingetInstallPreflightAuthority.Assess(request, observation with { Installer = observation.Installer! with { Architecture = UpdateArchitecture.X64, Scope = UpdateScope.User } }).IsActionable);
        Assert.IsTrue(ChocolateySetupPreparation.IsReservedCatalogEntry("pcs:chocolatey-setup"));
        Assert.IsFalse(ChocolateySetupPreparation.IsReservedCatalogEntry("pcs:tool"));
    }

    [TestMethod]
    [DataRow("disabled")]
    [DataRow("chocolatey:executable-unavailable")]
    public async Task SetupMissingOptionalChocolateyRequiresRepeatedPositiveAbsence(string warning)
    {
        await using ChoiceFixtures.Database database = await ChoiceFixtures.Database.CreateAsync();
        await database.EnableSourcesAsync();
        database.Local.Name = "Chocolatey installed inventory";
        database.Local.Result = new([], [], [], [warning]);
        ChoiceFixtures.Local registry = new() { Name = "Windows uninstall registry" };
        ChoiceFixtures.Authority authority = new(ChoiceFixtures.Ready);
        ChoiceFixtures.Health health = AbsentChocolatey();
        using InventoryWorkflow workflow = database.Workflow(preflight: authority, providers: [registry, database.Local]);
        InstallSelectionPreparation prepared = await new ChocolateySetupPreparation(workflow, TrustedWinget(), health, UpdateExecutionCapabilities.UserAndMachine).PrepareAsync(CancellationToken.None);
        Assert.IsNotNull(prepared.Selection, prepared.Message);
        Assert.AreEqual(2, health.Calls, "The ambiguous optional-provider warning requires a repeated positive observation after scan.");
        Assert.AreEqual(1, registry.Scans);
        Assert.HasCount(1, authority.Requests);
        Assert.AreEqual(UpdateArchitecture.X86, prepared.Selection.Architecture);
        Assert.AreEqual(UpdateScope.Machine, prepared.Selection.Scope);
    }

    [TestMethod]
    [DataRow(ProviderPresence.Unknown, ProviderProbeState.Current)]
    [DataRow(ProviderPresence.Present, ProviderProbeState.Current)]
    [DataRow(ProviderPresence.Absent, ProviderProbeState.Partial)]
    public async Task OptionalProviderWarningCannotOverrideChangedOrUncertainPresence(ProviderPresence presence, ProviderProbeState state)
    {
        await using ChoiceFixtures.Database database = await ChoiceFixtures.Database.CreateAsync();
        await database.EnableSourcesAsync();
        database.Local.Name = "Chocolatey installed inventory";
        database.Local.Result = new([], [], [], ["chocolatey:executable-unavailable"]);
        ChoiceFixtures.Health health = AbsentChocolatey();
        database.Local.OnScan = () => health.Report = health.Report with { Presence = presence, State = state };
        ChoiceFixtures.Authority authority = new(ChoiceFixtures.Ready);
        using InventoryWorkflow workflow = database.Workflow(preflight: authority);
        Assert.IsNull((await new ChocolateySetupPreparation(workflow, TrustedWinget(), health, UpdateExecutionCapabilities.UserAndMachine).PrepareAsync(CancellationToken.None)).Selection);
        Assert.AreEqual(2, health.Calls);
        Assert.HasCount(0, authority.Requests);
    }

    [TestMethod]
    [DataRow("fixture-local", "chocolatey:executable-unavailable")]
    [DataRow("Chocolatey installed inventory spoof", "disabled")]
    [DataRow("Chocolatey installed inventory", "partial")]
    [DataRow("Chocolatey installed inventory", "chocolatey:executable-access-denied")]
    [DataRow("Windows uninstall registry", "access-denied")]
    [DataRow("WinGet installed inventory", "winget:executable-unavailable")]
    public async Task SetupRejectsOtherWarningsAndSpoofedProviderOrigin(string providerName, string warning)
    {
        await using ChoiceFixtures.Database database = await ChoiceFixtures.Database.CreateAsync();
        await database.EnableSourcesAsync();
        database.Local.Name = providerName;
        database.Local.Result = new([], [], [], [warning]);
        ChoiceFixtures.Health health = AbsentChocolatey();
        ChoiceFixtures.Authority authority = new(ChoiceFixtures.Ready);
        using InventoryWorkflow workflow = database.Workflow(preflight: authority);
        Assert.IsNull((await new ChocolateySetupPreparation(workflow, TrustedWinget(), health, UpdateExecutionCapabilities.UserAndMachine).PrepareAsync(CancellationToken.None)).Selection);
        Assert.HasCount(0, authority.Requests);
        Assert.AreEqual(1, health.Calls, "No warning is erased or broadened into positive absence.");
    }

    [TestMethod]
    public async Task MissingChocolateyNeverHidesPartialRegistryOrDetectedSetup()
    {
        await using ChoiceFixtures.Database database = await ChoiceFixtures.Database.CreateAsync();
        await database.EnableSourcesAsync();
        database.Local.Name = "Chocolatey installed inventory";
        database.Local.Result = new([], [], [], ["disabled"]);
        ChoiceFixtures.Local registry = new() { Name = "Windows uninstall registry", Result = new([], [], [], ["partial"]) };
        ChoiceFixtures.Authority authority = new(ChoiceFixtures.Ready);
        using InventoryWorkflow workflow = database.Workflow(preflight: authority, providers: [registry, database.Local]);
        ChocolateySetupPreparation setup = new(workflow, TrustedWinget(), AbsentChocolatey(), UpdateExecutionCapabilities.UserAndMachine);
        Assert.IsNull((await setup.PrepareAsync(CancellationToken.None)).Selection);
        registry.Result = ChoiceFixtures.Registry("Chocolatey", "Chocolatey Software");
        Assert.IsNull((await setup.PrepareAsync(CancellationToken.None)).Selection, "Fresh positive client absence does not override a detected installation.");
        Assert.HasCount(0, authority.Requests);
    }

    [TestMethod]
    public async Task FailedRepeatedHealthAndCancellationAfterScanCannotQuerySetup()
    {
        await using ChoiceFixtures.Database database = await ChoiceFixtures.Database.CreateAsync();
        await database.EnableSourcesAsync();
        database.Local.Name = "Chocolatey installed inventory";
        database.Local.Result = new([], [], [], ["disabled"]);
        ChoiceFixtures.Health health = AbsentChocolatey();
        health.Observe = calls => calls == 1 ? health.Report : throw new UnauthorizedAccessException("fixture observation failure");
        ChoiceFixtures.Authority authority = new(ChoiceFixtures.Ready);
        using InventoryWorkflow workflow = database.Workflow(preflight: authority);
        Assert.IsNull((await new ChocolateySetupPreparation(workflow, TrustedWinget(), health, UpdateExecutionCapabilities.UserAndMachine).PrepareAsync(CancellationToken.None)).Selection);
        using CancellationTokenSource cancellation = new();
        database.Local.OnScan = cancellation.Cancel;
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => new ChocolateySetupPreparation(workflow, TrustedWinget(), AbsentChocolatey(), UpdateExecutionCapabilities.UserAndMachine).PrepareAsync(cancellation.Token));
        Assert.HasCount(0, authority.Requests);
    }

    [TestMethod]
    public async Task ValidCatalogCannotReplaceFixedReservedSetupIdentity()
    {
        await using ChoiceFixtures.Database database = await ChoiceFixtures.Database.CreateAsync();
        await database.EnableSourcesAsync();
        ChoiceFixtures.Authority authority = new(ChoiceFixtures.Ready);
        using InventoryWorkflow workflow = database.Workflow(preflight: authority, catalogOverride: ChoiceFixtures.Catalog("Vendor.Other"));
        Assert.IsNull((await new ChocolateySetupPreparation(workflow, TrustedWinget(), AbsentChocolatey(), UpdateExecutionCapabilities.UserAndMachine).PrepareAsync(CancellationToken.None)).Selection);
        Assert.HasCount(0, authority.Requests, "Validated display catalog data cannot redirect the fixed provider setup query.");
    }

    private static ChoiceFixtures.Health TrustedWinget() => new(ChoiceFixtures.Report(CatalogProviderKind.WinGet, ProviderPresence.Present, ProviderTrust.Trusted));
    private static ChoiceFixtures.Health AbsentChocolatey() => new(ChoiceFixtures.Report(CatalogProviderKind.Chocolatey, ProviderPresence.Absent, ProviderTrust.Unknown));
}
