using PCSteward.Domain;

namespace PCSteward.Application;

/// <summary>
/// FR-UPD-15, FR-HIS-01, TM-04/13; Chocolatey Confirm design §2 C6 and owner decision 2 (§9): whether an earlier Chocolatey client
/// update still needs attention before another can be confirmed. A client job needs attention when it is not final (claimed or
/// uncertain), has no readable result, or ended in anything other than Success after the helper may have started (settlement
/// other than NotInvoked). A declined administrator prompt and an update that never started do not block, because nothing ran.
/// Such a job stops blocking once a later read-only check, started after the job's result was recorded, verified the installed
/// client again (design §2 R3: every Chocolatey route refuses until the tree matches a reviewed release).
/// <para>A history row whose integrity cannot be verified could be a client job, so it fails closed and needs attention.</para>
/// Display and press-time condition only: it never admits anything, and every dispatch still re-observes the client.
/// </summary>
public static class ChocolateyClientUpdateAttention
{
    /// <summary>How many recent history rows are read for this check.</summary>
    public const int HistoryWindow = 50;

    /// <summary>True when an earlier client job needs attention that no verified check started after it has cleared.</summary>
    public static bool EarlierUpdateNeedsAttention(IEnumerable<ReviewedUpdateHistoryRecord>? records, DateTimeOffset verifiedCheckStartedAt)
    {
        // An unreadable history fails closed: the owner sees why Confirm is off, and nothing is started.
        if (records is null) return true;
        foreach (ReviewedUpdateHistoryRecord? record in records)
        {
            if (record is null || !record.IntegrityVerified || record.Operation is null) return true;
            if (record.Operation.RouteContract != UpdateRouteContract.ChocolateyClientSelfUpdateV1) continue;
            DateTimeOffset recordedAt = record.Terminal?.OccurredAt ?? record.ClaimedAt;
            if (NeedsAttention(record) && recordedAt >= verifiedCheckStartedAt) return true;
        }
        return false;
    }

    /// <summary>Owner decision 2: any non-Success result after the helper may have started; never a declined prompt or a not-started job.</summary>
    internal static bool NeedsAttention(ReviewedUpdateHistoryRecord record) =>
        record.State != UpdateDispatchState.Final || record.Terminal is not { } terminal ||
        terminal.Outcome != UpdateDispatchOutcome.Success && terminal.Outcome != UpdateDispatchOutcome.ElevationDenied &&
        terminal.Settlement != UpdateDispatchSettlementState.NotInvoked;
}
