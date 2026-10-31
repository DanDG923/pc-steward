namespace PCSteward.Application;

public enum UpdateDispatchSettlementState { InvocationUnknown, NotInvoked, OwnedWorkStopped, OwnedWorkUnsettled }

/// <summary>Live finalization binding. Trusted orchestration supplies independently observed native facts; stored data never creates it.</summary>
public sealed class UpdateDispatchSettlement
{
    private readonly UpdateDispatchClaim claim;
    private readonly OperationOwnership ownership;
    private int attempted;
    private UpdateDispatchSettlement(UpdateDispatchClaim claim, OperationOwnership ownership, UpdateDispatchSettlementState state,
        bool rootCompleted, bool directExit, bool ioComplete, bool pipeEof, bool terminationFailed, bool executionUncertain)
    {
        this.claim = claim ?? throw new ArgumentNullException(nameof(claim));
        this.ownership = ownership ?? throw new ArgumentNullException(nameof(ownership));
        State = state;
        Facts = string.Join('/', rootCompleted, directExit, ioComplete, pipeEof, terminationFailed, executionUncertain);
    }
    public UpdateDispatchSettlementState State { get; }
    public string Facts { get; }
    public bool ProvesSettlement => State is UpdateDispatchSettlementState.NotInvoked or UpdateDispatchSettlementState.OwnedWorkStopped;
    public bool PermitsVerifiedOutcome => State == UpdateDispatchSettlementState.OwnedWorkStopped;

    internal static UpdateDispatchSettlement FromObservedWork(UpdateDispatchClaim claim, OperationOwnership ownership, Task itemOwnedRoot,
        bool directExit, bool ioComplete, bool pipeEof, bool terminationFailed, bool executionUncertain)
    {
        ArgumentNullException.ThrowIfNull(itemOwnedRoot);
        bool completed = itemOwnedRoot.IsCompletedSuccessfully;
        bool stopped = completed && directExit && ioComplete && pipeEof && !terminationFailed && !executionUncertain;
        return new(claim, ownership, stopped ? UpdateDispatchSettlementState.OwnedWorkStopped : UpdateDispatchSettlementState.OwnedWorkUnsettled,
            completed, directExit, ioComplete, pipeEof, terminationFailed, executionUncertain);
    }

    internal static UpdateDispatchSettlement BeforeInvocation(UpdateDispatchClaim claim, OperationOwnership ownership) =>
        new(claim, ownership, UpdateDispatchSettlementState.NotInvoked, false, false, false, false, false, false);

    public bool TryConsumeFor(UpdateDispatchClaim originalClaim, OperationOwnership originalOwnership) =>
        ReferenceEquals(claim, originalClaim) && ReferenceEquals(ownership, originalOwnership) &&
        ownership.CanDispatchFor(claim.Draft.Operation) && Interlocked.Exchange(ref attempted, 1) == 0;
}
