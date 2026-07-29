using PCSteward.Application;
using PCSteward.Provider.WinGet;
using PCSteward.Domain;

namespace PCSteward.Application.IntegrationTests;

[TestClass]
public sealed class WingetInstalledInventoryProviderTests
{
    private static readonly string[] ExpectedArguments = ["list", "--disable-interactivity"];
    private static readonly string[] ExpectedVersionArguments = ["--version"];
    private static readonly string[] ExpectedSourceListArguments = ["list", "--source", "winget", "--disable-interactivity"];
    private static readonly string[] ExpectedUpdateArguments = ["upgrade", "--source", "winget", "--disable-interactivity"];
    [TestMethod]
    public async Task UsesFixedExecutableReadOnlyArgumentsAndParsesBoundedTableOutput()
    {
        FakeProcess process = new(new WingetProcessResult(0, InstalledTable(("Contoso App", "Contoso.App", "1.2.3", "winget")), string.Empty, false));
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
        SequencedProcess process = new(new WingetProcessResult[] { new(0, "v1.9.0", "", false), new(0, InstalledTable(("Contoso Widget", "Contoso.Widget", "1.0.0", "winget")), "", false), new(0, "Name  Id  Version  Available  Source\n----------------------------------------------\nContoso Widget  Contoso.Widget  1.0.0  1.1.0  winget", "", false) });
        PCSteward.Domain.SourceSnapshot snapshot = await new WingetUpdateSourceConnector(process, "winget").RefreshAsync(CancellationToken.None);
        CollectionAssert.AreEqual(ExpectedVersionArguments, process.Calls[0].ToArray()); CollectionAssert.AreEqual(ExpectedSourceListArguments, process.Calls[1].ToArray()); CollectionAssert.AreEqual(ExpectedUpdateArguments, process.Calls[2].ToArray());
        Assert.AreEqual("v1.9.0", snapshot.ProviderVersion); Assert.AreEqual(PCSteward.Domain.SourceSnapshotState.Live, snapshot.State); Assert.AreEqual("Contoso.Widget", snapshot.Packages.Single().PackageId);
    }
    [TestMethod]
    public async Task SourceConnectorRecordsManagedCurrentAppsAlongsideAvailableUpdates()
    {
        SequencedProcess process = new(new WingetProcessResult[]
        {
            new(0, "v1.9.0", "", false),
            new(0, InstalledTable(("Current Tool", "Contoso.Current", "2.0", "winget"), ("Update Tool", "Contoso.Update", "1.0", "winget")), "", false),
            new(0, "Name  Id  Version  Available  Source\n----------------------------------------------\nUpdate Tool  Contoso.Update  1.0  1.1  winget", "", false)
        });

        PCSteward.Domain.SourceSnapshot snapshot = await new WingetUpdateSourceConnector(process, "winget").RefreshAsync(CancellationToken.None);

        Assert.AreEqual("2.0", snapshot.Packages.Single(package => package.PackageId == "Contoso.Current").AvailableVersion);
        Assert.AreEqual("1.1", snapshot.Packages.Single(package => package.PackageId == "Contoso.Update").AvailableVersion);
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
    public async Task ExactPostUpdateQueryRequiresOneExpectedPackageAndVersion()
    {
        ImmutableUpdateJob job = new UpdateJobReview(Guid.NewGuid(), Guid.NewGuid(), new PCSteward.Domain.UpdateJobSelection("winget:contoso", PCSteward.Domain.UpdateProvider.WinGet, PCSteward.Domain.UpdateSource.WinGet, "Contoso.Widget", "1.0", "1.1", PCSteward.Domain.UpdateScope.User, PCSteward.Domain.UpdateArchitecture.X64, PCSteward.Domain.UpdateChannel.Stable, "fixture")).Confirm(DateTimeOffset.UtcNow);
        ExportingProcess matching = new("{\"Sources\":[{\"SourceDetails\":{\"Name\":\"winget\"},\"Packages\":[{\"PackageIdentifier\":\"Contoso.Widget\",\"Version\":\"1.1\"}]}]}");
        ExportingProcess mismatch = new("{\"Sources\":[{\"SourceDetails\":{\"Name\":\"winget\"},\"Packages\":[{\"PackageIdentifier\":\"Contoso.Other\",\"Version\":\"1.1\"}]}]}");

        Assert.IsTrue(await new WingetInstalledInventoryProvider(matching).HasExactExpectedVersionAsync(job, CancellationToken.None));
        Assert.IsFalse(await new WingetInstalledInventoryProvider(mismatch).HasExactExpectedVersionAsync(job, CancellationToken.None));
        CollectionAssert.Contains(matching.Arguments.ToArray(), "export"); CollectionAssert.Contains(matching.Arguments.ToArray(), "--include-versions");
    }
    [TestMethod]
    public async Task ExactPostUpdateQueryCannotAuthorizeSuccessFromDisplayTableText()
    {
        ImmutableUpdateJob job = new UpdateJobReview(Guid.NewGuid(), Guid.NewGuid(), new PCSteward.Domain.UpdateJobSelection("winget:contoso", PCSteward.Domain.UpdateProvider.WinGet, PCSteward.Domain.UpdateSource.WinGet, "Contoso.Widget", "1.0", "1.1", PCSteward.Domain.UpdateScope.User, PCSteward.Domain.UpdateArchitecture.X64, PCSteward.Domain.UpdateChannel.Stable, "fixture")).Confirm(DateTimeOffset.UtcNow);
        FakeProcess forgedTable = new(new WingetProcessResult(0, "Name              Id              Version\nContoso Widget    Contoso.Widget  1.1", string.Empty, false));

        Assert.IsFalse(await new WingetInstalledInventoryProvider(forgedTable).HasExactExpectedVersionAsync(job, CancellationToken.None));
        CollectionAssert.Contains(forgedTable.Arguments.ToArray(), "export");
        Assert.IsFalse(forgedTable.Arguments.Contains("list"));
    }
    [TestMethod]
    public async Task DesktopCompositionUsesOneDisplayCallAndNeverEmitsExecutionCompatibility()
    {
        FakeProcess process = new(new WingetProcessResult(0, InstalledTable(
            ("Contoso Widget", "Contoso.Widget", "1.0", "winget"),
            ("Fabrikam Tool", "Fabrikam.Tool", "2.0", "winget")), string.Empty, false));
        WingetLocalInventoryProvider desktopProvider = new(() => true, new WingetInstalledInventoryProvider(process));

        ProviderScanResult result = await desktopProvider.ScanAsync(CancellationToken.None);

        Assert.AreEqual(1, process.CallCount);
        Assert.HasCount(2, result.Instances);
        Assert.IsTrue(result.Instances.All(instance => instance.Scope == "Unknown" && instance.Architecture == "Unknown"));
        Assert.IsTrue(result.RawRecords.All(record => record.Payload.Contains("compatibility=display-only", StringComparison.Ordinal)));
    }
    [TestMethod]
    public void HeaderBoundariesResistInjectedSpacingAndDoNotDependOnEnglishLabels()
    {
        const string injectedName = "Trusted  Contoso.Widget  1.0";
        WingetInventoryResult injected = WingetInstalledInventoryProvider.ParseTable(InstalledTable((injectedName, "Contoso.Actual", "9.9", "winget")));
        string localized = FixedTable("Nom", "Identifiant", "Version", "Source", ("Outil Fabrikam", "Fabrikam.Tool", "2.0", "winget"));
        WingetInventoryResult localizedResult = WingetInstalledInventoryProvider.ParseTable(localized);

        Assert.AreEqual("Contoso.Actual", injected.Records.Single().Id);
        Assert.AreEqual("9.9", injected.Records.Single().Version);
        Assert.AreEqual("Fabrikam.Tool", localizedResult.Records.Single().Id);
    }
    [TestMethod]
    public void LeadingSourceWarningDoesNotDiscardAValidInstalledTable()
    {
        WingetInventoryResult result = WingetInstalledInventoryProvider.ParseTable("Failed when searching source; results will not be included: msstore\n" + InstalledTable(("Contoso Widget", "Contoso.Widget", "1.0", "winget")));

        Assert.AreEqual("Contoso.Widget", result.Records.Single().Id);
    }
    [TestMethod]
    public void AvailableDisplayTableVariantsRemainUntrustedPresentationEvidence()
    {
        WingetAvailableUpdateResult injected = WingetUpdateSourceConnector.ParseAvailableTable(
            "Name  Id  Version  Available  ActualId  ActualVersion  Source\n" +
            "----------------------------------------------------------------\n" +
            "Trusted  Contoso.Widget  1.0.0  9.9.9  Actual.Id  2.0.0  winget",
            "v1",
            "winget");
        WingetAvailableUpdateResult localized = WingetUpdateSourceConnector.ParseAvailableTable(
            "Nom  Identifiant  Installée  Disponible  Source\n" +
            "------------------------------------------------\n" +
            "Widget  Contoso.Widget  1.0.0  1.1.0  winget",
            "v1",
            "winget");

        Assert.AreEqual("9.9.9", injected.Records.Single().AvailableVersion);
        Assert.AreEqual("1.1.0", localized.Records.Single().AvailableVersion);
        Assert.IsFalse(typeof(WingetUpdateSourceConnector).GetInterfaces().Contains(typeof(IStructuredUpdateCandidateAuthority)));
    }
    [TestMethod]
    public void MalformedAndAmbiguousDisplayRowsRemainNonActionable()
    {
        WingetInventoryResult malformed = WingetInstalledInventoryProvider.ParseTable(
            "Name                                    Id                              Version         Source\n" +
            "------------------------------------------------------------------------------------------\n" +
            "Contoso Widget                          not an id                       1.0             winget");
        WingetInventoryResult duplicate = WingetInstalledInventoryProvider.ParseTable(InstalledTable(
            ("Contoso Widget", "Contoso.Widget", "1.0", "winget"),
            ("Contoso Widget copy", "Contoso.Widget", "2.0", "winget")));

        Assert.HasCount(0, malformed.Records);
        CollectionAssert.Contains(malformed.Warnings.ToArray(), "winget:malformed-row");
        Assert.HasCount(2, duplicate.Records);
    }
    [TestMethod]
    public void ProcessBoundaryHasNoExecutablePathParameterAndOversizedOutputFailsClosed()
    {
        Type[] parameterTypes = typeof(WindowsWingetProcess).GetMethod(nameof(WindowsWingetProcess.RunAsync))!.GetParameters().Select(parameter => parameter.ParameterType).ToArray();
        Assert.IsFalse(parameterTypes.Contains(typeof(string)));

        WingetProcessResult oversized = new(-1, new string('x', 1_000_000), string.Empty, false, true);
        CollectionAssert.Contains(WingetUpdateSourceConnector.Classify(oversized, "v1", "winget").Warnings.ToArray(), "winget:output-too-large");
    }
    [TestMethod]
    public async Task CurrentUserAliasAndUnsignedExecutableCannotBecomeProviderAuthority()
    {
        string unsigned = Path.Combine(Path.GetTempPath(), $"pcsteward-winget-{Guid.NewGuid():N}.exe");
        await File.WriteAllTextAsync(unsigned, "not-a-signed-executable");
        try
        {
            Assert.IsFalse(MachineTrustedWinget.IsMicrosoftTrusted(unsigned));
        }
        finally { File.Delete(unsigned); }
    }
    private sealed class FakeProcess(WingetProcessResult result) : IWingetProcess
    {
        public IReadOnlyList<string> Arguments { get; private set; } = [];
        public int CallCount { get; private set; }
        public Task<WingetProcessResult> RunAsync(IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken) { Arguments = arguments; CallCount++; return Task.FromResult(result); }
    }
    private sealed class SequencedProcess(IEnumerable<WingetProcessResult> results) : IWingetProcess
    {
        private readonly Queue<WingetProcessResult> results = new(results); public List<IReadOnlyList<string>> Calls { get; } = [];
        public Task<WingetProcessResult> RunAsync(IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken) { Calls.Add(arguments); return Task.FromResult(results.Dequeue()); }
    }
    private sealed class ExportingProcess(string json) : IWingetProcess
    {
        public IReadOnlyList<string> Arguments { get; private set; } = [];
        public async Task<WingetProcessResult> RunAsync(IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken)
        {
            Arguments = arguments;
            int output = Array.IndexOf(arguments.ToArray(), "--output");
            await File.WriteAllTextAsync(arguments[output + 1], json, cancellationToken);
            return new WingetProcessResult(0, string.Empty, string.Empty, false);
        }
    }

    private static string InstalledTable(params (string Name, string Id, string Version, string Source)[] rows) =>
        FixedTable("Name", "Id", "Version", "Source", rows);

    private static string FixedTable(
        string nameHeader,
        string idHeader,
        string versionHeader,
        string sourceHeader,
        params (string Name, string Id, string Version, string Source)[] rows) =>
        $"{nameHeader,-40}{idHeader,-32}{versionHeader,-16}{sourceHeader}\n" +
        new string('-', 96) + "\n" +
        string.Join("\n", rows.Select(row => $"{row.Name,-40}{row.Id,-32}{row.Version,-16}{row.Source}"));
}
