using PCSteward.Application;

namespace PCSteward.Provider.WinGet;

/// <summary>
/// Task 51 (FR-UPD-09, AC-UPD-07/08): the restart meaning of a WinGet client exit code. Only these typed HRESULT exit codes are
/// restart evidence; text on standard output or error (for example "Restart your PC to finish installation") never is.
/// </summary>
public enum WingetRestartSignal
{
    /// <summary>The exit code says nothing about a restart.</summary>
    None,

    /// <summary><c>APPINSTALLER_CLI_ERROR_INSTALL_REBOOT_REQUIRED_TO_FINISH</c>: the install finished and waits for a restart.</summary>
    RestartRequiredToFinish,

    /// <summary><c>APPINSTALLER_CLI_ERROR_INSTALL_REBOOT_INITIATED</c>: the installer finished and started a restart itself.</summary>
    RestartInitiated,

    /// <summary>
    /// <c>APPINSTALLER_CLI_ERROR_INSTALL_REBOOT_REQUIRED_FOR_INSTALL</c>: the installer needs a restart <b>before</b> it can install,
    /// so the update was not applied. This is a failed attempt, never a restart-pending update.
    /// </summary>
    RestartRequiredBeforeInstall
}

/// <summary>
/// The WinGet client's documented restart exit codes (winget-cli <c>AppInstallerErrors.h</c>). The client maps an installer's own
/// restart return codes (for example MSI 3010 and 1641, or Inno Setup 8) to these through the manifest's expected return codes.
/// Current clients finish <see cref="WingetRestartSignal.RestartRequiredToFinish"/> with exit code 0 and only a text warning, which
/// is deliberately not read; older clients exit with the code itself. A raw installer code such as 3010 is not a WinGet client exit
/// code and carries no restart meaning here.
/// </summary>
public static class WingetRestartExitCodes
{
    /// <summary>0x8A150109.</summary>
    public const int RebootRequiredToFinish = unchecked((int)0x8A150109);

    /// <summary>0x8A15010A.</summary>
    public const int RebootRequiredForInstall = unchecked((int)0x8A15010A);

    /// <summary>0x8A15010B.</summary>
    public const int RebootInitiated = unchecked((int)0x8A15010B);

    public static WingetRestartSignal Classify(int exitCode) => exitCode switch
    {
        RebootRequiredToFinish => WingetRestartSignal.RestartRequiredToFinish,
        RebootInitiated => WingetRestartSignal.RestartInitiated,
        RebootRequiredForInstall => WingetRestartSignal.RestartRequiredBeforeInstall,
        _ => WingetRestartSignal.None
    };

    /// <summary>True when the exit code reliably says the update finished and only a restart is left.</summary>
    public static bool ReportsFinishedPendingRestart(int exitCode) =>
        Classify(exitCode) is WingetRestartSignal.RestartRequiredToFinish or WingetRestartSignal.RestartInitiated;

    /// <summary>A fixed, presentation-safe diagnostic prefix for a restart exit code; empty for <see cref="WingetRestartSignal.None"/>.</summary>
    public static string DiagnosticPrefix(int exitCode) => Classify(exitCode) switch
    {
        WingetRestartSignal.RestartRequiredToFinish => "provider-reported-restart-required-to-finish",
        WingetRestartSignal.RestartInitiated => "provider-reported-restart-initiated",
        WingetRestartSignal.RestartRequiredBeforeInstall => UpdateOutcomePresenter.RestartBeforeInstallDiagnostic,
        _ => string.Empty
    };
}
