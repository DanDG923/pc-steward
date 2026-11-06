namespace PCSteward.Provider.Chocolatey.Native;

/// <summary>Closed launch classification (Task 50 design rev 5 §4.1, L1).</summary>
internal enum NativeInvocation
{
    /// <summary>No process ran user code: creation failed, or a never-resumed process was confirmed terminated.</summary>
    NotStarted,

    /// <summary>The primary thread was resumed.</summary>
    Started,

    /// <summary>A process may have run and its termination is not confirmed.</summary>
    Unknown
}

/// <summary>
/// Launcher facts for one job-scoped launch (wire facts 4–13 plus the diagnostic-only <see cref="LingeringTerminated"/>).
/// <see cref="ExitCode"/> is present exactly when <see cref="DirectExitObserved"/>. <see cref="AggregateSettled"/> is
/// reported only after the job's active-process count was observed at zero while the job handle was open; for a started
/// launch it additionally requires the primary exit and both pipe readers at end of file.
/// </summary>
internal sealed record NativeProcessObservation(NativeInvocation Invocation, int? ExitCode, bool DirectExitObserved,
    bool OwnedIoCompleted, bool PipeEofObserved, bool TerminationFailed, bool AggregateSettled, bool Cancelled,
    bool TimedOut, bool OutputLimitExceeded, bool LingeringTerminated, string StandardOutput, string StandardError);
