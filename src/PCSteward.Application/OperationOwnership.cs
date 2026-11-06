using PCSteward.Domain;

namespace PCSteward.Application;

public static class OperationGateContract
{
    public const string NamePrefix = "Global\\PCSteward.DataOperation.v2.";
}

/// <summary>Process-local proof of a held gate's lifetime; never a persisted approval or dispatch permit.</summary>
public sealed class OperationOwnership
{
    private readonly object sync = new();
    private readonly string gateName;
    private bool active = true;
    private bool recoveryRequired;
    private bool reconciled;

    internal OperationOwnership(string requestingUserSid, string gateName, bool recoveryRequired, bool reconciled = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestingUserSid);
        ArgumentException.ThrowIfNullOrWhiteSpace(gateName);
        RequestingUserSid = requestingUserSid;
        this.gateName = gateName;
        this.recoveryRequired = recoveryRequired;
        this.reconciled = reconciled;
    }

    public string RequestingUserSid { get; }
    public bool IsActive { get { lock (sync) return active; } }
    public bool RecoveryRequired { get { lock (sync) return recoveryRequired; } }
    public bool CanReconcile
    {
        get { lock (sync) return active && string.Equals(gateName, OperationGateContract.NamePrefix + RequestingUserSid, StringComparison.Ordinal); }
    }

    public bool CanDispatchFor(ReviewedUpdateOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        lock (sync)
        {
            return active && reconciled && !recoveryRequired &&
                string.Equals(RequestingUserSid, operation.RequestingUserSid, StringComparison.Ordinal) &&
                string.Equals(gateName, OperationGateContract.NamePrefix + operation.RequestingUserSid, StringComparison.Ordinal);
        }
    }

    internal void MarkReconciled()
    {
        lock (sync)
        {
            if (!active) throw new InvalidOperationException("Released operation ownership cannot be reconciled or revived.");
            recoveryRequired = false;
            reconciled = true;
        }
    }

    internal void RequireRecovery()
    {
        lock (sync)
        {
            if (!active) throw new InvalidOperationException("Released operation ownership cannot acquire recovery state.");
            recoveryRequired = true;
            reconciled = false;
        }
    }

    internal void Release()
    {
        lock (sync) active = false;
    }
}
