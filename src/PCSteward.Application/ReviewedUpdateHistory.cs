using System.Globalization;
using System.Text;
using PCSteward.Domain;

namespace PCSteward.Application;

/// <summary>Parsed reserved terminal slot. Passive history data only; it never recreates a settlement or approval.</summary>
public sealed record ReviewedUpdateTerminalRecord(UpdateDispatchOutcome Outcome, DateTimeOffset OccurredAt, int? ExitCode,
    UpdateDispatchSettlementState Settlement, string Diagnostic)
{
    private const string Header = "pc-steward-update-terminal:2";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    /// <summary>Strictly parses the version-2 reserved payload written by <see cref="UpdateDispatchTerminal"/>; anything else is unreadable.</summary>
    public static bool TryParse(ReadOnlySpan<byte> reserved, out ReviewedUpdateTerminalRecord? record)
    {
        record = null;
        if (reserved.Length != UpdateDispatchTerminal.ReservedBytes) return false;
        try
        {
            int offset = 0;
            string[] fields = new string[7];
            for (int index = 0; index < fields.Length; index++)
            {
                int newline = reserved[offset..].IndexOf((byte)'\n');
                if (newline < 0 || newline > 256) return false;
                ReadOnlySpan<byte> field = reserved.Slice(offset, newline);
                foreach (byte value in field) if (value is < 0x20 or > 0x7E) return false;
                fields[index] = Encoding.ASCII.GetString(field);
                offset += newline + 1;
            }
            if (fields[0] != Header ||
                !Enum.TryParse(fields[1], false, out UpdateDispatchOutcome outcome) || !Enum.IsDefined(outcome) || outcome.ToString() != fields[1] ||
                !DateTimeOffset.TryParseExact(fields[2], "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset occurredAt) ||
                !Enum.TryParse(fields[4], false, out UpdateDispatchSettlementState settlement) || !Enum.IsDefined(settlement) || settlement.ToString() != fields[4] ||
                !int.TryParse(fields[6], NumberStyles.None, CultureInfo.InvariantCulture, out int length) ||
                length.ToString(CultureInfo.InvariantCulture) != fields[6] || length > 4096 * 4 || offset + length > reserved.Length)
                return false;
            int? exitCode = null;
            if (fields[3] != "None")
            {
                if (!int.TryParse(fields[3], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int exit) ||
                    exit.ToString(CultureInfo.InvariantCulture) != fields[3]) return false;
                exitCode = exit;
            }
            foreach (byte value in reserved[(offset + length)..]) if (value != 0) return false;
            string diagnostic = StrictUtf8.GetString(reserved.Slice(offset, length));
            record = new(outcome, occurredAt, exitCode, settlement, diagnostic);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or DecoderFallbackException) { return false; }
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

/// <summary>Presentation-safe history row. Every string is fixed or derived from frozen review data; none asserts helper qualification.</summary>
public sealed record ReviewedUpdateHistoryView(string Title, string Status, string InstallerMode, string CandidateEvidence,
    string? Disclosure, bool BlocksOtherUpdates, string Diagnostic);

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
            record.Terminal is null ? string.Empty : SanitizedDiagnosticBundle.Sanitize(record.Terminal.Diagnostic));
    }

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
