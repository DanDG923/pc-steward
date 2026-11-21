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
            Assert.IsNotNull(WingetCandidateCompatibilityRules.WhyNotUsed(observation, "winget", "Contoso.Cli", "2.0.0", "2.1.0"), observation.ToString());
        }
        Assert.IsNull(WingetCandidateCompatibilityRules.Evaluate(null, "winget", "Contoso.Cli", "2.0.0", "2.1.0"));
        Assert.IsNull(WingetCandidateCompatibilityRules.WhyNotUsed(Observation(), "winget", "Contoso.Cli", "2.0.0", "2.1.0"));
    }

    [TestMethod]
    public void EachRefusedCheckIsNamedAndOnlyABoundedVersionIsEchoed()
    {
        (WinGetCandidateCompatibilityObservation Observation, string Reason)[] cases =
        [
            (Observation() with { CatalogIsPredefined = false }, "catalog-not-predefined"),
            (Observation() with { CatalogIsTrusted = false }, "catalog-not-trusted"),
            (Observation() with { ExactIdMatch = false }, "not-an-exact-id-match"),
            (Observation() with { WasLimitExceeded = true }, "too-many-matches"),
            (Observation() with { MatchCount = 2 }, "matches-2"),
            (Observation() with { IsUpdateAvailable = false }, "no-update-available"),
            (Observation() with { PackageId = "Contoso.Other" }, "another-package"),
            (Observation() with { CandidateSourceId = "msstore" }, "another-source"),
            (Observation() with { InstalledVersion = @"C:\Users\alice" }, "installed-version-unreadable"),
            (Observation() with { CandidateVersion = "2.1.0 & x" }, "available-version-unreadable"),
            (Observation() with { InstalledVersion = "2.0.0.3" }, "installed-version-differs-2.0.0.3"),
            (Observation() with { CandidateVersion = "2.2.0" }, "available-version-differs-2.2.0")
        ];

        foreach ((WinGetCandidateCompatibilityObservation observation, string reason) in cases)
        {
            Assert.AreEqual(reason, WingetCandidateCompatibilityRules.WhyNotUsed(observation, "winget", "Contoso.Cli", "2.0.0", "2.1.0"));
        }
    }

    [TestMethod]
    public async Task APackageWhoseDetailedReadIsNotUsedGetsAWarningNamingItAndTheCheck()
    {
        SourceSnapshot differs = await new WingetUpdateSourceConnector(Process(), "winget", new FakeCompatibility(_ => [Observation() with { InstalledVersion = "2.0.0.3" }])).RefreshAsync(CancellationToken.None);
        SourceSnapshot unread = await new WingetUpdateSourceConnector(Process(), "winget", new FakeCompatibility(_ => new WinGetCandidateCompatibilityRead([], new Dictionary<string, string> { ["Contoso.Cli"] = "read-failed-COMException-0x80004005" }))).RefreshAsync(CancellationToken.None);
        SourceSnapshot missing = await new WingetUpdateSourceConnector(Process(), "winget", new FakeCompatibility(_ => [])).RefreshAsync(CancellationToken.None);
        SourceSnapshot duplicate = await new WingetUpdateSourceConnector(Process(), "winget", new FakeCompatibility(_ => [Observation(), Observation()])).RefreshAsync(CancellationToken.None);
        SourceSnapshot used = await new WingetUpdateSourceConnector(Process(), "winget", new FakeCompatibility(_ => [Observation()])).RefreshAsync(CancellationToken.None);

        CollectionAssert.Contains(differs.Warnings.ToArray(), "winget:detail-unused:Contoso.Cli:installed-version-differs-2.0.0.3");
        CollectionAssert.Contains(unread.Warnings.ToArray(), "winget:detail-unused:Contoso.Cli:read-failed-COMException-0x80004005");
        CollectionAssert.Contains(missing.Warnings.ToArray(), "winget:detail-unused:Contoso.Cli:not-read");
        CollectionAssert.Contains(duplicate.Warnings.ToArray(), "winget:detail-unused:Contoso.Cli:more-than-one-answer");
        Assert.IsFalse(used.Warnings.Any(warning => warning.Contains("detail-unused", StringComparison.Ordinal)));
        // The warning only explains: the update stays a review item with the missing-evidence reasons, as before.
        Assert.AreEqual("Unknown", differs.Packages.Single(package => package.PackageId == "Contoso.Cli").Scope);
        Assert.AreEqual(UpdateIntelligenceStatus.ReviewRequired, ConfirmedAssessment(differs).Status);
    }

    [TestMethod]
    public void WinGetsSingleInstalledProductCodeBecomesTheRegistryProductKeyAndThePublisherIsKept()
    {
        WingetCandidateCompatibility evidence = WingetCandidateCompatibilityRules.Evaluate(Observation() with { InstalledProductCodes = [" {A1B2C3D4-0000-1111-2222-333344445555} "] }, "winget", "Contoso.Cli", "2.0.0", "2.1.0")!;

        Assert.AreEqual(RegistryKey("{A1B2C3D4-0000-1111-2222-333344445555}"), evidence.LinkedProductKey);
        Assert.AreEqual("Contoso, Inc.", evidence.Publisher);
    }

    [TestMethod]
    public void NoneSeveralOrUnboundedProductCodesGiveNoRegistryProductKey()
    {
        string[][] refused = [[], ["{A}", "{B}"], ["{A}", ""], [new string('x', 256)], ["{A}\u0001B"]];
        foreach (string[] codes in refused)
        {
            Assert.IsNull(WingetCandidateCompatibilityRules.Evaluate(Observation() with { InstalledProductCodes = codes }, "winget", "Contoso.Cli", "2.0.0", "2.1.0")!.LinkedProductKey, string.Join("|", codes));
        }
        Assert.AreEqual(RegistryKey("{A}"), WingetCandidateCompatibilityRules.Evaluate(Observation() with { InstalledProductCodes = ["{A}", "{a}"] }, "winget", "Contoso.Cli", "2.0.0", "2.1.0")!.LinkedProductKey);
        Assert.IsNull(WingetCandidateCompatibilityRules.Evaluate(Observation() with { CandidatePublisher = "  " }, "winget", "Contoso.Cli", "2.0.0", "2.1.0")!.Publisher);
    }

    [TestMethod]
    public void WinGetsSingleInstalledPackageFamilyBecomesTheMsixProductKey()
    {
        // Owner's PC, 2026-11-13: WinGet reports the family in lower case; the MSIX inventory names it as Windows does.
        WingetCandidateCompatibility evidence = WingetCandidateCompatibilityRules.Evaluate(MsixObservation(), "winget", "Contoso.Studio", "3.3.0.4529", "3.4.0.4650")!;

        Assert.AreEqual(MsixKey("Contoso.Studio_g54w8ztgkx496"), evidence.LinkedProductKey);
        Assert.AreEqual(("User", "X64", "Stable"), (evidence.Scope, evidence.Architecture, evidence.Channel));
    }

    [TestMethod]
    public void NoneSeveralMalformedOrMixedPackageFamiliesGiveNoMsixProductKey()
    {
        string[][] refused = [["a_g54w8ztgkx496"], ["contoso.studio_g54w8ztgkx496", "contoso.other_g54w8ztgkx496"], ["contoso.studio"], ["contoso.studio_short"], ["contoso studio_g54w8ztgkx496"], [""]];
        foreach (string[] families in refused)
        {
            WingetCandidateCompatibility evidence = WingetCandidateCompatibilityRules.Evaluate(MsixObservation() with { InstalledPackageFamilyNames = families }, "winget", "Contoso.Studio", "3.3.0.4529", "3.4.0.4650")!;
            Assert.IsNull(evidence.LinkedProductKey, string.Join("|", families));
            Assert.AreEqual("Unknown", evidence.Scope, string.Join("|", families));
        }

        // A product code and a package family together are two different local apps: neither is linked.
        WingetCandidateCompatibility mixed = WingetCandidateCompatibilityRules.Evaluate(MsixObservation() with { InstalledProductCodes = ["{A}"] }, "winget", "Contoso.Studio", "3.3.0.4529", "3.4.0.4650")!;
        Assert.IsNull(mixed.LinkedProductKey);
        Assert.AreEqual("Unknown", mixed.Scope);
        Assert.AreEqual(MsixKey("Contoso.Studio_g54w8ztgkx496"), WingetCandidateCompatibilityRules.Evaluate(MsixObservation() with { InstalledPackageFamilyNames = ["contoso.studio_g54w8ztgkx496", "Contoso.Studio_G54W8ZTGKX496"] }, "winget", "Contoso.Studio", "3.3.0.4529", "3.4.0.4650")!.LinkedProductKey);
    }

    [TestMethod]
    public void APackageFamilyIsLinkedOnlyThroughAnMsixInstaller()
    {
        // Codex security review (PR #92): a catalog that names an installed MSIX family but offers another installer type must not gain
        // exact identity from the family, because Windows' family and publisher check would never run.
        foreach (string? type in new[] { "Exe", "Msi", "Wix", "Inno", "Nullsoft", "Burn", "Portable", "Zip", "MSStore", null, "" })
        {
            WingetCandidateCompatibility evidence = WingetCandidateCompatibilityRules.Evaluate(MsixObservation() with { InstallerType = type }, "winget", "Contoso.Studio", "3.3.0.4529", "3.4.0.4650")!;
            Assert.IsNull(evidence.LinkedProductKey, type ?? "null");
            Assert.AreEqual("Unknown", evidence.Scope, type ?? "null");
        }
        Assert.AreEqual(MsixKey("Contoso.Studio_g54w8ztgkx496"), WingetCandidateCompatibilityRules.Evaluate(MsixObservation() with { InstallerType = " msix " }, "winget", "Contoso.Studio", "3.3.0.4529", "3.4.0.4650")!.LinkedProductKey);
    }

    [TestMethod]
    public void OnlyAnMsixInstallerWithNoScopeOfItsOwnIsTakenAsPerUser()
    {
        Assert.AreEqual("Unknown", WingetCandidateCompatibilityRules.Evaluate(MsixObservation() with { InstallerType = "Exe" }, "winget", "Contoso.Studio", "3.3.0.4529", "3.4.0.4650")!.Scope);
        Assert.AreEqual("Unknown", WingetCandidateCompatibilityRules.Evaluate(MsixObservation() with { InstallerType = null }, "winget", "Contoso.Studio", "3.3.0.4529", "3.4.0.4650")!.Scope);
        Assert.AreEqual("Machine", WingetCandidateCompatibilityRules.Evaluate(MsixObservation() with { InstallerScope = "System" }, "winget", "Contoso.Studio", "3.3.0.4529", "3.4.0.4650")!.Scope);
        Assert.AreEqual("Machine", WingetCandidateCompatibilityRules.Evaluate(MsixObservation() with { InstalledScope = "Machine" }, "winget", "Contoso.Studio", "3.3.0.4529", "3.4.0.4650")!.Scope);
        Assert.AreEqual("Unknown", WingetCandidateCompatibilityRules.Evaluate(MsixObservation() with { InstalledPackageFamilyNames = [] }, "winget", "Contoso.Studio", "3.3.0.4529", "3.4.0.4650")!.Scope);
    }

    [TestMethod]
    public async Task TheMsixAppWinGetLinksIsOfferedOnceAndItsWinGetRowIsNotShownTwice()
    {
        SourceSnapshot snapshot = await new WingetUpdateSourceConnector(MsixProcess(), "winget", new FakeCompatibility(_ => [MsixObservation()])).RefreshAsync(CancellationToken.None);
        // The MSIX row as the inventory now builds it (package family identity, shown names), and WinGet's own installed record of it.
        ProductMatchProfile msix = new(MsixKey("Contoso.Studio_g54w8ztgkx496"), "Contoso Studio", null, "Contoso.Studio_g54w8ztgkx496", null, "Contoso Audio, Inc.", "3.3.0.4529", "User", "X64", null);
        ProductMatchProfile wingetOnly = new("winget:winget:Contoso.Studio", "Contoso Studio", "Contoso.Studio", null, null, null, "3.3.0.4529", "Unknown", "Unknown", null, InstalledSourceId: "winget", HasConcreteLocalEvidence: false);

        UpdateIntelligenceSummary summary = UpdateIntelligence.Assess([msix, wingetOnly], [snapshot]);

        UpdateIntelligenceItem item = summary.Items.Single();
        Assert.AreEqual(msix.StableProductKey, item.StableProductKey);
        Assert.AreEqual(UpdateIntelligenceStatus.VerifiedUpdate, item.Status, item.Explanation);
        Assert.AreEqual(MappingOrigin.ProviderSupplied, item.Mapping!.Origin);
        Assert.AreEqual("3.4.0.4650", item.Mapping.CandidateVersion);
    }

    [TestMethod]
    public async Task AnMsixAppOfAnotherArchitectureStaysReview()
    {
        SourceSnapshot snapshot = await new WingetUpdateSourceConnector(MsixProcess(), "winget", new FakeCompatibility(_ => [MsixObservation() with { InstallerArchitecture = "Arm64" }])).RefreshAsync(CancellationToken.None);
        ProductMatchProfile msix = new(MsixKey("Contoso.Studio_g54w8ztgkx496"), "Contoso Studio", null, "Contoso.Studio_g54w8ztgkx496", null, "Contoso Audio, Inc.", "3.3.0.4529", "User", "X64", null);

        UpdateIntelligenceItem item = UpdateIntelligence.Assess([msix], [snapshot]).Items.Single();

        Assert.AreEqual(UpdateIntelligenceStatus.ReviewRequired, item.Status);
        CollectionAssert.Contains(item.Mapping!.ReviewReasons.ToArray(), MappingReviewReason.ArchitectureConflict);
    }

    [TestMethod]
    public async Task OnePackageWhoseReadThrowsDoesNotLoseThePackagesAroundIt()
    {
        WinGetCandidateCompatibilityRead read = await WingetCandidateCompatibilityRules.CollectAsync(
            ThreePackages,
            id => id == "Contoso.Broken" ? throw new InvalidOperationException(@"manifest C:\Users\alice\x.yaml could not be loaded") : Task.FromResult(WinGetCandidateCompatibilityReadOne.Read(Observation() with { RequestedPackageId = id })),
            CancellationToken.None);

        CollectionAssert.AreEqual(TheTwoReadable, read.Observations.Select(observation => observation.RequestedPackageId).ToArray());
        // Owner's PC, 2026-11-21: the package left out says why, by the exception's type and HRESULT only, never its message.
        Assert.AreEqual($"read-failed-InvalidOperationException-0x{new InvalidOperationException().HResult:X8}", read.Unread["Contoso.Broken"]);
        Assert.HasCount(1, read.Unread);
    }

    [TestMethod]
    public async Task APackageTheReaderLeavesOutKeepsTheReadersReason()
    {
        WinGetCandidateCompatibilityRead read = await WingetCandidateCompatibilityRules.CollectAsync(
            ThreePackages,
            id => Task.FromResult(id == "Contoso.Broken" ? WinGetCandidateCompatibilityReadOne.NotRead("matches-2") : WinGetCandidateCompatibilityReadOne.Read(Observation() with { RequestedPackageId = id })),
            CancellationToken.None);

        CollectionAssert.AreEqual(TheTwoReadable, read.Observations.Select(observation => observation.RequestedPackageId).ToArray());
        Assert.AreEqual("matches-2", read.Unread["Contoso.Broken"]);
    }

    [TestMethod]
    public async Task CancellationStillStopsTheWholeRead()
    {
        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => WingetCandidateCompatibilityRules.CollectAsync(ThreePackages, _ => Task.FromResult(WinGetCandidateCompatibilityReadOne.Read(Observation())), cancelled.Token));
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
        SourceSnapshot failed = await new WingetUpdateSourceConnector(Process(), "winget", new FakeCompatibility((Func<IReadOnlyList<string>, WinGetCandidateCompatibilityRead>)(_ => throw new InvalidOperationException("class not registered")))).RefreshAsync(CancellationToken.None);
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

    [TestMethod]
    public void WithNoInstallerForTheInstalledScopeTheLinkIsKeptAndTheUpdateIsHeldBack()
    {
        // Owner's PC, 2026-11-19 (MEGAsync, shape only): installed for one account; WinGet's package has only an every-user installer.
        WingetCandidateCompatibility evidence = WingetCandidateCompatibilityRules.Evaluate(
            Observation() with { HasApplicableInstaller = false, InstalledScope = "User", InstallerScope = "Unknown", InstallerArchitecture = "Unknown", InstalledProductCodes = ["ContosoCliKey"] },
            "winget", "Contoso.Cli", "2.0.0", "2.1.0")!;

        Assert.AreEqual(new WingetCandidateCompatibility("User", "Unknown", "Stable", "Contoso, Inc.", RegistryKey("ContosoCliKey"), SourceInstallerFit.NoInstallerForInstalledScope), evidence);
        // A package family is never linked without an msix installer, so it isn't linked here either.
        Assert.IsNull(WingetCandidateCompatibilityRules.Evaluate(MsixObservation() with { HasApplicableInstaller = false, InstallerType = null }, "winget", "Contoso.Studio", "3.3.0.4529", "3.4.0.4650")!.LinkedProductKey);
    }

    [TestMethod]
    [DataRow("Msi", null, "System", SourceInstallerFit.NoObjection)]
    [DataRow("Wix", null, "System", SourceInstallerFit.NoObjection)]
    [DataRow("Exe", null, "System", SourceInstallerFit.NoObjection)]
    [DataRow("Inno", null, "System", SourceInstallerFit.NoObjection)]
    [DataRow("Nullsoft", null, "System", SourceInstallerFit.NoObjection)]
    [DataRow("Burn", null, "System", SourceInstallerFit.NoObjection)]
    [DataRow("Zip", "Wix", "System", SourceInstallerFit.InstallerNotSupported)]
    [DataRow("Zip", "Msi", "System", SourceInstallerFit.InstallerNotSupported)]
    [DataRow("Msix", "Unknown", "System", SourceInstallerFit.InstallerNotSupported)]
    [DataRow("MSStore", "Unknown", "System", SourceInstallerFit.InstallerNotSupported)]
    [DataRow("Portable", "Unknown", "System", SourceInstallerFit.InstallerNotSupported)]
    [DataRow("Font", "Unknown", "System", SourceInstallerFit.InstallerNotSupported)]
    [DataRow("Unknown", "Unknown", "System", SourceInstallerFit.InstallerNotSupported)]
    [DataRow("Exe", "Unknown", "Unknown", SourceInstallerFit.InstallerNotSupported)]
    public void AnEveryUserUpdateIsHeldBackUnlessTheReviewedRouteCanRunItsInstaller(string type, string? nested, string installerScope, SourceInstallerFit expected)
    {
        // Owner's PC, 2026-11-19 (Microsoft GameInput, shape only): a zip around an MSI, every user, was Ready and then refused at review.
        WingetCandidateCompatibility evidence = WingetCandidateCompatibilityRules.Evaluate(
            Observation() with { InstallerType = type, NestedInstallerType = nested, InstallerScope = installerScope, InstalledScope = "Machine" }, "winget", "Contoso.Cli", "2.0.0", "2.1.0")!;

        Assert.AreEqual("Machine", evidence.Scope);
        Assert.AreEqual(expected, evidence.InstallerFit);
        // The review's own rule: the lowercase type must be one the route parses, and the installer must itself say every user.
        bool reviewAccepts = WinGetMachineEvidence.TryParseInstallerType(type.ToLowerInvariant(), out _) && nested is null or "Unknown" && installerScope == "System";
        Assert.AreEqual(reviewAccepts, expected == SourceInstallerFit.NoObjection);
    }

    [TestMethod]
    public void AOneAccountUpdateIsHeldBackExactlyWhenThePerUserReviewWouldRefuseItsInstaller()
    {
        string[] types = ["Unknown", "Inno", "Wix", "Msi", "Nullsoft", "Zip", "Msix", "Exe", "Burn", "MSStore", "Portable", "Font"];
        string?[] nestedTypes = [null, "Unknown", "Inno", "Wix", "Msi", "Nullsoft", "Zip", "Msix", "Exe", "Burn", "MSStore", "Portable", "Font"];
        UpdateJobSelection selection = new("winget:contoso.cli", UpdateProvider.WinGet, UpdateSource.WinGet, "Contoso.Cli", "2.0.0", "2.1.0", UpdateScope.User,
            UpdateArchitecture.X64, UpdateChannel.Unknown, "winget-com-v1");
        foreach (string type in types)
        foreach (string? nested in nestedTypes)
        foreach (string installerScope in new[] { "User", "Unknown" })
        {
            WinGetCandidateCompatibilityObservation observation = Observation() with { InstallerType = type, NestedInstallerType = nested, InstallerScope = installerScope, InstalledScope = "User" };
            WingetCandidateCompatibility evidence = WingetCandidateCompatibilityRules.Evaluate(observation, "winget", "Contoso.Cli", "2.0.0", "2.1.0")!;
            WingetUserInstallerCheck review = WingetUserInstallerRules.Evaluate(
                new("Contoso.Cli", true, true, true, 1, false, "Contoso.Cli", 1, type, nested, installerScope, "Unknown"), selection);
            bool reviewAccepts = review.IsMsix || review.Installer is not null;
            string label = $"{type}/{nested ?? "null"}/{installerScope}";
            // An msix installer with no scope of its own is taken as one account only when WinGet links the package family; the review
            // accepts it either way, so only the fit is compared where the scope stayed one account.
            if (evidence.Scope != "User") continue;
            Assert.AreEqual(reviewAccepts, evidence.InstallerFit == SourceInstallerFit.NoObjection, label);
        }
    }

    [TestMethod]
    public void OnlyTheWinGetSourceAndAKnownScopeAndInstallerTypeAreChecked()
    {
        WinGetCandidateCompatibilityObservation zip = Observation() with { InstallerType = "Zip", NestedInstallerType = "Wix" };
        Assert.AreEqual(SourceInstallerFit.NoObjection, WingetCandidateCompatibilityRules.InstallerFit("msstore", "Machine", zip));
        Assert.AreEqual(SourceInstallerFit.NoObjection, WingetCandidateCompatibilityRules.InstallerFit("winget", "Unknown", zip));
        Assert.AreEqual(SourceInstallerFit.NoObjection, WingetCandidateCompatibilityRules.InstallerFit("winget", "Machine", zip with { InstallerType = null }));
        Assert.AreEqual(SourceInstallerFit.InstallerNotSupported, WingetCandidateCompatibilityRules.InstallerFit("winget", "Machine", zip));
        // An MSIX app's update (scope taken as one account through its package family) raises no objection.
        Assert.AreEqual(SourceInstallerFit.NoObjection, WingetCandidateCompatibilityRules.Evaluate(MsixObservation(), "winget", "Contoso.Studio", "3.3.0.4529", "3.4.0.4650")!.InstallerFit);
    }

    [TestMethod]
    public async Task AZipWrappedEveryUserUpdateIsCachedAndAssessedAsCantUpdateNotReady()
    {
        SourceSnapshot snapshot = await new WingetUpdateSourceConnector(Process(), "winget",
            new FakeCompatibility(_ => [Observation() with { InstallerType = "Zip", NestedInstallerType = "Wix", InstalledProductCodes = ["ContosoCliKey"] }])).RefreshAsync(CancellationToken.None);
        Assert.AreEqual(SourceInstallerFit.InstallerNotSupported, snapshot.Packages.Single(package => package.PackageId == "Contoso.Cli").InstallerFit);
        ProductMatchProfile product = new(RegistryKey("ContosoCliKey"), "Contoso CLI", null, null, null, "Contoso", "2.0.0", "Machine", "x64", null);

        UpdateIntelligenceItem item = UpdateIntelligence.Assess([product], [snapshot]).Items.Single();

        Assert.AreEqual(UpdateIntelligenceStatus.ReviewRequired, item.Status, item.Explanation);
        Assert.IsFalse(item.Mapping!.IsActionable);
        Assert.AreEqual(MappingOrigin.ProviderSupplied, item.Mapping.Origin);
        CollectionAssert.AreEqual(new[] { MappingReviewReason.InstallerNotSupported }, item.Mapping.ReviewReasons.ToArray());
        // The owner's confirmation can't settle it either.
        DeclaredPackageMapping confirmed = new(product.StableProductKey, "winget", "Contoso.Cli", MappingOrigin.UserConfirmed);
        Assert.AreEqual(UpdateIntelligenceStatus.ReviewRequired, UpdateIntelligence.Assess([product], [snapshot], [confirmed]).Items.Single().Status);
    }

    [TestMethod]
    public async Task AnAppInstalledForOneAccountWithOnlyAnEveryUserInstallerIsOneRowThatCantUpdate()
    {
        // Owner's PC, 2026-11-19 (MEGAsync, shape only): the account's uninstall entry, WinGet's own record of the package at the version
        // it maps that entry to, and the app's Explorer add-on, a separate MSIX package with the same name and publisher at that version.
        SourceSnapshot snapshot = await new WingetUpdateSourceConnector(Process(), "winget", new FakeCompatibility(_ =>
            [Observation() with { HasApplicableInstaller = false, InstalledScope = "User", InstallerScope = "Unknown", InstallerArchitecture = "Unknown", CandidatePublisher = "Contoso", InstalledProductCodes = ["ContosoCliKey"] }]))
            .RefreshAsync(CancellationToken.None);
        ProductMatchProfile entry = new(RegistryKey("ContosoCliKey"), "Contoso CLI", null, null, null, "Contoso", "2.0", "User", "x64", null);
        ProductMatchProfile addOn = new(MsixKey("Contoso.CliShell_g54w8ztgkx496"), "Contoso CLI", null, "Contoso.CliShell_g54w8ztgkx496", null, "Contoso", "2.0.0", "User", "X64", null);
        ProductMatchProfile wingetOnly = new("winget:winget:Contoso.Cli", "Contoso CLI", "Contoso.Cli", null, null, null, "2.0.0", "Unknown", "Unknown", null, InstalledSourceId: "winget", HasConcreteLocalEvidence: false);

        UpdateIntelligenceSummary summary = UpdateIntelligence.Assess([entry, addOn, wingetOnly], [snapshot]);

        UpdateIntelligenceItem item = summary.Items.Single(candidate => candidate.StableProductKey == entry.StableProductKey);
        Assert.AreEqual(UpdateIntelligenceStatus.ReviewRequired, item.Status, item.Explanation);
        Assert.AreEqual(MappingOrigin.ProviderSupplied, item.Mapping!.Origin);
        CollectionAssert.Contains(item.Mapping.ReviewReasons.ToArray(), MappingReviewReason.NoInstallerForInstalledScope);
        Assert.AreEqual(UpdateIntelligenceStatus.Unmanaged, summary.Items.Single(candidate => candidate.StableProductKey == addOn.StableProductKey).Status, "the add-on doesn't take the linked package by name");
        Assert.IsFalse(summary.Items.Any(candidate => candidate.StableProductKey == wingetOnly.StableProductKey), "WinGet's own record is the entry's row");
        // An owner's recorded mapping for the add-on still holds.
        DeclaredPackageMapping mapped = new(addOn.StableProductKey, "winget", "Contoso.Cli", MappingOrigin.UserConfirmed);
        Assert.IsNotNull(UpdateIntelligence.Assess([entry, addOn], [snapshot], [mapped]).Items.Single(candidate => candidate.StableProductKey == addOn.StableProductKey).Mapping);
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

    // Owner's PC, 2026-11-13 (Elgato Wave Link, shape only): WinGet records no installed scope for an MSIX app, its MSIX installer names
    // none, and WinGet links the installed package by its package family, not a product code.
    private static WinGetCandidateCompatibilityObservation MsixObservation() => new(
        "Contoso.Studio", true, true, true, 1, false, "Contoso.Studio", "3.3.0.4529", "3.4.0.4650", "winget", true, "", true, "Unknown", "X64", "", "Contoso Audio, Inc.", [],
        ["contoso.studio_g54w8ztgkx496"], "Msix");

    private static string MsixKey(string familyName) =>
        InventoryNormalization.ProductFingerprint(new RawInstallEvidence("msix", familyName, "Contoso Studio", "Contoso Audio, Inc.", "3.3.0.4529", "User", "X64", null, "Application", "fixture"));

    private static SequenceProcess MsixProcess() => new(
    [
        new(0, "v1.29.380", "", false),
        new(0, "Name            Id               Version      Source\n----------------------------------------------------\nContoso Studio  Contoso.Studio   3.3.0.4529   winget", "", false),
        new(0, "Name            Id               Version      Available    Source\n-----------------------------------------------------------------\nContoso Studio  Contoso.Studio   3.3.0.4529   3.4.0.4650   winget\n1 upgrades available.", "", false)
    ]);

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

    private sealed class FakeCompatibility(Func<IReadOnlyList<string>, WinGetCandidateCompatibilityRead> answer) : IWinGetCandidateCompatibilityQuery
    {
        public FakeCompatibility(Func<IReadOnlyList<string>, IReadOnlyList<WinGetCandidateCompatibilityObservation>> observations)
            : this(ids => new WinGetCandidateCompatibilityRead(observations(ids), new Dictionary<string, string>())) { }

        public List<string> Requested { get; } = [];
        public Task<WinGetCandidateCompatibilityRead> QueryAsync(string sourceId, IReadOnlyList<string> packageIds, CancellationToken cancellationToken)
        {
            Requested.AddRange(packageIds);
            return Task.FromResult(answer(packageIds));
        }
    }

    private sealed class SlowCompatibility : IWinGetCandidateCompatibilityQuery
    {
        public async Task<WinGetCandidateCompatibilityRead> QueryAsync(string sourceId, IReadOnlyList<string> packageIds, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return WinGetCandidateCompatibilityRead.Empty;
        }
    }
}
