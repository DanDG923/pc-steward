using PCSteward.Domain;

namespace PCSteward.Domain.UnitTests;

/// <summary>
/// FR-DIA-02/03 (ADR-027 Q1/Q6, Task 64): deterministic, synthetic fixtures for the policy arithmetic, time normalization,
/// ordering, within-source de-duplication and noncausal "occurred near" labels.
/// </summary>
[TestClass]
public sealed class DiagnosticTimelineTests
{
    private static readonly DateTimeOffset Collected = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Incident = new(2026, 11, 12, 9, 0, 0, TimeSpan.Zero);

    [TestMethod]
    [TestCategory("FR-DIA-03")]
    [DataRow(0, 500, DisplayName = "empty app history leaves 500 per OS source")]
    [DataRow(120, 470, DisplayName = "floor((2000-120)/4)")]
    [DataRow(499, 375, DisplayName = "floor((2000-499)/4)")]
    [DataRow(500, 375, DisplayName = "full app history leaves 375 per OS source")]
    public void OperatingSystemSlotsFollowTheAcceptedFormula(int appRecords, int expected) =>
        Assert.AreEqual(expected, DiagnosticCollectionPolicy.OperatingSystemSlots(appRecords));

    [TestMethod]
    [TestCategory("FR-DIA-03")]
    public void SlotsRejectImpossibleAppCounts()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => DiagnosticCollectionPolicy.OperatingSystemSlots(-1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => DiagnosticCollectionPolicy.OperatingSystemSlots(501));
    }

    [TestMethod]
    [TestCategory("FR-DIA-03")]
    public void ReaderBudgetIsTheSmallerOfFifteenSecondsAndAnEqualShare()
    {
        Assert.AreEqual(TimeSpan.FromSeconds(13.75), DiagnosticCollectionPolicy.ReaderBudget(TimeSpan.FromSeconds(55), 4));
        Assert.AreEqual(TimeSpan.FromSeconds(10), DiagnosticCollectionPolicy.ReaderBudget(TimeSpan.FromSeconds(40), 4));
        Assert.AreEqual(TimeSpan.FromSeconds(15), DiagnosticCollectionPolicy.ReaderBudget(TimeSpan.FromSeconds(100), 4));
        Assert.AreEqual(TimeSpan.Zero, DiagnosticCollectionPolicy.ReaderBudget(TimeSpan.FromSeconds(-1), 2));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => DiagnosticCollectionPolicy.ReaderBudget(TimeSpan.FromSeconds(10), 0));
    }

    [TestMethod]
    [TestCategory("FR-DIA-03")]
    public void LookbackIsSevenDaysOrNarrowerAndRetentionThirtyDays()
    {
        Assert.AreEqual(Collected.AddDays(-7), DiagnosticCollectionPolicy.LookbackStart(Collected));
        Assert.AreEqual(Collected.AddDays(-2), DiagnosticCollectionPolicy.LookbackStart(Collected, TimeSpan.FromDays(2)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => DiagnosticCollectionPolicy.LookbackStart(Collected, TimeSpan.FromDays(8)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => DiagnosticCollectionPolicy.LookbackStart(Collected, TimeSpan.Zero));
        Assert.AreEqual(Collected.AddDays(30), DiagnosticCollectionPolicy.ExpiresAt(Collected));
    }

    [TestMethod]
    [TestCategory("FR-DIA-02")]
    public void DaylightSavingAmbiguousAndSkippedLocalTimesAreNotGuessed()
    {
        TimeZoneInfo zone = UkLikeZone();
        DiagnosticOccurrence repeated = DiagnosticTimeNormalizer.FromLocalWallClock(new DateTime(2026, 10, 25, 1, 30, 0), zone, Collected);
        DiagnosticOccurrence skipped = DiagnosticTimeNormalizer.FromLocalWallClock(new DateTime(2026, 3, 29, 1, 30, 0), zone, Collected);
        DiagnosticOccurrence summer = DiagnosticTimeNormalizer.FromLocalWallClock(new DateTime(2026, 7, 1, 10, 0, 0), zone, Collected);

        Assert.AreEqual(DiagnosticTimeQuality.Ambiguous, repeated.Quality);
        Assert.IsNull(repeated.Utc);
        Assert.AreEqual(DiagnosticTimeQuality.Ambiguous, skipped.Quality);
        Assert.IsNull(skipped.Utc);
        Assert.AreEqual(DiagnosticTimeQuality.Known, summer.Quality);
        Assert.AreEqual(new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero), summer.Utc);
        Assert.AreEqual(TimeSpan.FromHours(1), summer.OriginalOffset, "the known original offset is preserved");
    }

    [TestMethod]
    [TestCategory("FR-DIA-02")]
    public void FutureConflictingAndMissingTimesAreFlaggedNotClamped()
    {
        DiagnosticOccurrence future = DiagnosticTimeNormalizer.FromInstant(Collected.AddMinutes(5), Collected);
        DiagnosticOccurrence conflicting = DiagnosticTimeNormalizer.FromInstant(Incident, Collected, sourceReportsConflict: true);
        DiagnosticOccurrence missing = DiagnosticTimeNormalizer.FromInstant(null, Collected);
        DiagnosticOccurrence offset = DiagnosticTimeNormalizer.FromInstant(new DateTimeOffset(2026, 11, 12, 10, 0, 0, TimeSpan.FromHours(1)), Collected);

        Assert.AreEqual(DiagnosticTimeQuality.Future, future.Quality);
        Assert.AreEqual(Collected.AddMinutes(5), future.Utc, "a future time is kept as reported, never clamped");
        Assert.AreEqual(DiagnosticTimeQuality.Conflicting, conflicting.Quality);
        Assert.AreEqual(DiagnosticTimeQuality.Unknown, missing.Quality);
        Assert.AreEqual(TimeSpan.Zero, offset.Utc!.Value.Offset);
        Assert.AreEqual(Incident, offset.Utc);
        Assert.AreEqual(TimeSpan.FromHours(1), offset.OriginalOffset);
    }

    [TestMethod]
    [TestCategory("FR-DIA-02")]
    public void ProximityWindowIsInclusiveAtBothFifteenMinuteBoundaries()
    {
        DiagnosticRecord crash = Os("app-crash", Incident, DiagnosticSourceKind.ApplicationEventLog, DiagnosticEventKind.ApplicationCrash);
        DiagnosticRecord before = App("before", Incident.AddMinutes(-15));
        DiagnosticRecord after = App("after", Incident.AddMinutes(15));
        DiagnosticRecord tooEarly = App("too-early", Incident.AddMinutes(-15).AddTicks(-1));
        DiagnosticRecord tooLate = App("too-late", Incident.AddMinutes(15).AddTicks(1));
        DiagnosticTimeline timeline = DiagnosticTimelineBuilder.Build(Snapshot(crash, before, after, tooEarly, tooLate));

        IReadOnlyList<DiagnosticNearbyChange> nearby = DiagnosticTimelineBuilder.NearbyChanges(timeline, crash.Key);

        string[] expected = ["before", "after"];
        CollectionAssert.AreEqual(expected, nearby.Select(change => change.Change.SourceRecordId).ToArray());
        Assert.AreEqual(TimeSpan.FromMinutes(-15), nearby[0].OffsetFromIncident);
        Assert.AreEqual(TimeSpan.FromMinutes(15), nearby[1].OffsetFromIncident);
        Assert.AreEqual("Occurred near", DiagnosticNearbyChange.Label);
    }

    [TestMethod]
    [TestCategory("FR-DIA-02")]
    public void UnknownInProgressAndOtherSourceRecordsAreNeverLabelled()
    {
        DiagnosticRecord shutdown = Os("41-1", Incident, DiagnosticSourceKind.SystemEventLog, DiagnosticEventKind.UnexpectedShutdown);
        DiagnosticRecord inProgress = App("running", Incident.AddMinutes(1)) with { Outcome = DiagnosticOutcomeKind.InProgress };
        DiagnosticRecord untimed = App("untimed", null);
        DiagnosticRecord future = App("future", Incident.AddMinutes(2)) with { Occurrence = new(Incident.AddMinutes(2), TimeSpan.Zero, DiagnosticTimeQuality.Future) };
        DiagnosticRecord windowsUpdate = Os("wu-1", Incident.AddMinutes(3), DiagnosticSourceKind.WindowsUpdateHistory, DiagnosticEventKind.WindowsUpdate);
        DiagnosticRecord unrelated = App("unrelated-but-near", Incident.AddMinutes(4));
        DiagnosticTimeline timeline = DiagnosticTimelineBuilder.Build(Snapshot(shutdown, inProgress, untimed, future, windowsUpdate, unrelated));

        IReadOnlyList<DiagnosticNearbyChange> nearby = DiagnosticTimelineBuilder.NearbyChanges(timeline, shutdown.Key);

        string[] expected = ["unrelated-but-near"];
        CollectionAssert.AreEqual(expected, nearby.Select(change => change.Change.SourceRecordId).ToArray(),
            "an unrelated nearby change is still only 'occurred near'; uncertain times and other sources get no label");
        Assert.AreEqual(1, timeline.Untimed.Count);
    }

    [TestMethod]
    [TestCategory("FR-DIA-02")]
    public void AnIncidentWithoutAKnownTimeProducesNoLabels()
    {
        DiagnosticRecord ambiguous = Os("amb", null, DiagnosticSourceKind.SystemEventLog, DiagnosticEventKind.UnexpectedShutdown) with { Occurrence = new(null, null, DiagnosticTimeQuality.Ambiguous) };
        DiagnosticRecord conflicting = Os("conf", Incident, DiagnosticSourceKind.SystemEventLog, DiagnosticEventKind.BugCheck) with { Occurrence = new(Incident, TimeSpan.Zero, DiagnosticTimeQuality.Conflicting) };
        DiagnosticTimeline timeline = DiagnosticTimelineBuilder.Build(Snapshot(ambiguous, conflicting, App("near", Incident)));

        Assert.IsEmpty(DiagnosticTimelineBuilder.NearbyChanges(timeline, ambiguous.Key));
        Assert.IsEmpty(DiagnosticTimelineBuilder.NearbyChanges(timeline, conflicting.Key));
        Assert.IsEmpty(DiagnosticTimelineBuilder.NearbyChanges(timeline, new DiagnosticRecordKey(DiagnosticSourceKind.SystemEventLog, "missing")));
    }

    [TestMethod]
    [TestCategory("FR-DIA-02")]
    public void KernelPowerIsDescribedAsAnUnexpectedShutdownOnlyAndNeverACause()
    {
        StringAssert.Contains(DiagnosticTimelineBuilder.UnexpectedShutdownNote, "did not shut down cleanly");
        StringAssert.Contains(DiagnosticTimelineBuilder.UnexpectedShutdownNote, "can be after the shutdown itself");
        StringAssert.Contains(DiagnosticTimelineBuilder.UnexpectedShutdownNote, "does not say what caused it");
        StringAssert.Contains(DiagnosticTimelineBuilder.ProximityNote, "does not mean one caused the other");
        string[] nearbyProperties = [.. typeof(DiagnosticNearbyChange).GetProperties().Select(property => property.Name)];
        string[] allowed = ["Change", "OffsetFromIncident"];
        CollectionAssert.AreEquivalent(allowed, nearbyProperties, "no cause, score or ranking field exists");
    }

    [TestMethod]
    [TestCategory("FR-DIA-02")]
    public void OrderingIsStableByTimeThenSourceThenIdentityRegardlessOfInputOrder()
    {
        DiagnosticRecord[] records =
        [
            Os("b", Incident, DiagnosticSourceKind.ApplicationEventLog, DiagnosticEventKind.ApplicationHang),
            Os("a", Incident, DiagnosticSourceKind.ApplicationEventLog, DiagnosticEventKind.ApplicationCrash),
            Os("z", Incident, DiagnosticSourceKind.SystemEventLog, DiagnosticEventKind.BugCheck),
            App("app", Incident),
            Os("early", Incident.AddSeconds(-1), DiagnosticSourceKind.WindowsUpdateHistory, DiagnosticEventKind.WindowsUpdate),
            App("untimed-2", null),
            App("untimed-1", null)
        ];
        string[] expected = ["early", "app", "z", "a", "b"];

        DiagnosticTimeline forward = DiagnosticTimelineBuilder.Build(Snapshot(records));
        DiagnosticTimeline reversed = DiagnosticTimelineBuilder.Build(Snapshot([.. records.Reverse()]));

        CollectionAssert.AreEqual(expected, forward.Timed.Select(record => record.SourceRecordId).ToArray());
        CollectionAssert.AreEqual(expected, reversed.Timed.Select(record => record.SourceRecordId).ToArray());
        string[] untimed = ["untimed-1", "untimed-2"];
        CollectionAssert.AreEqual(untimed, reversed.Untimed.Select(record => record.SourceRecordId).ToArray());
    }

    [TestMethod]
    [TestCategory("FR-DIA-02")]
    public void DuplicatesAreRemovedOnlyWithinASource()
    {
        DiagnosticRecord first = Os("rec-7", Incident, DiagnosticSourceKind.SystemEventLog, DiagnosticEventKind.UnexpectedShutdown);
        DiagnosticRecord repeated = first with { Summary = "seen twice" };
        DiagnosticRecord sameIdOtherSource = Os("rec-7", Incident, DiagnosticSourceKind.Reliability, DiagnosticEventKind.UnexpectedShutdown);
        DiagnosticRecord weakA = Os("weak-1", Incident, DiagnosticSourceKind.Reliability, DiagnosticEventKind.ApplicationCrash) with { WeakIdentity = true, Subject = "Contoso" };
        DiagnosticRecord weakSame = weakA with { SourceRecordId = "weak-2" };
        DiagnosticRecord weakDifferent = weakA with { SourceRecordId = "weak-3", Subject = "Fabrikam" };

        DiagnosticTimeline timeline = DiagnosticTimelineBuilder.Build(Snapshot(first, repeated, sameIdOtherSource, weakA, weakSame, weakDifferent));

        Assert.AreEqual(2, timeline.DuplicatesRemoved);
        Assert.HasCount(4, timeline.Timed);
        Assert.AreEqual(2, timeline.Timed.Count(record => record.SourceRecordId == "rec-7"), "no cross-source merge on equal identity or time");
    }

    [TestMethod]
    [TestCategory("FR-DIA-01")]
    public void SnapshotWithNoReadableSourceHasNoCoverageRatherThanACleanResult()
    {
        DiagnosticSnapshot denied = Snapshot() with
        {
            Sources = [.. Enum.GetValues<DiagnosticSourceKind>().Select(source => new DiagnosticSourceResult(source, DiagnosticSourceOutcome.Denied, DiagnosticSourceReason.AccessDenied, Collected.AddDays(-7), Collected, 500, 0, 0, 0))]
        };
        DiagnosticSnapshot empty = denied with { Sources = [.. denied.Sources.Select(source => source with { Outcome = DiagnosticSourceOutcome.Empty, Reason = DiagnosticSourceReason.None })] };

        Assert.IsFalse(denied.HasCoverage);
        Assert.IsTrue(denied.IsPartial);
        Assert.IsTrue(empty.HasCoverage);
        Assert.IsFalse(empty.IsPartial);
        Assert.IsFalse(empty.IsExpired(Collected.AddDays(30).AddTicks(-1)));
        Assert.IsTrue(empty.IsExpired(Collected.AddDays(30)));
    }

    internal static DiagnosticRecord App(string id, DateTimeOffset? at) =>
        new(DiagnosticSourceKind.PcStewardHistory, id, false, Occurrence(at), DiagnosticEventKind.PcStewardUpdate, DiagnosticOutcomeKind.Succeeded, null, null, "Contoso Widget", "1.0 to 1.1", null, false);

    internal static DiagnosticRecord Os(string id, DateTimeOffset? at, DiagnosticSourceKind source, DiagnosticEventKind kind) =>
        new(source, id, false, Occurrence(at), kind, DiagnosticOutcomeKind.NotApplicable, 41, null, null, null, null, false);

    private static DiagnosticOccurrence Occurrence(DateTimeOffset? at) => at is null ? DiagnosticOccurrence.Unknown : new(at.Value.ToUniversalTime(), TimeSpan.Zero, DiagnosticTimeQuality.Known);

    private static DiagnosticSnapshot Snapshot(params DiagnosticRecord[] records) =>
        new(Guid.Empty, DiagnosticCollectionPolicy.Version, Collected, Collected.AddDays(30), Collected.AddDays(-7), [], records, 0);

    private static TimeZoneInfo UkLikeZone()
    {
        TimeZoneInfo.TransitionTime start = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 1, 0, 0), 3, 5, DayOfWeek.Sunday);
        TimeZoneInfo.TransitionTime end = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), 10, 5, DayOfWeek.Sunday);
        TimeZoneInfo.AdjustmentRule rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(DateTime.MinValue.Date, DateTime.MaxValue.Date, TimeSpan.FromHours(1), start, end);
        return TimeZoneInfo.CreateCustomTimeZone("Synthetic UK-like", TimeSpan.Zero, "Synthetic UK-like", "Standard", "Summer", [rule]);
    }
}
