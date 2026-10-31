namespace PCSteward.Domain;

public enum InstallerMode { Silent, Interactive }
public enum InstallerModeBinding { Selected, NotControllable }
public enum UncontrolledInstallerModeReason { ProviderScriptControlled }

/// <summary>FR-UPD-15: reviewed mode data, never provider eligibility or execution authority.</summary>
public sealed record ReviewedInstallerMode
{
    private ReviewedInstallerMode(InstallerModeBinding binding, InstallerMode? mode, UncontrolledInstallerModeReason? reason)
    {
        Binding = binding;
        Mode = mode;
        Reason = reason;
    }

    public InstallerModeBinding Binding { get; }
    public InstallerMode? Mode { get; }
    public UncontrolledInstallerModeReason? Reason { get; }

    public static ReviewedInstallerMode Selected(InstallerMode mode)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        return new(InstallerModeBinding.Selected, mode, null);
    }

    public static ReviewedInstallerMode NotControllable(UncontrolledInstallerModeReason reason)
    {
        if (!Enum.IsDefined(reason)) throw new ArgumentOutOfRangeException(nameof(reason));
        return new(InstallerModeBinding.NotControllable, null, reason);
    }
}
