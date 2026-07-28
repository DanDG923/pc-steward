using PCSteward.Provider.WinGet;

namespace PCSteward.Application.IntegrationTests;

[TestClass]
public sealed class WingetInstalledInventoryProviderTests
{
    private static readonly string[] ExpectedArguments = ["list", "--disable-interactivity"];
    private static readonly string[] ExpectedVersionArguments = ["--version"];
    private static readonly string[] ExpectedUpdateArguments = ["upgrade", "--source", "winget", "--disable-interactivity"];
    [TestMethod]
    public async Task UsesFixedExecutableReadOnlyArgumentsAndParsesBoundedTableOutput()
    {
        FakeProcess process = new(new WingetProcessResult(0, "Name  Id  Version  Source\n--------------------------------\nContoso App  Contoso.App  1.2.3  winget", string.Empty, false));
        WingetInventoryResult result = await new WingetInstalledInventoryProvider(process).ReadInstalledAsync(CancellationToken.None);
        CollectionAssert.AreEqual(ExpectedArguments, process.Arguments.ToArray()); Assert.AreEqual("Contoso.App", result.Records.Single().Id);
    }
    [TestMethod]
    public async Task ClassifiesTimeoutAndNonzeroExitWithoutParsing()
    {
        WingetInventoryResult timeout = await new WingetInstalledInventoryProvider(new FakeProcess(new WingetProcessResult(0, string.Empty, string.Empty, true))).ReadInstalledAsync(CancellationToken.None);
        CollectionAssert.Contains(timeout.Warnings.ToArray(), "winget:timeout");
    }
    [TestMethod]
    public async Task UpdateSourceConnectorUsesOnlyFixedSourceArgumentsAndReturnsBoundedLiveRecords()
    {
        SequencedProcess process = new(new WingetProcessResult[] { new(0, "v1.9.0", "", false), new(0, "Name  Id  Version  Available  Source\n----------------------------------------------\nContoso Widget  Contoso.Widget  1.0.0  1.1.0  winget", "", false) });
        PCSteward.Domain.SourceSnapshot snapshot = await new WingetUpdateSourceConnector(process, "winget").RefreshAsync(CancellationToken.None);
        CollectionAssert.AreEqual(ExpectedVersionArguments, process.Calls[0].ToArray()); CollectionAssert.AreEqual(ExpectedUpdateArguments, process.Calls[1].ToArray());
        Assert.AreEqual("v1.9.0", snapshot.ProviderVersion); Assert.AreEqual(PCSteward.Domain.SourceSnapshotState.Live, snapshot.State); Assert.AreEqual("Contoso.Widget", snapshot.Packages.Single().PackageId);
    }
    [TestMethod]
    public void MalformedOrWrongSourceUpdateRowsAreExcludedAndUnsupportedSourcesAreRejected()
    {
        WingetAvailableUpdateResult result = WingetUpdateSourceConnector.ParseAvailableTable("Name  Id  Version  Available  Source\n----------------------------------------------\nBad  bad id!  1.0  2.0  winget\nOther  Other.App  1.0  2.0  msstore", "v1", "winget");
        Assert.HasCount(0, result.Records); CollectionAssert.Contains(result.Warnings.ToArray(), "winget:invalid-update-row"); Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new WingetUpdateSourceConnector(new FakeProcess(new WingetProcessResult(0, "", "", false)), "arbitrary"));
    }
    [TestMethod]
    public void SourceQualifiedQueryAcceptsRowsWhenWingetOmitsTheSourceColumn()
    {
        WingetAvailableUpdateResult result = WingetUpdateSourceConnector.ParseAvailableTable("Name                        Id              Version Available\n-------------------------------------------------------------\nMozilla Firefox (x64 en-US) Mozilla.Firefox 152.0.6 153.0.1\n1 upgrades available.", "v1", "winget");

        Assert.HasCount(1, result.Records); Assert.AreEqual("Mozilla.Firefox", result.Records.Single().Id); Assert.AreEqual("winget", result.Records.Single().Source);
    }
    [TestMethod]
    public void ProcessBoundaryHasNoExecutablePathParameterAndOversizedOutputFailsClosed()
    {
        Type[] parameterTypes = typeof(WindowsWingetProcess).GetMethod(nameof(WindowsWingetProcess.RunAsync))!.GetParameters().Select(parameter => parameter.ParameterType).ToArray();
        Assert.IsFalse(parameterTypes.Contains(typeof(string)));

        WingetProcessResult oversized = new(-1, new string('x', 1_000_000), string.Empty, false, true);
        CollectionAssert.Contains(WingetUpdateSourceConnector.Classify(oversized, "v1", "winget").Warnings.ToArray(), "winget:output-too-large");
    }
    private sealed class FakeProcess(WingetProcessResult result) : IWingetProcess
    {
        public IReadOnlyList<string> Arguments { get; private set; } = [];
        public Task<WingetProcessResult> RunAsync(IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken) { Arguments = arguments; return Task.FromResult(result); }
    }
    private sealed class SequencedProcess(IEnumerable<WingetProcessResult> results) : IWingetProcess
    {
        private readonly Queue<WingetProcessResult> results = new(results); public List<IReadOnlyList<string>> Calls { get; } = [];
        public Task<WingetProcessResult> RunAsync(IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken) { Calls.Add(arguments); return Task.FromResult(results.Dequeue()); }
    }
}
