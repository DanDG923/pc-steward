using PCSteward.Provider.WinGet;

namespace PCSteward.Application.IntegrationTests;

[TestClass]
public sealed class WingetStructuredUpdateCandidateAuthorityTests
{
    [TestMethod]
    public async Task ProductionAuthorityPreservesExactStructuredSourcePackageAndVersions()
    {
        WinGetStructuredCatalogObservation observation = ValidObservation();
        RecordingStructuredCatalogQuery query = new(observation);
        WingetStructuredUpdateCandidateAuthority authority = new(query, TimeSpan.FromSeconds(1));

        StructuredUpdateCandidateEvidence? evidence = await authority.ResolveAsync(
            "WINGET",
            "Contoso.Widget",
            CancellationToken.None);

        Assert.IsNotNull(evidence);
        Assert.AreEqual("winget", evidence.SourceId);
        Assert.AreEqual("Contoso.Widget", evidence.PackageId);
        Assert.AreEqual("1.0.0", evidence.InstalledVersion);
        Assert.AreEqual("1.1.0", evidence.CandidateVersion);
        Assert.AreEqual(("winget", "Contoso.Widget"), query.SingleCall);
    }

    [TestMethod]
    public async Task ProductionAuthorityFailsClosedForEveryNonExactOrUntrustedObservation()
    {
        WinGetStructuredCatalogObservation valid = ValidObservation();
        WinGetStructuredCatalogObservation[] hostile =
        [
            valid with { CatalogIsPredefined = false },
            valid with { CatalogIsTrusted = false },
            valid with { ExactIdMatch = false },
            valid with { MatchCount = 0 },
            valid with { MatchCount = 2 },
            valid with { WasLimitExceeded = true },
            valid with { IsUpdateAvailable = false },
            valid with { SourceId = "msstore" },
            valid with { CandidateSourceId = "msstore" },
            valid with { PackageId = "Contoso.Other" },
            valid with { InstalledVersion = "0 9" },
            valid with { CandidateVersion = "1 1" },
            valid with { CandidateVersion = "1.0.0" },
            valid with { CandidateVersion = "0.9.0" }
        ];

        foreach (WinGetStructuredCatalogObservation observation in hostile)
        {
            WingetStructuredUpdateCandidateAuthority authority = new(
                new RecordingStructuredCatalogQuery(observation),
                TimeSpan.FromSeconds(1));

            Assert.IsNull(
                await authority.ResolveAsync("winget", "Contoso.Widget", CancellationToken.None),
                observation.ToString());
        }
    }

    [TestMethod]
    public async Task ProductionAuthorityRejectsUnsupportedOrUnboundedInputsWithoutQuerying()
    {
        RecordingStructuredCatalogQuery query = new(ValidObservation());
        WingetStructuredUpdateCandidateAuthority authority = new(query, TimeSpan.FromSeconds(1));

        Assert.IsNull(await authority.ResolveAsync("private", "Contoso.Widget", CancellationToken.None));
        Assert.IsNull(await authority.ResolveAsync("winget", "Contoso Widget", CancellationToken.None));
        Assert.IsNull(await authority.ResolveAsync("winget", new string('a', 257), CancellationToken.None));
        Assert.AreEqual(0, query.CallCount);
    }

    [TestMethod]
    public async Task ProductionAuthorityBoundsAStalledComQueryAndPropagatesCallerCancellation()
    {
        WingetStructuredUpdateCandidateAuthority bounded = new(
            new StalledStructuredCatalogQuery(),
            TimeSpan.FromMilliseconds(20));

        Assert.IsNull(await bounded.ResolveAsync("winget", "Contoso.Widget", CancellationToken.None));

        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => bounded.ResolveAsync("winget", "Contoso.Widget", cancellation.Token));
    }

    private static WinGetStructuredCatalogObservation ValidObservation() => new(
        "winget",
        CatalogIsPredefined: true,
        CatalogIsTrusted: true,
        ExactIdMatch: true,
        MatchCount: 1,
        WasLimitExceeded: false,
        "Contoso.Widget",
        "1.0.0",
        "1.1.0",
        "winget",
        IsUpdateAvailable: true);

    private sealed class RecordingStructuredCatalogQuery(WinGetStructuredCatalogObservation result)
        : IWinGetStructuredCatalogQuery
    {
        private readonly List<(string SourceId, string PackageId)> calls = [];
        public int CallCount => calls.Count;
        public (string SourceId, string PackageId) SingleCall => calls.Single();

        public Task<WinGetStructuredCatalogObservation?> QueryAsync(
            string sourceId,
            string packageId,
            CancellationToken cancellationToken)
        {
            calls.Add((sourceId, packageId));
            return Task.FromResult<WinGetStructuredCatalogObservation?>(result);
        }
    }

    private sealed class StalledStructuredCatalogQuery : IWinGetStructuredCatalogQuery
    {
        public async Task<WinGetStructuredCatalogObservation?> QueryAsync(
            string sourceId,
            string packageId,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, CancellationToken.None);
            return null;
        }
    }
}
