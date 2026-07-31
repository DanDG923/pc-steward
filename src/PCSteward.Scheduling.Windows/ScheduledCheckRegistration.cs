using PCSteward.Application;
using System.Diagnostics;
using System.Globalization;

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
    Task EnsureAbsentAsync(CancellationToken cancellationToken);
}

public sealed class ScheduledCheckRegistrationService(ICurrentUserScheduledTaskRegistrar registrar)
{
    public async Task ApplyAsync(ScheduledCheckSettings settings, CancellationToken cancellationToken)
    {
        ScheduledCheckSettings normalized = settings.Normalize();
        if (!normalized.IsEnabled) { await registrar.EnsureAbsentAsync(cancellationToken).ConfigureAwait(false); return; }
        await registrar.RegisterOrUpdateAsync(ScheduledCheckTaskDefinition.From(normalized), cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Runs only the inbox Task Scheduler tool with a fixed executable and structured arguments.</summary>
public interface ITaskSchedulerCommandRunner
{
    Task<TaskSchedulerCommandResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken);
}

public sealed record TaskSchedulerCommandResult(int ExitCode, string StandardOutput, string StandardError);

public interface IExactScheduledTaskQuery
{
    Task<bool> ExistsAsync(string taskName, CancellationToken cancellationToken);
}

public sealed class WindowsTaskSchedulerCommandRunner : ITaskSchedulerCommandRunner
{
    public async Task<TaskSchedulerCommandResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ProcessStartInfo start = new()
        {
            FileName = Path.Combine(Environment.SystemDirectory, "schtasks.exe"),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Windows Task Scheduler could not be started.");
        Task<string> output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> error = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return new(process.ExitCode, await output.ConfigureAwait(false), await error.ConfigureAwait(false));
    }
}

/// <summary>Uses the Task Scheduler COM API so exact task absence is not inferred from localized command text.</summary>
public sealed class WindowsExactScheduledTaskQuery : IExactScheduledTaskQuery
{
    public Task<bool> ExistsAsync(string taskName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(taskName);
        try
        {
            Type serviceType = Type.GetTypeFromProgID("Schedule.Service") ?? throw new InvalidOperationException("The Windows Task Scheduler service is unavailable.");
            object service = Activator.CreateInstance(serviceType) ?? throw new InvalidOperationException("The Windows Task Scheduler service could not be opened.");
            serviceType.InvokeMember("Connect", System.Reflection.BindingFlags.InvokeMethod, null, service, [], CultureInfo.InvariantCulture);
            object folder = serviceType.InvokeMember("GetFolder", System.Reflection.BindingFlags.InvokeMethod, null, service, ["\\"], CultureInfo.InvariantCulture) ?? throw new InvalidOperationException("The Windows Task Scheduler root folder is unavailable.");
            Type folderType = folder.GetType();
            _ = folderType.InvokeMember("GetTask", System.Reflection.BindingFlags.InvokeMethod, null, folder, [taskName], CultureInfo.InvariantCulture);
            return Task.FromResult(true);
        }
        catch (Exception exception) when (IsTaskNotFound(exception))
        {
            return Task.FromResult(false);
        }
    }

    // The Task Scheduler COM binder can surface a missing task either as a COMException,
    // a TargetInvocationException wrapper, or a FileNotFoundException with the same
    // HRESULT.  The result is still the exact absence signal, not a scheduler failure.
    internal static bool IsTaskNotFound(Exception exception)
    {
        const int FileNotFoundHResult = unchecked((int)0x80070002);
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current.HResult == FileNotFoundHResult) return true;
        }
        return false;
    }
}

/// <summary>Current-user Task Scheduler adapter. The action path is supplied by desktop composition, never by metadata or settings.</summary>
public sealed class WindowsCurrentUserScheduledTaskRegistrar : ICurrentUserScheduledTaskRegistrar
{
    private readonly ITaskSchedulerCommandRunner runner;
    private readonly IExactScheduledTaskQuery exactTaskQuery;
    private readonly string scheduledHostPath;

    public WindowsCurrentUserScheduledTaskRegistrar(string scheduledHostPath, ITaskSchedulerCommandRunner? runner = null, IExactScheduledTaskQuery? exactTaskQuery = null)
    {
        if (string.IsNullOrWhiteSpace(scheduledHostPath) || !Path.IsPathFullyQualified(scheduledHostPath) || !string.Equals(Path.GetFileName(scheduledHostPath), "PCSteward.ScheduledHost.exe", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The scheduled host must be the fixed PC Steward scheduled-host executable.", nameof(scheduledHostPath));
        this.scheduledHostPath = Path.GetFullPath(scheduledHostPath);
        this.runner = runner ?? new WindowsTaskSchedulerCommandRunner();
        this.exactTaskQuery = exactTaskQuery ?? new WindowsExactScheduledTaskQuery();
    }

    public async Task RegisterOrUpdateAsync(ScheduledCheckTaskDefinition definition, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        List<string> arguments = ["/Create", "/TN", ScheduledCheckTaskDefinition.TechnicalTaskName, "/TR", QuoteTaskAction(scheduledHostPath), "/SC", definition.Cadence == ScheduleCadence.Daily ? "DAILY" : "WEEKLY", "/ST", definition.LocalStartTime.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture), "/RL", "LIMITED", "/IT", "/F"];
        if (definition.Cadence == ScheduleCadence.Weekly) arguments.AddRange(["/D", definition.WeeklyDay.ToString()[..3].ToUpperInvariant()]);
        if ((await runner.RunAsync(arguments, cancellationToken).ConfigureAwait(false)).ExitCode != 0) throw new InvalidOperationException("Windows Task Scheduler rejected the current-user scan-only task.");
    }

    public async Task RemoveAsync(CancellationToken cancellationToken)
    {
        TaskSchedulerCommandResult result = await runner.RunAsync(["/Delete", "/TN", ScheduledCheckTaskDefinition.TechnicalTaskName, "/F"], cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0) throw new InvalidOperationException("Windows Task Scheduler could not remove the PC Steward task.");
    }

    public async Task EnsureAbsentAsync(CancellationToken cancellationToken)
    {
        if (await exactTaskQuery.ExistsAsync(ScheduledCheckTaskDefinition.TechnicalTaskName, cancellationToken).ConfigureAwait(false))
        {
            await RemoveAsync(cancellationToken).ConfigureAwait(false);
            if (await exactTaskQuery.ExistsAsync(ScheduledCheckTaskDefinition.TechnicalTaskName, cancellationToken).ConfigureAwait(false)) throw new InvalidOperationException("The exact PC Steward scheduled task still exists after removal.");
        }
    }

    internal static string QuoteTaskAction(string fullPath) => '"' + fullPath + '"';
}
