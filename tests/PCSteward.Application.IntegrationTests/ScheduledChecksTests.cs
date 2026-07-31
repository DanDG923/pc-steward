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
