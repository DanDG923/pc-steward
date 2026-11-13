using PCSteward.Domain;
using PCSteward.Provider.WinGet;

namespace PCSteward.Application.IntegrationTests;

/// <summary>
/// Owner's PC, 2026-11-13 (0.1.6): after GitHub CLI's MSI upgrade the reviewed uninstall entry was gone, replaced by one under a
/// new product code. WinGet's own link names the new entry, and only an exact, trusted, single package at exactly the target
/// version with exactly one product code gives a key.
/// </summary>
[TestClass]
public sealed class WingetPostUpdateRegistryLinkTests
{
    private static readonly string NewEntryKey = WingetCandidateCompatibilityRules.RegistryProductKey(["{NEW-CODE}"])!;

    [TestMethod]
    public void ExactTrustedPackageAtTheTargetVersionNamesItsSingleEntry()
    {
        Assert.IsNotNull(NewEntryKey);
        Assert.AreEqual(
            InventoryNormalization.ProductFingerprint(new RawInstallEvidence("registry", "{new-code}", "x", null, null, "Machine", "x64", null, "Application", "registry")),
            WingetPostUpdateRegistryLink.Evaluate(Observation(), "Contoso.Cli", "2.1.0"));
    }

    [TestMethod]
    public void AnythingLessThanAnExactSingleTrustedMatchAtTheTargetGivesNoKey()
    {
        Assert.IsNull(WingetPostUpdateRegistryLink.Evaluate(null, "Contoso.Cli", "2.1.0"));
        Assert.IsNull(WingetPostUpdateRegistryLink.Evaluate(Observation() with { CatalogIsPredefined = false }, "Contoso.Cli", "2.1.0"));
        Assert.IsNull(WingetPostUpdateRegistryLink.Evaluate(Observation() with { CatalogIsTrusted = false }, "Contoso.Cli", "2.1.0"));
        Assert.IsNull(WingetPostUpdateRegistryLink.Evaluate(Observation() with { ExactIdMatch = false }, "Contoso.Cli", "2.1.0"));
        Assert.IsNull(WingetPostUpdateRegistryLink.Evaluate(Observation() with { MatchCount = 2 }, "Contoso.Cli", "2.1.0"));
        Assert.IsNull(WingetPostUpdateRegistryLink.Evaluate(Observation() with { WasLimitExceeded = true }, "Contoso.Cli", "2.1.0"));
        Assert.IsNull(WingetPostUpdateRegistryLink.Evaluate(Observation() with { PackageId = "Contoso.Other" }, "Contoso.Cli", "2.1.0"));
        Assert.IsNull(WingetPostUpdateRegistryLink.Evaluate(Observation() with { InstalledVersion = "2.0.0" }, "Contoso.Cli", "2.1.0"));
        Assert.IsNull(WingetPostUpdateRegistryLink.Evaluate(Observation() with { InstalledProductCodes = [] }, "Contoso.Cli", "2.1.0"));
        Assert.IsNull(WingetPostUpdateRegistryLink.Evaluate(Observation() with { InstalledProductCodes = ["{NEW-CODE}", "{OTHER-CODE}"] }, "Contoso.Cli", "2.1.0"));
    }

    [TestMethod]
    public async Task ResolveAsksOnlyForWinGetJobsAndTreatsAFailedOrSlowReadAsNoLink()
    {
        WingetPostUpdateRegistryLink working = new(new FixedQuery(Observation()), TimeSpan.FromSeconds(5));
        WingetPostUpdateRegistryLink failing = new(new FixedQuery(null, fail: true), TimeSpan.FromSeconds(5));
        WingetPostUpdateRegistryLink slow = new(new FixedQuery(Observation(), hang: true), TimeSpan.FromMilliseconds(50));

        Assert.AreEqual(NewEntryKey, await working.ResolveAsync(Job(UpdateProvider.WinGet, UpdateSource.WinGet), CancellationToken.None));
        Assert.IsNull(await working.ResolveAsync(Job(UpdateProvider.Chocolatey, UpdateSource.ChocolateyCommunity), CancellationToken.None));
        Assert.IsNull(await failing.ResolveAsync(Job(UpdateProvider.WinGet, UpdateSource.WinGet), CancellationToken.None));
        Assert.IsNull(await slow.ResolveAsync(Job(UpdateProvider.WinGet, UpdateSource.WinGet), CancellationToken.None));
    }

    /// <summary>Codex on PR #81: the desktop composes the inventories, so the composite must pass the link on.</summary>
    [TestMethod]
    public async Task TheDesktopCompositePassesOnTheOneLinkedKey()
    {
        ImmutableUpdateJob job = Job(UpdateProvider.WinGet, UpdateSource.WinGet);
        CompositePostUpdatePackageInventory linkedThenNone = new([new LinkedInventory("KEY-A"), new LinkedInventory(null)]);
        CompositePostUpdatePackageInventory noneThenLinked = new([new LinkedInventory(null), new LinkedInventory("KEY-A")]);
        CompositePostUpdatePackageInventory sameTwice = new([new LinkedInventory("KEY-A"), new LinkedInventory("KEY-A")]);
        CompositePostUpdatePackageInventory twoDifferent = new([new LinkedInventory("KEY-A"), new LinkedInventory("KEY-B")]);
        CompositePostUpdatePackageInventory none = new([new LinkedInventory(null)]);

        Assert.AreEqual("KEY-A", await linkedThenNone.GetLinkedProductKeyAfterUpdateAsync(job, CancellationToken.None));
        Assert.AreEqual("KEY-A", await noneThenLinked.GetLinkedProductKeyAfterUpdateAsync(job, CancellationToken.None));
        Assert.AreEqual("KEY-A", await sameTwice.GetLinkedProductKeyAfterUpdateAsync(job, CancellationToken.None));
        Assert.IsNull(await twoDifferent.GetLinkedProductKeyAfterUpdateAsync(job, CancellationToken.None));
        Assert.IsNull(await none.GetLinkedProductKeyAfterUpdateAsync(job, CancellationToken.None));
    }

    private static WinGetInstalledLinkObservation Observation() => new(true, true, true, 1, false, "Contoso.Cli", "2.1.0", ["{NEW-CODE}"]);

    private static ImmutableUpdateJob Job(UpdateProvider provider, UpdateSource source) =>
        new UpdateJobReview(Guid.NewGuid(), Guid.NewGuid(), new UpdateJobSelection("reviewed-key", provider, source, "Contoso.Cli", "2.0.0", "2.1.0", UpdateScope.Machine, UpdateArchitecture.X64, UpdateChannel.Stable, "fixture")).Confirm(DateTimeOffset.UtcNow);

    private sealed class LinkedInventory(string? key) : IPostUpdatePackageInventory
    {
        public Task<bool> HasExactExpectedVersionAsync(ImmutableUpdateJob job, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<string?> GetLinkedProductKeyAfterUpdateAsync(ImmutableUpdateJob job, CancellationToken cancellationToken) => Task.FromResult(key);
    }

    private sealed class FixedQuery(WinGetInstalledLinkObservation? observation, bool fail = false, bool hang = false) : IWinGetInstalledLinkQuery
    {
        public async Task<WinGetInstalledLinkObservation?> QueryAsync(string sourceId, string packageId, CancellationToken cancellationToken)
        {
            if (fail) throw new InvalidOperationException("WinGet could not be read.");
            if (hang) await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            return observation;
        }
    }
}
