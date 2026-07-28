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
