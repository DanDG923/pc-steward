using PCSteward.Domain;

namespace PCSteward.Provider.Chocolatey.Native;

/// <summary>The settled stage-C refusal reasons (rev 3 §3.1 stage C with §10 N3, design rev 6 §4 table).</summary>
internal enum ChocolateyCandidateRefusal
{
    InstalledMismatch,
    PreflightQueryFailed,
    CandidateMismatch
}

/// <summary>
/// FR-UPD-15, TM-03/04/07/13; Task 50 design rev 6 §5.4 (rev-5 F1): the stage-C result as plain values. The kind is closed
/// (the base constructor is private; exactly these four arms derive from it); each arm has non-private construction so a
/// fake item can build it, and carries no authority. Each constructor enforces the rev 3 structural predicates that apply
/// to stage C, so an inconsistent value cannot be built:
/// <list type="bullet">
/// <item>versions are supported native versions in normalized form (S12); a fingerprint is 64 uppercase hex;</item>
/// <item><see cref="Refused"/>: a candidate needs an installed value and is never present with
/// <c>PreflightQueryFailed</c> or <c>InstalledMismatch</c> (S7); <c>outputLimitExceeded</c> only with
/// <c>PreflightQueryFailed</c> (S10); <c>CandidateMismatch</c> carries the installed value and fingerprint (N1, P4);</item>
/// <item><see cref="Undetermined"/>: cancelled, timed out or not settled (S11).</item>
/// </list>
/// </summary>
internal abstract record ChocolateyCandidateValues
{
    private ChocolateyCandidateValues() { }

    /// <summary>The unique unpinned stable strictly-newer candidate equals the frozen candidate; every stage-C root settled.</summary>
    internal sealed record Matched : ChocolateyCandidateValues
    {
        internal Matched(string candidate) => Candidate = RequireVersion(candidate, nameof(candidate));

        private Matched(string candidate, bool wingetMachine) =>
            Candidate = wingetMachine && UpdateJobSelection.IsVersionText(candidate)
                ? candidate
                : throw new ArgumentException("Reviewed version text is required.", nameof(candidate));

        /// <summary>D4b design §5.1: stage C passed for the machine WinGet route (the frozen candidate as reviewed version text).</summary>
        internal static Matched ForWinGetMachine(string candidate) => new(candidate, wingetMachine: true);

        internal string Candidate { get; }
    }

    /// <summary>A settled refusal with the values "as far as observed" (field 16 installed, 17 candidate, 18 fingerprint).</summary>
    internal sealed record Refused : ChocolateyCandidateValues
    {
        internal Refused(ChocolateyCandidateRefusal reason, string? installed, string? candidate, string? fingerprint, bool outputLimitExceeded)
        {
            if (!Enum.IsDefined(reason)) throw new ArgumentOutOfRangeException(nameof(reason));
            if (installed is not null) RequireVersion(installed, nameof(installed));
            if (candidate is not null) RequireVersion(candidate, nameof(candidate));
            if (fingerprint is not null && !IsFingerprint(fingerprint)) throw new ArgumentException("A 64-character uppercase hex fingerprint is required.", nameof(fingerprint));
            bool consistent = reason switch
            {
                // installed may be null: a well-formed stage-C list with no record observed the package gone (N3; P2 allows null).
                ChocolateyCandidateRefusal.InstalledMismatch => candidate is null && !outputLimitExceeded,
                ChocolateyCandidateRefusal.PreflightQueryFailed => candidate is null,
                ChocolateyCandidateRefusal.CandidateMismatch => installed is not null && fingerprint is not null && !outputLimitExceeded,
                _ => false,
            };
            if (!consistent) throw new ArgumentException("The values do not satisfy the stage-C predicates for the reason.", nameof(reason));
            Reason = reason;
            Installed = installed;
            Candidate = candidate;
            Fingerprint = fingerprint;
            OutputLimitExceeded = outputLimitExceeded;
        }

        internal ChocolateyCandidateRefusal Reason { get; }

        internal string? Installed { get; }

        internal string? Candidate { get; }

        internal string? Fingerprint { get; }

        internal bool OutputLimitExceeded { get; }
    }

    /// <summary>A query was interrupted or its root is not known to be settled; never a settled refusal.</summary>
    internal sealed record Undetermined : ChocolateyCandidateValues
    {
        internal Undetermined(bool cancelled, bool timedOut, bool aggregateSettlementConfirmed, bool outputLimitExceeded)
        {
            if (!cancelled && !timedOut && aggregateSettlementConfirmed)
                throw new ArgumentException("Undetermined needs cancellation, a timeout or an unconfirmed settlement.", nameof(aggregateSettlementConfirmed));
            Cancelled = cancelled;
            TimedOut = timedOut;
            AggregateSettlementConfirmed = aggregateSettlementConfirmed;
            OutputLimitExceeded = outputLimitExceeded;
        }

        internal bool Cancelled { get; }

        internal bool TimedOut { get; }

        internal bool AggregateSettlementConfirmed { get; }

        internal bool OutputLimitExceeded { get; }
    }

    /// <summary>The item refused the call before any I/O or launch (§5.4, rev-4 L5); produced by the item, not by stage C.</summary>
    internal sealed record OutOfOrder : ChocolateyCandidateValues
    {
        internal OutOfOrder() { }
    }

    internal static bool IsFingerprint(string? value) =>
        value is { Length: 64 } && value.All(static character => character is >= '0' and <= '9' or >= 'A' and <= 'F');

    private static string RequireVersion(string? value, string name) =>
        ChocolateyQueryParser.IsNormalizedVersion(value) ? value! : throw new ArgumentException("A normalized native version is required.", name);
}

/// <summary>
/// The stage-C inputs that do not come from the queries: the target, the stage-B installed value and fingerprint, and the
/// frozen installed and candidate values. Stage C runs only after a stage-B <c>Observed</c>, whose installed value equals
/// the frozen one (stage B step 8a), and the frozen candidate is stable and strictly newer (the reviewed route shape);
/// anything else is a caller fault and throws.
/// </summary>
internal sealed record ChocolateyStageCBaseline
{
    internal ChocolateyStageCBaseline(string packageId, string stageBInstalled, string stageBFingerprint, string expectedFingerprint,
        string frozenInstalled, string frozenCandidate)
    {
        // N3 review L3: field 18 of a CandidateMismatch is the stage-B fingerprint, and P4 needs it equal to the frozen
        // operation's F. Stage B is called with that expected fingerprint; the baseline re-checks it so P4 cannot fail.
        if (!ChocolateyCandidateValues.IsFingerprint(expectedFingerprint) ||
            !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.ASCII.GetBytes(stageBFingerprint), System.Text.Encoding.ASCII.GetBytes(expectedFingerprint)))
            throw new ArgumentException("The stage-B fingerprint must equal the frozen operation's expected fingerprint.", nameof(expectedFingerprint));
        if (!ChocolateyNativePackageId.IsSupported(packageId) || ChocolateyClientReleasePolicy.IsClientTarget(packageId))
            throw new ArgumentException("A supported non-client package ID is required.", nameof(packageId));
        foreach ((string value, string name) in new[] { (stageBInstalled, nameof(stageBInstalled)), (frozenInstalled, nameof(frozenInstalled)), (frozenCandidate, nameof(frozenCandidate)) })
            if (!ChocolateyQueryParser.IsNormalizedVersion(value)) throw new ArgumentException("A normalized native version is required.", name);
        if (!ChocolateyCandidateValues.IsFingerprint(stageBFingerprint))
            throw new ArgumentException("A 64-character uppercase hex fingerprint is required.", nameof(stageBFingerprint));
        if (!string.Equals(stageBInstalled, frozenInstalled, StringComparison.Ordinal))
            throw new ArgumentException("Stage C runs only after stage B observed the frozen installed version.", nameof(stageBInstalled));
        if (!ChocolateyStageCMapping.IsStableStrictlyNewer(frozenInstalled, frozenCandidate))
            throw new ArgumentException("The frozen candidate must be stable and strictly newer.", nameof(frozenCandidate));
        PackageId = packageId;
        StageBInstalled = stageBInstalled;
        StageBFingerprint = stageBFingerprint;
        FrozenInstalled = frozenInstalled;
        FrozenCandidate = frozenCandidate;
    }

    internal string PackageId { get; }

    internal string StageBInstalled { get; }

    internal string StageBFingerprint { get; }

    internal string FrozenInstalled { get; }

    internal string FrozenCandidate { get; }
}

/// <summary>
/// FR-UPD-15, TM-03/04/07/13; Task 50 design rev 6 §4 result-mapping table with rev 3 §3.1 stage C, §10 N1 and N3 and the
/// rev-4 M2 correction: the pure stage-C decision over the two strict query outcomes. Rows, in order:
/// <list type="number">
/// <item>An interrupted query, or one whose root is not confirmed settled → <see cref="ChocolateyCandidateValues.Undetermined"/>
/// with the matching flags (never a settled refusal).</item>
/// <item>A settled query failure (non-zero exit, no process, incomplete capture, malformed, duplicate or missing record, or
/// either output bound) → <c>PreflightQueryFailed</c>, candidate null, <c>outputLimitExceeded</c> for either bound.</item>
/// <item>A well-formed <c>list --exact</c> value ≠ the stage-B value → <c>InstalledMismatch</c> with the stage-C value (N3);
/// <c>outdated</c> is then not run.</item>
/// <item>The target's <c>outdated</c> installed field ≠ the list value → <c>PreflightQueryFailed</c>, candidate null (rev-4 M2).</item>
/// <item>No unique unpinned stable strictly-newer candidate → <c>CandidateMismatch</c>, candidate null; a candidate ≠ the
/// frozen one → <c>CandidateMismatch</c> with that candidate. Field 16 is the stage-C list value and field 18 the stage-B
/// fingerprint (N1), so P4 holds and the refusal settles.</item>
/// <item>Otherwise → <see cref="ChocolateyCandidateValues.Matched"/>.</item>
/// </list>
/// A <c>PreflightQueryFailed</c> reports the stage-C list value when the list query was well-formed, otherwise the
/// stage-B value (as for a stage-C out-of-order refusal, §5.4 F2), and the stage-B fingerprint; it makes no equality claim
/// (P6).
/// </summary>
internal static class ChocolateyStageCMapping
{
    /// <summary>
    /// The decision after the <c>list --exact</c> query alone: a terminal value (rows 1–3), or null when the list value
    /// equals the stage-B value and the <c>outdated</c> query must run.
    /// </summary>
    internal static ChocolateyCandidateValues? AfterList(ChocolateyStageCBaseline baseline, ChocolateyQueryOutcome<ChocolateyInstalledRecord> list)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(list);
        if (Settled(list.Observation, list.Status, baseline, baseline.StageBInstalled) is { } decided) return decided;
        // N3 review L1: a settled, well-formed list with no record observed the package gone at stage C: InstalledMismatch with
        // installed null (N3; P2 allows null), not a PreflightQueryFailed carrying a value stage C did not see.
        if (list.Records.Count == 0)
            return new ChocolateyCandidateValues.Refused(ChocolateyCandidateRefusal.InstalledMismatch, null, null, baseline.StageBFingerprint, false);
        if (list.Records.Count != 1 || !string.Equals(list.Records[0].Id, baseline.PackageId, StringComparison.OrdinalIgnoreCase) ||
            !ChocolateyQueryParser.IsNormalizedVersion(list.Records[0].Version))
            return QueryFailed(baseline, baseline.StageBInstalled, outputLimitExceeded: false);
        string listed = list.Records[0].Version;
        return string.Equals(listed, baseline.StageBInstalled, StringComparison.Ordinal)
            ? null
            : new ChocolateyCandidateValues.Refused(ChocolateyCandidateRefusal.InstalledMismatch, listed, null, baseline.StageBFingerprint, false);
    }

    /// <summary>The full stage-C decision. <paramref name="outdated"/> may be null only when <see cref="AfterList"/> is terminal.</summary>
    internal static ChocolateyCandidateValues Map(ChocolateyStageCBaseline baseline, ChocolateyQueryOutcome<ChocolateyInstalledRecord> list,
        ChocolateyQueryOutcome<ChocolateyNativeOutdatedRecord>? outdated)
    {
        if (AfterList(baseline, list) is { } decided) return decided;
        ArgumentNullException.ThrowIfNull(outdated);
        string listed = list.Records[0].Version;
        if (Settled(outdated.Observation, outdated.Status, baseline, listed) is { } failed) return failed;

        ChocolateyNativeOutdatedRecord[] target = [.. outdated.Records.Where(record =>
            string.Equals(record.Id, baseline.PackageId, StringComparison.OrdinalIgnoreCase))];
        if (target.Length > 1) return QueryFailed(baseline, listed, outputLimitExceeded: false);
        if (target.Length == 0) return CandidateMismatch(baseline, listed, null);
        ChocolateyNativeOutdatedRecord record = target[0];
        if (!ChocolateyQueryParser.IsNormalizedVersion(record.InstalledVersion) || !ChocolateyQueryParser.IsNormalizedVersion(record.AvailableVersion))
            return QueryFailed(baseline, listed, outputLimitExceeded: false);
        // rev-4 M2: the two client queries disagree about the installed version; the query result is inconsistent.
        if (!string.Equals(record.InstalledVersion, listed, StringComparison.Ordinal)) return QueryFailed(baseline, listed, outputLimitExceeded: false);
        if (record.Pinned || !IsStableStrictlyNewer(listed, record.AvailableVersion)) return CandidateMismatch(baseline, listed, null);
        if (!string.Equals(record.AvailableVersion, baseline.FrozenCandidate, StringComparison.Ordinal))
            return CandidateMismatch(baseline, listed, record.AvailableVersion);
        return new ChocolateyCandidateValues.Matched(record.AvailableVersion);
    }

    /// <summary>A stable candidate strictly newer than <paramref name="installed"/>, by the same rule as the fixed upgrade builder.</summary>
    internal static bool IsStableStrictlyNewer(string installed, string candidate) =>
        ChocolateyNativeVersion.TryParse(candidate, out ChocolateyNativeVersion? parsed) && !parsed!.IsPrerelease &&
        VersionComparison.Compare(new VersionComparisonRequest(installed, candidate)).Outcome == VersionComparisonOutcome.Newer;

    /// <summary>Rows 1–2 for one query: null when it parsed, otherwise Undetermined or PreflightQueryFailed.</summary>
    private static ChocolateyCandidateValues? Settled(NativeProcessObservation observation, ChocolateyQueryStatus status,
        ChocolateyStageCBaseline baseline, string installed) => status switch
    {
        ChocolateyQueryStatus.Parsed => null,
        ChocolateyQueryStatus.Interrupted or ChocolateyQueryStatus.Unsettled => new ChocolateyCandidateValues.Undetermined(
            observation.Cancelled, observation.TimedOut,
            observation.AggregateSettled && observation.Invocation != NativeInvocation.Unknown && status != ChocolateyQueryStatus.Unsettled,
            observation.OutputLimitExceeded),
        ChocolateyQueryStatus.OutputLimitExceeded or ChocolateyQueryStatus.RecordLimitExceeded =>
            QueryFailed(baseline, installed, outputLimitExceeded: true),
        _ => QueryFailed(baseline, installed, outputLimitExceeded: false),
    };

    private static ChocolateyCandidateValues.Refused QueryFailed(ChocolateyStageCBaseline baseline, string installed, bool outputLimitExceeded) =>
        new(ChocolateyCandidateRefusal.PreflightQueryFailed, installed, null, baseline.StageBFingerprint, outputLimitExceeded);

    private static ChocolateyCandidateValues.Refused CandidateMismatch(ChocolateyStageCBaseline baseline, string listed, string? candidate) =>
        new(ChocolateyCandidateRefusal.CandidateMismatch, listed, candidate, baseline.StageBFingerprint, false);
}

/// <summary>
/// FR-UPD-15, TM-03/04/07/13; ADR-031 design §7.1 C: the pure stage-C decision of the client self-update over the strict
/// <c>list chocolatey --exact</c> outcome alone (no <c>outdated</c>: the candidate is the compiled offer rule's target).
/// Rows, in order: an interrupted or unsettled query → <see cref="ChocolateyCandidateValues.Undetermined"/>; a settled query
/// failure (including either output bound) → <c>PreflightQueryFailed</c>; no record → <c>InstalledMismatch</c> with installed
/// null; a malformed, duplicate or foreign record → <c>PreflightQueryFailed</c>; a listed version ≠ the stage-B source release
/// → <c>InstalledMismatch</c> with the stage-C value (N3); no strictly newer listed release → <c>CandidateMismatch</c>,
/// candidate null; the list's offer ≠ the frozen candidate → <c>CandidateMismatch</c> with the offer; otherwise
/// <see cref="ChocolateyCandidateValues.Matched"/> with the offer, which field 17 then reports (equality-checked by P1/P4/P5).
/// Field 18 is the stage-B fingerprint (N1).
/// </summary>
internal static class ChocolateyClientStageCMapping
{
    internal static ChocolateyCandidateValues Map(ChocolateyReviewedRelease installed, string stageBInstalled, string stageBFingerprint,
        string frozenCandidate, ChocolateyQueryOutcome<ChocolateyInstalledRecord> list)
    {
        ArgumentNullException.ThrowIfNull(installed);
        ArgumentNullException.ThrowIfNull(list);
        if (!installed.List.Contains(installed) || !string.Equals(stageBInstalled, installed.Version, StringComparison.Ordinal) ||
            !ChocolateyCandidateValues.IsFingerprint(stageBFingerprint) || !ChocolateyQueryParser.IsNormalizedVersion(frozenCandidate))
            throw new ArgumentException("Stage C runs only after a stage-B client observation of the frozen source release.", nameof(stageBInstalled));
        NativeProcessObservation observation = list.Observation;
        switch (list.Status)
        {
            case ChocolateyQueryStatus.Parsed:
                break;
            case ChocolateyQueryStatus.Interrupted or ChocolateyQueryStatus.Unsettled:
                return new ChocolateyCandidateValues.Undetermined(observation.Cancelled, observation.TimedOut,
                    observation.AggregateSettled && observation.Invocation != NativeInvocation.Unknown && list.Status != ChocolateyQueryStatus.Unsettled,
                    observation.OutputLimitExceeded);
            case ChocolateyQueryStatus.OutputLimitExceeded or ChocolateyQueryStatus.RecordLimitExceeded:
                return QueryFailed(stageBInstalled, stageBFingerprint, outputLimitExceeded: true);
            default:
                return QueryFailed(stageBInstalled, stageBFingerprint, outputLimitExceeded: false);
        }
        if (list.Records.Count == 0)
            return new ChocolateyCandidateValues.Refused(ChocolateyCandidateRefusal.InstalledMismatch, null, null, stageBFingerprint, false);
        if (list.Records.Count != 1 ||
            !string.Equals(list.Records[0].Id, PCSteward.Domain.ReviewedUpdateOperation.ChocolateyClientPackageId, StringComparison.OrdinalIgnoreCase) ||
            !ChocolateyQueryParser.IsNormalizedVersion(list.Records[0].Version))
            return QueryFailed(stageBInstalled, stageBFingerprint, outputLimitExceeded: false);
        string listed = list.Records[0].Version;
        if (!string.Equals(listed, installed.Version, StringComparison.Ordinal))
            return new ChocolateyCandidateValues.Refused(ChocolateyCandidateRefusal.InstalledMismatch, listed, null, stageBFingerprint, false);
        ChocolateyReviewedRelease? offer = installed.List.OfferTarget(installed);
        if (offer is null)
            return new ChocolateyCandidateValues.Refused(ChocolateyCandidateRefusal.CandidateMismatch, listed, null, stageBFingerprint, false);
        if (!string.Equals(offer.Version, frozenCandidate, StringComparison.Ordinal))
            return new ChocolateyCandidateValues.Refused(ChocolateyCandidateRefusal.CandidateMismatch, listed, offer.Version, stageBFingerprint, false);
        return new ChocolateyCandidateValues.Matched(offer.Version);
    }

    private static ChocolateyCandidateValues.Refused QueryFailed(string installed, string fingerprint, bool outputLimitExceeded) =>
        new(ChocolateyCandidateRefusal.PreflightQueryFailed, installed, null, fingerprint, outputLimitExceeded);
}
