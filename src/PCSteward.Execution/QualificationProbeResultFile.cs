using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace PCSteward.Execution;

/// <summary>
/// FR-UPD-15, TM-03/04/07/13; qualification probe spec rev 4 §5.3: the closed result document, schema
/// <c>pc-steward-qualification-probe-result:2</c>, compact JSON in the spec's exact property order. It holds the desktop's own
/// outcome and transport facts, the desktop's <c>TryHold</c> statuses, the pins (once step 6 held) and fields 3–16 of an accepted
/// response (no nonce). It holds no path, user name, SID, fingerprint value, nonce or pipe name: the fingerprint appears only
/// as <c>fingerprintPresent</c> and <c>fingerprintLength</c>. <c>Create</c> throws unless the document is closed and
/// consistent, so an inconsistent run writes nothing.
/// </summary>
internal static class QualificationProbeResultDocument
{
    internal const string Schema = "pc-steward-qualification-probe-result:2";

    /// <summary>§5.3a bound: the largest valid result file is at most 4,096 B (a writer test pins it).</summary>
    internal const int MaximumBytes = 4096;

    internal const string TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

    /// <summary>§5.3: the top-level properties, in order.</summary>
    internal static readonly IReadOnlyList<string> Fields = Array.AsReadOnly(new[]
    {
        "schema", "desktopOutcome", "capabilityStatus", "initialLaunchTrust", "finalLaunchTrust", "helperProcessCreated", "pipeConnected",
        "desktopPeerVerified", "helperFileIdEqual", "responseFrameReceived", "pipeEofObserved", "helperExitObserved",
        "bindingHeldUntilExit", "terminationFailed", "helperExitCode", "pins", "probe", "startedAtUtc", "finishedAtUtc",
    });

    /// <summary>§5.3 <c>pins</c>, in order.</summary>
    internal static readonly IReadOnlyList<string> PinFields = Array.AsReadOnly(new[]
    {
        "ledgerSha256", "capabilityFileSha256", "desktopImageSha256", "helperImageSha256", "signerThumbprint", "signerNotAfterUtc",
    });

    /// <summary>§5.3 <c>probe</c>: response fields 3–16, in order.</summary>
    internal static readonly IReadOnlyList<string> ProbeFields = Array.AsReadOnly(new[]
    {
        "tokenVerdict", "g1Status", "observation", "failedStep", "failedCheck", "failedAccess", "refusalReason", "refusalDetail",
        "nodesEvaluated", "ancestorsEvaluated", "observedClientVersion", "fingerprintPresent", "fingerprintLength", "diagnostic",
    });

    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Indented = false,
        SkipValidation = false
    };

    // Outcomes that end before step 6: no TryHold ran, so every status is NotEvaluated and there are no pins.
    private static readonly QualificationProbeDesktopOutcome[] BeforeHold =
    [
        QualificationProbeDesktopOutcome.GateBusy, QualificationProbeDesktopOutcome.GateUnavailable, QualificationProbeDesktopOutcome.GateRecoveryRequired,
        QualificationProbeDesktopOutcome.GateResetPending, QualificationProbeDesktopOutcome.DesktopNotLimited, QualificationProbeDesktopOutcome.ConflictingProcess,
    ];

    internal static byte[] Create(QualificationProbeRun run, DateTimeOffset startedAtUtc, DateTimeOffset finishedAtUtc) =>
        Create(run, startedAtUtc, finishedAtUtc, QualificationProbeContract.Compiled);

    internal static byte[] Create(QualificationProbeRun run, DateTimeOffset startedAtUtc, DateTimeOffset finishedAtUtc, QualificationProbeContract contract)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(contract);
        string? violation = Violation(run, startedAtUtc, finishedAtUtc, contract);
        if (violation is not null) throw new ArgumentException($"The probe result is not a closed result document ({violation}).", nameof(run));
        byte[] document = Encode(run, startedAtUtc, finishedAtUtc);
        return document.Length <= MaximumBytes ? document : throw new ArgumentException("The probe result exceeds 4,096 B.", nameof(run));
    }

    /// <summary>The first broken rule, or null when the run is a closed, consistent document.</summary>
    internal static string? Violation(QualificationProbeRun run, DateTimeOffset startedAtUtc, DateTimeOffset finishedAtUtc, QualificationProbeContract contract)
    {
        QualificationProbeDesktopOutcome outcome = run.Outcome;
        if (!Enum.IsDefined(outcome)) return "outcome";
        IReadOnlyList<QualificationProbeCapabilityEntry> statuses = run.CapabilityStatus ?? [];
        if (!statuses.Select(entry => entry.Token).SequenceEqual(contract.G1Keys, StringComparer.Ordinal) ||
            !statuses.All(entry => Enum.IsDefined(entry.Status)))
            return "capabilityStatus";
        bool allNotEvaluated = statuses.All(entry => entry.Status == QualificationProbeCapabilityStatus.NotEvaluated);
        bool allSupported = statuses.All(entry => entry.Status == QualificationProbeCapabilityStatus.Supported);
        bool beforeHold = BeforeHold.Contains(outcome) || (outcome == QualificationProbeDesktopOutcome.LaunchTrustFailed && run.Pins is null);
        if (beforeHold ? !allNotEvaluated || run.Pins is not null
            : outcome == QualificationProbeDesktopOutcome.CapabilityNotHeld ? statuses.Any(entry => entry.Status == QualificationProbeCapabilityStatus.NotEvaluated) || run.Pins is not null
            : !allSupported || run.Pins is null)
            return "capabilityStatus/pins";
        if (run.Pins is { } pins && !(IsHex(pins.LedgerSha256, 64) && IsHex(pins.CapabilityFileSha256, 64) && IsHex(pins.DesktopImageSha256, 64) &&
                IsHex(pins.HelperImageSha256, 64) && IsHex(pins.SignerThumbprint, 40) && pins.SignerNotAfterUtc.Offset == TimeSpan.Zero))
            return "pins";
        if ((run.Probe is not null) != (outcome == QualificationProbeDesktopOutcome.ResponseAccepted)) return "probe";
        if (run.Probe is { } probe && QualificationProbeWireCodec.RuleViolations(new string('0', 64), probe, contract).Count != 0) return "probe";
        if (run.Transport is null) return "transport";
        if (startedAtUtc.Offset != TimeSpan.Zero || finishedAtUtc.Offset != TimeSpan.Zero || finishedAtUtc < startedAtUtc) return "timestamps";
        return null;
    }

    private static byte[] Encode(QualificationProbeRun run, DateTimeOffset startedAtUtc, DateTimeOffset finishedAtUtc)
    {
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream, WriterOptions))
        {
            QualificationProbeTransportFacts t = run.Transport;
            writer.WriteStartObject();
            writer.WriteString("schema", Schema);
            writer.WriteString("desktopOutcome", run.Outcome.ToString());
            writer.WriteStartObject("capabilityStatus");
            foreach (QualificationProbeCapabilityEntry entry in run.CapabilityStatus) writer.WriteString(entry.Token, entry.Status.ToString());
            writer.WriteEndObject();
            writer.WriteBoolean("initialLaunchTrust", t.InitialLaunchTrust);
            writer.WriteBoolean("finalLaunchTrust", t.FinalLaunchTrust);
            writer.WriteBoolean("helperProcessCreated", t.HelperProcessCreated);
            writer.WriteBoolean("pipeConnected", t.PipeConnected);
            writer.WriteBoolean("desktopPeerVerified", t.DesktopPeerVerified);
            writer.WriteBoolean("helperFileIdEqual", t.HelperFileIdEqual);
            writer.WriteBoolean("responseFrameReceived", t.ResponseFrameReceived);
            writer.WriteBoolean("pipeEofObserved", t.PipeEofObserved);
            writer.WriteBoolean("helperExitObserved", t.HelperExitObserved);
            writer.WriteBoolean("bindingHeldUntilExit", t.BindingHeldUntilExit);
            writer.WriteBoolean("terminationFailed", t.TerminationFailed);
            if (t.HelperExitCode is int code) writer.WriteNumber("helperExitCode", code);
            else writer.WriteNull("helperExitCode");
            if (run.Pins is not { } pins) writer.WriteNull("pins");
            else
            {
                writer.WriteStartObject("pins");
                writer.WriteString("ledgerSha256", pins.LedgerSha256);
                writer.WriteString("capabilityFileSha256", pins.CapabilityFileSha256);
                writer.WriteString("desktopImageSha256", pins.DesktopImageSha256);
                writer.WriteString("helperImageSha256", pins.HelperImageSha256);
                writer.WriteString("signerThumbprint", pins.SignerThumbprint);
                writer.WriteString("signerNotAfterUtc", Timestamp(pins.SignerNotAfterUtc));
                writer.WriteEndObject();
            }
            if (run.Probe is not { } facts) writer.WriteNull("probe");
            else
            {
                writer.WriteStartObject("probe");
                writer.WriteString("tokenVerdict", facts.Verdict.ToString());
                writer.WriteStartObject("g1Status");
                foreach (QualificationProbeCapabilityEntry entry in facts.G1Status) writer.WriteString(entry.Token, entry.Status.ToString());
                writer.WriteEndObject();
                writer.WriteString("observation", facts.Observation.ToString());
                if (facts.FailedStep is int step) writer.WriteNumber("failedStep", step);
                else writer.WriteNull("failedStep");
                writer.WriteString("failedCheck", facts.FailedCheck.ToString());
                writer.WriteString("failedAccess", facts.FailedAccess.ToString());
                writer.WriteString("refusalReason", facts.RefusalReason.ToString());
                writer.WriteString("refusalDetail", facts.RefusalDetail.ToString());
                writer.WriteNumber("nodesEvaluated", facts.NodesEvaluated);
                writer.WriteNumber("ancestorsEvaluated", facts.AncestorsEvaluated);
                if (facts.ObservedClientVersion is null) writer.WriteNull("observedClientVersion");
                else writer.WriteString("observedClientVersion", facts.ObservedClientVersion);
                writer.WriteBoolean("fingerprintPresent", facts.FingerprintPresent);
                writer.WriteNumber("fingerprintLength", facts.FingerprintLength);
                writer.WriteString("diagnostic", facts.Diagnostic);
                writer.WriteEndObject();
            }
            writer.WriteString("startedAtUtc", Timestamp(startedAtUtc));
            writer.WriteString("finishedAtUtc", Timestamp(finishedAtUtc));
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }

    internal static string Timestamp(DateTimeOffset value) => value.UtcDateTime.ToString(TimestampFormat, CultureInfo.InvariantCulture);

    private static bool IsHex(string? value, int length) =>
        value is not null && value.Length == length && value.All(static c => c is >= '0' and <= '9' or >= 'A' and <= 'F');
}

/// <summary>One existing path component as seen through a no-follow open: whether it is a directory or reparse point, and its final path.</summary>
internal sealed record QualificationResultNode(bool IsDirectory, bool IsReparsePoint, string FinalPath);

/// <summary>The held result folder (§5.3): a no-follow, no-delete-share directory handle.</summary>
internal interface IQualificationResultFolder : IDisposable
{
    /// <summary>The canonical folder path the handle was opened at.</summary>
    string Path { get; }

    QualificationResultNode Node { get; }

    /// <summary>
    /// Creates <paramref name="leaf"/> in the folder with <c>CREATE_NEW</c> (never overwrites), checks the new file's final
    /// path and reparse attribute, then writes <paramref name="content"/> once in full, flushes and closes. False on any failure.
    /// </summary>
    bool TryWriteNew(string leaf, byte[] content);
}

/// <summary>The file-system primitives of the result writer. Production is <see cref="WindowsQualificationResultFileSystem"/>.</summary>
internal interface IQualificationResultFileSystem
{
    /// <summary><c>GetLongPathNameW</c>: the long form with 8.3 names expanded; null on failure.</summary>
    string? GetLongPath(string path);

    /// <summary>Opens an existing component with <c>FILE_FLAG_OPEN_REPARSE_POINT</c> (no follow); null when it cannot be opened.</summary>
    QualificationResultNode? Inspect(string path);

    /// <summary><c>CreateDirectoryW</c>: false when the folder exists or cannot be created (there is no reuse or merge).</summary>
    bool TryCreateDirectory(string path);

    /// <summary>Opens and holds the folder just created (no follow, no delete share); null on failure.</summary>
    IQualificationResultFolder? OpenFolder(string path);
}

/// <summary>
/// FR-UPD-15; probe spec rev 4 §5.3 location rules, platform-independent so every rule is testable: the result folder is
/// <c>&lt;temp&gt;\pcsteward-exact-artifact</c> and the file <c>qualification-result.json</c>. The temp path must be a fully
/// qualified local drive path (no UNC or device prefix, no relative or dot segment). It is canonicalised to its long form, and
/// every existing component from the drive root must open without following a reparse point, must be a directory without a
/// reparse attribute, and must report a final path equal to the canonical path. An existing folder refuses. The desktop never
/// deletes or overwrites anything.
/// </summary>
internal static class QualificationResultFile
{
    internal const string FolderName = "pcsteward-exact-artifact";
    internal const string FileName = "qualification-result.json";

    /// <summary>§5.2 step 1: creates and holds the fresh folder; null means no result file can be written.</summary>
    internal static IQualificationResultFolder? TryCreateFolder(string? tempPath, IQualificationResultFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        if (!TryParseLocal(tempPath, out _)) return null;
        string? longPath = fileSystem.GetLongPath(tempPath!);
        if (!TryParseLocal(longPath, out string[] segments)) return null;
        string canonical = string.Empty;
        foreach (string component in Components(segments))
        {
            QualificationResultNode? node = fileSystem.Inspect(component);
            if (node is null || !node.IsDirectory || node.IsReparsePoint || !SamePath(node.FinalPath, component)) return null;
            canonical = component;
        }
        string folder = Join(canonical, FolderName);
        if (!fileSystem.TryCreateDirectory(folder)) return null;
        IQualificationResultFolder? held = fileSystem.OpenFolder(folder);
        if (held is null) return null;
        if (!held.Node.IsDirectory || held.Node.IsReparsePoint || !SamePath(held.Node.FinalPath, folder) || !SamePath(held.Path, folder))
        {
            held.Dispose();
            return null;
        }
        return held;
    }

    /// <summary>Writes the result document once into the held folder.</summary>
    internal static bool TryWrite(IQualificationResultFolder folder, byte[] document)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(document);
        return document.Length is > 0 and <= QualificationProbeResultDocument.MaximumBytes && folder.TryWriteNew(FileName, document);
    }

    /// <summary>
    /// A fully qualified local drive path: <c>X:\</c> followed by segments separated by single backslashes, with no forward
    /// slash, no UNC or device prefix, no empty, <c>.</c> or <c>..</c> segment and no character that is invalid in a name.
    /// </summary>
    internal static bool TryParseLocal(string? path, out string[] segments)
    {
        segments = [];
        if (string.IsNullOrEmpty(path) || path.Length < 3 || path.Length > 32_000 || !char.IsAsciiLetter(path[0]) || path[1] != ':' || path[2] != '\\')
            return false;
        string rest = path[3..].TrimEnd('\\');
        if (path[3..].Length - rest.Length > 1) return false;
        List<string> parts = [path[..2] + "\\"];
        if (rest.Length > 0)
        {
            foreach (string segment in rest.Split('\\'))
            {
                if (segment.Length == 0 || segment is "." or ".." || segment.EndsWith(' ') || segment.EndsWith('.') ||
                    segment.Any(static c => c < 0x20 || c is '/' or ':' or '*' or '?' or '"' or '<' or '>' or '|'))
                    return false;
                parts.Add(segment);
            }
        }
        segments = [.. parts];
        return true;
    }

    /// <summary>The drive root, then each longer prefix: <c>C:\</c>, <c>C:\Users</c>, <c>C:\Users\me</c>, …</summary>
    internal static IEnumerable<string> Components(IReadOnlyList<string> segments)
    {
        string current = segments[0];
        yield return current;
        for (int index = 1; index < segments.Count; index++)
        {
            current = Join(current, segments[index]);
            yield return current;
        }
    }

    internal static string Join(string parent, string leaf) => parent.EndsWith('\\') ? parent + leaf : parent + "\\" + leaf;

    internal static bool SamePath(string? first, string? second) =>
        first is not null && second is not null && string.Equals(first.TrimEnd('\\'), second.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) &&
        first.Length > 0;
}

/// <summary>
/// The Windows result-file primitives (§5.3): <c>GetLongPathNameW</c>; component opens with <c>FILE_FLAG_BACKUP_SEMANTICS |
/// FILE_FLAG_OPEN_REPARSE_POINT</c> and <c>FILE_READ_ATTRIBUTES</c> only; <c>CreateDirectoryW</c>; a held folder handle without
/// delete sharing; and a <c>CREATE_NEW</c> file that is checked before it is written. Final paths come from
/// <see cref="DeploymentFileAccess.TryGetFinalPath"/>, which refuses UNC results. Every failure is a refusal.
/// </summary>
internal sealed class WindowsQualificationResultFileSystem : IQualificationResultFileSystem
{
    internal static WindowsQualificationResultFileSystem Instance { get; } = new();

    private WindowsQualificationResultFileSystem()
    {
    }

    public string? GetLongPath(string path)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrEmpty(path)) throw new PlatformNotSupportedException("The result file is written on Windows only.");
        char[] buffer = new char[32768];
        uint length = Native.GetLongPathName(path, buffer, (uint)buffer.Length);
        return length == 0 || length >= buffer.Length ? null : new string(buffer, 0, (int)length);
    }

    public QualificationResultNode? Inspect(string path)
    {
        using SafeFileHandle handle = Open(path, Native.FileReadAttributes, Native.ShareAll, Native.OpenExisting, Native.BackupSemantics | Native.OpenReparsePoint);
        return handle.IsInvalid ? null : Describe(handle);
    }

    public bool TryCreateDirectory(string path)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("The result file is written on Windows only.");
        return Native.CreateDirectory(path, IntPtr.Zero);
    }

    public IQualificationResultFolder? OpenFolder(string path)
    {
        SafeFileHandle handle = Open(path, Native.FileListDirectory | Native.FileReadAttributes, Native.FileShareRead | Native.FileShareWrite,
            Native.OpenExisting, Native.BackupSemantics | Native.OpenReparsePoint);
        QualificationResultNode? node = handle.IsInvalid ? null : Describe(handle);
        if (node is null)
        {
            handle.Dispose();
            return null;
        }
        return new HeldFolder(handle, path, node);
    }

    private static SafeFileHandle Open(string path, uint access, uint share, uint disposition, uint flags)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("The result file is written on Windows only.");
        return Native.CreateFile(path, access, share, IntPtr.Zero, disposition, flags, IntPtr.Zero);
    }

    private static QualificationResultNode? Describe(SafeFileHandle handle)
    {
        if (!Native.GetFileInformationByHandle(handle, out Native.ByHandleFileInformation information) ||
            !DeploymentFileAccess.TryGetFinalPath(handle, out string finalPath))
            return null;
        FileAttributes attributes = (FileAttributes)information.FileAttributes;
        return new((attributes & FileAttributes.Directory) != 0, (attributes & FileAttributes.ReparsePoint) != 0, finalPath);
    }

    private sealed class HeldFolder(SafeFileHandle handle, string path, QualificationResultNode node) : IQualificationResultFolder
    {
        public string Path { get; } = path;
        public QualificationResultNode Node { get; } = node;

        public bool TryWriteNew(string leaf, byte[] content)
        {
            if (handle.IsClosed || string.IsNullOrEmpty(leaf) || leaf.Contains('\\') || leaf.Contains('/')) return false;
            string target = QualificationResultFile.Join(Path, leaf);
            using SafeFileHandle file = Open(target, Native.GenericWrite, 0, Native.CreateNew, Native.FileAttributeNormal | Native.OpenReparsePoint);
            if (file.IsInvalid) return false;
            QualificationResultNode? created = Describe(file);
            if (created is null || created.IsDirectory || created.IsReparsePoint || !QualificationResultFile.SamePath(created.FinalPath, target)) return false;
            try
            {
                using FileStream stream = new(file, FileAccess.Write, bufferSize: 0);
                stream.Write(content);
                stream.Flush(flushToDisk: true);
                return true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException or ObjectDisposedException)
            {
                return false;
            }
        }

        public void Dispose() => handle.Dispose();
    }

    private static class Native
    {
        internal const uint FileListDirectory = 0x0001;
        internal const uint FileReadAttributes = 0x0080;
        internal const uint GenericWrite = 0x40000000;
        internal const uint FileShareRead = 0x00000001;
        internal const uint FileShareWrite = 0x00000002;
        internal const uint ShareAll = 0x00000007;
        internal const uint CreateNew = 1;
        internal const uint OpenExisting = 3;
        internal const uint FileAttributeNormal = 0x00000080;
        internal const uint BackupSemantics = 0x02000000;
        internal const uint OpenReparsePoint = 0x00200000;

        [StructLayout(LayoutKind.Sequential)]
        internal struct ByHandleFileInformation
        {
            internal uint FileAttributes;
            // Three FILETIME values, each two DWORDs (4-byte aligned, so no padding after the attributes).
            internal uint CreationTimeLow;
            internal uint CreationTimeHigh;
            internal uint LastAccessTimeLow;
            internal uint LastAccessTimeHigh;
            internal uint LastWriteTimeLow;
            internal uint LastWriteTimeHigh;
            internal uint VolumeSerialNumber;
            internal uint FileSizeHigh;
            internal uint FileSizeLow;
            internal uint NumberOfLinks;
            internal uint FileIndexHigh;
            internal uint FileIndexLow;
        }

        [DllImport("kernel32.dll", EntryPoint = "GetLongPathNameW", SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern uint GetLongPathName(string shortPath, [Out] char[] longPath, uint bufferLength);

        [DllImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern SafeFileHandle CreateFile(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes,
            uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

        [DllImport("kernel32.dll", EntryPoint = "CreateDirectoryW", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CreateDirectory(string path, IntPtr securityAttributes);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetFileInformationByHandle(SafeFileHandle file, out ByHandleFileInformation information);
    }
}
