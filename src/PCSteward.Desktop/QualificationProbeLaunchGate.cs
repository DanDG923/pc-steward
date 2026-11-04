using PCSteward.Execution;
using PCSteward.Storage;

namespace PCSteward.Desktop;

/// <summary>
/// FR-UPD-15, TM-03/04/07/13; qualification probe spec rev 4 §5.1 and §5.2 step 2 (review M-2 b/c, D5): the probe branch's view
/// of the common current-user data-operation gate. It acquires the gate with <c>TryAcquire(TimeSpan.Zero, …)</c> on the
/// calling thread (the session is thread-affine; the probe runner disposes it there), reads <c>RecoveryRequired</c> of this
/// acquisition once, and reads the factory-reset marker through the read-only <see cref="FactoryReset.GetMarkerState"/>. It
/// references no database, scheduled-task registrar, <c>DataOperationLease</c>, window or view model, and no other
/// <see cref="FactoryReset"/> member.
/// </summary>
internal static class QualificationProbeLaunchGate
{
    internal static IQualificationProbeGateSession? TryAcquire()
    {
        CurrentUserDataOperationSession? session = CurrentUserDataOperationSession.TryAcquire(TimeSpan.Zero, CancellationToken.None);
        if (session is null) return null;
        try { return new HeldGate(session); }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    private sealed class HeldGate(CurrentUserDataOperationSession session) : IQualificationProbeGateSession
    {
        public bool RecoveryRequired { get; } = session.Context.RecoveryRequired;

        public bool ResetMarkerSettled() => FactoryReset.GetMarkerState() == FactoryResetMarkerState.None;

        public void Dispose() => session.Dispose();
    }
}
