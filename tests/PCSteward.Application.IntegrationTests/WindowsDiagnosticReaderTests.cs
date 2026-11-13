using System.Xml.Linq;
using PCSteward.Application;
using PCSteward.Diagnostics.Windows;
using PCSteward.Domain;

namespace PCSteward.Application.IntegrationTests;

/// <summary>
/// FR-DIA-01 (ADR-027 Q1, Task 64 stage 3): the closed System/Application event log, Reliability and Windows Update history
/// allowlists, their source-side queries, projections, paging bounds and status mapping, with synthetic source answers. One
/// smoke test runs the event log queries against the CI machine's own logs only to prove Windows accepts them.
/// </summary>
[TestClass]
public sealed class WindowsDiagnosticReaderTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset From = Now.AddDays(-7);

    [TestMethod]
    [TestCategory("FR-DIA-01")]
    public void EventLogQueriesNameProviderEventIdAndTimeTogether()
    {
        foreach ((DiagnosticSourceKind source, string channel) in new[] { (DiagnosticSourceKind.SystemEventLog, "System"), (DiagnosticSourceKind.ApplicationEventLog, "Application") })
        {
            XElement query = XElement.Parse(EventLogDiagnosticCatalog.BuildQuery(source, From, Now)).Elements("Query").Single();
            Assert.AreEqual(channel, (string?)query.Attribute("Path"));
            XElement[] selects = [.. query.Elements("Select")];
            Assert.HasCount(3, selects);
            Assert.IsFalse(query.Elements("Suppress").Any());
            foreach ((XElement select, EventLogMapping mapping) in selects.Zip(EventLogDiagnosticCatalog.For(source)))
            {
                Assert.AreEqual(channel, (string?)select.Attribute("Path"));
                Assert.AreEqual(
                    $"*[System[Provider[@Name='{mapping.Provider}'] and (EventID={mapping.EventId}) and TimeCreated[@SystemTime>='2026-11-06T12:00:00.000Z' and @SystemTime<='2026-11-13T12:00:00.000Z']]]",
                    select.Value);
            }
        }
    }

    [TestMethod]
    [TestCategory("FR-DIA-01")]
    public void EventLogProjectionKeepsOnlyAllowlistedEventsAndTheKernelPowerBugcheckCode()
    {
        DateTime at = new(2026, 11, 12, 8, 30, 0, DateTimeKind.Utc);
        DiagnosticRecordCandidate power = EventLogDiagnosticCatalog.Project(DiagnosticSourceKind.SystemEventLog, new("System", "Microsoft-Windows-Kernel-Power", 41, 123, at, 0x9F), Now)!;
        Assert.AreEqual("System:123", power.SourceRecordId);
        Assert.AreEqual(DiagnosticEventKind.UnexpectedShutdown, power.Kind);
        Assert.AreEqual(0x9FL, power.ResultCode);
        Assert.AreEqual(41, power.EventId);
        Assert.AreEqual(new DateTimeOffset(at), power.Occurrence.Utc);
        Assert.IsNull(power.Subject);
        Assert.IsNull(power.Summary);

        DiagnosticRecordCandidate shutdown = EventLogDiagnosticCatalog.Project(DiagnosticSourceKind.SystemEventLog, new("System", "EventLog", 6008, 7, at, 55), Now)!;
        Assert.IsNull(shutdown.ResultCode, "only Kernel-Power 41's bugcheck code is a result code");
        Assert.IsNull(EventLogDiagnosticCatalog.Project(DiagnosticSourceKind.SystemEventLog, new("System", "Some-Other-Provider", 41, 1, at, null), Now), "event ID alone never matches");
        Assert.IsNull(EventLogDiagnosticCatalog.Project(DiagnosticSourceKind.SystemEventLog, new("Application", "EventLog", 6008, 1, at, null), Now));
        Assert.IsNull(EventLogDiagnosticCatalog.Project(DiagnosticSourceKind.ApplicationEventLog, new("System", "Microsoft-Windows-Kernel-Power", 41, 1, at, null), Now));

        DiagnosticRecordCandidate crash = EventLogDiagnosticCatalog.Project(DiagnosticSourceKind.ApplicationEventLog, new("Application", "Application Error", 1000, null, null, null), Now)!;
        Assert.AreEqual(DiagnosticEventKind.ApplicationCrash, crash.Kind);
        Assert.AreEqual(DiagnosticTimeQuality.Unknown, crash.Occurrence.Quality);
        Assert.IsTrue(DiagnosticRecordAdmission.Admit(crash, DiagnosticRedactionContext.None).Admitted, "a missing record id gets a weak identity");
    }

    [TestMethod]
    [TestCategory("FR-DIA-01")]
    public async Task EventLogReaderPassesStatusLimitsAndCountsUnexpectedEventsAsRejected()
    {
        FakeEventLog log = new() { Answer = new(DiagnosticReaderStatus.Completed, [new("System", "EventLog", 6008, 1, Now.UtcDateTime.AddHours(-1), null), new("System", "Unexpected", 1, 2, Now.UtcDateTime.AddHours(-2), null)], true) };
        EventLogDiagnosticReader reader = new(DiagnosticSourceKind.SystemEventLog, log);
        DiagnosticReaderResult result = await reader.ReadAsync(new(DiagnosticSourceKind.SystemEventLog, From, Now, 2), CancellationToken.None);

        Assert.AreEqual("System", log.Channel);
        Assert.AreEqual(2, log.Maximum);
        Assert.IsTrue(result.MoreMatchingRecordsExist);
        Assert.HasCount(2, result.Records);
        Assert.IsTrue(DiagnosticRecordAdmission.Admit(result.Records[0], DiagnosticRedactionContext.None).Admitted);
        Assert.IsFalse(DiagnosticRecordAdmission.Admit(result.Records[1], DiagnosticRedactionContext.None).Admitted);

        log.Answer = new(DiagnosticReaderStatus.Denied, [], false);
        Assert.AreEqual(DiagnosticReaderStatus.Denied, (await reader.ReadAsync(new(DiagnosticSourceKind.SystemEventLog, From, Now, 2), CancellationToken.None)).Status);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new EventLogDiagnosticReader(DiagnosticSourceKind.Reliability, log));
    }

    [TestMethod]
    [TestCategory("FR-DIA-01")]
    public void ReliabilityQueryIsOneNarrowTimeFilteredAllowlist()
    {
        string wql = ReliabilityDiagnosticCatalog.BuildQuery(From, Now);
        StringAssert.StartsWith(wql, "SELECT LogFile, RecordNumber, SourceName, EventIdentifier, TimeGenerated, ProductName FROM Win32_ReliabilityRecords WHERE TimeGenerated >= '20261106120000.000000+000' AND TimeGenerated <= '20261113120000.000000+000' AND (");
        Assert.AreEqual(ReliabilityDiagnosticCatalog.Records.Count, wql.Split(" OR ").Length);
        foreach (string forbidden in new[] { "*", "Message", "User", "ComputerName", "InsertionStrings", "Win32_Product" })
            Assert.IsFalse(wql.Contains(forbidden, StringComparison.Ordinal), forbidden);
    }

    [TestMethod]
    [TestCategory("FR-DIA-01")]
    [DataRow("20261113101500.000000+060", "2026-11-13T09:15:00+00:00")]
    [DataRow("20261113101500.250000-300", "2026-11-13T15:15:00.25+00:00")]
    [DataRow("20261113101500.000000+000", "2026-11-13T10:15:00+00:00")]
    [DataRow("20261113101500.000000", null)]
    [DataRow("2026111310****.******+000", null)]
    [DataRow("20261313101500.000000+000", null)]
    [DataRow("20261113101500.000000+900", null)]
    [DataRow(null, null)]
    public void ReliabilityTimesAreParsedFromCimDatetimeOnly(string? value, string? expected)
    {
        DateTimeOffset? parsed = ReliabilityDiagnosticCatalog.FromDmtf(value);
        Assert.AreEqual(expected is null ? null : DateTimeOffset.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), parsed?.ToUniversalTime());
    }

    [TestMethod]
    [TestCategory("FR-DIA-01")]
    public async Task ReliabilityRecordsAreProjectedNewestFirstWithTheProductNameOnly()
    {
        FakeReliability wmi = new()
        {
            Answer = new(DiagnosticReaderStatus.Completed,
            [
                new("Application", 10, "Application Error", 1000, "20261110080000.000000+000", "Contoso Widget"),
                new("Application", 11, "MsiInstaller", 11707, "20261112080000.000000+000", "Contoso Suite"),
                new("Bad Log", 12, "MsiInstaller", 11724, "20261111080000.000000+000", "Fabrikam Tool"),
                new("Application", 13, "Unknown Source", 1, "20261112090000.000000+000", "Anything")
            ], false)
        };
        DiagnosticReaderResult result = await new ReliabilityDiagnosticReader(wmi).ReadAsync(new(DiagnosticSourceKind.Reliability, From, Now, 3), CancellationToken.None);

        Assert.AreEqual(ReliabilityDiagnosticCatalog.BuildQuery(From, Now), wmi.Wql);
        Assert.IsTrue(result.MoreMatchingRecordsExist, "four returned for three slots");
        Assert.HasCount(3, result.Records);
        Assert.AreEqual("Application:11", result.Records[0].SourceRecordId);
        Assert.AreEqual(DiagnosticEventKind.SoftwareInstalled, result.Records[0].Kind);
        Assert.AreEqual(DiagnosticOutcomeKind.Succeeded, result.Records[0].Outcome);
        Assert.AreEqual("Contoso Suite", result.Records[0].Subject);
        Assert.IsNull(result.Records[1].SourceRecordId, "an unexpected log name gives no source identity");
        Assert.AreEqual(DiagnosticEventKind.SoftwareRemoved, result.Records[1].Kind);
        Assert.AreEqual(DiagnosticEventKind.ApplicationCrash, result.Records[2].Kind);
        Assert.IsFalse(result.Records.Any(record => record.Subject == "Anything"));
    }

    [TestMethod]
    [TestCategory("FR-DIA-01")]
    public void WindowsUpdateHistoryPagingStopsAtTheWindowTheLimitOrTheCeiling()
    {
        DateTime newest = Now.UtcDateTime.AddMinutes(-1);
        WindowsUpdateRawEntry[] history = [.. Enumerable.Range(0, 300).Select(index => Entry(newest.AddHours(-index)))];
        List<(int Start, int Count)> pages = [];
        IReadOnlyList<WindowsUpdateRawEntry> Page(int start, int count)
        {
            pages.Add((start, count));
            return [.. history.Skip(start).Take(count)];
        }

        WindowsUpdateHistoryAnswer inWindow = WindowsUpdateHistoryCatalog.Page(history.Length, Page, From, Now, 500, CancellationToken.None);
        Assert.HasCount(168, inWindow.Entries, "hourly entries in seven days, inclusive of the start");
        Assert.IsFalse(inWindow.MoreMatched);
        Assert.HasCount(4, pages, "paging stopped at the first older entry");

        pages.Clear();
        WindowsUpdateHistoryAnswer limited = WindowsUpdateHistoryCatalog.Page(history.Length, Page, From, Now, 10, CancellationToken.None);
        Assert.HasCount(10, limited.Entries);
        Assert.IsTrue(limited.MoreMatched);
        Assert.HasCount(1, pages);

        WindowsUpdateRawEntry[] future = [Entry(Now.UtcDateTime.AddHours(1)), Entry(newest)];
        WindowsUpdateHistoryAnswer skipped = WindowsUpdateHistoryCatalog.Page(future.Length, (start, count) => [.. future.Skip(start).Take(count)], From, Now, 10, CancellationToken.None);
        Assert.HasCount(1, skipped.Entries, "entries after the collection start are outside the window");

        WindowsUpdateRawEntry[] endless = [.. Enumerable.Range(0, 3000).Select(_ => Entry(newest))];
        int inspected = 0;
        WindowsUpdateHistoryAnswer ceiling = WindowsUpdateHistoryCatalog.Page(endless.Length, (start, count) => { inspected += count; return [.. endless.Skip(start).Take(count)]; }, From, Now, 5000, CancellationToken.None);
        Assert.AreEqual(WindowsUpdateHistoryCatalog.InspectionCeiling, inspected);
        Assert.IsTrue(ceiling.MoreMatched, "the ceiling is reported as truncation");

        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() => WindowsUpdateHistoryCatalog.Page(history.Length, Page, From, Now, 10, cancelled.Token));
    }

    [TestMethod]
    [TestCategory("FR-DIA-01")]
    public void WindowsUpdateEntriesProjectIdentityOutcomeTitleAndCode()
    {
        DateTime at = new(2026, 11, 12, 3, 4, 5, DateTimeKind.Utc);
        WindowsUpdateRawEntry entry = new(at, 1, 4, unchecked((int)0x80240022), "2026-11 Cumulative Update (KB0000000)", "8F2C1A3B-0D4E-4F5A-9B6C-7D8E9F0A1B2C", 201);
        DiagnosticRecordCandidate candidate = WindowsUpdateHistoryCatalog.Project(entry, Now);

        Assert.AreEqual("8f2c1a3b-0d4e-4f5a-9b6c-7d8e9f0a1b2c:201:20261112030405:1", candidate.SourceRecordId);
        Assert.AreEqual(DiagnosticOutcomeKind.Failed, candidate.Outcome);
        Assert.AreEqual((long)unchecked((int)0x80240022), candidate.ResultCode);
        Assert.AreEqual("Install", candidate.Detail);
        Assert.AreEqual("2026-11 Cumulative Update (KB0000000)", candidate.Subject);
        Assert.IsTrue(DiagnosticRecordAdmission.Admit(candidate, DiagnosticRedactionContext.None).Admitted);

        DiagnosticRecordCandidate partial = WindowsUpdateHistoryCatalog.Project(entry with { ResultCode = 3, HResult = 0, Operation = 2, UpdateId = "not-a-guid" }, Now);
        Assert.AreEqual("Uninstall, completed with errors", partial.Detail);
        Assert.IsNull(partial.ResultCode);
        Assert.IsNull(partial.SourceRecordId);
        Assert.AreEqual(DiagnosticOutcomeKind.Succeeded, WindowsUpdateHistoryCatalog.Outcome(2));
        Assert.AreEqual(DiagnosticOutcomeKind.Cancelled, WindowsUpdateHistoryCatalog.Outcome(5));
        Assert.AreEqual(DiagnosticOutcomeKind.Unknown, WindowsUpdateHistoryCatalog.Outcome(0));
    }

    [TestMethod]
    [TestCategory("FR-DIA-01")]
    public async Task EveryWindowsSourceFeedsTheOrchestratorWithoutRejections()
    {
        DateTime at = Now.UtcDateTime.AddHours(-3);
        FakeEventLog log = new() { Answer = new(DiagnosticReaderStatus.Completed, [new("System", "Microsoft-Windows-Kernel-Power", 41, 9, at, 0)], false) };
        FakeEventLog application = new() { Answer = new(DiagnosticReaderStatus.Completed, [new("Application", "Application Hang", 1002, 4, at, null)], false) };
        FakeReliability wmi = new() { Answer = new(DiagnosticReaderStatus.Completed, [new("Application", 1, "Application Error", 1000, ReliabilityDiagnosticCatalog.ToDmtf(at), @"Contoso at C:\Users\someone\a.exe")], false) };
        FakeWindowsUpdate update = new() { Answer = new(DiagnosticReaderStatus.Completed, [Entry(at)], false) };
        IDiagnosticSourceReader[] readers =
        [
            new EventLogDiagnosticReader(DiagnosticSourceKind.SystemEventLog, log),
            new EventLogDiagnosticReader(DiagnosticSourceKind.ApplicationEventLog, application),
            new ReliabilityDiagnosticReader(wmi),
            new WindowsUpdateHistoryDiagnosticReader(update)
        ];

        DiagnosticCollectionConsent consent = new([DiagnosticSourceKind.SystemEventLog, DiagnosticSourceKind.ApplicationEventLog, DiagnosticSourceKind.Reliability, DiagnosticSourceKind.WindowsUpdateHistory], TimeSpan.FromDays(7), DiagnosticCollectionPolicy.Version);
        DiagnosticSnapshot snapshot = (await new DiagnosticCollectionOrchestrator(readers, DiagnosticRedactionContext.None, new FixedClock(Now)).CollectAsync(consent, CancellationToken.None)).Snapshot!;

        Assert.HasCount(4, snapshot.Records);
        Assert.IsTrue(snapshot.Sources.Skip(1).All(source => source.Outcome == DiagnosticSourceOutcome.Available && source.RecordsRejected == 0));
        Assert.AreEqual("Contoso at [path]", snapshot.Records.Single(record => record.Source == DiagnosticSourceKind.Reliability).Subject);
    }

    [TestMethod]
    [TestCategory("FR-DIA-01")]
    public void WindowsAcceptsBothEventLogQueries()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Windows only.");
        WindowsEventLogQueryRunner runner = new();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        foreach (DiagnosticSourceKind source in new[] { DiagnosticSourceKind.SystemEventLog, DiagnosticSourceKind.ApplicationEventLog })
        {
            string channel = EventLogDiagnosticCatalog.For(source)[0].Channel;
            EventLogQueryAnswer answer = runner.Run(channel, EventLogDiagnosticCatalog.BuildQuery(source, now.AddDays(-7), now), 5, CancellationToken.None);
            Assert.AreEqual(DiagnosticReaderStatus.Completed, answer.Status, channel);
            Assert.IsLessThanOrEqualTo(5, answer.Events.Count);
            Assert.IsTrue(answer.Events.All(item => EventLogDiagnosticCatalog.Project(source, item, now) is not null), "every returned event is allowlisted");
        }
    }

    private static WindowsUpdateRawEntry Entry(DateTime at) => new(at, 1, 2, 0, "Security Intelligence Update", Guid.NewGuid().ToString(), 1);

    private sealed class FakeEventLog : IEventLogQueryRunner
    {
        public EventLogQueryAnswer Answer { get; set; } = new(DiagnosticReaderStatus.Completed, [], false);
        public string? Channel { get; private set; }
        public int Maximum { get; private set; }

        public EventLogQueryAnswer Run(string channel, string structuredQuery, int maximumEvents, CancellationToken cancellationToken)
        {
            Channel = channel;
            Maximum = maximumEvents;
            return Answer;
        }
    }

    private sealed class FakeReliability : IReliabilityRecordSource
    {
        public ReliabilityQueryAnswer Answer { get; set; } = new(DiagnosticReaderStatus.Completed, [], false);
        public string? Wql { get; private set; }

        public ReliabilityQueryAnswer Query(string wql, int maximumRecords, CancellationToken cancellationToken)
        {
            Wql = wql;
            return Answer;
        }
    }

    private sealed class FakeWindowsUpdate : IWindowsUpdateHistorySource
    {
        public WindowsUpdateHistoryAnswer Answer { get; set; } = new(DiagnosticReaderStatus.Completed, [], false);

        public WindowsUpdateHistoryAnswer Read(DateTimeOffset fromUtc, DateTimeOffset toUtc, int maximumEntries, CancellationToken cancellationToken) => Answer;
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
