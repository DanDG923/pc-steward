using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using PCSteward.Application;
using PCSteward.Domain;

namespace PCSteward.Provider.WinGet;

public sealed record WingetProcessResult(int ExitCode, string StandardOutput, string StandardError, bool TimedOut, bool OutputLimitExceeded = false);
internal interface IWingetProcess { Task<WingetProcessResult> RunAsync(IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken); }
public interface IWingetUpdateExecutor { Task<WingetProcessResult> ExecuteAsync(ImmutableUpdateJob job, CancellationToken cancellationToken); }
public sealed record WingetInstalledRecord(string Name, string Id, string Version, string? Source);
public sealed record WingetInventoryResult(IReadOnlyCollection<WingetInstalledRecord> Records, IReadOnlyCollection<string> Warnings);
public sealed record WingetAvailableUpdateRecord(string Name, string Id, string InstalledVersion, string AvailableVersion, string Source);
public sealed record WingetAvailableUpdateResult(string? ProviderVersion, IReadOnlyCollection<WingetAvailableUpdateRecord> Records, IReadOnlyCollection<string> Warnings);

public sealed class WingetInstalledInventoryProvider
{
    private const int MaximumOutputLength = 1_000_000;
    private readonly IWingetProcess process;
    public WingetInstalledInventoryProvider() : this(new WindowsWingetProcess()) { }
    internal WingetInstalledInventoryProvider(IWingetProcess process) => this.process = process;
    public static IReadOnlyList<string> BuildInstalledListArguments() => ["list", "--disable-interactivity"];
    public async Task<WingetInventoryResult> ReadInstalledAsync(CancellationToken cancellationToken)
    {
        WingetProcessResult result = await process.RunAsync(BuildInstalledListArguments(), TimeSpan.FromSeconds(30), cancellationToken);
        if (result.OutputLimitExceeded || result.StandardOutput.Length > MaximumOutputLength) return new WingetInventoryResult([], ["winget:output-too-large"]);
        if (result.TimedOut) return new WingetInventoryResult([], ["winget:timeout"]);
        if (result.ExitCode != 0) return new WingetInventoryResult([], [$"winget:exit-{result.ExitCode}"]);
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
    public WingetUpdateSourceConnector(string sourceId) : this(new WindowsWingetProcess(), sourceId) { }
    internal WingetUpdateSourceConnector(IWingetProcess process, string sourceId)
    {
        this.process = process;
        SourceId = CanonicalSource(sourceId);
    }
    public string SourceId { get; }
    public TimeSpan Timeout => TimeSpan.FromSeconds(45);
    public static IReadOnlyList<string> BuildAvailableUpdateArguments(string sourceId) => ["upgrade", "--source", CanonicalSource(sourceId), "--disable-interactivity"];
    public async Task<SourceSnapshot> RefreshAsync(CancellationToken cancellationToken)
    {
        WingetProcessResult versionResult = await process.RunAsync(["--version"], Timeout, cancellationToken).ConfigureAwait(false);
        string? providerVersion = ReadVersion(versionResult);
        WingetProcessResult result = await process.RunAsync(BuildAvailableUpdateArguments(SourceId), Timeout, cancellationToken).ConfigureAwait(false);
        WingetAvailableUpdateResult parsed = Classify(result, providerVersion, SourceId);
        if (parsed.Warnings.Any(warning => warning is "winget:timeout" or "winget:output-too-large" or "winget:unrecognized-output" || warning.StartsWith("winget:exit-", StringComparison.Ordinal))) throw new InvalidOperationException(string.Join(",", parsed.Warnings));
        return new(SourceId, parsed.ProviderVersion, DateTimeOffset.UtcNow, SourceSnapshotState.Live,
            parsed.Records.Select(record => new SourcePackageRecord(SourceId, record.Id, record.Name, null, record.InstalledVersion, record.AvailableVersion, "Unknown", "Unknown", null)).ToArray(), parsed.Warnings);
    }
    public static WingetAvailableUpdateResult Classify(WingetProcessResult result, string? providerVersion, string sourceId)
    {
        string expectedSource = CanonicalSource(sourceId);
        if (result.OutputLimitExceeded || result.StandardOutput.Length > MaximumOutputLength) return new(providerVersion, [], ["winget:output-too-large"]);
        if (result.TimedOut) return new(providerVersion, [], ["winget:timeout"]);
        if (result.ExitCode != 0) return new(providerVersion, [], [$"winget:exit-{result.ExitCode}"]);
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

/// <summary>Provider-owned construction for an exact, user-scope WinGet update operation.</summary>
public sealed class WingetUpdateExecutor : IWingetUpdateExecutor
{
    private static readonly TimeSpan UpdateTimeout = TimeSpan.FromMinutes(15);
    private readonly IWingetProcess process;

    public WingetUpdateExecutor() : this(new WindowsWingetProcess()) { }
    internal WingetUpdateExecutor(IWingetProcess process) => this.process = process ?? throw new ArgumentNullException(nameof(process));

    public Task<WingetProcessResult> ExecuteAsync(ImmutableUpdateJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        if (job.Selection.Provider != UpdateProvider.WinGet) throw new ArgumentException("Only WinGet jobs are supported.", nameof(job));
        if (job.Selection.Scope != UpdateScope.User) throw new ArgumentException("The non-elevated provider accepts only user-scope jobs.", nameof(job));
        return process.RunAsync(BuildUpdateArguments(job), UpdateTimeout, cancellationToken);
    }

    public static IReadOnlyList<string> BuildUpdateArguments(ImmutableUpdateJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        if (!job.HasValidDigest) throw new ArgumentException("The update job failed its integrity check.", nameof(job));
        if (job.Selection.Provider != UpdateProvider.WinGet) throw new ArgumentException("Only WinGet jobs are supported.", nameof(job));
        if (job.Selection.Scope != UpdateScope.User) throw new ArgumentException("Only user-scope operations may run without elevation.", nameof(job));
        return
        [
            "upgrade",
            "--id", job.Selection.PackageId,
            "--version", job.Selection.CandidateVersion,
            "--source", Source(job.Selection.Source),
            "--exact",
            "--scope", "user",
            "--architecture", Architecture(job.Selection.Architecture),
            "--disable-interactivity"
        ];
    }

    private static string Source(UpdateSource source) => source switch
    {
        UpdateSource.WinGet => "winget",
        UpdateSource.MicrosoftStore => "msstore",
        _ => throw new ArgumentOutOfRangeException(nameof(source))
    };

    private static string Architecture(UpdateArchitecture architecture) => architecture switch
    {
        UpdateArchitecture.X64 => "x64",
        UpdateArchitecture.X86 => "x86",
        UpdateArchitecture.Arm64 => "arm64",
        UpdateArchitecture.Neutral => "neutral",
        _ => throw new ArgumentOutOfRangeException(nameof(architecture))
    };
}

/// <summary>Provider-owned construction for an exact, machine-scope WinGet update operation.</summary>
public sealed class WingetMachineUpdateExecutor : IWingetUpdateExecutor
{
    private static readonly TimeSpan UpdateTimeout = TimeSpan.FromMinutes(15);
    private readonly IWingetProcess process;

    public WingetMachineUpdateExecutor() : this(new WindowsWingetProcess()) { }
    internal WingetMachineUpdateExecutor(IWingetProcess process) => this.process = process ?? throw new ArgumentNullException(nameof(process));

    public Task<WingetProcessResult> ExecuteAsync(ImmutableUpdateJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        if (job.Selection.Provider != UpdateProvider.WinGet) throw new ArgumentException("Only WinGet jobs are supported.", nameof(job));
        if (job.Selection.Scope != UpdateScope.Machine) throw new ArgumentException("The elevated provider accepts only machine-scope jobs.", nameof(job));
        return process.RunAsync(BuildUpdateArguments(job), UpdateTimeout, cancellationToken);
    }

    public static IReadOnlyList<string> BuildUpdateArguments(ImmutableUpdateJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        if (!job.HasValidDigest) throw new ArgumentException("The update job failed its integrity check.", nameof(job));
        if (job.Selection.Provider != UpdateProvider.WinGet) throw new ArgumentException("Only WinGet jobs are supported.", nameof(job));
        if (job.Selection.Scope != UpdateScope.Machine) throw new ArgumentException("Only machine-scope operations may run through the elevated helper.", nameof(job));
        return
        [
            "upgrade",
            "--id", job.Selection.PackageId,
            "--version", job.Selection.CandidateVersion,
            "--source", Source(job.Selection.Source),
            "--exact",
            "--scope", "machine",
            "--architecture", Architecture(job.Selection.Architecture),
            "--disable-interactivity"
        ];
    }

    private static string Source(UpdateSource source) => source switch
    {
        UpdateSource.WinGet => "winget",
        UpdateSource.MicrosoftStore => "msstore",
        _ => throw new ArgumentOutOfRangeException(nameof(source))
    };

    private static string Architecture(UpdateArchitecture architecture) => architecture switch
    {
        UpdateArchitecture.X64 => "x64",
        UpdateArchitecture.X86 => "x86",
        UpdateArchitecture.Arm64 => "arm64",
        UpdateArchitecture.Neutral => "neutral",
        _ => throw new ArgumentOutOfRangeException(nameof(architecture))
    };
}

internal sealed class WindowsWingetProcess : IWingetProcess
{
    private const int MaximumCapturedOutputLength = 1_000_000;

    public async Task<WingetProcessResult> RunAsync(IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ProcessStartInfo start = new() { FileName = ResolveWingetExecutable(), UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using Process process = new() { StartInfo = start };
        process.Start();
        TaskCompletionSource overflow = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<BoundedRead> output = ReadBoundedAsync(process.StandardOutput, overflow);
        Task<BoundedRead> error = ReadBoundedAsync(process.StandardError, overflow);
        Task exit = process.WaitForExitAsync(CancellationToken.None);
        Task cancellation = Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); Task delay = Task.Delay(timeout, CancellationToken.None);
        Task completed = await Task.WhenAny(exit, delay, cancellation, overflow.Task);
        if (completed == cancellation)
        {
            Terminate(process); await exit; await Task.WhenAll(output, error); cancellationToken.ThrowIfCancellationRequested();
        }
        if (completed == overflow.Task)
        {
            Terminate(process); await exit; BoundedRead stdout = await output; BoundedRead stderr = await error;
            return new WingetProcessResult(-1, stdout.Text, stderr.Text, false, true);
        }
        if (completed == delay)
        {
            Terminate(process); await exit; BoundedRead stdout = await output; BoundedRead stderr = await error;
            return new WingetProcessResult(-1, stdout.Text, stderr.Text, true, stdout.ExceededLimit || stderr.ExceededLimit);
        }
        BoundedRead completedOutput = await output; BoundedRead completedError = await error;
        return new WingetProcessResult(process.ExitCode, completedOutput.Text, completedError.Text, false, completedOutput.ExceededLimit || completedError.ExceededLimit);
    }

    private static async Task<BoundedRead> ReadBoundedAsync(StreamReader reader, TaskCompletionSource overflow)
    {
        StringBuilder captured = new(Math.Min(MaximumCapturedOutputLength, 4096)); char[] buffer = new char[4096]; bool exceededLimit = false;
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length)).ConfigureAwait(false)) != 0)
        {
            int available = MaximumCapturedOutputLength - captured.Length;
            if (available > 0) captured.Append(buffer, 0, Math.Min(available, read));
            if (read > available)
            {
                exceededLimit = true;
                overflow.TrySetResult();
            }
        }
        return new BoundedRead(captured.ToString(), exceededLimit);
    }

    private sealed record BoundedRead(string Text, bool ExceededLimit);
    private static void Terminate(Process process) { if (!process.HasExited) process.Kill(entireProcessTree: true); }
    private static string ResolveWingetExecutable()
    {
        string localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify);
        if (string.IsNullOrWhiteSpace(localApplicationData) || !Path.IsPathFullyQualified(localApplicationData))
        {
            throw new IOException("The fixed WinGet application-alias root is unavailable.");
        }

        return Path.GetFullPath(Path.Combine(localApplicationData, "Microsoft", "WindowsApps", "winget.exe"));
    }
}
