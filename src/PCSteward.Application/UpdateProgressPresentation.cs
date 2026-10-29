using PCSteward.Domain;

namespace PCSteward.Application;

/// <summary>Pure, review-bound consumer of the closed local progress schema.</summary>
public sealed class UpdateProgressPresentation(Guid jobId, Guid correlationId, UpdateScope scope)
{
    private uint sequence;
    private UpdateProgressPhase phase;
    public string Label { get; private set; } = "Preparing reviewed update";
    public bool IsUnavailable { get; private set; }
    public bool IsFinal { get; private set; }
    public bool IsIndeterminate { get; } = true;

    public void MarkUnavailable() { IsUnavailable = true; Label = "Progress unavailable"; }

    public bool Consume(UpdateProgressFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (IsUnavailable) return false;
        // The mailbox coalesces phases but assigns contiguous sequence numbers at pull time.
        bool routeValid = (scope is UpdateScope.User or UpdateScope.Machine) &&
            !(scope == UpdateScope.User && frame.Phase is UpdateProgressPhase.ElevationRequested or UpdateProgressPhase.HandedToHelper) &&
            !(scope == UpdateScope.Machine && frame.Phase == UpdateProgressPhase.ProcessStarted);
        bool terminalValid = frame.Phase == UpdateProgressPhase.Final
            ? frame.Completion is { } completion && Enum.IsDefined(completion) &&
                (completion == UpdateProgressCompletion.Recorded
                    ? frame.RecordedStatus is { } status && Enum.IsDefined(status) && status is not
                        (UpdateHistoryStatus.Queued or UpdateHistoryStatus.Running or UpdateHistoryStatus.RunningApplicationWarning)
                    : frame.RecordedStatus is null)
            : frame.Completion is null && frame.RecordedStatus is null;
        if (jobId == Guid.Empty || correlationId == Guid.Empty || frame.JobId != jobId || frame.CorrelationId != correlationId ||
            IsFinal || frame.Degraded || sequence >= 256 || frame.Sequence != sequence + 1 ||
            !Enum.IsDefined(frame.Phase) || frame.Phase <= phase || !routeValid || !terminalValid)
        {
            MarkUnavailable();
            return false;
        }
        sequence = frame.Sequence;
        phase = frame.Phase;
        IsFinal = phase == UpdateProgressPhase.Final;
        Label = phase switch
        {
            UpdateProgressPhase.Queued => "Reviewed update queued",
            UpdateProgressPhase.Preparing => "Revalidating reviewed update",
            UpdateProgressPhase.ElevationRequested => "Requesting elevation; provider execution not observed",
            UpdateProgressPhase.HandedToHelper => "Update request sent; helper validation and provider execution indeterminate",
            UpdateProgressPhase.ProcessStarted => "Provider client started; installer progress indeterminate",
            UpdateProgressPhase.Verifying => "Verifying installed version",
            _ => FinalLabel(frame)
        };
        return true;
    }

    private static string FinalLabel(UpdateProgressFrame frame) => frame.Completion switch
    {
        UpdateProgressCompletion.RecordingFailed => "Outcome recording failed; no success recorded",
        UpdateProgressCompletion.AcknowledgementRequired => "New running application warning; review required",
        _ => frame.RecordedStatus switch
        {
            UpdateHistoryStatus.Success => "Complete — expected version verified",
            UpdateHistoryStatus.RestartRequired => "Restart required",
            UpdateHistoryStatus.Inconclusive or UpdateHistoryStatus.VerificationRequired => "Installed outcome uncertain; verification required",
            UpdateHistoryStatus.Cancelled => "Update cancelled",
            _ => "Update failed; see recorded outcome"
        }
    };
}
