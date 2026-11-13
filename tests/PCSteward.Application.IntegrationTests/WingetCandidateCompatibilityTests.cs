using PCSteward.Domain;
using PCSteward.Provider.WinGet;

namespace PCSteward.Application.IntegrationTests;

/// <summary>
/// Owner's PC, 2026-11-13 (0.1.5): a WinGet update the owner confirmed stayed "review" with MissingArchitectureEvidence,
/// MissingScopeEvidence and MissingChannelEvidence, because the display table names none of them. The connector now reads
/// them from WinGet's structured API, and anything it can't read keeps the update a review item.
/// </summary>
[TestClass]
public sealed class WingetCandidateCompatibilityTests
{
    private static readonly MappingReviewReason[] MissingEvidence =
        [MappingReviewReason.MissingArchitectureEvidence, MappingReviewReason.MissingScopeEvidence, MappingReviewReason.MissingChannelEvidence];
    private static readonly string[] OnlyTheUpdate = ["Contoso.Cli"];
    private static readonly string[] ThreePackages = ["Contoso.Cli", "Contoso.Broken", "Contoso.Other"];
    private static readonly string[] TheTwoReadable = ["Contoso.Cli", "Contoso.Other"];

    [TestMethod]
    public void MachineInstallerInTheDefaultChannelGivesMachineX64Stable()
    {
        WingetCandidateCompatibility? evidence = WingetCandidateCompatibilityRules.Evaluate(Observation(), "winget", "Contoso.Cli", "2.0.0", "2.1.0");

        Assert.AreEqual(new WingetCandidateCompatibility("Machine", "X64", "Stable", "Contoso, Inc.", null), evidence);
    }

    [TestMethod]
    public void InstallerWithoutAScopeKeepsTheScopeWinGetRecordedForTheInstalledApp()
    {
        Assert.AreEqual("Machine", WingetCandidateCompatibilityRules.Evaluate(Observation() with { InstallerScope = "Unknown", InstalledScope = "Machine" }, "winget", "Contoso.Cli", "2.0.0", "2.1.0")!.Scope);
        Assert.AreEqual("User", WingetCandidateCompatibilityRules.Evaluate(Observation() with { InstallerScope = "Unknown", InstalledScope = "User" }, "winget", "Contoso.Cli", "2.0.0", "2.1.0")!.Scope);
        Assert.AreEqual("Unknown", WingetCandidateCompatibilityRules.Evaluate(Observation() with { InstallerScope = "Unknown", InstalledScope = "" }, "winget", "Contoso.Cli", "2.0.0", "2.1.0")!.Scope);
        Assert.AreEqual("User", WingetCandidateCompatibilityRules.Evaluate(Observation() with { InstallerScope = "User", InstalledScope = "Machine" }, "winget", "Contoso.Cli", "2.0.0", "2.1.0")!.Scope);
    }

    [TestMethod]
    public void NamedChannelsAndUnlistedArchitecturesStayUnknownOrAsReported()
    {
        Assert.AreEqual("Unknown", WingetCandidateCompatibilityRules.Evaluate(Observation() with { Channel = "beta" }, "winget", "Contoso.Cli", "2.0.0", "2.1.0")!.Channel);
        Assert.AreEqual("Neutral", WingetCandidateCompatibilityRules.Evaluate(Observation() with { InstallerArchitecture = "Neutral" }, "winget", "Contoso.Cli", "2.0.0", "2.1.0")!.Architecture);
        Assert.AreEqual("Arm64", WingetCandidateCompatibilityRules.Evaluate(Observation() with { InstallerArchitecture = "Arm64" }, "winget", "Contoso.Cli", "2.0.0", "2.1.0")!.Architecture);
        Assert.AreEqual("Unknown", WingetCandidateCompatibilityRules.Evaluate(Observation() with { InstallerArchitecture = "X86OnArm64" }, "winget", "Contoso.Cli", "2.0.0", "2.1.0")!.Architecture);
    }

    [TestMethod]
    public void AnythingButTheSameExactTrustedUpdateGivesNoEvidence()
    {
        WinGetCandidateCompatibilityObservation[] refused =
        [
            Observation() with { CatalogIsPredefined = false },
            Observation() with { CatalogIsTrusted = false },
            Observation() with { ExactIdMatch = false },
            Observation() with { MatchCount = 2 },
            Observation() with { WasLimitExceeded = true },
            Observation() with { IsUpdateAvailable = false },
            Observation() with { HasApplicableInstaller = false },
            Observation() with { PackageId = "Contoso.Other" },
            Observation() with { RequestedPackageId = "Contoso.Other" },
            Observation() with { CandidateSourceId = "msstore" },
            Observation() with { InstalledVersion = "1.9.0" },
            Observation() with { CandidateVersion = "2.2.0" },
            Observation() with { CandidateVersion = "2.1.0 & x" }
        ];

        foreach (WinGetCandidateCompatibilityObservation observation in refused)
        {
            Assert.IsNull(WingetCandidateCompatibilityRules.Evaluate(observation, "winget", "Contoso.Cli", "2.0.0", "2.1.0"), observation.ToString());
        }
        Assert.IsNull(WingetCandidateCompatibilityRules.Evaluate(null, "winget", "Contoso.Cli", "2.0.0", "2.1.0"));
    }

    [TestMethod]
    public void WinGetsSingleInstalledProductCodeBecomesTheRegistryProductKeyAndThePublisherIsKept()
    {
        WingetCandidateCompatibility evidence = WingetCandidateCompatibilityRules.Evaluate(Observation() with { InstalledProductCodes = [" {A1B2C3D4-0000-1111-2222-333344445555} "] }, "winget", "Contoso.Cli", "2.0.0", "2.1.0")!;

        Assert.AreEqual(RegistryKey("{A1B2C3D4-0000-1111-2222-333344445555}"), evidence.RegistryProductKey);
        Assert.AreEqual("Contoso, Inc.", evidence.Publisher);
    }

    [TestMethod]
    public void NoneSeveralOrUnboundedProductCodesGiveNoRegistryProductKey()
    {
        string[][] refused = [[], ["{A}", "{B}"], ["{A}", ""], [new string('x', 256)], ["{A}\u0001B"]];
        foreach (string[] codes in refused)
        {
            Assert.IsNull(WingetCandidateCompatibilityRules.Evaluate(Observation() with { InstalledProductCodes = codes }, "winget", "Contoso.Cli", "2.0.0", "2.1.0")!.RegistryProductKey, string.Join("|", codes));
        }
        Assert.AreEqual(RegistryKey("{A}"), WingetCandidateCompatibilityRules.Evaluate(Observation() with { InstalledProductCodes = ["{A}", "{a}"] }, "winget", "Contoso.Cli", "2.0.0", "2.1.0")!.RegistryProductKey);
        Assert.IsNull(WingetCandidateCompatibilityRules.Evaluate(Observation() with { CandidatePublisher = "  " }, "winget", "Contoso.Cli", "2.0.0", "2.1.0")!.Publisher);
    }

    [TestMethod]
    public async Task OnePackageWhoseReadThrowsDoesNotLoseThePackagesAroundIt()
    {
        IReadOnlyList<WinGetCandidateCompatibilityObservation> observations = await WingetCandidateCompatibilityRules.CollectAsync(
            ThreePackages,
            id => id == "Contoso.Broken" ? throw new InvalidOperationException("manifest could not be loaded") : Task.FromResult<WinGetCandidateCompatibilityObservation?>(Observation() with { RequestedPackageId = id }),
            CancellationToken.None);

        CollectionAssert.AreEqual(TheTwoReadable, observations.Select(observation => observation.RequestedPackageId).ToArray());
    }

    [TestMethod]
    public async Task CancellationStillStopsTheWholeRead()
    {
        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => WingetCandidateCompatibilityRules.CollectAsync(ThreePackages, _ => Task.FromResult<WinGetCandidateCompatibilityObservation?>(Observation()), cancelled.Token));
    }

    [TestMethod]
    public async Task TheWindowsEntryWinGetLinksIsOfferedWithoutConfirmation()
    {
        SourceSnapshot snapshot = await new WingetUpdateSourceConnector(Process(), "winget", new FakeCompatibility(_ => [Observation() with { InstalledProductCodes = ["ContosoCliKey"] }])).RefreshAsync(CancellationToken.None);
        // A registry product whose name and publisher do not match the package: only WinGet's own link can match it.
        ProductMatchProfile product = new(RegistryKey("ContosoCliKey"), "Contoso Command Line", null, null, null, "Someone Else", "2.0.0", "Machine", "x64", null);

        UpdateIntelligenceItem item = UpdateIntelligence.Assess([product], [snapshot]).Items.Single();

        Assert.AreEqual(UpdateIntelligenceStatus.VerifiedUpdate, item.Status, item.Explanation);
        Assert.AreEqual(MappingOrigin.ProviderSupplied, item.Mapping!.Origin);
    }

    [TestMethod]
    public async Task ASameNameSamePublisherWindowsEntryIsOfferedWithoutConfirmation()
    {
        SourceSnapshot snapshot = await new WingetUpdateSourceConnector(Process(), "winget", new FakeCompatibility(_ => [Observation()])).RefreshAsync(CancellationToken.None);
        ProductMatchProfile sameName = new("registry:contoso-cli", "Contoso CLI", null, null, null, "Contoso Inc", "2.0.0", "Machine", "x64", null);
        ProductMatchProfile otherPublisher = sameName with { StableProductKey = "registry:other", Publisher = "Fabrikam" };

        UpdateIntelligenceSummary summary = UpdateIntelligence.Assess([sameName, otherPublisher], [snapshot]);

        Assert.AreEqual(UpdateIntelligenceStatus.VerifiedUpdate, summary.Items.Single(item => item.StableProductKey == sameName.StableProductKey).Status);
        Assert.AreEqual(UpdateIntelligenceStatus.ReviewRequired, summary.Items.Single(item => item.StableProductKey == otherPublisher.StableProductKey).Status);
    }

    [TestMethod]
    public async Task ConnectorQueriesOnlyUpdatesAndAConfirmedMachineUpdateBecomesVerified()
    {
        FakeCompatibility compatibility = new(_ => [Observation()]);
        SourceSnapshot snapshot = await new WingetUpdateSourceConnector(Process(), "winget", compatibility).RefreshAsync(CancellationToken.None);

        CollectionAssert.AreEqual(OnlyTheUpdate, compatibility.Requested.ToArray());
        SourcePackageRecord update = snapshot.Packages.Single(package => package.PackageId == "Contoso.Cli");
        Assert.AreEqual(("Machine", "X64", "Stable"), (update.Scope, update.Architecture, update.Channel));
        SourcePackageRecord current = snapshot.Packages.Single(package => package.PackageId == "Contoso.Current");
        Assert.AreEqual(("Unknown", "Unknown", (string?)null), (current.Scope, current.Architecture, current.Channel));

        UpdateIntelligenceItem item = ConfirmedAssessment(snapshot);
        Assert.AreEqual(UpdateIntelligenceStatus.VerifiedUpdate, item.Status, item.Explanation);
        Assert.IsTrue(item.Mapping!.IsActionable);
    }

    [TestMethod]
    public async Task WithoutStructuredEvidenceAConfirmedUpdateStaysReviewWithTheMissingEvidenceReasons()
    {
        SourceSnapshot snapshot = await new WingetUpdateSourceConnector(Process(), "winget").RefreshAsync(CancellationToken.None);

        UpdateIntelligenceItem item = ConfirmedAssessment(snapshot);
        Assert.AreEqual(UpdateIntelligenceStatus.ReviewRequired, item.Status);
        CollectionAssert.IsSubsetOf(MissingEvidence, item.Mapping!.ReviewReasons.ToArray());
    }

    [TestMethod]
    public async Task ConflictingInstallerArchitectureOrScopeStaysReview()
    {
        SourceSnapshot x86 = await new WingetUpdateSourceConnector(Process(), "winget", new FakeCompatibility(_ => [Observation() with { InstallerArchitecture = "X86" }])).RefreshAsync(CancellationToken.None);
        SourceSnapshot user = await new WingetUpdateSourceConnector(Process(), "winget", new FakeCompatibility(_ => [Observation() with { InstallerScope = "User" }])).RefreshAsync(CancellationToken.None);

        CollectionAssert.Contains(ConfirmedAssessment(x86).Mapping!.ReviewReasons.ToArray(), MappingReviewReason.ArchitectureConflict);
        CollectionAssert.Contains(ConfirmedAssessment(user).Mapping!.ReviewReasons.ToArray(), MappingReviewReason.ScopeConflict);
        Assert.AreEqual(UpdateIntelligenceStatus.ReviewRequired, ConfirmedAssessment(x86).Status);
        Assert.AreEqual(UpdateIntelligenceStatus.ReviewRequired, ConfirmedAssessment(user).Status);
    }

    [TestMethod]
    public async Task FailedOrSlowStructuredReadKeepsTheSnapshotLiveButUnknown()
    {
        SourceSnapshot failed = await new WingetUpdateSourceConnector(Process(), "winget", new FakeCompatibility(_ => throw new InvalidOperationException("class not registered"))).RefreshAsync(CancellationToken.None);
        SourceSnapshot slow = await new WingetUpdateSourceConnector(Process(), "winget", new SlowCompatibility(), TimeSpan.FromMilliseconds(50)).RefreshAsync(CancellationToken.None);

        foreach (SourceSnapshot snapshot in new[] { failed, slow })
        {
            Assert.AreEqual(SourceSnapshotState.Live, snapshot.State);
            CollectionAssert.Contains(snapshot.Warnings.ToArray(), "winget:compatibility-unavailable");
            Assert.AreEqual("Unknown", snapshot.Packages.Single(package => package.PackageId == "Contoso.Cli").Scope);
            Assert.AreEqual(UpdateIntelligenceStatus.ReviewRequired, ConfirmedAssessment(snapshot).Status);
        }
    }

    [TestMethod]
    public async Task AnAnswerForAnotherPackageOrADuplicateAnswerIsNotUsed()
    {
        SourceSnapshot other = await new WingetUpdateSourceConnector(Process(), "winget", new FakeCompatibility(_ => [Observation() with { RequestedPackageId = "Contoso.Current" }])).RefreshAsync(CancellationToken.None);
        SourceSnapshot duplicate = await new WingetUpdateSourceConnector(Process(), "winget", new FakeCompatibility(_ => [Observation(), Observation()])).RefreshAsync(CancellationToken.None);

        Assert.AreEqual("Unknown", other.Packages.Single(package => package.PackageId == "Contoso.Cli").Scope);
        Assert.AreEqual("Unknown", duplicate.Packages.Single(package => package.PackageId == "Contoso.Cli").Scope);
    }

    private static UpdateIntelligenceItem ConfirmedAssessment(SourceSnapshot snapshot)
    {
        // The installed app as the registry sees it: machine scope, x64, no channel; the owner confirmed the WinGet package.
        ProductMatchProfile product = new("registry:contoso-cli", "Contoso CLI", null, null, null, "Contoso", "2.0.0", "Machine", "X64", null);
        DeclaredPackageMapping confirmed = new(product.StableProductKey, "winget", "Contoso.Cli", MappingOrigin.UserConfirmed);
        return UpdateIntelligence.Assess([product], [snapshot], [confirmed]).Items.Single();
    }

    private static WinGetCandidateCompatibilityObservation Observation() => new(
        "Contoso.Cli", true, true, true, 1, false, "Contoso.Cli", "2.0.0", "2.1.0", "winget", true, "Machine", true, "System", "X64", "", "Contoso, Inc.", []);

    private static string RegistryKey(string keyName) =>
        InventoryNormalization.ProductFingerprint(new RawInstallEvidence("registry", keyName, "Contoso CLI", "Contoso", "2.0.0", "Machine", "x64", null, "Application", "fixture"));

    private static SequenceProcess Process() => new(
    [
        new(0, "v1.29.380", "", false),
        new(0, "Name          Id               Version  Source\n------------------------------------------------\nContoso CLI   Contoso.Cli      2.0.0    winget\nCurrent Tool  Contoso.Current  1.0      winget", "", false),
        new(0, "Name          Id               Version  Available  Source\n-----------------------------------------------------------\nContoso CLI   Contoso.Cli      2.0.0    2.1.0      winget\n1 upgrades available.", "", false)
    ]);

    private sealed class SequenceProcess(IEnumerable<WingetProcessResult> results) : IWingetProcess
    {
        private readonly Queue<WingetProcessResult> results = new(results);
        public Task<WingetProcessResult> RunAsync(IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken) => Task.FromResult(results.Dequeue());
    }

    private sealed class FakeCompatibility(Func<IReadOnlyList<string>, IReadOnlyList<WinGetCandidateCompatibilityObservation>> answer) : IWinGetCandidateCompatibilityQuery
    {
        public List<string> Requested { get; } = [];
        public Task<IReadOnlyList<WinGetCandidateCompatibilityObservation>> QueryAsync(string sourceId, IReadOnlyList<string> packageIds, CancellationToken cancellationToken)
        {
            Requested.AddRange(packageIds);
            return Task.FromResult(answer(packageIds));
        }
    }

    private sealed class SlowCompatibility : IWinGetCandidateCompatibilityQuery
    {
        public async Task<IReadOnlyList<WinGetCandidateCompatibilityObservation>> QueryAsync(string sourceId, IReadOnlyList<string> packageIds, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return [];
        }
    }
}
