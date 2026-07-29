using PCSteward.Application;
using PCSteward.Scheduling.Windows;

namespace PCSteward.Application.IntegrationTests;

[TestClass]
public sealed class ScheduledChecksTests
{
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
        Assert.AreEqual(1, registrar.Removed);
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

    private sealed class RecordingRegistrar : ICurrentUserScheduledTaskRegistrar
    {
        public List<ScheduledCheckTaskDefinition> Registered { get; } = [];
        public int Removed { get; private set; }
        public Task RegisterOrUpdateAsync(ScheduledCheckTaskDefinition definition, CancellationToken cancellationToken) { Registered.Add(definition); return Task.CompletedTask; }
        public Task RemoveAsync(CancellationToken cancellationToken) { Removed++; return Task.CompletedTask; }
    }

    private sealed class RecordingTaskSchedulerCommandRunner : ITaskSchedulerCommandRunner
    {
        public List<string> Arguments { get; private set; } = [];
        public Task<int> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken) { Arguments = arguments.ToList(); return Task.FromResult(0); }
    }
}
