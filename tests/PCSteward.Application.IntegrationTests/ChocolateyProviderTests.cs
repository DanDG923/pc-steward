using PCSteward.Domain;
using PCSteward.Provider.Chocolatey;

namespace PCSteward.Application.IntegrationTests;

[TestClass]
public sealed class ChocolateyProviderTests
{
    [TestMethod]
    public async Task CommunityConnectorUsesOnlyFixedCommunityArgumentsAndReturnsBoundedRecords()
    {
        RecordingProcess process = new(
            new(0, "2.6.0\n", "", false),
            new(0, "git|2.45.1|2.46.0|false\n7zip|24.08|24.09\n", "", false));
        ChocolateyUpdateSourceConnector connector = new(process);

        SourceSnapshot snapshot = await connector.RefreshAsync(CancellationToken.None);

        Assert.AreEqual(ChocolateyUpdateSourceConnector.CommunitySourceId, snapshot.SourceId);
        Assert.AreEqual("2.6.0", snapshot.ProviderVersion);
        CollectionAssert.AreEqual(new[] { "outdated", "--source", ChocolateyUpdateSourceConnector.CommunitySourceUrl, "--ignore-unfound", "--limit-output", "--no-color", "--execution-timeout", "60" }, process.Calls[1].ToArray());
        Assert.HasCount(2, snapshot.Packages);
        Assert.IsFalse(snapshot.Packages.Any(package => package.RequiresConnectorSpecificVersionSemantics));
    }

    [TestMethod]
    public void MalformedInjectedOrOversizedRowsNeverBecomeSourceEvidence()
    {
        ChocolateyParseResult<ChocolateyOutdatedRecord> malformed = ChocolateyUpdateSourceConnector.ParseOutdated(new(0, "git|2.45.1|2.46.0|false|extra\nmalicious;arg|1.0|2.0\n", "", false));
        ChocolateyParseResult<ChocolateyOutdatedRecord> oversized = ChocolateyUpdateSourceConnector.ParseOutdated(new(-1, new string('x', 1_000_000), "", false, true));

        Assert.HasCount(0, malformed.Records);
        CollectionAssert.Contains(malformed.Warnings.ToArray(), "chocolatey:malformed-outdated-row");
        Assert.HasCount(0, oversized.Records);
        CollectionAssert.Contains(oversized.Warnings.ToArray(), "chocolatey:output-too-large");
    }

    [TestMethod]
    public async Task LocalChocolateyRegistrationNeverInfersCommunitySourceProvenance()
    {
        ChocolateyLocalInventoryProvider provider = new(() => true, new RecordingProcess(new ChocolateyProcessResult(0, "git|2.45.1\n", "", false)));

        ProviderScanResult scan = await provider.ScanAsync(CancellationToken.None);

        Assert.HasCount(1, scan.Instances);
        Assert.IsNull(scan.Instances.Single().SourceId);
        Assert.AreEqual("chocolatey:unknown:git", scan.Products.Single().StableKey);
        StringAssert.Contains(scan.RawRecords.Single().Payload, "manual-community-mapping-required");
    }

    [TestMethod]
    public void ImmutableMachineJobBuildsOnlyTheProviderOwnedChocolateyOperation()
    {
        ImmutableUpdateJob job = new UpdateJobReview(Guid.NewGuid(), Guid.NewGuid(), new UpdateJobSelection(
            "chocolatey:chocolatey-community:git", UpdateProvider.Chocolatey, UpdateSource.ChocolateyCommunity,
            "git", "2.45.1", "2.46.0", UpdateScope.Machine, UpdateArchitecture.Neutral, UpdateChannel.Stable,
            "chocolatey-fixed-community-v1")).Confirm(DateTimeOffset.UtcNow);

        IReadOnlyList<string> arguments = ChocolateyMachineUpdateExecutor.BuildUpdateArguments(job);

        CollectionAssert.AreEqual(new[] { "upgrade", "git", "--version", "2.46.0", "--source", ChocolateyUpdateSourceConnector.CommunitySourceUrl, "--yes", "--limit-output", "--no-color", "--execution-timeout", "300" }, arguments.ToArray());
        Assert.ThrowsExactly<ArgumentException>(() => ChocolateyMachineUpdateExecutor.BuildUpdateArguments(new UpdateJobReview(Guid.NewGuid(), Guid.NewGuid(), new UpdateJobSelection(
            "winget:contoso.widget", UpdateProvider.WinGet, UpdateSource.WinGet, "Contoso.Widget", "1.0", "1.1", UpdateScope.Machine, UpdateArchitecture.X64, UpdateChannel.Stable, "winget-com-v1")).Confirm(DateTimeOffset.UtcNow)));
    }

    [TestMethod]
    public async Task ExactPostUpdateInventoryRejectsWrongVersionAndUnsignedFilesCannotBecomeClientAuthority()
    {
        ChocolateyPostUpdatePackageInventory matching = new(new RecordingProcess(new ChocolateyProcessResult(0, "git|2.46.0\n", "", false)));
        ChocolateyPostUpdatePackageInventory wrongVersion = new(new RecordingProcess(new ChocolateyProcessResult(0, "git|2.45.1\n", "", false)));
        ImmutableUpdateJob job = new UpdateJobReview(Guid.NewGuid(), Guid.NewGuid(), new UpdateJobSelection(
            "chocolatey:chocolatey-community:git", UpdateProvider.Chocolatey, UpdateSource.ChocolateyCommunity,
            "git", "2.45.1", "2.46.0", UpdateScope.Machine, UpdateArchitecture.Neutral, UpdateChannel.Stable,
            "chocolatey-fixed-community-v1")).Confirm(DateTimeOffset.UtcNow);
        string unsigned = Path.Combine(Path.GetTempPath(), $"pcsteward-chocolatey-{Guid.NewGuid():N}.exe");
        await File.WriteAllTextAsync(unsigned, "not a signed executable");
        try
        {
            Assert.IsTrue(await matching.HasExactExpectedVersionAsync(job, CancellationToken.None));
            Assert.IsFalse(await wrongVersion.HasExactExpectedVersionAsync(job, CancellationToken.None));
            Assert.IsFalse(MachineTrustedChocolatey.IsChocolateyTrusted(unsigned));
        }
        finally { File.Delete(unsigned); }
    }

    [TestMethod]
    public void ChocolateyClientTrustRejectsWritableLayoutsWrongSignersAndReplacement()
    {
        ChocolateyClientTrustEvidence trusted = new(true, true, true, true, true);

        Assert.IsTrue(MachineTrustedChocolatey.IsChocolateyTrustAccepted(trusted));
        foreach (ChocolateyClientTrustEvidence rejected in new[]
        {
            trusted with { CallerCannotModifyLayout = false },
            trusted with { ExactSigner = false },
            trusted with { NoReparseComponents = false }
        })
        {
            Assert.IsFalse(MachineTrustedChocolatey.IsChocolateyTrustAccepted(rejected));
        }
        Assert.IsTrue(MachineTrustedChocolatey.DigestsMatch(new byte[32], new byte[32]));
        Assert.IsFalse(MachineTrustedChocolatey.DigestsMatch(new byte[32], Enumerable.Repeat((byte)1, 32).ToArray()));
    }

    private sealed class RecordingProcess(params ChocolateyProcessResult[] results) : IChocolateyProcess
    {
        private readonly Queue<ChocolateyProcessResult> results = new(results);
        public List<IReadOnlyList<string>> Calls { get; } = [];
        public Task<ChocolateyProcessResult> RunAsync(IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken)
        {
            Calls.Add(arguments);
            return Task.FromResult(results.Dequeue());
        }
    }
}
