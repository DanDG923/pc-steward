using PCSteward.Application;
using System.Diagnostics;

namespace PCSteward.Scheduling.Windows;

/// <summary>Fixed current-user task contract. It deliberately contains no executable command, arguments, update verb or elevation option.</summary>
public sealed record ScheduledCheckTaskDefinition(
    ScheduleCadence Cadence,
    DayOfWeek WeeklyDay,
    TimeOnly LocalStartTime,
    bool RunBelowNormalPriority)
{
    public const string TechnicalTaskName = "PCSteward.ScheduledCheck";

    public static ScheduledCheckTaskDefinition From(ScheduledCheckSettings settings)
    {
        ScheduledCheckSettings normalized = settings.Normalize();
        if (!normalized.IsEnabled || normalized.Cadence == ScheduleCadence.Manual) throw new InvalidOperationException("Manual settings do not define a scheduled task.");
        return new(normalized.Cadence, normalized.WeeklyDay, normalized.LocalStartTime, true);
    }
}

public interface ICurrentUserScheduledTaskRegistrar
{
    Task RegisterOrUpdateAsync(ScheduledCheckTaskDefinition definition, CancellationToken cancellationToken);
    Task RemoveAsync(CancellationToken cancellationToken);
}

public sealed class ScheduledCheckRegistrationService(ICurrentUserScheduledTaskRegistrar registrar)
{
    public async Task ApplyAsync(ScheduledCheckSettings settings, CancellationToken cancellationToken)
    {
        ScheduledCheckSettings normalized = settings.Normalize();
        if (!normalized.IsEnabled) { await registrar.RemoveAsync(cancellationToken).ConfigureAwait(false); return; }
        await registrar.RegisterOrUpdateAsync(ScheduledCheckTaskDefinition.From(normalized), cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Runs only the inbox Task Scheduler tool with a fixed executable and structured arguments.</summary>
public interface ITaskSchedulerCommandRunner
{
    Task<int> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken);
}

public sealed class WindowsTaskSchedulerCommandRunner : ITaskSchedulerCommandRunner
{
    public async Task<int> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ProcessStartInfo start = new()
        {
            FileName = Path.Combine(Environment.SystemDirectory, "schtasks.exe"),
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Windows Task Scheduler could not be started.");
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return process.ExitCode;
    }
}

/// <summary>Current-user Task Scheduler adapter. The action path is supplied by desktop composition, never by metadata or settings.</summary>
public sealed class WindowsCurrentUserScheduledTaskRegistrar : ICurrentUserScheduledTaskRegistrar
{
    private readonly ITaskSchedulerCommandRunner runner;
    private readonly string scheduledHostPath;

    public WindowsCurrentUserScheduledTaskRegistrar(string scheduledHostPath, ITaskSchedulerCommandRunner? runner = null)
    {
        if (string.IsNullOrWhiteSpace(scheduledHostPath) || !Path.IsPathFullyQualified(scheduledHostPath) || !string.Equals(Path.GetFileName(scheduledHostPath), "PCSteward.ScheduledHost.exe", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The scheduled host must be the fixed PC Steward scheduled-host executable.", nameof(scheduledHostPath));
        this.scheduledHostPath = Path.GetFullPath(scheduledHostPath);
        this.runner = runner ?? new WindowsTaskSchedulerCommandRunner();
    }

    public async Task RegisterOrUpdateAsync(ScheduledCheckTaskDefinition definition, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        List<string> arguments = ["/Create", "/TN", ScheduledCheckTaskDefinition.TechnicalTaskName, "/TR", QuoteTaskAction(scheduledHostPath), "/SC", definition.Cadence == ScheduleCadence.Daily ? "DAILY" : "WEEKLY", "/ST", definition.LocalStartTime.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture), "/RL", "LIMITED", "/IT", "/F"];
        if (definition.Cadence == ScheduleCadence.Weekly) arguments.AddRange(["/D", definition.WeeklyDay.ToString()[..3].ToUpperInvariant()]);
        if (await runner.RunAsync(arguments, cancellationToken).ConfigureAwait(false) != 0) throw new InvalidOperationException("Windows Task Scheduler rejected the current-user scan-only task.");
    }

    public async Task RemoveAsync(CancellationToken cancellationToken)
    {
        int exitCode = await runner.RunAsync(["/Delete", "/TN", ScheduledCheckTaskDefinition.TechnicalTaskName, "/F"], cancellationToken).ConfigureAwait(false);
        if (exitCode is not 0 and not 1) throw new InvalidOperationException("Windows Task Scheduler could not remove the PC Steward task.");
    }

    internal static string QuoteTaskAction(string fullPath) => '"' + fullPath + '"';
}
