namespace PCSteward.Application;

/// <summary>ADR-031 design §2.2/§8.1: which runtime-watchdog stop the helper reported for a client self-update run.</summary>
public enum ReviewedClientWatchdogStop { None, SetupOverrideDuringRun, RootWritableDuringRun, WatchFailedDuringRun }

/// <summary>
/// FR-UPD-15, FR-UPD-01/03–09, TM-03/04/07/13; ADR-031 design §8 (owner-approved W1): the helper-reported client run fault of
/// one accepted <c>ChocolateyClientSelfUpdateV1</c> reply — at most one watchdog stop (the binding keeps only the first) and
/// whether the release of the binding's holds after resume was incomplete. Private construction: the single factory
/// <see cref="FromAcceptedHelperReply"/> has exactly one <c>src</c> call site, the protocol-2 interpreter, after
/// authentication, correlation, S1–S14, P1–P6 and H (architecture test). It is never a positive fact: the workflow uses it
/// only to keep or lower an outcome and to retain the fixed recovery warnings (design §8.3), and the client postcheck applies
/// it before any re-observation.
/// </summary>
public sealed class ReviewedClientRunFault
{
    private ReviewedClientRunFault(ReviewedClientWatchdogStop watchdogStop, bool releaseIncomplete)
    {
        WatchdogStop = watchdogStop;
        ReleaseIncomplete = releaseIncomplete;
    }

    /// <summary>The watchdog stop reported for the run, or <see cref="ReviewedClientWatchdogStop.None"/>.</summary>
    public ReviewedClientWatchdogStop WatchdogStop { get; }

    /// <summary>The release of every hold after resume failed part-way (design §2 R3).</summary>
    public bool ReleaseIncomplete { get; }

    /// <summary>No watchdog stop and a complete release: the only value that lets verification decide the outcome.</summary>
    public bool IsNone => WatchdogStop == ReviewedClientWatchdogStop.None && !ReleaseIncomplete;

    /// <summary>
    /// The single factory. Its only <c>src</c> call site is <c>ReviewedUpdateProtocol2Interpreter</c> (architecture test), which
    /// calls it only for an accepted client self-update reply. An undefined watchdog value throws.
    /// </summary>
    public static ReviewedClientRunFault FromAcceptedHelperReply(ReviewedClientWatchdogStop watchdogStop, bool releaseIncomplete) =>
        Enum.IsDefined(watchdogStop)
            ? new ReviewedClientRunFault(watchdogStop, releaseIncomplete)
            : throw new ArgumentOutOfRangeException(nameof(watchdogStop));

    public override string ToString() => IsNone ? "None" : $"{WatchdogStop}{(ReleaseIncomplete ? "+ReleaseIncomplete" : string.Empty)}";
}
