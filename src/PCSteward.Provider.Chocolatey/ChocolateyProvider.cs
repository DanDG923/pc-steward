using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using PCSteward.Application;
using PCSteward.Domain;
using PCSteward.Provider.WinGet;

namespace PCSteward.Provider.Chocolatey;

public sealed record ChocolateyProcessResult(int ExitCode, string StandardOutput, string StandardError, bool TimedOut, bool OutputLimitExceeded = false);
internal interface IChocolateyProcess { Task<ChocolateyProcessResult> RunAsync(IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken); }
public sealed record ChocolateyInstalledRecord(string Id, string Version);
public sealed record ChocolateyOutdatedRecord(string Id, string InstalledVersion, string AvailableVersion);
public sealed record ChocolateyParseResult<T>(IReadOnlyCollection<T> Records, IReadOnlyCollection<string> Warnings);

/// <summary>Reads only the local Chocolatey registration list. It does not contact a package source.</summary>
public sealed partial class ChocolateyLocalInventoryProvider : ILocalInventoryProvider
{
    private readonly Func<bool> enabled;
    private readonly IChocolateyProcess process;

    public ChocolateyLocalInventoryProvider(Func<bool> enabled) : this(enabled, new WindowsChocolateyProcess()) { }
    internal ChocolateyLocalInventoryProvider(Func<bool> enabled, IChocolateyProcess process)
    {
        this.enabled = enabled ?? throw new ArgumentNullException(nameof(enabled));
        this.process = process ?? throw new ArgumentNullException(nameof(process));
    }

    public string Name => "Chocolatey installed inventory";
    public TimeSpan Timeout => TimeSpan.FromSeconds(35);
    public static IReadOnlyList<string> BuildInstalledListArguments() => ["list", "--limit-output", "--no-color"];

    public async Task<ProviderScanResult> ScanAsync(CancellationToken cancellationToken)
    {
        if (!enabled()) return new([], [], [], ["disabled"]);
        ChocolateyProcessResult result;
        try { result = await process.RunAsync(BuildInstalledListArguments(), Timeout, cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) { return new([], [], [], [FailureWarning(exception)]); }
        ChocolateyParseResult<ChocolateyInstalledRecord> parsed = ParseInstalled(result);
        List<CanonicalProduct> products = [];
        List<InstallationInstance> instances = [];
        List<RawProviderRecord> raw = [];
        foreach (ChocolateyInstalledRecord record in parsed.Records)
        {
            Guid productId = StableGuid("product|chocolatey|" + record.Id);
            products.Add(new(productId, "chocolatey:unknown:" + record.Id, record.Id));
            // Chocolatey's local list has no reliable per-package source provenance. Do not infer
            // Community origin from an installed ID; a user-confirmed exact mapping is required.
            instances.Add(new(StableGuid("instance|chocolatey|" + record.Id + "|" + record.Version), productId, "chocolatey", record.Id, record.Version, "Machine", "Neutral", Classification: "Application", SourceId: null));
            raw.Add(new(Guid.NewGuid(), Guid.Empty, "chocolatey", record.Id, $"id={record.Id};version={record.Version};source=unknown;compatibility=manual-community-mapping-required"));
        }
        return new(products, instances, raw, parsed.Warnings);
    }

    internal static ChocolateyParseResult<ChocolateyInstalledRecord> ParseInstalled(ChocolateyProcessResult result)
    {
        if (result.OutputLimitExceeded || result.StandardOutput.Length > ChocolateyUpdateSourceConnector.MaximumOutputLength) return new([], ["chocolatey:output-too-large"]);
        if (result.TimedOut) return new([], ["chocolatey:timeout"]);
        if (result.ExitCode != 0) return new([], [$"chocolatey:exit-{result.ExitCode}"]);
        List<ChocolateyInstalledRecord> records = [];
        List<string> warnings = [];
        foreach (string line in Lines(result.StandardOutput))
        {
            string[] columns = line.Split('|');
            if (columns.Length != 2 || !ValidPackageId(columns[0]) || !ValidVersion(columns[1])) { warnings.Add("chocolatey:malformed-local-row"); continue; }
            records.Add(new(columns[0], columns[1]));
        }
        return new(records.GroupBy(record => record.Id, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() == 1).Select(group => group.Single()).ToArray(), warnings.Distinct(StringComparer.Ordinal).ToArray());
    }

    private static string FailureWarning(Exception exception) => exception switch
    {
        UnauthorizedAccessException => "chocolatey:executable-access-denied",
        IOException => "chocolatey:executable-unavailable",
        Win32Exception => "chocolatey:executable-launch-failed",
        _ => "chocolatey:runtime-error"
    };
    private static Guid StableGuid(string value) => new(SHA256.HashData(Encoding.UTF8.GetBytes(value)).AsSpan(0, 16));
    internal static IEnumerable<string> Lines(string value) => value.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.Trim());
    internal static bool ValidPackageId(string value) => value.Length is > 0 and <= 256 && PackageId().IsMatch(value);
    internal static bool ValidVersion(string value) => value.Length is > 0 and <= 128 && Version().IsMatch(value);
    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9.-]{0,255}$", RegexOptions.CultureInvariant)] private static partial Regex PackageId();
    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._+-]{0,127}$", RegexOptions.CultureInvariant)] private static partial Regex Version();
}

/// <summary>Opt-in fixed-community-feed discovery. Chocolatey results remain review-only; package scripts are never execution authority.</summary>
public sealed class ChocolateyUpdateSourceConnector : IUpdateSourceConnector, IStructuredUpdateCandidateAuthority
{
    public const string CommunitySourceId = "chocolatey-community";
    public const string CommunitySourceUrl = "https://community.chocolatey.org/api/v2/";
    internal const int MaximumOutputLength = 1_000_000;
    private readonly IChocolateyProcess process;

    public ChocolateyUpdateSourceConnector() : this(new WindowsChocolateyProcess()) { }
    internal ChocolateyUpdateSourceConnector(IChocolateyProcess process) => this.process = process ?? throw new ArgumentNullException(nameof(process));
    public string SourceId => CommunitySourceId;
    public TimeSpan Timeout => TimeSpan.FromSeconds(45);
    public static IReadOnlyList<string> BuildOutdatedArguments() => ["outdated", "--source", CommunitySourceUrl, "--ignore-unfound", "--limit-output", "--no-color", "--execution-timeout", "60"];

    public async Task<SourceSnapshot> RefreshAsync(CancellationToken cancellationToken)
    {
        ChocolateyProcessResult version = await process.RunAsync(["--version", "--no-color"], Timeout, cancellationToken).ConfigureAwait(false);
        ChocolateyProcessResult outdated = await process.RunAsync(BuildOutdatedArguments(), Timeout, cancellationToken).ConfigureAwait(false);
        ChocolateyParseResult<ChocolateyOutdatedRecord> parsed = ParseOutdated(outdated);
        if (parsed.Warnings.Any(warning => warning is "chocolatey:timeout" or "chocolatey:output-too-large" || warning.StartsWith("chocolatey:exit-", StringComparison.Ordinal))) throw new InvalidOperationException(string.Join(',', parsed.Warnings));
        SourcePackageRecord[] packages = parsed.Records.Select(record => new SourcePackageRecord(CommunitySourceId, record.Id, record.Id, null, record.InstalledVersion, record.AvailableVersion, "Machine", "Neutral", null)).ToArray();
        string? providerVersion = version.ExitCode == 0 && !version.TimedOut && !version.OutputLimitExceeded && ChocolateyLocalInventoryProvider.ValidVersion(version.StandardOutput.Trim()) ? version.StandardOutput.Trim() : null;
        return new(CommunitySourceId, providerVersion, DateTimeOffset.UtcNow, SourceSnapshotState.Live, packages, parsed.Warnings);
    }

    public async Task<StructuredUpdateCandidateEvidence?> ResolveAsync(string sourceId, string packageId, CancellationToken cancellationToken)
    {
        if (!string.Equals(sourceId, CommunitySourceId, StringComparison.OrdinalIgnoreCase) || !ChocolateyLocalInventoryProvider.ValidPackageId(packageId)) return null;
        ChocolateyProcessResult result;
        try { result = await process.RunAsync(BuildOutdatedArguments(), Timeout, cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return null; }
        ChocolateyParseResult<ChocolateyOutdatedRecord> parsed = ParseOutdated(result);
        if (parsed.Warnings.Count != 0) return null;
        ChocolateyOutdatedRecord[] matches = parsed.Records.Where(record => string.Equals(record.Id, packageId, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length != 1 || VersionComparison.Compare(new(matches[0].InstalledVersion, matches[0].AvailableVersion, false)).Outcome != VersionComparisonOutcome.Newer) return null;
        return new(CommunitySourceId, matches[0].Id, matches[0].InstalledVersion, matches[0].AvailableVersion);
    }

    internal static ChocolateyParseResult<ChocolateyOutdatedRecord> ParseOutdated(ChocolateyProcessResult result)
    {
        if (result.OutputLimitExceeded || result.StandardOutput.Length > MaximumOutputLength) return new([], ["chocolatey:output-too-large"]);
        if (result.TimedOut) return new([], ["chocolatey:timeout"]);
        if (result.ExitCode != 0) return new([], [$"chocolatey:exit-{result.ExitCode}"]);
        List<ChocolateyOutdatedRecord> records = [];
        List<string> warnings = [];
        foreach (string line in ChocolateyLocalInventoryProvider.Lines(result.StandardOutput))
        {
            string[] columns = line.Split('|');
            bool pinnedColumnValid = columns.Length == 3 || columns.Length == 4 && (columns[3].Equals("true", StringComparison.OrdinalIgnoreCase) || columns[3].Equals("false", StringComparison.OrdinalIgnoreCase));
            if (!pinnedColumnValid || !ChocolateyLocalInventoryProvider.ValidPackageId(columns[0]) || !ChocolateyLocalInventoryProvider.ValidVersion(columns[1]) || !ChocolateyLocalInventoryProvider.ValidVersion(columns[2])) { warnings.Add("chocolatey:malformed-outdated-row"); continue; }
            records.Add(new(columns[0], columns[1], columns[2]));
        }
        return new(records.GroupBy(record => record.Id, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() == 1).Select(group => group.Single()).ToArray(), warnings.Distinct(StringComparer.Ordinal).ToArray());
    }
}

/// <summary>Provider-owned machine-scope operation for an immutable Chocolatey Community update.</summary>
public sealed class ChocolateyMachineUpdateExecutor : IProtectedMachineUpdateExecutor
{
    private static readonly TimeSpan UpdateTimeout = TimeSpan.FromMinutes(10);
    private readonly IChocolateyProcess process;
    public ChocolateyMachineUpdateExecutor() : this(new WindowsChocolateyProcess()) { }
    internal ChocolateyMachineUpdateExecutor(IChocolateyProcess process) => this.process = process ?? throw new ArgumentNullException(nameof(process));
    public Task<ChocolateyProcessResult> ExecuteAsync(ImmutableUpdateJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        return process.RunAsync(BuildUpdateArguments(job), UpdateTimeout, cancellationToken);
    }
    async Task<ProviderProcessResult> IProtectedMachineUpdateExecutor.ExecuteProtectedAsync(ImmutableUpdateJob job, CancellationToken cancellationToken)
    {
        ChocolateyProcessResult result = await ExecuteAsync(job, cancellationToken).ConfigureAwait(false);
        return new(result.ExitCode, result.StandardOutput, result.StandardError, result.TimedOut, result.OutputLimitExceeded);
    }
    public static IReadOnlyList<string> BuildUpdateArguments(ImmutableUpdateJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        if (!job.HasValidDigest) throw new ArgumentException("The update job failed its integrity check.", nameof(job));
        if (job.Selection.Provider != UpdateProvider.Chocolatey || job.Selection.Source != UpdateSource.ChocolateyCommunity) throw new ArgumentException("Only Chocolatey Community jobs are supported.", nameof(job));
        if (job.Selection.Scope != UpdateScope.Machine) throw new ArgumentException("Chocolatey updates require the protected machine-scope helper.", nameof(job));
        return ["upgrade", job.Selection.PackageId, "--version", job.Selection.CandidateVersion, "--source", ChocolateyUpdateSourceConnector.CommunitySourceUrl, "--yes", "--limit-output", "--no-color", "--execution-timeout", "300"];
    }
}

/// <summary>Re-inventories one exact Chocolatey registration after the provider exits; display rows are never success authority.</summary>
public sealed class ChocolateyPostUpdatePackageInventory : IPostUpdatePackageInventory
{
    private readonly IChocolateyProcess process;
    public ChocolateyPostUpdatePackageInventory() : this(new WindowsChocolateyProcess()) { }
    internal ChocolateyPostUpdatePackageInventory(IChocolateyProcess process) => this.process = process ?? throw new ArgumentNullException(nameof(process));
    public async Task<bool> HasExactExpectedVersionAsync(ImmutableUpdateJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        if (job.Selection.Provider != UpdateProvider.Chocolatey || job.Selection.Source != UpdateSource.ChocolateyCommunity) return false;
        ChocolateyProcessResult result;
        try { result = await process.RunAsync(["list", job.Selection.PackageId, "--exact", "--limit-output", "--no-color"], TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false); }
        catch (Exception) when (!cancellationToken.IsCancellationRequested) { return false; }
        ChocolateyParseResult<ChocolateyInstalledRecord> parsed = ChocolateyLocalInventoryProvider.ParseInstalled(result);
        return parsed.Warnings.Count == 0 && parsed.Records.Count(record => string.Equals(record.Id, job.Selection.PackageId, StringComparison.OrdinalIgnoreCase) && string.Equals(record.Version, job.Selection.CandidateVersion, StringComparison.Ordinal)) == 1;
    }
}

internal sealed class WindowsChocolateyProcess : IChocolateyProcess
{
    private const int MaximumCapturedOutputLength = 1_000_000;
    public async Task<ChocolateyProcessResult> RunAsync(IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ProcessStartInfo start = new() { FileName = MachineTrustedChocolatey.ResolveExecutable(), UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using Process process = new() { StartInfo = start };
        process.Start();
        TaskCompletionSource overflow = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<BoundedRead> output = ReadBoundedAsync(process.StandardOutput, overflow); Task<BoundedRead> error = ReadBoundedAsync(process.StandardError, overflow); Task exit = process.WaitForExitAsync(CancellationToken.None);
        Task cancellation = Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); Task delay = Task.Delay(timeout, CancellationToken.None); Task completed = await Task.WhenAny(exit, delay, cancellation, overflow.Task).ConfigureAwait(false);
        if (completed == cancellation) { Terminate(process); await exit.ConfigureAwait(false); await Task.WhenAll(output, error).ConfigureAwait(false); cancellationToken.ThrowIfCancellationRequested(); }
        if (completed == overflow.Task) { Terminate(process); await exit.ConfigureAwait(false); BoundedRead stdout = await output.ConfigureAwait(false); BoundedRead stderr = await error.ConfigureAwait(false); return new(-1, stdout.Text, stderr.Text, false, true); }
        if (completed == delay) { Terminate(process); await exit.ConfigureAwait(false); BoundedRead stdout = await output.ConfigureAwait(false); BoundedRead stderr = await error.ConfigureAwait(false); return new(-1, stdout.Text, stderr.Text, true, stdout.ExceededLimit || stderr.ExceededLimit); }
        BoundedRead completedOutput = await output.ConfigureAwait(false); BoundedRead completedError = await error.ConfigureAwait(false); return new(process.ExitCode, completedOutput.Text, completedError.Text, false, completedOutput.ExceededLimit || completedError.ExceededLimit);
    }
    private static async Task<BoundedRead> ReadBoundedAsync(StreamReader reader, TaskCompletionSource overflow)
    {
        StringBuilder captured = new(Math.Min(MaximumCapturedOutputLength, 4096)); char[] buffer = new char[4096]; bool exceeded = false; int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length)).ConfigureAwait(false)) != 0) { int available = MaximumCapturedOutputLength - captured.Length; if (available > 0) captured.Append(buffer, 0, Math.Min(available, read)); if (read > available) { exceeded = true; overflow.TrySetResult(); } }
        return new(captured.ToString(), exceeded);
    }
    private static void Terminate(Process process) { if (!process.HasExited) process.Kill(entireProcessTree: true); }
    private sealed record BoundedRead(string Text, bool ExceededLimit);
}

/// <summary>Accepts only Chocolatey's signed client at the canonical machine root; PATH aliases and reparse points are denied.</summary>
internal static class MachineTrustedChocolatey
{
    private static readonly Guid ActionGenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
    internal static string ResolveExecutable()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Chocolatey resolution requires Windows.");
        string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData, Environment.SpecialFolderOption.DoNotVerify);
        if (string.IsNullOrWhiteSpace(programData) || !Path.IsPathFullyQualified(programData)) throw new IOException("The machine ProgramData root is unavailable.");
        string root = Path.GetFullPath(Path.Combine(programData, "chocolatey")); string executable = Path.Combine(root, "bin", "choco.exe");
        if (!File.Exists(executable)) throw new IOException("Chocolatey is not installed at the protected machine root.");
        RejectReparse(root); RejectReparse(Path.Combine(root, "bin")); RejectReparse(executable);
        if (!IsChocolateyTrusted(executable)) throw new IOException("The Chocolatey client is not signed by Chocolatey Software, Inc.");
        return executable;
    }
    internal static bool IsChocolateyTrusted(string path)
    {
        if (!File.Exists(path) || !VerifyTrust(path)) return false;
        try
        {
            #pragma warning disable SYSLIB0057
            using X509Certificate certificate = X509Certificate.CreateFromSignedFile(path);
            #pragma warning restore SYSLIB0057
            return certificate.Subject.Contains("CN=Chocolatey Software, Inc", StringComparison.OrdinalIgnoreCase);
        }
        catch (CryptographicException) { return false; }
        catch (IOException) { return false; }
    }
    private static void RejectReparse(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("A reparse point is not an accepted Chocolatey path.");
    }
    private static bool VerifyTrust(string path)
    {
        IntPtr pathPointer = Marshal.StringToCoTaskMemUni(path);
        WinTrustFileInfo fileInfo = new() { Size = (uint)Marshal.SizeOf<WinTrustFileInfo>(), FilePath = pathPointer };
        IntPtr fileInfoPointer = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
        try
        {
            Marshal.StructureToPtr(fileInfo, fileInfoPointer, false);
            WinTrustData data = new() { Size = (uint)Marshal.SizeOf<WinTrustData>(), UiChoice = 2, UnionChoice = 1, FileInfo = fileInfoPointer, ProviderFlags = 0x1000 };
            return WinVerifyTrust(IntPtr.Zero, ActionGenericVerifyV2, ref data) == 0;
        }
        finally { Marshal.FreeHGlobal(fileInfoPointer); Marshal.FreeCoTaskMem(pathPointer); }
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct WinTrustFileInfo { public uint Size; public IntPtr FilePath; public IntPtr FileHandle; public IntPtr KnownSubject; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct WinTrustData { public uint Size; public IntPtr PolicyCallbackData; public IntPtr SipClientData; public uint UiChoice; public uint RevocationChecks; public uint UnionChoice; public IntPtr FileInfo; public uint StateAction; public IntPtr StateData; public IntPtr UrlReference; public uint ProviderFlags; public uint UiContext; public IntPtr SignatureSettings; }
    [DllImport("wintrust.dll", SetLastError = true)] private static extern int WinVerifyTrust(IntPtr windowHandle, in Guid actionId, ref WinTrustData trustData);
}
