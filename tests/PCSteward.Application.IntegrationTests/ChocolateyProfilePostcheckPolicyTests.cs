using PCSteward.Provider.Chocolatey;

namespace PCSteward.Application.IntegrationTests;

/// <summary>FR-UPD-15/08/09; TM-04/13: synthetic permission policy and no query through a changed client.</summary>
[TestClass]
public sealed class ChocolateyProfilePostcheckPolicyTests
{
    private static readonly string[] ExpectedOrder = ["files", "query"];
    [TestMethod]
    public void DefaultSetupLogFileWriteIsNonLoadingWhileDirectoriesAndGuardInputsStayProtected()
    {
        Assert.AreEqual(ChocolateyTreeContentPolicy.NonLoadingLogContents, ChocolateyTreeCoveragePolicy.Classify("logs/chocolatey.log", false));
        foreach (string path in new[] { "", "logs", "logs/archive", "config/chocolatey.config", "helpers/chocolateyInstaller.psm1", "tools/checksum.exe", "lib/git/git.nupkg" })
            Assert.AreEqual(ChocolateyTreeContentPolicy.Protected, ChocolateyTreeCoveragePolicy.Classify(path, path is "" or "logs" or "logs/archive"));
    }

    [TestMethod]
    [DataRow("hooks")]
    [DataRow("extensions")]
    [DataRow("license")]
    [DataRow("lib-bkp")]
    [DataRow("lib-bad")]
    public void NamedOptionalTreeAbsenceNeedsNoFabricatedEmptyDirectory(string path) =>
        Assert.IsTrue(ChocolateyTreeCoveragePolicy.MayBeAbsent(path));

    [TestMethod]
    public void OptionalAndNonLoadingRulesDoNotApplyToNestedOrEscapingNames()
    {
        Assert.IsFalse(ChocolateyTreeCoveragePolicy.MayBeAbsent("extensions/nested"));
        Assert.IsFalse(ChocolateyTreeCoveragePolicy.MayBeAbsent("config"));
        Assert.ThrowsExactly<ArgumentException>(() => ChocolateyTreeCoveragePolicy.Classify("logs/../config", false));
        Assert.ThrowsExactly<ArgumentException>(() => ChocolateyTreeCoveragePolicy.Classify("/logs/a", false));
    }

    [TestMethod]
    [DataRow("engine")]
    [DataRow("code")]
    [DataRow("client-registration")]
    [DataRow("licensed-registration")]
    [DataRow("missing")]
    [DataRow("malformed")]
    public async Task ChangedOrUnverifiableClientNeverExecutesItsOwnPostVerification(string change)
    {
        ChocolateyClientStateData baseline = Baseline();
        ChocolateyClientStateData? after = change switch
        {
            "engine" => baseline with { EngineSha256 = new string('C', 64) },
            "code" => baseline with { CodeDigest = new string('C', 64) },
            "client-registration" => baseline with { ClientRegistrationDigest = new string('D', 64) },
            "licensed-registration" => baseline with { LicensedExtensionRegistrationDigest = new string('D', 64) },
            "malformed" => baseline with { CodeDigest = "bad" },
            _ => null
        };
        int queries = 0;
        ChocolateyPostClientResult<string> result = await ChocolateyPostUpdateClientCheck.RunAsync(baseline,
            _ => Task.FromResult(after), _ => { queries++; return Task.FromResult("2.0.0"); }, CancellationToken.None);
        Assert.AreEqual(0, queries);
        Assert.IsNull(result.Value);
        Assert.AreEqual(change is "missing" or "malformed" ? ChocolateyClientComparison.Unverifiable : ChocolateyClientComparison.ClientChanged, result.Comparison);
    }

    [TestMethod]
    public async Task UnchangedFileSnapshotPrecedesTargetQueryAndStillReturnsOnlyQueryData()
    {
        List<string> order = [];
        ChocolateyPostClientResult<string> result = await ChocolateyPostUpdateClientCheck.RunAsync(Baseline(),
            _ => { order.Add("files"); return Task.FromResult<ChocolateyClientStateData?>(Baseline()); },
            _ => { order.Add("query"); return Task.FromResult("not-verified"); }, CancellationToken.None);
        CollectionAssert.AreEqual(ExpectedOrder, order.ToArray());
        Assert.AreEqual(ChocolateyClientComparison.Unchanged, result.Comparison);
        Assert.AreEqual("not-verified", result.Value);
    }

    [TestMethod]
    public async Task CancellationAfterFileCaptureCannotRunTargetQuery()
    {
        using CancellationTokenSource cancellation = new();
        int queries = 0;
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => ChocolateyPostUpdateClientCheck.RunAsync(Baseline(),
            _ => { cancellation.Cancel(); return Task.FromResult<ChocolateyClientStateData?>(Baseline()); },
            _ => { queries++; return Task.FromResult("not-verified"); }, cancellation.Token));
        Assert.AreEqual(0, queries);
    }

    private static ChocolateyClientStateData Baseline() => new(ChocolateyClientReleasePolicy.EngineSha256, new string('A', 64), new string('B', 64), null);
}
