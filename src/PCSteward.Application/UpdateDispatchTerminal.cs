using System.Globalization;
using System.Text;

namespace PCSteward.Application;

/// <summary>
/// Final dispatch record written into the reserved 16 KiB terminal slot. Version 2 is written whenever the helper check is
/// NotObserved (byte-identical to earlier builds); version 3 adds the desktop-derived helper check (adapter gates §B.3/§B.4).
/// </summary>
public sealed class UpdateDispatchTerminal
{
    public const int ReservedBytes = 16 * 1024;
    internal const string HeaderV2 = "pc-steward-update-terminal:2";
    internal const string HeaderV3 = "pc-steward-update-terminal:3";
    internal const string HelperCheckPrefix = "helper-check:";
    internal const string HelperRefusalPrefix = "helper-refusal:";
    internal const string HelperInstalledPrefix = "helper-installed:";
    internal const string HelperCandidatePrefix = "helper-candidate:";

    public UpdateDispatchTerminal(UpdateDispatchOutcome outcome, DateTimeOffset occurredAt, int? exitCode, string diagnostic,
        RecordedHelperCandidateCheck? helperCheck = null)
    {
        if (!Enum.IsDefined(outcome) || occurredAt == default || diagnostic is null || diagnostic.Length > 4096)
            throw new ArgumentException("A bounded terminal outcome is required.");
        Outcome = outcome;
        OccurredAt = occurredAt.ToUniversalTime();
        ExitCode = exitCode;
        Diagnostic = SanitizedDiagnosticBundle.Sanitize(diagnostic);
        if (Diagnostic.Length > 4096) throw new ArgumentException("Sanitized diagnostic exceeds the terminal reservation.");
        try { _ = new UTF8Encoding(false, true).GetByteCount(Diagnostic); }
        catch (EncoderFallbackException exception) { throw new ArgumentException("Sanitized diagnostic is not valid UTF-16 text.", exception); }
        HelperCheck = helperCheck ?? RecordedHelperCandidateCheck.NotObserved;
    }
    public UpdateDispatchOutcome Outcome { get; }
    public DateTimeOffset OccurredAt { get; }
    public int? ExitCode { get; }
    public string Diagnostic { get; }
    /// <summary>Desktop-derived history data only; never authority and never an input to the outcome.</summary>
    public RecordedHelperCandidateCheck HelperCheck { get; }
    public byte[] GetReservedPayload(UpdateDispatchSettlement? settlement = null)
    {
        byte[] diagnostic = new UTF8Encoding(false, true).GetBytes(Diagnostic);
        string outcome = Outcome.ToString(), occurredAt = OccurredAt.ToString("O", CultureInfo.InvariantCulture),
            exitCode = ExitCode?.ToString(CultureInfo.InvariantCulture) ?? "None", state = settlement?.State.ToString() ?? "InvocationUnknown",
            facts = settlement?.Facts ?? "None", length = diagnostic.Length.ToString(CultureInfo.InvariantCulture);
        RecordedHelperCandidateCheck check = HelperCheck;
        byte[] header = Encoding.ASCII.GetBytes((check.Kind == RecordedHelperCheckKind.NotObserved
            ? string.Join('\n', HeaderV2, outcome, occurredAt, exitCode, state, facts, length)
            : string.Join('\n', HeaderV3, outcome, occurredAt, exitCode, state, facts,
                HelperCheckPrefix + check.Kind, HelperRefusalPrefix + check.RefusalReason,
                HelperInstalledPrefix + (check.ReportedInstalledVersion ?? RecordedHelperCandidateCheck.NoneToken),
                HelperCandidatePrefix + (check.ReportedCandidateVersion ?? RecordedHelperCandidateCheck.NoneToken), length)) + "\n");
        if (header.Length + diagnostic.Length > ReservedBytes) throw new InvalidOperationException("Terminal representation exceeds its reservation.");
        byte[] reserved = new byte[ReservedBytes];
        header.CopyTo(reserved, 0);
        diagnostic.CopyTo(reserved, header.Length);
        return reserved;
    }
}
