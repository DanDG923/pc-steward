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
    private static readonly string[] ExpectedOfficialPlusIds = ["Microsoft.VCRedist.2015+.x64", "Microsoft.VCRedist.2015+.x86", "Notepad++.Notepad++"];
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
    public async Task SourceConnectorFailsClosedWithoutAnUnscopedGlobalFallback()
    {
        SequencedProcess process = new(new WingetProcessResult[]
        {
            new(0, "v1.9.0", "", false),
            new(1, "", "", false)
        });

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => new WingetUpdateSourceConnector(process, "winget").RefreshAsync(CancellationToken.None));

        Assert.HasCount(2, process.Calls);
        CollectionAssert.AreEqual(ExpectedSourceListArguments, process.Calls[1].ToArray());
    }
    [TestMethod]
    public void MalformedOrWrongSourceUpdateRowsAreExcludedAndUnsupportedSourcesAreRejected()
    {
        WingetAvailableUpdateResult result = WingetUpdateSourceConnector.ParseAvailableTable("Name  Id  Version  Available  Source\n----------------------------------------------\nBad  bad id!  1.0  2.0  winget\nOther  Other.App  1.0  2.0  msstore", "v1", "winget");
        Assert.HasCount(0, result.Records); CollectionAssert.Contains(result.Warnings.ToArray(), "winget:invalid-update-row"); Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new WingetUpdateSourceConnector(new FakeProcess(new WingetProcessResult(0, "", "", false)), "arbitrary"));
    }
    [TestMethod]
    public void TheNoLinkedPackageLineAloneMeansNoUpdates()
    {
        // Real WinGet 1.29.380 `upgrade --source msstore` output (owner's PC, 2026-11-13) when no Store app has an update; exit 0.
        WingetAvailableUpdateResult result = WingetUpdateSourceConnector.Classify(new WingetProcessResult(0, "No installed package found matching input criteria.\r\n", "", false), "v1.29.380", "msstore");

        Assert.HasCount(0, result.Records);
        Assert.HasCount(0, result.Warnings);
    }
    [TestMethod]
    public void TheNoLinkedPackageLineIsNotAcceptedWithOtherOutputOrAFailingExit()
    {
        WingetAvailableUpdateResult extra = WingetUpdateSourceConnector.ParseAvailableTable("Something else\nNo installed package found matching input criteria.", "v1", "msstore");
        WingetAvailableUpdateResult failed = WingetUpdateSourceConnector.Classify(new WingetProcessResult(-1978335212, "No installed package found matching input criteria.", "", false), "v1", "msstore");

        CollectionAssert.Contains(extra.Warnings.ToArray(), "winget:unrecognized-output");
        CollectionAssert.Contains(failed.Warnings.ToArray(), "winget:exit--1978335212");
    }
    [TestMethod]
    public void SourceQualifiedQueryAcceptsRowsWhenWingetOmitsTheSourceColumn()
    {
        WingetAvailableUpdateResult result = WingetUpdateSourceConnector.ParseAvailableTable("Name                        Id              Version Available\n-------------------------------------------------------------\nMozilla Firefox (x64 en-US) Mozilla.Firefox 152.0.6 153.0.1\n1 upgrades available.", "v1", "winget");

        Assert.HasCount(1, result.Records); Assert.AreEqual("Mozilla.Firefox", result.Records.Single().Id); Assert.AreEqual("winget", result.Records.Single().Source);
    }
    // Shape of real WinGet 1.29.380 `upgrade --source winget` output (owner's PC, 2026-11-13; package names replaced): a count
    // line, an explicit-targeting table and an unknown-version notice follow the main table.
    private static readonly string[] ExpectedFooterRecordIds = ["Contoso.Widget", "Contoso.DesktopRuntime.8", "Fabrikam.Chat"];
    private const string UpgradeOutputWithNotices =
        "Name                                             Id                                     Version     Available\n" +
        "---------------------------------------------------------------------------------------------------------------\n" +
        "Contoso Widget                                   Contoso.Widget                         2.103.3     2.104.0\n" +
        "Contoso Desktop Runtime - 8.0.33 (x64)           Contoso.DesktopRuntime.8               8.0.33      8.0.34\n" +
        "2 upgrades available.\n" +
        "The following packages have an upgrade available, but require explicit targeting for upgrade:\n" +
        "Name     Id            Version  Available\n" +
        "-----------------------------------------\n" +
        "Fabrikam Fabrikam.Chat 1.0.9267 1.0.9268\n" +
        "1 package(s) have version numbers that cannot be determined. Use --include-unknown to see all results.\n" +
        "1 package(s) have pins that prevent upgrade. Use the 'winget pin' command to view and edit pins. Using the --include-pinned argument may show more results.";

    [TestMethod]
    public void UpgradeCountExplicitTargetingTableAndNoticesAreReadNotRejected()
    {
        WingetAvailableUpdateResult result = WingetUpdateSourceConnector.ParseAvailableTable(UpgradeOutputWithNotices, "v1.29.380", "winget");

        CollectionAssert.AreEqual(ExpectedFooterRecordIds, result.Records.Select(record => record.Id).ToArray());
        Assert.AreEqual("1.0.9268", result.Records.Single(record => record.Id == "Fabrikam.Chat").AvailableVersion);
        Assert.HasCount(0, result.Warnings);
    }

    [TestMethod]
    public void AnyOtherLineAfterTheUpgradeTableIsStillMalformed()
    {
        foreach (string extra in new[] { "2 upgrades available. Run now", "Something else happened.", "The following packages have an upgrade available:" })
        {
            WingetAvailableUpdateResult result = WingetUpdateSourceConnector.ParseAvailableTable(
                "Name            Id              Version  Available\n--------------------------------------------------\nContoso Widget  Contoso.Widget  1.0.0    1.1.0\n" + extra, "v1", "winget");
            Assert.AreEqual("Contoso.Widget", result.Records.Single().Id, extra);
            CollectionAssert.Contains(result.Warnings.ToArray(), "winget:malformed-update-row", extra);
        }
    }

    [TestMethod]
    public async Task RealUpgradeOutputWithACountLineKeepsTheSourceLive()
    {
        SequencedProcess process = new(new WingetProcessResult[]
        {
            new(0, "v1.29.380", "", false),
            new(0, InstalledTableWithAvailable(
                ("Contoso Widget", "Contoso.Widget", "2.103.3", "2.104.0", "winget"),
                ("Fabrikam Chat", "Fabrikam.Chat", "1.0.9267", "1.0.9268", "winget"),
                ("Northwind Tool", "Northwind.Tool", "3.0.0", "", "winget")), "", false),
            new(0, UpgradeOutputWithNotices, "", false)
        });

        SourceSnapshot snapshot = await new WingetUpdateSourceConnector(process, "winget").RefreshAsync(CancellationToken.None);

        Assert.AreEqual(SourceSnapshotState.Live, snapshot.State);
        Assert.AreEqual("2.104.0", snapshot.Packages.Single(package => package.PackageId == "Contoso.Widget").AvailableVersion);
        Assert.AreEqual("1.0.9268", snapshot.Packages.Single(package => package.PackageId == "Fabrikam.Chat").AvailableVersion);
        Assert.AreEqual("3.0.0", snapshot.Packages.Single(package => package.PackageId == "Northwind.Tool").AvailableVersion);
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
    public async Task ShortMalformedUpdateOutputCannotProduceALiveSnapshot()
    {
        SequencedProcess process = new(new WingetProcessResult[]
        {
            new(0, "v1.9.0", "", false),
            new(0, InstalledTable(("Contoso Widget", "Contoso.Widget", "1.0", "winget")), "", false),
            new(0, "Name  Id  Version  Available  Source\n----------------------------------------------\nBad", "", false)
        });

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => new WingetUpdateSourceConnector(process, "winget").RefreshAsync(CancellationToken.None));
    }
    [TestMethod]
    public async Task DesktopInventoryReportsAStableLocalWarningWhenTheReadOnlyProcessCannotLaunch()
    {
        WingetLocalInventoryProvider desktopProvider = new(() => true, new WingetInstalledInventoryProvider(new FaultingProcess(new IOException("fixture"))));

        ProviderScanResult result = await desktopProvider.ScanAsync(CancellationToken.None);

        Assert.HasCount(0, result.Instances);
        CollectionAssert.Contains(result.Warnings.ToArray(), "winget:executable-unavailable");
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
    public void SourceQualifiedInstalledTableTreatsFourthAvailableColumnAsDisplayOnly()
    {
        const string output =
            "Name                                                           Id                                                 Version              Available\n" +
            "----------------------------------------------------------------------------------------------------------------------------------------------------\n" +
            "Microsoft Visual Studio Code (User)                            Microsoft.VisualStudioCode                         1.129.0              1.130.0";

        WingetInventoryResult result = WingetInstalledInventoryProvider.ParseTable(output);

        WingetInstalledRecord record = result.Records.Single();
        Assert.AreEqual("Microsoft.VisualStudioCode", record.Id);
        Assert.AreEqual("1.129.0", record.Version);
        Assert.IsNull(record.Source);
        Assert.HasCount(0, result.Warnings);
    }
    [TestMethod]
    public void InstalledTableAcceptsBlankAvailableCellWithoutLosingExactSource()
    {
        const string output =
            "Name                                    Id                              Version         Available       Source\n" +
            "--------------------------------------------------------------------------------------------------------\n" +
            "Current Tool                            Contoso.Current                 2.0                             winget";

        WingetInventoryResult result = WingetInstalledInventoryProvider.ParseTable(output);

        WingetInstalledRecord record = result.Records.Single();
        Assert.AreEqual("Contoso.Current", record.Id);
        Assert.AreEqual("winget", record.Source);
        Assert.HasCount(0, result.Warnings);
    }
    [TestMethod]
    public void OfficialPlusPackageIdsParseButComparatorVersionsRemainNonActionable()
    {
        string output = InstalledTableWithAvailable(
            ("Microsoft Visual C++ 2015-2022 Redistributable (x64)", "Microsoft.VCRedist.2015+.x64", "14.44.35211.0", "", "winget"),
            ("Microsoft Visual C++ 2015-2022 Redistributable (x86)", "Microsoft.VCRedist.2015+.x86", "14.44.35211.0", "", "winget"),
            ("Notepad++", "Notepad++.Notepad++", "8.8.2", "8.8.3", "winget"),
            ("Inexact One", "Contoso.InexactOne", "> 1.8.8", "", "winget"),
            ("Inexact Two", "Contoso.InexactTwo", "> 3.13.5", "", "winget"),
            ("Inexact Three", "Contoso.InexactThree", "> 1.8.9", "", "winget"),
            ("Inexact Four", "Contoso.InexactFour", "> 1.8.9", "", "winget"));

        WingetInventoryResult result = WingetInstalledInventoryProvider.ParseTable(output);
        WingetAvailableUpdateResult available = WingetUpdateSourceConnector.ParseAvailableTable(
            "Name                                    Id                              Version         Available       Source\n" +
            "--------------------------------------------------------------------------------------------------------\n" +
            "Notepad++                               Notepad++.Notepad++              8.8.2           8.8.3           winget",
            "v1.29.280",
            "winget");

        CollectionAssert.AreEquivalent(
            ExpectedOfficialPlusIds,
            result.Records.Select(record => record.Id).ToArray());
        Assert.AreEqual("Notepad++.Notepad++", available.Records.Single().Id);
        Assert.IsFalse(result.Records.Any(record => record.Id.StartsWith("Contoso.Inexact", StringComparison.Ordinal)));
        CollectionAssert.Contains(result.Warnings.ToArray(), "winget:inexact-installed-version-skipped");
        CollectionAssert.DoesNotContain(result.Warnings.ToArray(), "winget:malformed-row");
    }
    [TestMethod]
    public async Task InexactInstalledVersionWarningIsNonfatalAndExactSourceCandidateRemainsLive()
    {
        SequencedProcess process = new(new WingetProcessResult[]
        {
            new(0, "v1.29.280", "", false),
            new(0, InstalledTableWithAvailable(
                ("Inexact Tool", "Contoso.Inexact", "> 1.8.8", "", "winget"),
                ("Microsoft Visual Studio Code (User)", "Microsoft.VisualStudioCode", "1.129.0", "1.130.0", "winget")), "", false),
            new(0,
                "Name                                                           Id                                                 Version              Available            Source\n" +
                "---------------------------------------------------------------------------------------------------------------------------------------------------------------------\n" +
                "Microsoft Visual Studio Code (User)                            Microsoft.VisualStudioCode                         1.129.0              1.130.0              winget",
                "", false)
        });

        SourceSnapshot snapshot = await new WingetUpdateSourceConnector(process, "winget").RefreshAsync(CancellationToken.None);

        Assert.AreEqual(SourceSnapshotState.Live, snapshot.State);
        SourcePackageRecord package = snapshot.Packages.Single();
        Assert.AreEqual("Microsoft.VisualStudioCode", package.PackageId);
        Assert.AreEqual("1.130.0", package.AvailableVersion);
        CollectionAssert.Contains(snapshot.Warnings.ToArray(), "winget:inexact-installed-version-skipped");
        CollectionAssert.DoesNotContain(snapshot.Warnings.ToArray(), "winget:malformed-row");
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
    private sealed class FaultingProcess(Exception exception) : IWingetProcess
    {
        public Task<WingetProcessResult> RunAsync(IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken) => Task.FromException<WingetProcessResult>(exception);
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

    private static string InstalledTableWithAvailable(params (string Name, string Id, string Version, string Available, string Source)[] rows) =>
        $"{"Name",-64}{"Id",-51}{"Version",-21}{"Available",-21}Source\n" +
        new string('-', 168) + "\n" +
        string.Join("\n", rows.Select(row => $"{row.Name,-64}{row.Id,-51}{row.Version,-21}{row.Available,-21}{row.Source}"));

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
