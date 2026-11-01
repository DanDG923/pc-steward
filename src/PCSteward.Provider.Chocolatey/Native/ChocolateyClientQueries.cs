namespace PCSteward.Provider.Chocolatey.Native;

/// <summary>
/// How one fixed client query ended, in decision order (§4 result-mapping table). Only <see cref="Parsed"/> carries
/// records. <see cref="Interrupted"/> and <see cref="Unsettled"/> map to Undetermined; every other status is a settled
/// query failure (<c>PreflightQueryFailed</c>).
/// </summary>
internal enum ChocolateyQueryStatus
{
    /// <summary>Exit 0, settled, every record strict and unique (possibly zero records).</summary>
    Parsed,

    /// <summary>The caller cancelled or the launch deadline elapsed.</summary>
    Interrupted,

    /// <summary>The launch job's settlement was not confirmed, or the invocation is unknown.</summary>
    Unsettled,

    /// <summary>No process ran (a definitive pre-creation failure); settled.</summary>
    NotStarted,

    /// <summary>The launcher's 1,000,000-character capture ceiling was exceeded.</summary>
    OutputLimitExceeded,

    /// <summary>More than 1,024 logical records.</summary>
    RecordLimitExceeded,

    /// <summary>A settled launch without an observed exit code or both streams at end of file.</summary>
    Incomplete,

    /// <summary>A settled non-zero exit.</summary>
    ExitFailure,

    /// <summary>A record that is not exactly the strict pinned shape.</summary>
    Malformed,

    /// <summary>Two records for the same package ID (case-insensitive).</summary>
    Duplicate
}

/// <summary>One strict <c>outdated --limit-output</c> record: <c>id|installed|available|pinned</c>, versions normalized.</summary>
internal sealed record ChocolateyNativeOutdatedRecord(string Id, string InstalledVersion, string AvailableVersion, bool Pinned);

/// <summary>
/// The launcher facts of one fixed query plus its strict parse. Private construction; produced only by
/// <see cref="ChocolateyQueryParser"/>. <see cref="Records"/> is empty unless <see cref="Status"/> is
/// <see cref="ChocolateyQueryStatus.Parsed"/>.
/// </summary>
internal sealed record ChocolateyQueryOutcome<T>
{
    private ChocolateyQueryOutcome(NativeProcessObservation observation, ChocolateyQueryStatus status, IReadOnlyList<T> records)
    {
        Observation = observation;
        Status = status;
        Records = records;
    }

    internal NativeProcessObservation Observation { get; }

    internal ChocolateyQueryStatus Status { get; }

    internal IReadOnlyList<T> Records { get; }

    /// <summary>Parser only (an architecture test pins the call site).</summary>
    internal static ChocolateyQueryOutcome<T> Create(NativeProcessObservation observation, ChocolateyQueryStatus status, IReadOnlyList<T> records)
    {
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentNullException.ThrowIfNull(records);
        if (!Enum.IsDefined(status) || status != ChocolateyQueryStatus.Parsed && records.Count != 0)
            throw new ArgumentException("Only a parsed query carries records.", nameof(records));
        return new ChocolateyQueryOutcome<T>(observation, status, Array.AsReadOnly(records.ToArray()));
    }
}

/// <summary>
/// FR-UPD-15, TM-03/04/13; Profile V2 "Installed library, store and preflight agreement" and "Numeric limits": the pure,
/// strict parser for the two fixed stage-C queries. Interrupts and unsettled launches come first (they are never a
/// settled refusal), then the capture ceiling (1,000,000 characters), the exit, and the records: at most 1,024 non-empty
/// logical lines (LF or CRLF), each exactly the pinned column shape, with a supported native package ID, versions in
/// supported normalized form (<see cref="ChocolateyNativeVersion.Normalized"/> equal to the text) and no duplicate ID.
/// Any other line refuses the whole query; nothing is skipped, trimmed, deduplicated or repaired. Standard error is
/// bounded by the launcher and never parsed.
/// </summary>
internal static class ChocolateyQueryParser
{
    internal const int MaximumCharacters = LaunchJob.OutputLimitCharacters;
    internal const int MaximumRecords = 1_024;

    /// <summary><c>list &lt;ID&gt; --exact</c>: zero or one <c>id|version</c> record, and its ID is <paramref name="packageId"/>.</summary>
    internal static ChocolateyQueryOutcome<ChocolateyInstalledRecord> ParseListExact(NativeProcessObservation observation, string packageId)
    {
        ArgumentNullException.ThrowIfNull(observation);
        List<string[]> rows = [];
        if (!ChocolateyNativePackageId.IsSupported(packageId)) throw new ArgumentException("A supported package ID is required.", nameof(packageId));
        if ((Precheck(observation) ?? TrySplit(observation.StandardOutput, out rows)) is { } early)
            return ChocolateyQueryOutcome<ChocolateyInstalledRecord>.Create(observation, early, []);
        List<ChocolateyInstalledRecord> records = [];
        foreach (string[] columns in rows)
        {
            if (columns.Length != 2 || !string.Equals(columns[0], packageId, StringComparison.OrdinalIgnoreCase) ||
                !ChocolateyNativePackageId.IsSupported(columns[0]) || !IsNormalizedVersion(columns[1]))
                return ChocolateyQueryOutcome<ChocolateyInstalledRecord>.Create(observation, ChocolateyQueryStatus.Malformed, []);
            if (records.Count != 0)
                return ChocolateyQueryOutcome<ChocolateyInstalledRecord>.Create(observation, ChocolateyQueryStatus.Duplicate, []);
            records.Add(new ChocolateyInstalledRecord(columns[0], columns[1]));
        }
        return ChocolateyQueryOutcome<ChocolateyInstalledRecord>.Create(observation, ChocolateyQueryStatus.Parsed, records);
    }

    /// <summary><c>outdated</c>: any number (within the bound) of unique <c>id|installed|available|true-or-false</c> records.</summary>
    internal static ChocolateyQueryOutcome<ChocolateyNativeOutdatedRecord> ParseOutdated(NativeProcessObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        List<string[]> rows = [];
        if ((Precheck(observation) ?? TrySplit(observation.StandardOutput, out rows)) is { } early)
            return ChocolateyQueryOutcome<ChocolateyNativeOutdatedRecord>.Create(observation, early, []);
        List<ChocolateyNativeOutdatedRecord> records = [];
        HashSet<string> ids = new(StringComparer.OrdinalIgnoreCase);
        foreach (string[] columns in rows)
        {
            bool? pinned = columns.Length == 4 ? ParsePinned(columns[3]) : null;
            if (pinned is null || !ChocolateyNativePackageId.IsSupported(columns[0]) || !IsNormalizedVersion(columns[1]) ||
                !IsNormalizedVersion(columns[2]))
                return ChocolateyQueryOutcome<ChocolateyNativeOutdatedRecord>.Create(observation, ChocolateyQueryStatus.Malformed, []);
            if (!ids.Add(columns[0]))
                return ChocolateyQueryOutcome<ChocolateyNativeOutdatedRecord>.Create(observation, ChocolateyQueryStatus.Duplicate, []);
            records.Add(new ChocolateyNativeOutdatedRecord(columns[0], columns[1], columns[2], pinned.Value));
        }
        return ChocolateyQueryOutcome<ChocolateyNativeOutdatedRecord>.Create(observation, ChocolateyQueryStatus.Parsed, records);
    }

    /// <summary>Supported native grammar, at most 128 characters, already in normalized form.</summary>
    internal static bool IsNormalizedVersion(string? text) =>
        text is { Length: > 0 and <= 128 } && ChocolateyNativeVersion.TryParse(text, out ChocolateyNativeVersion? version) &&
        string.Equals(version!.Normalized, text, StringComparison.Ordinal);

    private static ChocolateyQueryStatus? Precheck(NativeProcessObservation observation)
    {
        if (observation.Cancelled || observation.TimedOut) return ChocolateyQueryStatus.Interrupted;
        if (!observation.AggregateSettled || observation.Invocation == NativeInvocation.Unknown) return ChocolateyQueryStatus.Unsettled;
        if (observation.Invocation != NativeInvocation.Started) return ChocolateyQueryStatus.NotStarted;
        if (observation.OutputLimitExceeded || observation.StandardOutput is null || observation.StandardError is null ||
            observation.StandardOutput.Length + observation.StandardError.Length > MaximumCharacters)
            return ChocolateyQueryStatus.OutputLimitExceeded;
        if (!observation.DirectExitObserved || observation.ExitCode is null || !observation.OwnedIoCompleted || !observation.PipeEofObserved)
            return ChocolateyQueryStatus.Incomplete;
        return observation.ExitCode == 0 ? null : ChocolateyQueryStatus.ExitFailure;
    }

    /// <summary>Non-empty logical lines split on <c>|</c>; a lone CR or any other control character is malformed.</summary>
    private static ChocolateyQueryStatus? TrySplit(string output, out List<string[]> rows)
    {
        rows = [];
        foreach (string line in output.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (line.Length == 0) continue;
            if (rows.Count == MaximumRecords) return ChocolateyQueryStatus.RecordLimitExceeded;
            if (line.Any(char.IsControl)) return ChocolateyQueryStatus.Malformed;
            rows.Add(line.Split('|'));
        }
        return null;
    }

    private static bool? ParsePinned(string value) =>
        value.Equals("true", StringComparison.OrdinalIgnoreCase) ? true :
        value.Equals("false", StringComparison.OrdinalIgnoreCase) ? false : null;
}

/// <summary>
/// FR-UPD-15, TM-03/04/07/13; Task 50 design rev 6 §4: the three fixed client launches through the held root-engine
/// binding. Each call creates its own job through <see cref="LaunchJob.RunAsync"/> (its only callers are these three
/// methods); arguments come only from the sealed <see cref="ChocolateyCompiledCommand"/> and the launch deadline is the
/// command's existing budget capped by <c>remaining</c>. The queries parse strictly; the upgrade returns the launcher's
/// wire facts unchanged. Reached only through the helper item (§5.4, N4); nothing outside <c>Native/</c> calls them.
/// </summary>
internal static class ChocolateyClientQueries
{
    internal static async Task<ChocolateyQueryOutcome<ChocolateyInstalledRecord>> ListExactAsync(ChocolateyHeldClientBinding client,
        ChocolateyCompiledCommand.ListExact command, TimeSpan remaining, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(command);
        NativeProcessObservation observation = await LaunchJob.RunAsync(client, command, remaining, cancellationToken).ConfigureAwait(false);
        return ChocolateyQueryParser.ParseListExact(observation, command.PackageId);
    }

    internal static async Task<ChocolateyQueryOutcome<ChocolateyNativeOutdatedRecord>> OutdatedAsync(ChocolateyHeldClientBinding client,
        ChocolateyCompiledCommand.Outdated command, TimeSpan remaining, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(command);
        NativeProcessObservation observation = await LaunchJob.RunAsync(client, command, remaining, cancellationToken).ConfigureAwait(false);
        return ChocolateyQueryParser.ParseOutdated(observation);
    }

    internal static Task<NativeProcessObservation> UpgradeAsync(ChocolateyHeldClientBinding client,
        ChocolateyCompiledCommand.Upgrade command, TimeSpan remaining, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(command);
        return LaunchJob.RunAsync(client, command, remaining, cancellationToken);
    }
}
