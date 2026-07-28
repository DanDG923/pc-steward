using System.Diagnostics;
using System.Text.RegularExpressions;
using PCSteward.Application;
using PCSteward.Domain;

namespace PCSteward.Provider.WinGet;

public sealed record WingetProcessResult(int ExitCode, string StandardOutput, string StandardError, bool TimedOut);
public interface IWingetProcess { Task<WingetProcessResult> RunAsync(string executable, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken); }
public sealed record WingetInstalledRecord(string Name, string Id, string Version, string? Source);
public sealed record WingetInventoryResult(IReadOnlyCollection<WingetInstalledRecord> Records, IReadOnlyCollection<string> Warnings);
public sealed record WingetAvailableUpdateRecord(string Name, string Id, string InstalledVersion, string AvailableVersion, string Source);
public sealed record WingetAvailableUpdateResult(string? ProviderVersion, IReadOnlyCollection<WingetAvailableUpdateRecord> Records, IReadOnlyCollection<string> Warnings);

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

/// <summary>Read-only, fixed-argument WinGet source connector. Parsed rows remain update intelligence only.</summary>
public sealed partial class WingetUpdateSourceConnector : IUpdateSourceConnector
{
    private const int MaximumOutputLength = 1_000_000;
    private const int MaximumFieldLength = 256;
    private readonly IWingetProcess process;
    public WingetUpdateSourceConnector(IWingetProcess process, string sourceId)
    {
        this.process = process;
        SourceId = CanonicalSource(sourceId);
    }
    public string SourceId { get; }
    public TimeSpan Timeout => TimeSpan.FromSeconds(45);
    public static IReadOnlyList<string> BuildAvailableUpdateArguments(string sourceId) => ["upgrade", "--source", CanonicalSource(sourceId), "--disable-interactivity"];
    public async Task<SourceSnapshot> RefreshAsync(CancellationToken cancellationToken)
    {
        WingetProcessResult versionResult = await process.RunAsync("winget.exe", ["--version"], Timeout, cancellationToken).ConfigureAwait(false);
        string? providerVersion = ReadVersion(versionResult);
        WingetProcessResult result = await process.RunAsync("winget.exe", BuildAvailableUpdateArguments(SourceId), Timeout, cancellationToken).ConfigureAwait(false);
        WingetAvailableUpdateResult parsed = Classify(result, providerVersion, SourceId);
        if (parsed.Warnings.Any(warning => warning is "winget:timeout" or "winget:output-too-large" or "winget:unrecognized-output" || warning.StartsWith("winget:exit-", StringComparison.Ordinal))) throw new InvalidOperationException(string.Join(",", parsed.Warnings));
        return new(SourceId, parsed.ProviderVersion, DateTimeOffset.UtcNow, SourceSnapshotState.Live,
            parsed.Records.Select(record => new SourcePackageRecord(SourceId, record.Id, record.Name, null, record.InstalledVersion, record.AvailableVersion, "Unknown", "Unknown", null)).ToArray(), parsed.Warnings);
    }
    public static WingetAvailableUpdateResult Classify(WingetProcessResult result, string? providerVersion, string sourceId)
    {
        string expectedSource = CanonicalSource(sourceId);
        if (result.TimedOut) return new(providerVersion, [], ["winget:timeout"]);
        if (result.ExitCode != 0) return new(providerVersion, [], [$"winget:exit-{result.ExitCode}"]);
        if (result.StandardOutput.Length > MaximumOutputLength) return new(providerVersion, [], ["winget:output-too-large"]);
        return ParseAvailableTable(result.StandardOutput, providerVersion, expectedSource);
    }
    public static WingetAvailableUpdateResult ParseAvailableTable(string output, string? providerVersion, string expectedSource)
    {
        List<WingetAvailableUpdateRecord> records = []; List<string> warnings = [];
        string[] lines = output.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2)
        {
            if (output.Contains("No applicable upgrade found", StringComparison.OrdinalIgnoreCase)) return new(providerVersion, [], providerVersion is null ? ["winget:provider-version-unavailable"] : []);
            return new(providerVersion, [], ["winget:unrecognized-output"]);
        }
        foreach (string line in lines.Skip(2))
        {
            string[] columns = Regex.Split(line.Trim(), @"\s{2,}").Where(value => value.Length > 0).ToArray();
            if (columns.Length < 5) { warnings.Add("winget:malformed-update-row"); continue; }
            string name = columns[0]; string id = columns[1]; string installed = columns[2]; string available = columns[3]; string source = columns[^1];
            if (!Valid(name) || !PackageId().IsMatch(id) || !Version(installed) || !Version(available) || !string.Equals(source, expectedSource, StringComparison.OrdinalIgnoreCase)) { warnings.Add("winget:invalid-update-row"); continue; }
            records.Add(new(name, id, installed, available, expectedSource));
        }
        if (providerVersion is null) warnings.Add("winget:provider-version-unavailable");
        return new(providerVersion, records, warnings.Distinct(StringComparer.Ordinal).ToArray());
    }
    private static string? ReadVersion(WingetProcessResult result) => result is { TimedOut: false, ExitCode: 0 } && Version(result.StandardOutput.Trim()) ? result.StandardOutput.Trim() : null;
    private static bool Valid(string value) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= MaximumFieldLength && value.All(character => !char.IsControl(character));
    private static bool Version(string value) => Valid(value) && value.All(character => char.IsLetterOrDigit(character) || character is '.' or '-' or '+' or '_');
    private static string CanonicalSource(string sourceId) => sourceId.Trim().ToLowerInvariant() switch { "winget" => "winget", "msstore" => "msstore", _ => throw new ArgumentOutOfRangeException(nameof(sourceId), "Only the fixed WinGet and Microsoft Store sources are supported.") };
    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9.-]{0,255}$")]
    private static partial Regex PackageId();
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
