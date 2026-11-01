using PCSteward.Domain;

namespace PCSteward.Application;

/// <summary>Recorded candidate-check kinds; mirrors the protocol-2 §4.1 kinds by name (Matched → Confirmed, Undetermined → Unconfirmed).</summary>
public enum RecordedHelperCheckKind { NotObserved, Confirmed, Refused, Unconfirmed }

/// <summary>Recorded refusal reason; same names as the protocol-2 wire enum. <c>None</c> unless the kind is Refused.</summary>
public enum RecordedHelperRefusalReason
{
    None, RequestNotAdmitted, MachineInputsUnsupported, SourceNotCommunity, InstalledMismatch, ProfileMismatch,
    PreflightQueryFailed, CandidateMismatch, ApprovalExpired
}

/// <summary>
/// Grammar port for persisted helper-reported versions (adapter gates §B.2). Persistence cannot reference the Chocolatey
/// provider, so every production composition root injects the codec's grammar. A grammar check only; never trust or authority.
/// </summary>
public interface IReviewedHelperVersionGrammar
{
    bool IsNormalized(string value);
}

/// <summary>
/// Desktop-derived, recorded at the time; reported by PC Steward's administrator helper; never authority (Task 50
/// adapter gates Part B, FR-HIS-01, FR-UPD-15). It never changes status, settlement or outcome and never feeds review,
/// approval, admission, dispatch, reconciliation or candidate provenance. Only the protocol-2 interpreter's adapter
/// creates a value other than <see cref="NotObserved"/> (architecture test).
/// </summary>
public sealed class RecordedHelperCandidateCheck
{
    public const int MaximumVersionLength = 128;
    internal const string NoneToken = "None";

    private RecordedHelperCandidateCheck(RecordedHelperCheckKind kind, RecordedHelperRefusalReason reason, string? installed, string? candidate)
    {
        Kind = kind;
        RefusalReason = reason;
        ReportedInstalledVersion = installed;
        ReportedCandidateVersion = candidate;
    }

    public static RecordedHelperCandidateCheck NotObserved { get; } =
        new(RecordedHelperCheckKind.NotObserved, RecordedHelperRefusalReason.None, null, null);

    public RecordedHelperCheckKind Kind { get; }
    public RecordedHelperRefusalReason RefusalReason { get; }
    /// <summary>Installed version as reported by the helper at the time; never a fact about the machine.</summary>
    public string? ReportedInstalledVersion { get; }
    /// <summary>Available version as reported by the helper at the time; never a fact about the source.</summary>
    public string? ReportedCandidateVersion { get; }

    /// <summary>
    /// The only public factory. Throws on a structural violation (R1–R4 and the structural version form). Production calls it
    /// from exactly one place: the protocol-2 interpreter's <c>ToRecorded</c> adapter (architecture test).
    /// </summary>
    public static RecordedHelperCandidateCheck FromDesktopDerived(RecordedHelperCheckKind kind, RecordedHelperRefusalReason reason,
        string? reportedInstalledVersion, string? reportedCandidateVersion)
    {
        if (!IsStructurallyValid(kind, reason, reportedInstalledVersion, reportedCandidateVersion))
            throw new ArgumentException("The desktop-derived helper check violates its structural rules.");
        return kind == RecordedHelperCheckKind.NotObserved
            ? NotObserved
            : new(kind, reason, reportedInstalledVersion, reportedCandidateVersion);
    }

    /// <summary>
    /// Strictly restores a recorded (never NotObserved) check from version-3 terminal header values. Used only by
    /// the history terminal parser. Without a grammar nothing restores (fail closed).
    /// </summary>
    internal static bool TryRestore(string kindText, string reasonText, string installedText, string candidateText,
        IReviewedHelperVersionGrammar? grammar, out RecordedHelperCandidateCheck? check)
    {
        check = null;
        if (grammar is null || !TryParseExact(kindText, out RecordedHelperCheckKind kind) || kind == RecordedHelperCheckKind.NotObserved ||
            !TryParseExact(reasonText, out RecordedHelperRefusalReason reason)) return false;
        string? installed = installedText == NoneToken ? null : installedText;
        string? candidate = candidateText == NoneToken ? null : candidateText;
        if (!IsStructurallyValid(kind, reason, installed, candidate) ||
            (installed is not null && !grammar.IsNormalized(installed)) || (candidate is not null && !grammar.IsNormalized(candidate)))
            return false;
        check = new(kind, reason, installed, candidate);
        return true;
    }

    /// <summary>
    /// All operation-bound and outcome × settlement invariants (§B.5): O-none, O-route, P1–P5 and the allowed pairs. One
    /// pure function shared by the workflow pre-check, the store write and the history reader. NotObserved is always consistent.
    /// </summary>
    public bool IsConsistentWith(ReviewedUpdateOperation? operation, UpdateDispatchOutcome outcome, UpdateDispatchSettlementState settlement)
    {
        if (Kind == RecordedHelperCheckKind.NotObserved) return true;
        if (operation is null || !operation.HasCurrentSchema || operation.RouteContract != UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3 ||
            !Enum.IsDefined(outcome) || !Enum.IsDefined(settlement)) return false;
        bool installedEqual = ReportedInstalledVersion is { } installed &&
            string.Equals(installed, operation.Selection.InstalledVersion, StringComparison.Ordinal);
        bool candidateEqual = ReportedCandidateVersion is { } candidate &&
            string.Equals(candidate, operation.Selection.CandidateVersion, StringComparison.Ordinal);
        bool operationBound = Kind switch
        {
            RecordedHelperCheckKind.Confirmed => installedEqual && candidateEqual,
            RecordedHelperCheckKind.Refused => RefusalReason switch
            {
                RecordedHelperRefusalReason.InstalledMismatch => !installedEqual,
                RecordedHelperRefusalReason.ProfileMismatch => installedEqual,
                RecordedHelperRefusalReason.CandidateMismatch => installedEqual && !candidateEqual,
                _ => true,
            } && (ReportedCandidateVersion is null || RefusalReason == RecordedHelperRefusalReason.CandidateMismatch || candidateEqual),
            RecordedHelperCheckKind.Unconfirmed => true,
            _ => false,
        };
        bool unsettled = settlement is UpdateDispatchSettlementState.InvocationUnknown or UpdateDispatchSettlementState.OwnedWorkUnsettled;
        bool allowedPair = Kind switch
        {
            RecordedHelperCheckKind.Confirmed => outcome switch
            {
                UpdateDispatchOutcome.Success or UpdateDispatchOutcome.Unverified => settlement == UpdateDispatchSettlementState.OwnedWorkStopped,
                UpdateDispatchOutcome.Failed => settlement is UpdateDispatchSettlementState.NotInvoked or UpdateDispatchSettlementState.OwnedWorkStopped,
                UpdateDispatchOutcome.Cancelled => settlement == UpdateDispatchSettlementState.NotInvoked,
                UpdateDispatchOutcome.Uncertain => unsettled,
                _ => false,
            },
            RecordedHelperCheckKind.Refused => outcome switch
            {
                UpdateDispatchOutcome.Refused => settlement == UpdateDispatchSettlementState.NotInvoked,
                UpdateDispatchOutcome.Uncertain => unsettled,
                _ => false,
            },
            RecordedHelperCheckKind.Unconfirmed => outcome switch
            {
                UpdateDispatchOutcome.Failed or UpdateDispatchOutcome.Cancelled => settlement == UpdateDispatchSettlementState.NotInvoked,
                UpdateDispatchOutcome.Uncertain => unsettled,
                _ => false,
            },
            _ => false,
        };
        return operationBound && allowedPair;
    }

    // R1–R4 (§B.5) plus the structural version form. The read side additionally requires the injected grammar.
    private static bool IsStructurallyValid(RecordedHelperCheckKind kind, RecordedHelperRefusalReason reason, string? installed, string? candidate)
    {
        if (!Enum.IsDefined(kind) || !Enum.IsDefined(reason) ||
            (installed is not null && !IsVersionForm(installed)) || (candidate is not null && !IsVersionForm(candidate)) ||
            (reason != RecordedHelperRefusalReason.None) != (kind == RecordedHelperCheckKind.Refused))
            return false;
        return kind switch
        {
            RecordedHelperCheckKind.NotObserved or RecordedHelperCheckKind.Unconfirmed => installed is null && candidate is null,
            RecordedHelperCheckKind.Confirmed => installed is not null && candidate is not null,
            RecordedHelperCheckKind.Refused => reason == RecordedHelperRefusalReason.RequestNotAdmitted
                ? installed is null && candidate is null
                : (candidate is null || (installed is not null && reason != RecordedHelperRefusalReason.PreflightQueryFailed)) &&
                  (reason != RecordedHelperRefusalReason.ProfileMismatch || installed is not null),
            _ => false,
        };
    }

    // 1–128 characters from [0-9A-Za-z.-], starting with a digit: a superset of the native normalized form.
    private static bool IsVersionForm(string value) => value.Length is > 0 and <= MaximumVersionLength && char.IsAsciiDigit(value[0]) &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-');

    private static bool TryParseExact<TEnum>(string text, out TEnum value) where TEnum : struct, Enum =>
        Enum.TryParse(text, false, out value) && Enum.IsDefined(value) && value.ToString() == text;
}
