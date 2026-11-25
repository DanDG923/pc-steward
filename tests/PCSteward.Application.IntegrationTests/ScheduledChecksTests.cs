using System.Diagnostics;
using Microsoft.Data.Sqlite;
using PCSteward.Persistence.Sqlite;
using PCSteward.Application;
using PCSteward.Scheduling.Windows;

namespace PCSteward.Application.IntegrationTests;

[TestClass]
public sealed class ScheduledChecksTests
{
    private static readonly string[] DeleteOnly = ["/Delete"];
    [TestMethod]
    public async Task RegistrationServiceCreatesOnlyEnabledDailyOrWeeklyTasksAndRemovesManualMode()
    {
        RecordingRegistrar registrar = new();
        ScheduledCheckRegistrationService service = new(registrar);

        await service.ApplyAsync(new ScheduledCheckSettings(ScheduleCadence.Weekly, true, true, DayOfWeek.Friday, new TimeOnly(18, 30)), CancellationToken.None);
        Assert.AreEqual(1, registrar.Registered.Count);
        Assert.AreEqual(ScheduleCadence.Weekly, registrar.Registered[0].Cadence);
        Assert.AreEqual(DayOfWeek.Friday, registrar.Registered[0].WeeklyDay);

        await service.ApplyAsync(ScheduledCheckSettings.Manual with { NotificationsEnabled = true }, CancellationToken.None);
        Assert.AreEqual(1, registrar.EnsuredAbsent);
    }

    [TestMethod]
    public void NotificationPolicyAllowsAttentionOrRepeatedFailureOnly()
    {
        ScheduledCheckSettings enabled = ScheduledCheckSettings.Manual with { NotificationsEnabled = true };
        Assert.IsTrue(ScheduledCheckNotificationPolicy.ShouldNotify(enabled, true, true, 0));
        Assert.IsFalse(ScheduledCheckNotificationPolicy.ShouldNotify(enabled, false, false, 1));
        Assert.IsTrue(ScheduledCheckNotificationPolicy.ShouldNotify(enabled, false, false, 2));
        Assert.IsFalse(ScheduledCheckNotificationPolicy.ShouldNotify(enabled with { NotificationsEnabled = false }, true, false, 4));
    }

    [TestMethod]
    public async Task ATimedOutOrFailedCheckIsRecordedAndRaisesTheFailureCount()
    {
        // FR-SCH-04 (four-model review F-09): the check's own token is spent by then, so the recorder uses its own limit.
        StatusStore store = new(new ScheduledCheckStatus(DateTimeOffset.UnixEpoch, "Completed", 0));

        ScheduledCheckStatus? first = await ScheduledCheckFailureRecorder.RecordAsync(store, ScheduledCheckFailureRecorder.TimedOutResult);
        ScheduledCheckStatus? second = await ScheduledCheckFailureRecorder.RecordAsync(store, ScheduledCheckFailureRecorder.FailedResult);

        Assert.AreEqual(1, first!.ConsecutiveFailures);
        Assert.AreEqual("Timed out", first.LastResult);
        Assert.AreEqual(2, second!.ConsecutiveFailures);
        Assert.AreEqual(second, store.Status);
        ScheduledCheckSettings notify = ScheduledCheckSettings.Manual with { NotificationsEnabled = true };
        Assert.IsFalse(ScheduledCheckNotificationPolicy.ShouldNotify(notify, false, false, first.ConsecutiveFailures));
        Assert.IsTrue(ScheduledCheckNotificationPolicy.ShouldNotify(notify, false, false, second.ConsecutiveFailures));
    }

    [TestMethod]
    public async Task AFailureThatCannotBeSavedIsReportedAsNotRecorded()
    {
        StatusStore store = new(new ScheduledCheckStatus(null, "Completed", int.MaxValue)) { FailSave = true };
        Assert.IsNull(await ScheduledCheckFailureRecorder.RecordAsync(store, ScheduledCheckFailureRecorder.FailedResult));
        store.FailSave = false;
        Assert.AreEqual(int.MaxValue, (await ScheduledCheckFailureRecorder.RecordAsync(store, ScheduledCheckFailureRecorder.FailedResult))!.ConsecutiveFailures);
    }

    [TestMethod]
    public async Task FailureRecorderReturnsWithinDeadlineWhileAnotherConnectionHoldsWriteLock()
    {
        string path = Path.Combine(Path.GetTempPath(), $"pcsteward-status-{Guid.NewGuid():N}.db");
        try
        {
            SqliteDatabase database = new(path);
            await database.MigrateAsync(CancellationToken.None);
            using SqliteConnection locker = new(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
            locker.Open();
            using SqliteTransaction transaction = locker.BeginTransaction();
            TimeSpan budget = TimeSpan.FromSeconds(2);
            Stopwatch elapsed = Stopwatch.StartNew();
            Assert.IsNull(await ScheduledCheckFailureRecorder.RecordAsync(database, ScheduledCheckFailureRecorder.FailedResult, writeLimit: budget));
            // Far below the 30 seconds an unbounded lock wait took, with room for a slow machine.
            Assert.IsTrue(elapsed.Elapsed < budget + TimeSpan.FromSeconds(5), $"Elapsed {elapsed.Elapsed}");
            transaction.Rollback();
            Assert.AreEqual(ScheduledCheckStatus.NotScheduled, await database.GetScheduledCheckStatusAsync(CancellationToken.None));
            ScheduledCheckStatus? saved = await ScheduledCheckFailureRecorder.RecordAsync(database, ScheduledCheckFailureRecorder.FailedResult, writeLimit: budget);
            Assert.AreEqual(1, saved?.ConsecutiveFailures);
            Assert.AreEqual(saved, await database.GetScheduledCheckStatusAsync(CancellationToken.None));
        }
        finally { File.Delete(path); }
    }
    private sealed class StatusStore(ScheduledCheckStatus status) : IScheduledCheckSettingsStore
    {
        public ScheduledCheckStatus Status { get; private set; } = status;
        public bool FailSave { get; set; }
        public Task<ScheduledCheckSettings> GetScheduledCheckSettingsAsync(CancellationToken cancellationToken) => Task.FromResult(ScheduledCheckSettings.Manual);
        public Task SaveScheduledCheckSettingsAsync(ScheduledCheckSettings settings, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<ScheduledCheckStatus> GetScheduledCheckStatusAsync(CancellationToken cancellationToken, TimeSpan? lockWaitLimit = null) => Task.FromResult(Status);
        public Task SaveScheduledCheckStatusAsync(ScheduledCheckStatus status, CancellationToken cancellationToken, TimeSpan? lockWaitLimit = null)
        {
            if (FailSave) throw new IOException("The status could not be saved.");
            Status = status;
            return Task.CompletedTask;
        }
    }

    [TestMethod]
    public async Task WindowsAdapterBuildsFixedScanOnlyTaskWithoutUpdateArguments()
    {
        RecordingTaskSchedulerCommandRunner runner = new();
        WindowsCurrentUserScheduledTaskRegistrar registrar = new(@"C:\Program Files\PC Steward\PCSteward.ScheduledHost.exe", runner);
        await registrar.RegisterOrUpdateAsync(new ScheduledCheckTaskDefinition(ScheduleCadence.Daily, DayOfWeek.Monday, new TimeOnly(9, 5), true), CancellationToken.None);

        CollectionAssert.Contains(runner.Arguments, "/Create");
        CollectionAssert.Contains(runner.Arguments, "/IT");
        CollectionAssert.Contains(runner.Arguments, "/RL");
        CollectionAssert.Contains(runner.Arguments, "LIMITED");
        CollectionAssert.DoesNotContain(runner.Arguments, "update");
        CollectionAssert.DoesNotContain(runner.Arguments, "install");
        StringAssert.Contains(runner.Arguments[runner.Arguments.IndexOf("/TR") + 1], "PCSteward.ScheduledHost.exe");
    }

    [TestMethod]
    public async Task ExactTaskRemovalUsesLanguageIndependentExactQueryBeforeAndAfterDeletion()
    {
        SequencedTaskSchedulerCommandRunner runner = new(new TaskSchedulerCommandResult(0, "deleted", ""));
        RecordingExactTaskQuery query = new(true, false);
        WindowsCurrentUserScheduledTaskRegistrar registrar = new(@"C:\Program Files\PC Steward\PCSteward.ScheduledHost.exe", runner, query);

        await registrar.EnsureAbsentAsync(CancellationToken.None);

        CollectionAssert.AreEqual(DeleteOnly, runner.Commands);
        Assert.AreEqual(2, query.Calls);
    }

    [TestMethod]
    public async Task ExactTaskRemovalFailsWhenTheTaskRemainsPresent()
    {
        SequencedTaskSchedulerCommandRunner runner = new(new TaskSchedulerCommandResult(0, "deleted", ""));
        WindowsCurrentUserScheduledTaskRegistrar registrar = new(@"C:\Program Files\PC Steward\PCSteward.ScheduledHost.exe", runner, new RecordingExactTaskQuery(true, true));

        try { await registrar.EnsureAbsentAsync(CancellationToken.None); Assert.Fail("A residual task must fail closed."); }
        catch (InvalidOperationException) { }
        CollectionAssert.AreEqual(DeleteOnly, runner.Commands);
    }

    [TestMethod]
    public async Task ExactTaskRemovalDoesNotInvokeSchtasksWhenComQueryProvesAbsence()
    {
        SequencedTaskSchedulerCommandRunner runner = new();
        RecordingExactTaskQuery query = new(false);
        WindowsCurrentUserScheduledTaskRegistrar registrar = new(@"C:\Program Files\PC Steward\PCSteward.ScheduledHost.exe", runner, query);

        await registrar.EnsureAbsentAsync(CancellationToken.None);

        Assert.AreEqual(1, query.Calls);
        Assert.AreEqual(0, runner.Commands.Count);
    }

    [TestMethod]
    public void ExactTaskQueryRecognizesNotFoundAcrossComBinderExceptionShapes()
    {
        Assert.IsTrue(WindowsExactScheduledTaskQuery.IsTaskNotFound(new System.IO.FileNotFoundException("missing")));
        Assert.IsTrue(WindowsExactScheduledTaskQuery.IsTaskNotFound(new System.Reflection.TargetInvocationException(new System.IO.FileNotFoundException("missing"))));
        Assert.IsFalse(WindowsExactScheduledTaskQuery.IsTaskNotFound(new InvalidOperationException("scheduler unavailable")));
    }

    private sealed class RecordingRegistrar : ICurrentUserScheduledTaskRegistrar
    {
        public List<ScheduledCheckTaskDefinition> Registered { get; } = [];
        public int Removed { get; private set; }
        public int EnsuredAbsent { get; private set; }
        public Task RegisterOrUpdateAsync(ScheduledCheckTaskDefinition definition, CancellationToken cancellationToken) { Registered.Add(definition); return Task.CompletedTask; }
        public Task RemoveAsync(CancellationToken cancellationToken) { Removed++; return Task.CompletedTask; }
        public Task EnsureAbsentAsync(CancellationToken cancellationToken) { EnsuredAbsent++; return Task.CompletedTask; }
    }

    private sealed class RecordingTaskSchedulerCommandRunner : ITaskSchedulerCommandRunner
    {
        public List<string> Arguments { get; private set; } = [];
        public Task<TaskSchedulerCommandResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken) { Arguments = arguments.ToList(); return Task.FromResult(new TaskSchedulerCommandResult(0, "", "")); }
    }

    private sealed class SequencedTaskSchedulerCommandRunner(params TaskSchedulerCommandResult[] results) : ITaskSchedulerCommandRunner
    {
        private readonly Queue<TaskSchedulerCommandResult> results = new(results);
        public List<string> Commands { get; } = [];
        public Task<TaskSchedulerCommandResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        {
            Commands.Add(arguments[0]);
            return Task.FromResult(this.results.Dequeue());
        }
    }

    private sealed class RecordingExactTaskQuery(params bool[] results) : IExactScheduledTaskQuery
    {
        private readonly Queue<bool> results = new(results);
        public int Calls { get; private set; }
        public Task<bool> ExistsAsync(string taskName, CancellationToken cancellationToken) { Calls++; return Task.FromResult(results.Dequeue()); }
    }
}
