namespace PCSteward.Execution;

/// <summary>Qualification probe spec rev 4 §5.1: how the desktop was started.</summary>
public enum QualificationProbeLaunch
{
    /// <summary>The existing desktop start: unchanged.</summary>
    Normal,

    /// <summary>Exactly two elements with element 1 ordinal-equal to <c>--qualify-chocolatey-helper</c>.</summary>
    Probe,

    /// <summary>Any other line with an element ordinal-equal to the flag: exits at once, acquiring and creating nothing.</summary>
    Malformed
}

/// <summary>
/// Probe spec rev 4 §5.1–§5.2 step 2: the common current-user data-operation gate as the probe holds it. The desktop's
/// composition acquires it with <c>CurrentUserDataOperationSession.TryAcquire(TimeSpan.Zero, …)</c> on the calling thread.
/// </summary>
public interface IQualificationProbeGateSession : IDisposable
{
    /// <summary>The acquisition observed an abandoned gate or legacy mutex (<c>Context.RecoveryRequired</c>).</summary>
    bool RecoveryRequired { get; }

    /// <summary>The read-only factory-reset marker state is <c>None</c> (any other state, or a failure, is not settled).</summary>
    bool ResetMarkerSettled();
}

/// <summary>
/// FR-UPD-15, TM-03/04/07/13; qualification probe spec rev 4 §5.1–§5.5 and §7 D1/D3/D5/D7: the desktop's probe entry. The
/// desktop calls <see cref="Classify"/> as the first statement of <c>App.OnLaunched</c>, before its own gate acquisition; only a
/// <see cref="QualificationProbeLaunch.Probe"/> start calls <see cref="Run"/>. The probe path never opens or creates the
/// database, runs no migration or reconciliation, takes no claim, writes no history, scans nothing, never touches the scheduled
/// task, completes or arms no factory reset (it only reads the marker state through the gate session), creates no window or
/// view model and sends no request frame. Its only output is the result file (§5.3), which is authoritative over the exit code.
/// </summary>
public static class QualificationProbeEntry
{
    /// <summary>§5.1: the only flag that selects the probe (ordinal).</summary>
    internal const string Flag = "--qualify-chocolatey-helper";

    /// <summary>§5.4: no result file was written (the folder or the file could not be created, or the run faulted).</summary>
    public const int NoResultFileExitCode = 60;

    /// <summary>§5.1: a malformed probe line exits at once.</summary>
    public const int MalformedExitCode = 61;

    /// <summary>§5.1: pure; element 0 is never compared (the runtime may report the main assembly path).</summary>
    public static QualificationProbeLaunch Classify(string[]? arguments)
    {
        if (arguments is null) return QualificationProbeLaunch.Normal;
        if (arguments.Length == 2 && string.Equals(arguments[1], Flag, StringComparison.Ordinal)) return QualificationProbeLaunch.Probe;
        return arguments.Any(static argument => string.Equals(argument, Flag, StringComparison.Ordinal))
            ? QualificationProbeLaunch.Malformed
            : QualificationProbeLaunch.Normal;
    }

    /// <summary>
    /// §5.1 probe branch, on the calling (<c>OnLaunched</c>) thread: step 1 (the fresh result folder), step 2 (the gate through
    /// <paramref name="acquireGate"/>, then the reset marker), steps 3–10 in <see cref="ReviewedHelperProbeTransport.ProbeAsync"/>
    /// on a worker this thread waits for, then the result file and the gate's disposal on this thread. Returns the §5.4 exit code.
    /// </summary>
    public static int Run(Func<IQualificationProbeGateSession?> acquireGate) =>
        new QualificationProbeRunner(Path.GetTempPath, WindowsQualificationResultFileSystem.Instance, TimeProvider.System,
            static cancellationToken => new ReviewedHelperProbeTransport().ProbeAsync(cancellationToken)).Run(acquireGate);

    /// <summary>§5.4: 0 only for <see cref="QualificationProbeDesktopOutcome.ResponseAccepted"/>; a fixed non-zero code per other outcome.</summary>
    internal static int ExitCode(QualificationProbeDesktopOutcome outcome) => outcome switch
    {
        QualificationProbeDesktopOutcome.ResponseAccepted => 0,
        QualificationProbeDesktopOutcome.GateBusy => 41,
        QualificationProbeDesktopOutcome.GateUnavailable => 42,
        QualificationProbeDesktopOutcome.GateRecoveryRequired => 43,
        QualificationProbeDesktopOutcome.GateResetPending => 44,
        QualificationProbeDesktopOutcome.DesktopNotLimited => 45,
        QualificationProbeDesktopOutcome.ConflictingProcess => 46,
        QualificationProbeDesktopOutcome.LaunchTrustFailed => 47,
        QualificationProbeDesktopOutcome.CapabilityNotHeld => 48,
        QualificationProbeDesktopOutcome.ElevationDeclined => 49,
        QualificationProbeDesktopOutcome.HelperNotStarted => 50,
        QualificationProbeDesktopOutcome.HelperNotConnected => 51,
        QualificationProbeDesktopOutcome.PeerNotVerified => 52,
        QualificationProbeDesktopOutcome.NoValidResponse => 53,
        QualificationProbeDesktopOutcome.ResponseRejected => 54,
        _ => NoResultFileExitCode,
    };
}

/// <summary>
/// The portable body of the probe branch (§5.1, §5.2 steps 1–2, §5.3 write, §5.4). Production is composed only by
/// <see cref="QualificationProbeEntry.Run"/>; the fixture tests substitute the temp path, the file system, the clock and the
/// probe (an architecture test pins that no other <c>src</c> code constructs it).
/// </summary>
internal sealed class QualificationProbeRunner(Func<string> tempPath, IQualificationResultFileSystem fileSystem, TimeProvider clock,
    Func<CancellationToken, Task<QualificationProbeRun>> probe)
{
    internal int Run(Func<IQualificationProbeGateSession?> acquireGate)
    {
        ArgumentNullException.ThrowIfNull(acquireGate);
        DateTimeOffset started = clock.GetUtcNow();
        // Step 1: the fresh result folder first. Without it no outcome can be recorded, so nothing else is done (no gate).
        IQualificationResultFolder? folder;
        try { folder = QualificationResultFile.TryCreateFolder(tempPath(), fileSystem); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { folder = null; }
        if (folder is null) return QualificationProbeEntry.NoResultFileExitCode;
        using (folder)
        {
            // Step 2: the gate on this thread; a null session is busy, a throw is unavailable.
            IQualificationProbeGateSession? session;
            try { session = acquireGate(); }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                return Write(folder, QualificationProbeRun.Refused(QualificationProbeDesktopOutcome.GateUnavailable), started);
            }
            if (session is null) return Write(folder, QualificationProbeRun.Refused(QualificationProbeDesktopOutcome.GateBusy), started);
            try
            {
                QualificationProbeRun? run =
                    Read(() => session.RecoveryRequired, fault: true) ? QualificationProbeRun.Refused(QualificationProbeDesktopOutcome.GateRecoveryRequired) :
                    !Read(session.ResetMarkerSettled, fault: false) ? QualificationProbeRun.Refused(QualificationProbeDesktopOutcome.GateResetPending) :
                    RunProbe();
                // The gate stays held until the result file is written (§5.6), then is released on this, the acquiring, thread.
                return run is null ? QualificationProbeEntry.NoResultFileExitCode : Write(folder, run, started);
            }
            finally
            {
                try { session.Dispose(); }
                catch (Exception exception) when (exception is not OutOfMemoryException) { }
            }
        }
    }

    /// <summary>Steps 3–10 on a worker the calling thread waits for (the gate session is thread-affine). A fault records nothing.</summary>
    private QualificationProbeRun? RunProbe()
    {
        try { return Task.Run(() => probe(CancellationToken.None)).GetAwaiter().GetResult(); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { return null; }
    }

    private int Write(IQualificationResultFolder folder, QualificationProbeRun run, DateTimeOffset started)
    {
        try
        {
            byte[] document = QualificationProbeResultDocument.Create(run, started, clock.GetUtcNow());
            return QualificationResultFile.TryWrite(folder, document)
                ? QualificationProbeEntry.ExitCode(run.Outcome)
                : QualificationProbeEntry.NoResultFileExitCode;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException) { return QualificationProbeEntry.NoResultFileExitCode; }
    }

    private static bool Read(Func<bool> read, bool fault)
    {
        try { return read(); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { return fault; }
    }
}
