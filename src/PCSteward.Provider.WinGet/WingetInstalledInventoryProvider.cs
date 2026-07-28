using System.Diagnostics;

namespace PCSteward.Provider.WinGet;

public sealed record WingetProcessResult(int ExitCode, string StandardOutput, string StandardError, bool TimedOut);
public interface IWingetProcess { Task<WingetProcessResult> RunAsync(string executable, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken); }
public sealed record WingetInstalledRecord(string Name, string Id, string Version, string? Source);
public sealed record WingetInventoryResult(IReadOnlyCollection<WingetInstalledRecord> Records, IReadOnlyCollection<string> Warnings);

public sealed class WingetInstalledInventoryProvider
{
    private const int MaximumOutputLength = 1_000_000;
    private readonly IWingetProcess process;
    public WingetInstalledInventoryProvider(IWingetProcess process) => this.process = process;
    public static IReadOnlyList<string> BuildInstalledListArguments() => ["list", "--disable-interactivity"];
    public async Task<WingetInventoryResult> ReadInstalledAsync(CancellationToken cancellationToken)
    {
        WingetProcessResult result = await process.RunAsync("winget.exe", BuildInstalledListArguments(), TimeSpan.FromSeconds(30), cancellationToken);
        if (result.TimedOut) return new WingetInventoryResult([], ["winget:timeout"]);
        if (result.ExitCode != 0) return new WingetInventoryResult([], [$"winget:exit-{result.ExitCode}"]);
        if (result.StandardOutput.Length > MaximumOutputLength) return new WingetInventoryResult([], ["winget:output-too-large"]);
        return ParseTable(result.StandardOutput);
    }
    public static WingetInventoryResult ParseTable(string output)
    {
        List<WingetInstalledRecord> records = []; List<string> warnings = [];
        string[] lines = output.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2) return new WingetInventoryResult([], ["winget:unrecognized-output"]);
        foreach (string line in lines.Skip(2))
        {
            string[] columns = line.Split("  ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (columns.Length < 3) { warnings.Add("winget:malformed-row"); continue; }
            records.Add(new WingetInstalledRecord(columns[0], columns[1], columns[2], columns.Length > 3 ? columns[^1] : null));
        }
        return new WingetInventoryResult(records, warnings);
    }
}

public sealed class WindowsWingetProcess : IWingetProcess
{
    public async Task<WingetProcessResult> RunAsync(string executable, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken)
    {
        ProcessStartInfo start = new() { FileName = executable, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using Process process = new() { StartInfo = start };
        process.Start(); Task<string> output = process.StandardOutput.ReadToEndAsync(CancellationToken.None); Task<string> error = process.StandardError.ReadToEndAsync(CancellationToken.None); Task exit = process.WaitForExitAsync(CancellationToken.None);
        Task cancellation = Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); Task delay = Task.Delay(timeout, CancellationToken.None);
        Task completed = await Task.WhenAny(exit, delay, cancellation);
        if (completed == cancellation)
        {
            Terminate(process); await exit; cancellationToken.ThrowIfCancellationRequested();
        }
        if (completed == delay)
        {
            Terminate(process); await exit; return new WingetProcessResult(-1, await output, await error, true);
        }
        return new WingetProcessResult(process.ExitCode, await output, await error, false);
    }
    private static void Terminate(Process process) { if (!process.HasExited) process.Kill(entireProcessTree: true); }
}
