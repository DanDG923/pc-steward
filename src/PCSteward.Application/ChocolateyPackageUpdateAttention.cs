using PCSteward.Domain;

namespace PCSteward.Application;

/// <summary>
/// FR-UPD-15, FR-HIS-01, TM-04/13; Chocolatey package Confirm addendum §3 C6 and owner decision 2 of the Confirm design (§9):
/// whether an earlier Chocolatey reviewed update still needs attention before a package update can be confirmed. Both Chocolatey
/// routes count, because a client update that did not finish cleanly changes the client every package update runs through. The
/// rule per job is the client screen's (<see cref="ChocolateyClientUpdateAttention.NeedsAttention"/>): not final, no readable
/// result, or anything other than Success after the helper may have started. A declined administrator prompt and an update that
/// never started do not block. Such a job stops blocking once a later check, started after the job's result was recorded,
/// observed the package and the client again.
/// <para>Unreadable history, and a row whose integrity cannot be verified, fail closed and need attention.</para>
/// Display and press-time condition only: it never admits anything, and every dispatch still re-observes the package.
/// </summary>
public static class ChocolateyPackageUpdateAttention
{
    /// <summary>True when an earlier Chocolatey job needs attention that no check started after it has cleared.</summary>
    public static bool EarlierUpdateNeedsAttention(IEnumerable<ReviewedUpdateHistoryRecord>? records, DateTimeOffset checkStartedAt)
    {
        if (records is null) return true;
        foreach (ReviewedUpdateHistoryRecord? record in records)
        {
            if (record is null || !record.IntegrityVerified || record.Operation is null) return true;
            if (record.Operation.RouteContract is not (UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3 or UpdateRouteContract.ChocolateyClientSelfUpdateV1))
                continue;
            DateTimeOffset recordedAt = record.Terminal?.OccurredAt ?? record.ClaimedAt;
            if (ChocolateyClientUpdateAttention.NeedsAttention(record) && recordedAt >= checkStartedAt) return true;
        }
        return false;
    }
}
