using System.Globalization;
using System.Text;
using PCSteward.Domain;

namespace PCSteward.Application;

/// <summary>
/// Parsed reserved terminal slot. Passive history data only; it never recreates a settlement or approval.
/// <see cref="HelperCheck"/> is NotObserved for every version-2 terminal.
/// </summary>
public sealed record ReviewedUpdateTerminalRecord(UpdateDispatchOutcome Outcome, DateTimeOffset OccurredAt, int? ExitCode,
    UpdateDispatchSettlementState Settlement, string Diagnostic)
{
    private const int MaximumV2DiagnosticBytes = 4096 * 4;
    private const int MaximumV3DiagnosticBytes = 4096 * 3;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public ReviewedUpdateTerminalRecord(UpdateDispatchOutcome outcome, DateTimeOffset occurredAt, int? exitCode,
        UpdateDispatchSettlementState settlement, string diagnostic, RecordedHelperCandidateCheck helperCheck)
        : this(outcome, occurredAt, exitCode, settlement, diagnostic)
    {
        HelperCheck = helperCheck ?? throw new ArgumentNullException(nameof(helperCheck));
    }

    /// <summary>Desktop-derived, recorded at the time; display only, never authority.</summary>
    public RecordedHelperCandidateCheck HelperCheck { get; private init; } = RecordedHelperCandidateCheck.NotObserved;

    /// <summary>
    /// Strictly parses the version-2 or version-3 reserved payload written by <see cref="UpdateDispatchTerminal"/>; anything
    /// else is unreadable. A version-3 payload needs <paramref name="grammar"/> and a recorded (never NotObserved) helper check.
    /// Operation-bound helper rules are checked separately by the history reader.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<byte> reserved, IReviewedHelperVersionGrammar? grammar, out ReviewedUpdateTerminalRecord? record)
    {
        record = null;
        if (reserved.Length != UpdateDispatchTerminal.ReservedBytes) return false;
        try
        {
            int offset = 0;
            if (!TryReadLine(reserved, ref offset, out string header)) return false;
            bool v3 = header == UpdateDispatchTerminal.HeaderV3;
            if (!v3 && header != UpdateDispatchTerminal.HeaderV2) return false;
            string[] fields = new string[v3 ? 11 : 7];
            fields[0] = header;
            for (int index = 1; index < fields.Length; index++)
                if (!TryReadLine(reserved, ref offset, out fields[index])) return false;
            string lengthText = fields[^1];
            if (!Enum.TryParse(fields[1], false, out UpdateDispatchOutcome outcome) || !Enum.IsDefined(outcome) || outcome.ToString() != fields[1] ||
                !DateTimeOffset.TryParseExact(fields[2], "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset occurredAt) ||
                !Enum.TryParse(fields[4], false, out UpdateDispatchSettlementState settlement) || !Enum.IsDefined(settlement) || settlement.ToString() != fields[4] ||
                !int.TryParse(lengthText, NumberStyles.None, CultureInfo.InvariantCulture, out int length) ||
                length.ToString(CultureInfo.InvariantCulture) != lengthText || length > (v3 ? MaximumV3DiagnosticBytes : MaximumV2DiagnosticBytes) ||
                offset + length > reserved.Length)
                return false;
            int? exitCode = null;
            if (fields[3] != "None")
            {
                if (!int.TryParse(fields[3], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int exit) ||
                    exit.ToString(CultureInfo.InvariantCulture) != fields[3]) return false;
                exitCode = exit;
            }
            RecordedHelperCandidateCheck helperCheck = RecordedHelperCandidateCheck.NotObserved;
            if (v3)
            {
                if (!IsSettlementFacts(fields[5], settlement) ||
                    !TryStrip(fields[6], UpdateDispatchTerminal.HelperCheckPrefix, out string kind) ||
                    !TryStrip(fields[7], UpdateDispatchTerminal.HelperRefusalPrefix, out string reason) ||
                    !TryStrip(fields[8], UpdateDispatchTerminal.HelperInstalledPrefix, out string installed) ||
                    !TryStrip(fields[9], UpdateDispatchTerminal.HelperCandidatePrefix, out string candidate) ||
                    !RecordedHelperCandidateCheck.TryRestore(kind, reason, installed, candidate, grammar, out RecordedHelperCandidateCheck? restored) ||
                    restored is null)
                    return false;
                helperCheck = restored;
            }
            foreach (byte value in reserved[(offset + length)..]) if (value != 0) return false;
            string diagnostic = StrictUtf8.GetString(reserved.Slice(offset, length));
            if (v3 && diagnostic.Length > 4096) return false;
            record = new(outcome, occurredAt, exitCode, settlement, diagnostic, helperCheck);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or DecoderFallbackException) { return false; }
    }

    // One header line: at most 256 bytes before its LF, printable ASCII only.
    private static bool TryReadLine(ReadOnlySpan<byte> reserved, ref int offset, out string line)
    {
        line = string.Empty;
        int newline = reserved[offset..].IndexOf((byte)'\n');
        if (newline < 0 || newline > 256) return false;
        ReadOnlySpan<byte> field = reserved.Slice(offset, newline);
        foreach (byte value in field) if (value is < 0x20 or > 0x7E) return false;
        line = Encoding.ASCII.GetString(field);
        offset += newline + 1;
        return true;
    }

    private static bool TryStrip(string field, string prefix, out string value)
    {
        value = field.StartsWith(prefix, StringComparison.Ordinal) ? field[prefix.Length..] : string.Empty;
        return value.Length != 0;
    }

    // Version 3 only: exactly what the writer emits ("None" only without a live settlement, otherwise six Booleans).
    private static bool IsSettlementFacts(string facts, UpdateDispatchSettlementState settlement)
    {
        if (facts == "None") return settlement == UpdateDispatchSettlementState.InvocationUnknown;
        string[] parts = facts.Split('/');
        return settlement != UpdateDispatchSettlementState.InvocationUnknown && parts.Length == 6 && parts.All(part => part is "True" or "False");
    }
}

/// <summary>
/// One stored dispatch claim as history. <see cref="Operation"/> is null when the canonical bytes failed restore or
/// integrity checks; <see cref="Terminal"/> is null while claimed or when unreadable. Never execution authority.
/// </summary>
public sealed record ReviewedUpdateHistoryRecord(Guid JobId, ReviewedUpdateOperation? Operation, UpdateDispatchState State,
    DateTimeOffset ClaimedAt, ReviewedUpdateTerminalRecord? Terminal, bool IntegrityVerified);

public interface IReviewedUpdateHistoryReader
{
    Task<IReadOnlyList<ReviewedUpdateHistoryRecord>> GetReviewedUpdateHistoryAsync(int maximumRecords, CancellationToken cancellationToken);
}

/// <summary>
/// Presentation-safe history row. Every string is fixed or derived from frozen review data; none asserts helper qualification.
/// <see cref="HelperReport"/> is the separate, desktop-derived helper report for the candidate-evidence area (never beside
/// the status line); it is never concatenated into <see cref="Status"/>.
/// </summary>
public sealed record ReviewedUpdateHistoryView(string Title, string Status, string InstallerMode, string CandidateEvidence,
    string? Disclosure, bool BlocksOtherUpdates, string Diagnostic, string? HelperReport = null);

/// <summary>
/// FR-HIS-01/FR-UPD-15: keeps what the user reviewed (mode, route, guard contract, disclosure, candidate intent) with the
/// recorded outcome. "Helper qualified" is never shown: no authenticated, correlated helper observation exists in stored data.
/// </summary>
public static class ReviewedUpdateHistoryPresentation
{
    public static ReviewedUpdateHistoryView Present(ReviewedUpdateHistoryRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        // Mirrors the store's barrier predicate (state other than Final blocks); integrity failures make details unknown.
        bool blocks = record.State != UpdateDispatchState.Final;
        ReviewedUpdateOperation? operation = record.IntegrityVerified ? record.Operation : null;
        string title = operation is null
            ? "Update record (details unreadable)"
            : $"{operation.Selection.PackageId} {operation.Selection.InstalledVersion} → {operation.Selection.CandidateVersion} ({operation.Selection.Provider}, {operation.Selection.Scope})";
        string status = !record.IntegrityVerified
            ? blocks
                ? "Record failed integrity checks; its outcome is unknown and it blocks other updates until safe reconciliation."
                : "Record failed integrity checks; its recorded outcome cannot be trusted."
            : record.State switch
            {
                UpdateDispatchState.DispatchClaimed => "Started or about to start; no result recorded. Other updates stay blocked until safe reconciliation.",
                UpdateDispatchState.Uncertain => "Uncertain: whether the update ran or finished is unknown. Other updates stay blocked until safe reconciliation; it is never retried automatically.",
                _ => record.Terminal is { } terminal ? Outcome(terminal) : "Finished, but the recorded result is unreadable.",
            };
        return new(title, status, operation is null ? "Unknown" : Mode(operation.Mode), operation is null ? "Unknown" : Candidate(operation),
            operation is null ? null : ReviewedUpdateProviderDisclosure.GetRiskText(operation), blocks,
            record.Terminal is null ? string.Empty : SanitizedDiagnosticBundle.Sanitize(record.Terminal.Diagnostic),
            record.IntegrityVerified && record.Terminal is { } recorded ? HelperReport(recorded.HelperCheck) : null);
    }

    internal const string HelperReportPrefix = "Recorded at the time: reported by PC Steward's administrator helper: ";

    // §B.6: fixed texts only. The diagnostic never produces a report; Unconfirmed and NotObserved have none.
    private static string? HelperReport(RecordedHelperCandidateCheck check) => check.Kind switch
    {
        RecordedHelperCheckKind.Confirmed => HelperReportPrefix + "the exact reviewed version matched its protected machine checks.",
        RecordedHelperCheckKind.Refused => HelperReportPrefix + $"it refused before updating because {RefusalText(check.RefusalReason)}. " +
            $"Versions it reported: installed {check.ReportedInstalledVersion ?? "not reported"}; available {check.ReportedCandidateVersion ?? "not reported"}.",
        _ => null,
    };

    private static string RefusalText(RecordedHelperRefusalReason reason) => reason switch
    {
        RecordedHelperRefusalReason.RequestNotAdmitted => "the request was not accepted (for example, it was no longer fresh or did not come from your account)",
        RecordedHelperRefusalReason.MachineInputsUnsupported => "Chocolatey's protected machine files or settings could not be confirmed as supported",
        RecordedHelperRefusalReason.SourceNotCommunity => "Chocolatey could not be confirmed to use only the official Community repository",
        RecordedHelperRefusalReason.InstalledMismatch => "the installed version it found differed from the reviewed version",
        RecordedHelperRefusalReason.ProfileMismatch => "Chocolatey's configuration changed after the review",
        RecordedHelperRefusalReason.PreflightQueryFailed => "its check for the available version did not complete cleanly",
        RecordedHelperRefusalReason.CandidateMismatch => "the available version it found differed from the reviewed version",
        RecordedHelperRefusalReason.ApprovalExpired => "the review's time limit passed before the update could start",
        _ => throw new InvalidOperationException("A refused helper check always has a defined reason."),
    };

    private static string Outcome(ReviewedUpdateTerminalRecord terminal) => terminal.Outcome switch
    {
        UpdateDispatchOutcome.Failed when terminal.Settlement == UpdateDispatchSettlementState.NotInvoked =>
            "Not updated: nothing was started.",
        UpdateDispatchOutcome.Success => "Updated: the target version was verified after the update.",
        UpdateDispatchOutcome.RestartRequired => "Restart required: a reliable restart-pending state was detected.",
        UpdateDispatchOutcome.Unverified => "The update ran (or was stopped after starting), but the new version could not be verified.",
        UpdateDispatchOutcome.Failed => "Update failed: the provider reported an error. Check the application's version before trying again.",
        UpdateDispatchOutcome.Cancelled => "Cancelled before anything was started.",
        UpdateDispatchOutcome.ElevationDenied => "Not updated: administrator approval was declined.",
        UpdateDispatchOutcome.Refused => "Not updated: the update was refused before changing the application (for example, the candidate could not be confirmed).",
        _ => "Uncertain: whether the update ran or finished is unknown.",
    };

    private static string Mode(ReviewedInstallerMode mode) => mode.Binding == InstallerModeBinding.Selected
        ? $"Installer mode reviewed as {mode.Mode}."
        : "Installer mode is controlled by the package's own scripts; PC Steward cannot choose silent or interactive.";

    private static string Candidate(ReviewedUpdateOperation operation) => operation.CandidateProvenance switch
    {
        ReviewedCandidateProvenance.UserCacheIntent =>
            "Reviewed candidate came from your account's provider cache (provisional intent). The protected helper must independently confirm the exact candidate before updating.",
        ReviewedCandidateProvenance.TrustedDesktopEvidenceRequired => "Reviewed candidate came from fresh desktop evidence.",
        _ => "Historical record without current candidate evidence semantics.",
    };
}
