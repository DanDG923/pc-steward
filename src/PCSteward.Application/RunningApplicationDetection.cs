using System.ComponentModel;
using System.Diagnostics;
using PCSteward.Domain;

namespace PCSteward.Application;

public sealed record RunningApplicationCoverage(
    int InspectedProcessCount,
    int ReadableNameCount,
    int InaccessibleProcessCount,
    int ExitedProcessCount,
    int UnavailableProcessCount,
    int InvalidNameCount,
    bool InspectionLimitReached,
    bool ProcessEnumerationUnavailable = false);

public sealed record RunningApplicationDetection(bool AppearsToBeRunning, IReadOnlyCollection<string> ProcessNames)
{
    public RunningApplicationCoverage? Coverage { get; init; }

    /// <summary>
    /// The matched names seen only in another user's Windows session. An app package is registered per account, so another user's open
    /// copy doesn't stop this account's update from being registered (MSIX design note §3.6). Empty when unknown: a process whose session
    /// can't be read counts as this session's.
    /// </summary>
    public IReadOnlyCollection<string> OnlyInOtherSessions { get; init; } = [];

    /// <summary>The matched names with a process in this app's own Windows session (or whose session couldn't be read).</summary>
    public IReadOnlyList<string> InThisSession =>
        [.. ProcessNames.Where(name => !OnlyInOtherSessions.Contains(name, StringComparer.OrdinalIgnoreCase))];

    /// <summary>True when a matched process is open in this app's own Windows session.</summary>
    public bool OpenInThisSession => AppearsToBeRunning && InThisSession.Count != 0;
    public string CoverageDisclosure =>
        (Coverage is { } coverage
            ? $"Inspected {coverage.InspectedProcessCount} process entries; readable names: {coverage.ReadableNameCount}; inaccessible: {coverage.InaccessibleProcessCount}; exited: {coverage.ExitedProcessCount}; unavailable: {coverage.UnavailableProcessCount}; invalid names: {coverage.InvalidNameCount}. " +
              (coverage.ProcessEnumerationUnavailable ? "Process enumeration was unavailable; coverage could not be established. " : string.Empty) +
              (coverage.InspectionLimitReached ? "The 4,096-entry inspection limit was reached; additional entries may be omitted. " : string.Empty)
            : "Snapshot coverage counts are unavailable. ") +
        "This package-derived exact-name heuristic inspects up to 4,096 process entries and accepts names up to 64 characters. Inaccessible or exited processes, invalid names and saturation can hide a running app; no match does not prove it is closed. An app can start between this check and invocation.";
    public static RunningApplicationDetection NotDetected { get; } = new(false, []);
}

public interface IRunningApplicationDetector
{
    Task<RunningApplicationDetection> DetectAsync(UpdateJobSelection selection, CancellationToken cancellationToken);
}

internal enum RunningProcessObservationKind { Name, Inaccessible, Exited, Unavailable, EnumerationUnavailable }
internal sealed record RunningProcessObservation(RunningProcessObservationKind Kind, string? Name = null, bool OtherSession = false);
internal sealed record RunningApplicationSnapshot(string[] Names, RunningApplicationCoverage Coverage, string[] OtherSessionNames);

/// <summary>Returns a bounded package-derived process-name snapshot and never closes a process.</summary>
public sealed class PackageDerivedRunningApplicationDetector : IRunningApplicationDetector
{
    private const int MaximumProcesses = 4096;
    private const int MaximumProcessNameLength = 64;
    private static readonly HashSet<string> AmbiguousSuffixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "app", "application", "client", "desktop", "installer", "launcher", "setup", "update", "updater"
    };
    private readonly Func<CancellationToken, IEnumerable<RunningProcessObservation>> observations;

    public PackageDerivedRunningApplicationDetector() : this(SnapshotProcesses) { }
    internal PackageDerivedRunningApplicationDetector(Func<IReadOnlyCollection<string>> processNames)
    {
        ArgumentNullException.ThrowIfNull(processNames);
        observations = _ => processNames().Select(name => new RunningProcessObservation(RunningProcessObservationKind.Name, name));
    }
    internal PackageDerivedRunningApplicationDetector(Func<CancellationToken, IEnumerable<RunningProcessObservation>> observations) =>
        this.observations = observations ?? throw new ArgumentNullException(nameof(observations));

    public Task<RunningApplicationDetection> DetectAsync(UpdateJobSelection selection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selection);
        cancellationToken.ThrowIfCancellationRequested();
        HashSet<string> candidates = new(StringComparer.OrdinalIgnoreCase);
        AddCandidate(candidates, selection.PackageId);
        string suffix = selection.PackageId[(selection.PackageId.LastIndexOf('.') + 1)..];
        if (!AmbiguousSuffixes.Contains(suffix)) AddCandidate(candidates, suffix);
        RunningApplicationSnapshot snapshot = Capture(observations(cancellationToken), cancellationToken);
        string[] here = [.. snapshot.Names.Where(candidates.Contains)];
        string[] elsewhere = [.. snapshot.OtherSessionNames.Where(candidates.Contains)];
        string[] matches = here.Concat(elsewhere)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        string[] onlyElsewhere = [.. matches.Where(name => !here.Contains(name, StringComparer.OrdinalIgnoreCase))];
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new RunningApplicationDetection(matches.Length != 0, matches) { Coverage = snapshot.Coverage, OnlyInOtherSessions = onlyElsewhere });
    }

    private static RunningApplicationSnapshot Capture(IEnumerable<RunningProcessObservation> source, CancellationToken cancellationToken)
    {
        int inspected = 0, inaccessible = 0, exited = 0, unavailable = 0, invalid = 0;
        bool enumerationUnavailable = false;
        List<string> names = [], otherSession = [];
        using IEnumerator<RunningProcessObservation> entries = source.GetEnumerator();
        while (inspected < MaximumProcesses)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!entries.MoveNext()) break;
            cancellationToken.ThrowIfCancellationRequested();
            RunningProcessObservation observation = entries.Current;
            if (observation.Kind == RunningProcessObservationKind.EnumerationUnavailable)
            {
                enumerationUnavailable = true;
                break;
            }
            inspected++;
            switch (observation.Kind)
            {
                case RunningProcessObservationKind.Inaccessible: inaccessible++; break;
                case RunningProcessObservationKind.Exited: exited++; break;
                case RunningProcessObservationKind.Name:
                    if (IsValidName(observation.Name)) (observation.OtherSession ? otherSession : names).Add(observation.Name!);
                    else invalid++;
                    break;
                default: unavailable++; break;
            }
        }
        // Do not read a 4,097th entry just to establish whether enumeration is complete.
        return new(names.ToArray(), new(inspected, names.Count + otherSession.Count, inaccessible, exited, unavailable, invalid, inspected == MaximumProcesses, enumerationUnavailable),
            otherSession.ToArray());
    }

    private static bool IsValidName(string? name) => name is { Length: > 0 and <= MaximumProcessNameLength } &&
        name.All(character => char.IsLetterOrDigit(character) || character is '.' or '-' or '_');

    private static void AddCandidate(HashSet<string> candidates, string value)
    {
        if (value.Length >= 4 && IsValidName(value)) candidates.Add(value);
    }

    private static IEnumerable<RunningProcessObservation> SnapshotProcesses(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Process[]? processes = null;
        try
        {
            try { processes = Process.GetProcesses(); }
            catch (Win32Exception) { }
            catch (NotSupportedException) { }
            if (processes is null)
            {
                yield return new(RunningProcessObservationKind.EnumerationUnavailable);
                yield break;
            }
            int session = CurrentSession();
            // GetProcesses materializes OS entries; name reads are bounded, but that native enumeration/allocation is not.
            foreach (Process process in processes.Take(MaximumProcesses))
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return Observe(process, session);
            }
        }
        finally
        {
            // Includes uninspected handles and cancellation/early disposal paths.
            if (processes is not null)
                foreach (Process process in processes) process.Dispose();
        }
    }

    private static RunningProcessObservation Observe(Process process, int session)
    {
        try { return new(RunningProcessObservationKind.Name, process.ProcessName, IsInOtherSession(process, session)); }
        catch (InvalidOperationException) { return new(RunningProcessObservationKind.Exited); }
        catch (Win32Exception) { return new(RunningProcessObservationKind.Inaccessible); }
        catch (NotSupportedException) { return new(RunningProcessObservationKind.Unavailable); }
    }

    /// <summary>True only when both sessions were read and differ; an unreadable session counts as this one (fails closed).</summary>
    private static bool IsInOtherSession(Process process, int session)
    {
        if (session < 0) return false;
        try { return process.SessionId != session; }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or NotSupportedException) { return false; }
    }

    private static int CurrentSession()
    {
        try { using Process current = Process.GetCurrentProcess(); return current.SessionId; }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or NotSupportedException) { return -1; }
    }
}
